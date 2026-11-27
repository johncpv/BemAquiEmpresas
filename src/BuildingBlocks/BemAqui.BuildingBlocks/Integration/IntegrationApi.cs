using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.BuildingBlocks.Integration;

/// <summary>
/// API de integração v1 (INT-03, ADR 0053): o grupo <c>/api/v1/integration</c> de cada módulo dono. Toda rota exige o principal de aplicativo
/// (esquema <c>App</c>, módulo <c>conecta</c>), a política única <c>app-api</c> e responde sem cache (dado pessoal). Cada rota soma o
/// <c>RequirePermission</c> do escopo; o grupo não acessa tabela nenhuma: chama o serviço ou o contrato do módulo dono.
/// </summary>
public static class IntegrationApi
{
    public const string Tag = "Integração (aplicativos)";
    public const string DocumentName = "integration-v1";
    public const string NotFoundMessage = "Não encontrado.";

    /// <summary>Grupo <c>/integration</c> de um módulo: aplicativo, <c>app-api</c> e <c>Cache-Control: no-store</c>.</summary>
    public static RouteGroupBuilder MapIntegrationGroup(this IEndpointRouteBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/integration").WithTags(Tag).RequireRateLimiting(ConnectedAppEndpoints.RateLimitPolicy);
        group.RequireConnectedApp();
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        return group;
    }

    /// <summary>404 igual para inexistente, de outro tenant ou de outro cliente (CA 20): nunca diz se o recurso existe.</summary>
    public static IResult NotFound() => SharedKernel.Error.NotFound(SharedKernel.Error.Codes.NotFound, NotFoundMessage).ToProblem();

    /// <summary>429 <c>rate_limited</c> com <c>Retry-After</c> (CA 9 e 21).</summary>
    public static IResult RateLimited(TimeSpan retryAfter, string message = "Muitas consultas. Tente de novo em instantes.") => new RetryAfterResult(retryAfter,
        Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: message, extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" }));

    private sealed class RetryAfterResult(TimeSpan wait, IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            httpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return inner.ExecuteAsync(httpContext);
        }
    }
}

/// <summary>
/// Códigos estáveis da API de integração (ADR 0053 §7, INT-03 CA 22). Vão no documento <c>integration-v1</c> (<c>x-error-codes</c>): remover ou
/// renomear um código quebra o teste de contrato.
/// </summary>
public static class IntegrationErrorCodes
{
    public static readonly string[] All =
    [
        "invalid_token", "insufficient_scope", "app_grantor_inactive", "module_not_released", "validation", "not_found", "rate_limited",
        "idempotency_key_required", "idempotency_key_reused", "idempotency_in_progress", "external_id_conflict", "insufficient_stock", "invalid_order_transition",
        "approval_required", "party_exists", "field_change_requires_backoffice", "order_cancel_requires_backoffice", "customer_not_billable", "app_quantity_limit",
        "too_many_open_orders", "price_changed", "concurrency_conflict", "bank_slip_method_missing",
    ];
}

/// <summary>Máscaras e normalizações da API de integração (LGPD, INT-03 CA 7 e 8). Puras: testadas por unidade.</summary>
public static class IntegrationData
{
    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase) { "da", "de", "do", "das", "dos", "e", "d'" };

    /// <summary>CPF <c>***.456.789-**</c>; CNPJ <c>**.345.678/0001-**</c>; outro formato, só os 4 últimos.</summary>
    public static string? MaskDocument(string? document)
    {
        var digits = new string((document ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return digits.Length switch
        {
            0 => null,
            11 => $"***.{digits[3..6]}.{digits[6..9]}-**",
            14 => $"**.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-**",
            _ => new string('*', Math.Max(0, digits.Length - 4)) + digits[Math.Max(0, digits.Length - 4)..],
        };
    }

    /// <summary>Primeiro nome e as iniciais dos outros ("Maria da Silva Oliveira" → "Maria S. O."), sem as partículas.</summary>
    public static string AbbreviateName(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var initials = parts.Skip(1).Where(p => !Particles.Contains(p)).Select(p => char.ToUpper(p[0], CultureInfo.GetCultureInfo("pt-BR")) + ".");
        return string.Join(' ', new[] { parts[0] }.Concat(initials));
    }

    /// <summary>
    /// Telefone E.164 do Brasil (<c>+5511999998888</c>) para os 10 ou 11 dígitos com DDD do cadastro; aceita também sem o <c>+55</c>. Fora disso, null.
    /// </summary>
    public static string? NationalPhone(string? phone)
    {
        var text = (phone ?? string.Empty).Trim();
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (text.StartsWith('+') && !digits.StartsWith("55", StringComparison.Ordinal))
        {
            return null; // E.164 de outro país: não é telefone do cadastro.
        }

        if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        return digits.Length is 10 or 11 ? digits : null;
    }

    /// <summary>Hash do termo de busca para a auditoria (nunca o termo): SHA-256 de tenant, tipo e termo normalizado, 32 hexa.</summary>
    public static string TermHash(Guid tenantId, string kind, string term) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tenantId.ToString("N") + ":" + kind + ":" + term)))[..32];

    /// <summary>Data de negócio em ISO 8601 (<c>yyyy-MM-dd</c>).</summary>
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Contador <c>integration_rate_limited{app}</c> (INT-03 CA 21): sobe a cada 429 de aplicativo, para o painel.</summary>
public static class IntegrationMetrics
{
    public const string MeterName = "BemAqui.Integration";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Limited = Meter.CreateCounter<long>("integration_rate_limited", description: "Chamadas de aplicativo recusadas pelo limite de uso");

    public static void RateLimited(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var app = AppClaims.IsApp(http.User) ? http.User.FindFirst(AppClaims.App)?.Value ?? "-" : "-";
        Limited.Add(1, new KeyValuePair<string, object?>("app", app));
    }
}
