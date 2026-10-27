using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Fiscal.Infrastructure.Emissor;

/// <summary>Cria o adaptador HTTP de uma conexão (cliente nomeado <c>emissor-fiscal</c>: sem redirect e sem log do HttpClient).</summary>
internal sealed class EmissorFiscalEmitterFactory(IHttpClientFactory clients, IOptions<FiscalIntegrationOptions> options, EmissorFiscalRateGate gate, IntegrationHealthTracker health, TimeProvider clock, ILogger<EmissorFiscalHttpEmitter> logger)
    : IFiscalEmitterFactory
{
    public const string ClientName = "emissor-fiscal";

    public IFiscalEmitter Create(FiscalEmitterConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var client = clients.CreateClient(ClientName);
        client.BaseAddress = connection.BaseUrl;
        client.Timeout = Timeout.InfiniteTimeSpan; // o limite é por chamada (CA 4), com o cancelamento abaixo
        return new EmissorFiscalHttpEmitter(client, connection, options.Value, gate, health, clock, logger);
    }
}

/// <summary>
/// Adaptador HTTP do EmissorFiscal (FIS-01 CA 4, ADR 0027): <c>X-Api-Key</c> e <c>Idempotency-Key</c>, JSON do guia de integração,
/// tempo limite de 30 s por chamada e nova tentativa só em leitura (uma, em falha transitória ou 429 com <c>Retry-After</c> curto). Toda chamada
/// espera a vez no <see cref="EmissorFiscalRateGate"/> (no máximo <c>MaxRequestsPerSecond</c> por processo, sem rajada); o 429 pausa todas pelo
/// <c>Retry-After</c>.
/// Tradução: 201/202/422 da emissão viram documento; 400 vira erro campo a campo; 401/403, <c>fiscal_integration_misconfigured</c>;
/// 404, <c>fiscal_not_found</c>; 409, conflito; 429, <c>fiscal_rate_limited</c> com o <c>Retry-After</c>; rede, tempo limite e 5xx,
/// <c>fiscal_unavailable</c>. O log leva só tenant, operação, situação HTTP, código de rejeição e duração (CA 12): nunca a chave, o
/// payload, a URL com a chave de acesso ou o XML.
/// </summary>
internal sealed partial class EmissorFiscalHttpEmitter(HttpClient http, FiscalEmitterConnection connection, FiscalIntegrationOptions options, EmissorFiscalRateGate gate, IntegrationHealthTracker health, TimeProvider clock, ILogger logger)
    : IFiscalEmitter
{
    public const string Integration = "emissor-fiscal";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ApiKeyHeader = "X-Api-Key";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
    };

    private enum Kind
    {
        Read,
        Write,
    }

    public Task<Result<NfePreview>> PreviewAsync(NfeIssueRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<NfePreview>("nfe.previa", Kind.Write, () => Post("api/v1/nfe/previa", EmissorFiscalMapping.ToEmissor(NfePayload.Normalize(request))), async (response, ct) =>
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return await ErrorAsync(response, ct);
            }

            var previa = await Read<EfPrevia>(response, ct);
            return new NfePreview(previa.ChaveDeAcesso ?? string.Empty, previa.Numero, previa.Serie, previa.Avisos ?? []);
        }, cancellationToken);

    public Task<Result<NfeDocument>> IssueNfeAsync(NfeIssueRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        var body = EmissorFiscalMapping.ToEmissor(NfePayload.Normalize(request));
        return SendAsync<NfeDocument>("nfe.emitir", Kind.Write, () =>
        {
            var message = Post("api/v1/nfe", body);
            message.Headers.Add(IdempotencyHeader, idempotencyKey);
            return message;
        }, DocumentAsync, cancellationToken);
    }

    public Task<Result<NfeDocument>> GetNfeAsync(string accessKey, CancellationToken cancellationToken = default) =>
        SendAsync<NfeDocument>("nfe.consultar", Kind.Read, () => new HttpRequestMessage(HttpMethod.Get, "api/v1/nfe/" + Key(accessKey)), DocumentAsync, cancellationToken);

    public Task<Result<NfeDocument>> ForceConsultAsync(string accessKey, CancellationToken cancellationToken = default) =>
        SendAsync<NfeDocument>("nfe.consulta_forcada", Kind.Read, () => new HttpRequestMessage(HttpMethod.Post, "api/v1/nfe/" + Key(accessKey) + "/consultar"), DocumentAsync, cancellationToken);

    public Task<Result<FiscalFile>> GetXmlAsync(string accessKey, CancellationToken cancellationToken = default) =>
        FileAsync("nfe.xml", "api/v1/nfe/" + Key(accessKey) + "/xml", "application/xml", Key(accessKey) + "-nfe.xml", cancellationToken);

    public Task<Result<FiscalFile>> GetDanfePdfAsync(string accessKey, CancellationToken cancellationToken = default) =>
        FileAsync("nfe.danfe", "api/v1/nfe/" + Key(accessKey) + "/danfe", "application/pdf", Key(accessKey) + "-danfe.pdf", cancellationToken);

    public Task<Result<NfeEventResult>> CancelNfeAsync(string accessKey, string justification, CancellationToken cancellationToken = default) =>
        SendAsync<NfeEventResult>("nfe.cancelar", Kind.Write, () => Post("api/v1/nfe/" + Key(accessKey) + "/cancelamento", new { justificativa = justification }), EventAsync, cancellationToken);

    public Task<Result<NfeEventResult>> CorrectNfeAsync(string accessKey, string correction, CancellationToken cancellationToken = default) =>
        SendAsync<NfeEventResult>("nfe.carta_correcao", Kind.Write, () => Post("api/v1/nfe/" + Key(accessKey) + "/carta-correcao", new { correcao = correction }), EventAsync, cancellationToken);

    public Task<Result<NumberRangeInvalidationResult>> InvalidateRangeAsync(NumberRangeInvalidation request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<NumberRangeInvalidationResult>("nfe.inutilizar", Kind.Write,
            () => Post("api/v1/nfe/inutilizacao", new EfInutilizacaoRequest(request.IssuerDocument, request.Series, request.FirstNumber, request.LastNumber, request.Justification)),
            async (response, ct) =>
            {
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity))
                {
                    return await ErrorAsync(response, ct);
                }

                var r = await Read<EfInutilizacao>(response, ct);
                return Result<NumberRangeInvalidationResult>.Success(new NumberRangeInvalidationResult(r.Aceito, r.Serie, r.NumeroInicial, r.NumeroFinal, r.Protocolo, r.Codigo, r.Motivo, r.AcaoSugerida));
            }, cancellationToken);
    }

    public Task<Result<IReadOnlyList<NfeEventResult>>> GetEventsAsync(string accessKey, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<NfeEventResult>>("nfe.eventos", Kind.Read, () => new HttpRequestMessage(HttpMethod.Get, "api/v1/nfe/" + Key(accessKey) + "/eventos"), async (response, ct) =>
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return await ErrorAsync(response, ct);
            }

            var events = await Read<List<EfEvento>>(response, ct);
            return Result<IReadOnlyList<NfeEventResult>>.Success(events.Select(EmissorFiscalMapping.ToEvent).ToList());
        }, cancellationToken);

    public Task<Result<SefazStatus>> SefazStatusAsync(string? issuerDocument, CancellationToken cancellationToken = default) =>
        SendAsync<SefazStatus>("sefaz.status", Kind.Read, () => new HttpRequestMessage(HttpMethod.Get, "api/v1/sefaz/status" + (issuerDocument is { Length: > 0 } d ? "?emitente=" + Digits(d) : string.Empty)),
            async (response, ct) =>
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return await ErrorAsync(response, ct);
                }

                var s = await Read<EfSefazStatus>(response, ct);
                return Result<SefazStatus>.Success(new SefazStatus(s.Online, s.Uf ?? string.Empty, s.Ambiente ?? string.Empty, s.Mensagem, s.TempoMedioResposta, s.ContingenciaSugerida, s.ConsultadoEm));
            }, cancellationToken);

    public Task<Result<EmitterAccount>> GetAccountAsync(CancellationToken cancellationToken = default) =>
        SendAsync<EmitterAccount>("me", Kind.Read, () => new HttpRequestMessage(HttpMethod.Get, "api/v1/me"), async (response, ct) =>
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return await ErrorAsync(response, ct);
            }

            return Result<EmitterAccount>.Success(EmissorFiscalMapping.ToAccount(await Read<EfMe>(response, ct)));
        }, cancellationToken);

    public Task<Result<CertificateUploadResult>> UploadCertificateAsync(string issuerDocument, ReadOnlyMemory<byte> pfx, Secret password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        return SendAsync<CertificateUploadResult>("certificado.enviar", Kind.Write, () =>
        {
            // ADR 0027 §3: o .pfx e a senha só em memória, no corpo do formulário (nunca na URL, em JSON ou em log).
            var form = new MultipartFormDataContent
            {
                { new ReadOnlyMemoryContent(pfx) { Headers = { ContentType = new MediaTypeHeaderValue("application/x-pkcs12") } }, "arquivo", "certificado.pfx" },
                { new StringContent(password.Reveal()), "senha" },
            };
            return new HttpRequestMessage(HttpMethod.Post, "api/v1/emitentes/" + Digits(issuerDocument) + "/certificado") { Content = form };
        }, async (response, ct) =>
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return await ErrorAsync(response, ct);
            }

            var sent = await Read<EfCertificadoEnviado>(response, ct);
            return Result<CertificateUploadResult>.Success(new CertificateUploadResult(sent.Id, sent.Thumbprint ?? string.Empty, sent.NotAfter));
        }, cancellationToken);
    }

    public Task<Result<NfseDocument>> IssueNfseAsync(NfseIssueRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        return SendAsync<NfseDocument>("nfse.emitir", Kind.Write, () =>
        {
            var message = Post("api/v1/nfse", EmissorFiscalMapping.ToEmissor(request));
            message.Headers.Add(IdempotencyHeader, idempotencyKey);
            return message;
        }, NfseAsync, cancellationToken);
    }

    public Task<Result<NfseDocument>> GetNfseAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        SendAsync<NfseDocument>("nfse.consultar", Kind.Read, () => new HttpRequestMessage(HttpMethod.Get, "api/v1/nfse/" + documentId.ToString("D")), NfseAsync, cancellationToken);

    /// <summary>201 autorizada, 202 processando ou em contingência, 422 rejeitada ou denegada; 200 da consulta.</summary>
    private static async Task<Result<NfeDocument>> DocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted or HttpStatusCode.UnprocessableEntity))
        {
            return await ErrorAsync(response, cancellationToken);
        }

        var body = await Read<EfNfeResponse>(response, cancellationToken);
        return EmissorFiscalMapping.ToDocument(body);
    }

    private static async Task<Result<NfseDocument>> NfseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted or HttpStatusCode.UnprocessableEntity))
        {
            return await ErrorAsync(response, cancellationToken);
        }

        return EmissorFiscalMapping.ToDocument(await Read<EfNfseResponse>(response, cancellationToken));
    }

    private static async Task<Result<NfeEventResult>> EventAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity))
        {
            return await ErrorAsync(response, cancellationToken);
        }

        return EmissorFiscalMapping.ToEvent(await Read<EfEvento>(response, cancellationToken));
    }

    private Task<Result<FiscalFile>> FileAsync(string operation, string path, string expectedType, string fileName, CancellationToken cancellationToken) =>
        SendAsync<FiscalFile>(operation, Kind.Read, () => new HttpRequestMessage(HttpMethod.Get, path), async (response, ct) =>
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return await ErrorAsync(response, ct);
            }

            var type = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(type, expectedType, StringComparison.OrdinalIgnoreCase))
            {
                // A DANFE em HTML nunca é repassada (XSS, ADR 0027 §5 item 2).
                return Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal devolveu um arquivo de tipo inesperado.");
            }

            return Result<FiscalFile>.Success(new FiscalFile(await response.Content.ReadAsByteArrayAsync(ct), expectedType, fileName));
        }, cancellationToken);

    /// <summary>
    /// Uma chamada com o tempo limite, a chave no cabeçalho e o log seguro. Leitura tenta de novo uma vez em falha transitória ou em 429
    /// com espera curta; escrita nunca (a dúvida fica com quem chamou, que reenvia com a mesma chave).
    /// </summary>
    private async Task<Result<T>> SendAsync<T>(string operation, Kind kind, Func<HttpRequestMessage> build, Func<HttpResponseMessage, CancellationToken, Task<Result<T>>> handle, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!await WaitTurnAsync(cancellationToken))
            {
                LogThrottled(logger, operation, connection.TenantId);
                return RateLimited(gate.PausedFor() is var left && left > TimeSpan.Zero ? left : null);
            }

            var started = clock.GetTimestamp();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            int? status = null;
            try
            {
                using var request = build();
                request.Headers.Add(ApiKeyHeader, connection.ApiKey.Reveal());
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    gate.Pause(RetryAfter(response)); // ninguém deste processo chama até passar o Retry-After
                }

                if (kind == Kind.Read && attempt == 1 && response.StatusCode == HttpStatusCode.TooManyRequests && RetryAfter(response) is { } wait && wait <= options.MaxReadRetryAfter)
                {
                    var ms1 = clock.GetElapsedTime(started).TotalMilliseconds;
                    LogCall(logger, operation, connection.TenantId, status.Value, null, ms1);
                    continue; // a vez seguinte no gate já espera o Retry-After
                }

                if (kind == Kind.Read && attempt == 1 && status >= 500)
                {
                    var ms2 = clock.GetElapsedTime(started).TotalMilliseconds;
                    LogCall(logger, operation, connection.TenantId, status.Value, null, ms2);
                    continue;
                }

                var result = await handle(response, timeout.Token);
                var rejection = result.IsSuccess && result.Value is NfeDocument { Rejection: { } r } ? r.Code : (int?)null;
                var ms3 = clock.GetElapsedTime(started).TotalMilliseconds;
                LogCall(logger, operation, connection.TenantId, status.Value, rejection, ms3);
                if (status < 500)
                {
                    health.RecordSuccess(Integration);
                }

                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException && !cancellationToken.IsCancellationRequested)
            {
                var elapsed = clock.GetElapsedTime(started).TotalMilliseconds;
                LogFailure(logger, operation, connection.TenantId, status, ex.GetType().Name, elapsed);
                if (kind == Kind.Read && attempt == 1 && ex is HttpRequestException or OperationCanceledException)
                {
                    continue;
                }

                await health.RecordFailureAsync(Integration, connection.TenantId.ToString("N"), operation + ": " + ex.GetType().Name, CancellationToken.None);
                return ex is JsonException or NotSupportedException
                    ? Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal respondeu fora do formato esperado.")
                    : Error.Unavailable(FiscalErrorCodes.Unavailable, ex is OperationCanceledException ? "O Emissor Fiscal não respondeu a tempo." : "Não foi possível falar com o Emissor Fiscal.");
            }
        }
    }

    /// <summary>Tradução das respostas que não são o caso esperado da operação (CA 4).</summary>
    private static async Task<Error> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await TryProblemAsync(response, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => Error.Validation(FiscalErrorCodes.Validation, "O Emissor Fiscal recusou o pedido: " + (problem?.Detail ?? problem?.Title ?? "dados inválidos") , Fields(problem?.Detail)),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Error.Unavailable(FiscalErrorCodes.IntegrationMisconfigured, "O Emissor Fiscal recusou a chave desta empresa. Confira a integração fiscal."),
            HttpStatusCode.NotFound => Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o documento."),
            HttpStatusCode.Conflict => Error.Conflict(FiscalErrorCodes.Conflict, problem?.Detail ?? "A situação da nota no Emissor Fiscal não permite esta operação."),
            HttpStatusCode.TooManyRequests => RateLimited(RetryAfter(response)),
            _ => Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal está indisponível (" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ")."),
        };
    }

    /// <summary>Espera a vez no limite do processo, até o tempo limite de uma chamada; falso quando não deu (fila cheia ou espera longa).</summary>
    private async Task<bool> WaitTurnAsync(CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(options.Timeout);
        try
        {
            return await gate.WaitAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static Error RateLimited(TimeSpan? retryAfter) =>
        new(FiscalErrorCodes.RateLimited, "Muitas chamadas ao Emissor Fiscal. Tente de novo em instantes.", ErrorType.TooManyRequests)
        {
            Data = new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfter is { } wait ? (int)Math.Ceiling(wait.TotalSeconds) : null },
        };

    /// <summary>"emitente: CNPJ inválido. itens: informe ao menos um item." → campo a campo.</summary>
    internal static Dictionary<string, string[]>? Fields(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var sentence in detail.Split(". ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = sentence.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon > 60)
            {
                continue;
            }

            var field = sentence[..colon].Trim();
            var message = sentence[(colon + 1)..].Trim().TrimEnd('.');
            if (field.Length == 0 || message.Length == 0 || field.Any(char.IsWhiteSpace))
            {
                continue;
            }

            fields[field] = fields.TryGetValue(field, out var existing) ? [.. existing, message] : [message];
        }

        return fields.Count == 0 ? null : fields;
    }

    private static async Task<EfProblem?> TryProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var type = response.Content.Headers.ContentType?.MediaType;
            return type is "application/problem+json" or "application/json"
                ? await response.Content.ReadFromJsonAsync<EfProblem>(Json, cancellationToken)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow is var d && d > TimeSpan.Zero ? d : TimeSpan.Zero,
        _ => null,
    };

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken) ?? throw new JsonException("Resposta vazia.");

    private static HttpRequestMessage Post<T>(string path, T body) => new(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };

    /// <summary>A chave de acesso só com dígitos (nada de caminho montado com texto de fora).</summary>
    private static string Key(string accessKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessKey);
        return accessKey.Length == 44 && accessKey.All(char.IsAsciiDigit) ? accessKey : throw new ArgumentException("Chave de acesso inválida.", nameof(accessKey));
    }

    private static string Digits(string document) => new(document.Where(char.IsAsciiDigit).ToArray());

    [LoggerMessage(Level = LogLevel.Information, Message = "EmissorFiscal {Operation} do tenant {TenantId}: HTTP {Status}, rejeição {RejectionCode}, {Elapsed:0.0} ms")]
    private static partial void LogCall(ILogger logger, string operation, Guid tenantId, int status, int? rejectionCode, double elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "EmissorFiscal {Operation} do tenant {TenantId}: sem vez no limite de chamadas; não chamou")]
    private static partial void LogThrottled(ILogger logger, string operation, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "EmissorFiscal {Operation} do tenant {TenantId}: falha {Error} (HTTP {Status}) em {Elapsed:0.0} ms")]
    private static partial void LogFailure(ILogger logger, string operation, Guid tenantId, int? status, string error, double elapsed);
}
