using System.Net;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application;

/// <summary>Seção <c>Fiscal:Emissor</c> (FIS-01 CA 7, ADR 0027 §2 e §4).</summary>
public sealed class FiscalIntegrationOptions
{
    public const string Section = "Fiscal:Emissor";

    /// <summary>
    /// Hosts aceitos na URL base: só o serviço do Emissor na rede dedicada <c>bqe-fiscal</c> (padrão <c>emissor-fiscal</c>). Nunca IP,
    /// <c>localhost</c>, endereço de metadados ou outro serviço (contra SSRF).
    /// </summary>
    public string[] AllowedHosts { get; set; } = ["emissor-fiscal"];

    /// <summary>URL sugerida no formulário (a do guia de integração do Emissor).</summary>
    public string DefaultBaseUrl { get; set; } = "http://emissor-fiscal:8080";

    /// <summary>
    /// Base do endereço que o Emissor chama para avisar (pela rede interna, ex.: <c>http://bqe-homolog-api:8080</c>); a tela mostra
    /// <c>{base}/webhooks/emissor-fiscal/{tenantKey}</c> para o cadastro do webhook no painel do Emissor.
    /// </summary>
    public string WebhookBaseUrl { get; set; } = "http://bqe-homolog-api:8080";

    /// <summary>Tempo limite de cada chamada (CA 4).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Espera máxima aceita no <c>Retry-After</c> de uma leitura antes de desistir (429, CA 4).</summary>
    public TimeSpan MaxReadRetryAfter { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Notas pendentes há mais que isso entram na conferência (CA 14).</summary>
    public TimeSpan PendingAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Contingência há mais que isso vira alerta (CA 12).</summary>
    public TimeSpan ContingencyAlertAfter { get; set; } = TimeSpan.FromHours(2);

    /// <summary>"Testar conexão" por minuto e usuário (política <c>fiscal-integration</c>, CA 18).</summary>
    public int TestsPerMinute { get; set; } = 5;
}

/// <summary>Conferência da URL base contra a lista do ambiente (CA 7).</summary>
public static class FiscalBaseUrl
{
    public const string Message = "Use o endereço interno do Emissor Fiscal (ex.: http://emissor-fiscal:8080).";

    public static Result<Uri> Validate(string? value, FiscalIntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is 0 or > Domain.FiscalIntegrationSettings.BaseUrlMaxLength
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/"
            || IPAddress.TryParse(uri.Host.Trim('[', ']'), out _)
            || uri.IsLoopback
            || !options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return Error.Validation(Error.Codes.Validation, Message, new Dictionary<string, string[]> { ["baseUrl"] = [Message] });
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }
}
