using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Finance.Application.Inter;
using BemAqui.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Infrastructure.Inter;

/// <summary>
/// Cliente da API do Inter (BNK-01a CA 7 a 11, ADR 0032 §2 e §3): OAuth2 <c>client_credentials</c> com mTLS, um handler por conta em
/// cache (<see cref="InterHandlerCache"/>), token em cache até 60 s antes de vencer, renovado sob lock e uma vez de novo num 401, URL
/// base fixa por ambiente. O log de cada chamada traz só tenant, integração, operação, método, situação HTTP, id de correlação do
/// Inter e duração: nunca token, client secret, <c>Authorization</c>, corpo ou certificado.
/// </summary>
public sealed partial class InterApiClient(
    InterHandlerCache handlers, InterTokenCache tokens, IInterHandlerFactory factory, IOptions<InterOptions> options, TimeProvider clock, ILogger<InterApiClient> logger)
    : IInterApiClient
{
    /// <summary>Cabeçalhos de correlação que o Inter pode devolver (o primeiro presente vai para o log).</summary>
    private static readonly string[] CorrelationHeaders = ["x-correlation-id", "x-request-id", "x-inter-correlation-id", "x-amzn-requestid"];

    public async Task<InterResult<InterTokenInfo>> ProbeAsync(InterCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        // Credencial nova: handler próprio, sem cache, descartado no fim (a atual continua intacta, CA 14).
        using var certificate = credentials.LoadCertificate();
        using var handler = factory.Create(certificate);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        var (token, failure) = await ProbeTokenAsync(invoker, credentials, cancellationToken);
        return token is null ? InterResult<InterTokenInfo>.Fail(failure!) : InterResult<InterTokenInfo>.Success(new InterTokenInfo(token.Scopes, token.ExpiresAt));
    }

    public async Task<InterResult<InterConnectionCheck>> TestConnectionAsync(InterCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var invoker = handlers.Get(credentials);
        var (probe, failure) = await ProbeTokenAsync(invoker, credentials, cancellationToken);
        if (probe is null)
        {
            return InterResult<InterConnectionCheck>.Fail(failure!);
        }

        var evaluation = InterScopes.Evaluate(probe.Scopes, options.Value);
        if (evaluation.CanPay || !probe.Scopes.Contains("extrato.read", StringComparer.Ordinal))
        {
            // Credencial que paga não segue (CA 8); sem o escopo de extrato, a leitura daria 403: o serviço mostra o que falta.
            return InterResult<InterConnectionCheck>.Success(new InterConnectionCheck(probe.Scopes, BalanceRead: false));
        }

        var balance = await SendAsync(credentials, new InterRequest("saldo", HttpMethod.Get, InterEnvironments.BalancePath), cancellationToken);
        if (!balance.Ok)
        {
            return InterResult<InterConnectionCheck>.Fail(balance.Failure!);
        }

        return balance.Value!.Status is >= 200 and < 300
            ? InterResult<InterConnectionCheck>.Success(new InterConnectionCheck(probe.Scopes, BalanceRead: true))
            : InterResult<InterConnectionCheck>.Fail(FailureFor(balance.Value.Status, token: false));
    }

    public async Task<InterResult<InterResponse>> SendAsync(InterCredentials credentials, InterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(request);
        var invoker = handlers.Get(credentials);
        InterAccessToken? stale = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (token, failure) = await tokens.GetOrRenewAsync(credentials.CacheKey, ct => RequestTokenAsync(invoker, credentials, options.Value.RequiredScopes, ct), stale, cancellationToken);
            if (token is null)
            {
                return InterResult<InterResponse>.Fail(failure!);
            }

            using var message = new HttpRequestMessage(request.Method, new Uri(InterEnvironments.BaseUrl(credentials.Environment), request.Path));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.Reveal());
            if (credentials.AccountNumber is { Length: > 0 } account)
            {
                message.Headers.TryAddWithoutValidation(InterEnvironments.AccountHeader, account);
            }

            message.Content = request.Content?.Invoke();
            var (response, error) = await SendLoggedAsync(invoker, message, credentials, request.Operation, cancellationToken);
            if (response is null)
            {
                return InterResult<InterResponse>.Fail(error!);
            }

            using (response)
            {
                // 401 de token (vencido ou revogado antes da hora): renova uma vez e repete (CA 10).
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    stale = token;
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return InterResult<InterResponse>.Success(new InterResponse((int)response.StatusCode, body));
            }
        }

        return InterResult<InterResponse>.Fail(FailureFor(401, token: false));
    }

    public void Forget(Guid tenantId, Guid integrationId)
    {
        tokens.Forget($"{tenantId:N}:{integrationId:N}:");
        handlers.Forget(tenantId, integrationId);
    }

    /// <summary>
    /// Token de prova (CA 8): pede os necessários mais os de pagamento; se o Inter recusar o pedido (400, escopo não autorizado), pede
    /// só os necessários. O que vier concedido decide: escopo de pagamento recusa a credencial.
    /// </summary>
    private async Task<(InterAccessToken? Token, InterFailure? Failure)> ProbeTokenAsync(HttpMessageInvoker invoker, InterCredentials credentials, CancellationToken cancellationToken)
    {
        var all = options.Value.RequiredScopes.Concat(options.Value.ProbeScopes).Distinct(StringComparer.Ordinal).ToArray();
        var (token, failure) = await RequestTokenAsync(invoker, credentials, all, cancellationToken);
        if (token is null && failure is { HttpStatus: 400 })
        {
            (token, failure) = await RequestTokenAsync(invoker, credentials, options.Value.RequiredScopes, cancellationToken);
        }

        return (token, failure);
    }

    private async Task<(InterAccessToken? Token, InterFailure? Failure)> RequestTokenAsync(HttpMessageInvoker invoker, InterCredentials credentials, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(InterEnvironments.BaseUrl(credentials.Environment), InterEnvironments.TokenPath))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret.Reveal(),
                ["grant_type"] = "client_credentials",
                ["scope"] = string.Join(' ', scopes),
            }),
        };
        var (response, error) = await SendLoggedAsync(invoker, message, credentials, "token", cancellationToken);
        if (response is null)
        {
            return (null, error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return (null, FailureFor((int)response.StatusCode, token: true));
            }

            TokenResponse? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<TokenResponse>(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                body = null;
            }

            if (body?.AccessToken is not { Length: > 0 } value)
            {
                return (null, new InterFailure(InterFailureKind.BankError, "O Inter respondeu o token num formato inesperado.", "Tente de novo em alguns minutos.", (int)response.StatusCode));
            }

            var lifetime = TimeSpan.FromSeconds(Math.Clamp(body.ExpiresIn ?? 3600, 60, 24 * 3600));
            var granted = body.Scope is null ? scopes.ToList() : InterScopes.Parse(body.Scope);
            return (new InterAccessToken(new Secret(value), clock.GetUtcNow() + lifetime, granted), null);
        }
    }

    private async Task<(HttpResponseMessage? Response, InterFailure? Failure)> SendLoggedAsync(
        HttpMessageInvoker invoker, HttpRequestMessage message, InterCredentials credentials, string operation, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.RequestTimeout);
        try
        {
            var response = await invoker.SendAsync(message, timeout.Token);
            var correlation = Correlation(response);
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogCall(logger, credentials.TenantId, credentials.IntegrationId, operation, message.Method.Method, (int)response.StatusCode, correlation, elapsed);
            return (response, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogFailure(logger, credentials.TenantId, credentials.IntegrationId, operation, message.Method.Method, "timeout", elapsed);
            return (null, new InterFailure(InterFailureKind.Unreachable, "O Inter demorou a responder.", "Tente de novo em alguns minutos."));
        }
        catch (Exception ex) when (ex is HttpRequestException or AuthenticationException or InvalidOperationException)
        {
            // Só o tipo da falha: a mensagem da exceção pode trazer o endereço ou detalhes do TLS.
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var reason = ex.GetType().Name;
            LogFailure(logger, credentials.TenantId, credentials.IntegrationId, operation, message.Method.Method, reason, elapsed);
            return (null, new InterFailure(InterFailureKind.Unreachable, "Não foi possível conectar ao Inter com este certificado.",
                "Confira se o certificado é o da integração gerada no Inter e tente de novo em alguns minutos."));
        }
    }

    private static string? Correlation(HttpResponseMessage response)
    {
        foreach (var header in CorrelationHeaders)
        {
            if (response.Headers.TryGetValues(header, out var values) && values.FirstOrDefault() is { Length: > 0 and <= 100 } value)
            {
                return value;
            }
        }

        return null;
    }

    internal static InterFailure FailureFor(int status, bool token) => status switch
    {
        400 or 401 when token => new InterFailure(InterFailureKind.CredentialsRefused, "O Inter recusou o client id, o client secret ou o certificado.",
            "Confira os dados da integração no Internet Banking do Inter (Soluções para sua empresa → Nova integração) e envie de novo.", status),
        401 => new InterFailure(InterFailureKind.CredentialsRefused, "O Inter recusou o acesso desta credencial.", "Teste a conexão de novo; se continuar, troque as credenciais.", status),
        403 => new InterFailure(InterFailureKind.Forbidden, "O Inter recusou a operação por falta de permissão na credencial.", InterScopes.MissingAction, status),
        429 => new InterFailure(InterFailureKind.BankError, "O Inter limitou as chamadas desta conta por alguns instantes.", "Aguarde um minuto e tente de novo.", status),
        >= 500 => new InterFailure(InterFailureKind.BankError, "O Inter está com instabilidade.", "Tente de novo em alguns minutos.", status),
        _ => new InterFailure(InterFailureKind.BankError, $"O Inter respondeu com a situação {status}.", "Tente de novo; se continuar, fale com o suporte.", status),
    };

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("token_type")] string? TokenType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inter {Operation} {Method} → {Status} (tenant {TenantId}, integração {IntegrationId}, correlação {Correlation}, {ElapsedMs} ms)")]
    private static partial void LogCall(ILogger logger, Guid tenantId, Guid integrationId, string operation, string method, int status, string? correlation, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inter {Operation} {Method} falhou: {Reason} (tenant {TenantId}, integração {IntegrationId}, {ElapsedMs} ms)")]
    private static partial void LogFailure(ILogger logger, Guid tenantId, Guid integrationId, string operation, string method, string reason, long elapsedMs);
}
