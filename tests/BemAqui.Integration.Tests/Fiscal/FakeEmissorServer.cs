using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// Fake HTTP do EmissorFiscal (FIS-01 CA 15) num <c>TestServer</c>, com as respostas do <c>docs/integracao/README.md</c> de lá: o cenário
/// da emissão vem da <c>naturezaOperacao</c> (AUTORIZAR, PROCESSAR, CONTINGENCIA, REJEITAR, DENEGAR, INVALIDO, CONFLITO, LIMITE, QUEDA);
/// a chave <c>chave-admin</c> tem o escopo admin, <c>chave-recusada</c> dá 401 e <c>chave-sem-escopo</c> dá 403. Guarda cada pedido.
/// </summary>
public sealed class FakeEmissorServer : IAsyncDisposable
{
    public const string ValidKey = "ef_chave-de-teste-do-bemaqui-0000000001";
    public const string AuthorizedKey = "35261047225005000186550020000000421234567890";
    public const string HtmlDanfeKey = "35261047225005000186550020000000439999999999";

    public static readonly Guid TenantId = Guid.Parse("f03e0000-0000-7000-8000-000000000001");
    public static readonly Guid DocumentId = Guid.Parse("019fca35-b0f8-7f92-aca0-b13de0f8088d");

    private readonly WebApplication _app;

    public ConcurrentQueue<(string Method, string Path, string? ApiKey, string? Idempotency, string Body)> Requests { get; } = new();

    public int ReadFailuresBeforeSuccess { get; set; }

    private FakeEmissorServer(WebApplication app) => _app = app;

    public static async Task<FakeEmissorServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var server = new FakeEmissorServer(app);
        server.Map(app);
        await app.StartAsync();
        return server;
    }

    public HttpMessageHandler CreateHandler() => _app.GetTestServer().CreateHandler();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static IResult Problem(int status, string title, string? detail = null) =>
        Results.Json(new { title, status, detail, traceId = "0HNN:1" }, statusCode: status, contentType: "application/problem+json");

    private static object Nfe(int situacao, string? chave = AuthorizedKey, object? rejeicao = null) => new
    {
        id = DocumentId,
        situacao,
        chave,
        serie = 2,
        numero = 42,
        protocolo = situacao == 2 ? "135260000000001" : null,
        autorizadaEm = situacao == 2 ? "2026-10-26T12:00:04.199326+00:00" : null,
        rejeicao,
        criadaEm = "2026-10-26T12:00:03.100000+00:00",
    };

    private async Task<string> RecordAsync(HttpContext http)
    {
        http.Request.EnableBuffering();
        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        http.Request.Body.Position = 0;
        Requests.Enqueue((http.Request.Method, http.Request.Path.Value ?? string.Empty, http.Request.Headers["X-Api-Key"].ToString(), http.Request.Headers["Idempotency-Key"].FirstOrDefault(), body));
        return body;
    }

    private static IResult? Auth(HttpContext http, bool admin = false)
    {
        var key = http.Request.Headers["X-Api-Key"].ToString();
        return key switch
        {
            "" or "chave-recusada-000000000000" => Problem(401, "Chave ausente ou inválida"),
            "chave-sem-escopo-0000000000000" => Problem(403, "A chave não tem o escopo"),
            _ when admin && key != "chave-admin-000000000000000" => Problem(403, "A chave não tem o escopo"),
            _ => null,
        };
    }

    private void Map(WebApplication app)
    {
        app.MapGet("/api/v1/me", async (HttpContext http) =>
        {
            await RecordAsync(http);
            if (Auth(http) is { } denied)
            {
                return denied;
            }

            var admin = http.Request.Headers["X-Api-Key"] == "chave-admin-000000000000000";
            return Results.Json(new
            {
                clienteId = Guid.Parse("8a1c0000-0000-7000-8000-000000000001"),
                nome = "BemAqui Empresas — tenant demo",
                tenantId = TenantId,
                escopos = admin ? new[] { "admin", "nfe:emitir", "nfe:eventos", "consultar" } : new[] { "nfe:emitir", "nfe:eventos", "consultar" },
                licencaBloqueada = false,
                limitePorMinuto = 120,
                emitentes = new[]
                {
                    new
                    {
                        cnpj = "47225005000186", razaoSocial = "WSP Equipamentos por Elevação LTDA", uf = "SP", codigoMunicipio = "3508504", ambiente = "homologacao", ativo = true,
                        certificado = new { cnpj = "47225005000186", possui = true, thumbprint = "AB12CD", titular = "WSP", validoDesde = "2026-03-10T00:00:00+00:00", validoAte = "2027-03-10T23:59:59+00:00", diasRestantes = 135, vencido = false },
                    },
                },
            });
        });

        app.MapPost("/api/v1/nfe", async (HttpContext http) =>
        {
            var body = await RecordAsync(http);
            if (Auth(http) is { } denied)
            {
                return denied;
            }

            if (string.IsNullOrWhiteSpace(http.Request.Headers["Idempotency-Key"]))
            {
                return Problem(400, "Idempotency-Key ausente", "O header Idempotency-Key é obrigatório na emissão.");
            }

            var nature = JsonNode.Parse(body)?["naturezaOperacao"]?.GetValue<string>() ?? string.Empty;
            return nature switch
            {
                "AUTORIZAR" => Results.Json(Nfe(2), statusCode: 201),
                "PROCESSAR" => Results.Json(Nfe(1), statusCode: 202),
                "CONTINGENCIA" => Results.Json(Nfe(6), statusCode: 202),
                "REJEITAR" => Results.Json(Nfe(3, rejeicao: new { codigo = 225, motivo = "Rejeicao: Falha no Schema XML", acaoSugerida = "Reporte com o número da nota." }), statusCode: 422),
                "DENEGAR" => Results.Json(Nfe(5, rejeicao: new { codigo = 301, motivo = "Uso Denegado: Irregularidade fiscal do emitente", acaoSugerida = (string?)null }), statusCode: 422),
                "INVALIDO" => Problem(400, "Requisição inválida", "emitente: CNPJ inválido. itens: informe ao menos um item."),
                "CONFLITO" => Problem(409, "Conflito", "A nota já foi cancelada."),
                "LIMITE" => RateLimited(http),
                _ => Results.StatusCode(500),
            };
        });

        app.MapGet("/api/v1/nfe/{chave}", async (string chave, HttpContext http) =>
        {
            await RecordAsync(http);
            if (ReadFailuresBeforeSuccess > 0)
            {
                ReadFailuresBeforeSuccess--;
                return Results.StatusCode(503);
            }

            return Auth(http) ?? (chave == AuthorizedKey ? Results.Json(Nfe(1, chave)) : Problem(404, "Não encontrada"));
        });

        app.MapPost("/api/v1/nfe/{chave}/consultar", async (string chave, HttpContext http) =>
        {
            await RecordAsync(http);
            return Auth(http) ?? (chave == AuthorizedKey ? Results.Json(Nfe(2, chave)) : Problem(404, "Não encontrada"));
        });

        app.MapGet("/api/v1/nfe/{chave}/xml", async (string chave, HttpContext http) =>
        {
            await RecordAsync(http);
            return Auth(http) ?? Results.Bytes(Encoding.UTF8.GetBytes("<nfeProc><protNFe><infProt><chNFe>" + chave + "</chNFe></infProt></protNFe></nfeProc>"), "application/xml");
        });

        app.MapGet("/api/v1/nfe/{chave}/danfe", async (string chave, HttpContext http) =>
        {
            await RecordAsync(http);
            return Auth(http) ?? (chave == HtmlDanfeKey ? (IResult)Results.Content("<html><script>alert(1)</script></html>", "text/html") : Results.Bytes("%PDF-1.7"u8.ToArray(), "application/pdf"));
        });

        app.MapPost("/api/v1/nfe/{chave}/cancelamento", async (string chave, HttpContext http) =>
        {
            await RecordAsync(http);
            return Auth(http) ?? Results.Json(new { id = Guid.CreateVersion7(), tipo = 1, aceito = true, protocolo = "135", codigo = 135, motivo = "Evento registrado", acaoSugerida = (string?)null, registradoEm = DateTimeOffset.UtcNow });
        });

        app.MapPost("/api/v1/nfe/inutilizacao", async (HttpContext http) =>
        {
            await RecordAsync(http);
            return Auth(http) ?? Results.Json(new { aceito = true, serie = 2, numeroInicial = 10, numeroFinal = 12, protocolo = "102", codigo = 102, motivo = "Inutilização homologada", acaoSugerida = (string?)null }, statusCode: 200);
        });

        app.MapGet("/api/v1/sefaz/status", async (HttpContext http) =>
        {
            await RecordAsync(http);
            return Auth(http) ?? Results.Json(new { online = false, uf = "SP", ambiente = "homologacao", mensagem = "Serviço paralisado", tempoMedioResposta = (int?)null, contingenciaSugerida = "SVC-AN", consultadoEm = DateTimeOffset.UtcNow });
        });

        app.MapPost("/api/v1/emitentes/{cnpj}/certificado", async (string cnpj, HttpContext http) =>
        {
            Requests.Enqueue((http.Request.Method, http.Request.Path.Value ?? string.Empty, http.Request.Headers["X-Api-Key"].ToString(), null, http.Request.ContentType ?? string.Empty));
            var form = await http.Request.ReadFormAsync();
            return form.Files["arquivo"] is { Length: > 0 } && form["senha"] == "senha-do-pfx" && !http.Request.QueryString.HasValue
                ? Results.Json(new { id = Guid.CreateVersion7(), thumbprint = "AB12CD", notAfter = "2027-03-10T23:59:59+00:00" })
                : Problem(400, "Certificado inválido");
        });
    }

    private static IResult RateLimited(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "7";
        return Problem(429, "Muitas requisições");
    }

    /// <summary>Assinatura como o Emissor manda, calculada aqui sem o validador do BemAqui (formato do guia: <c>t=…,v1=…</c>).</summary>
    public static string Sign(string secret, string body, long timestamp)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hex = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body)));
        return "t=" + timestamp + ",v1=" + hex;
    }
}

/// <summary><see cref="IHttpClientFactory"/> que entrega clientes do <see cref="FakeEmissorServer"/>.</summary>
public sealed class FakeEmissorHttpClients(FakeEmissorServer server) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(server.CreateHandler(), disposeHandler: false);
}

public static class JsonText
{
    public static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;
}
