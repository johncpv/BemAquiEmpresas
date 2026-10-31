using System.Net;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application;

/// <summary>Seção <c>Fiscal:Emissor</c> (FIS-01 CA 7, ADR 0027 §2 e §4, adendo de 27/10/2026: API pública do Emissor).</summary>
public sealed class FiscalIntegrationOptions
{
    public const string Section = "Fiscal:Emissor";

    /// <summary>
    /// Endereço do Emissor, fixo pela configuração do sistema (o tenant não escolhe): a API pública, em HTTPS, a mesma para homologação e
    /// produção (o ambiente vem do cadastro do emitente no Emissor, não da URL nem da chave).
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.fiscal.bemaquiapp.com.br";

    /// <summary>
    /// Hosts aceitos no <see cref="BaseUrl"/> (contra SSRF e configuração errada): só a API pública do Emissor. Nunca IP, <c>localhost</c>,
    /// endereço de metadados ou serviço interno; sempre HTTPS na porta padrão.
    /// </summary>
    public string[] AllowedHosts { get; set; } = ["api.fiscal.bemaquiapp.com.br"];

    /// <summary>Tempo limite de cada chamada (CA 4).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Espera máxima aceita no <c>Retry-After</c> de uma leitura antes de desistir (429, CA 4).</summary>
    public TimeSpan MaxReadRetryAfter { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Chamadas por segundo ao Emissor, somando todos os tenants e jobs deste processo. O Emissor limita a 10 por segundo por IP; a
    /// homologação e a produção do BemAqui saem do mesmo IP da VPS, então cada uma fica com 4 (8 no total, com folga).
    /// </summary>
    public int MaxRequestsPerSecond { get; set; } = 4;

    /// <summary>Chamadas esperando a vez; além disso, a chamada desiste com <c>fiscal_rate_limited</c> sem ir ao Emissor.</summary>
    public int MaxQueuedRequests { get; set; } = 200;

    /// <summary>Teto da pausa geral depois de um 429 (o <c>Retry-After</c> maior que isso é limitado a ele).</summary>
    public TimeSpan MaxRateLimitPause { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Notas pendentes há mais que isso entram na conferência (CA 14).</summary>
    public TimeSpan PendingAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Contingência há mais que isso vira alerta (CA 12).</summary>
    public TimeSpan ContingencyAlertAfter { get; set; } = TimeSpan.FromHours(2);

    /// <summary>"Testar conexão" por minuto e usuário (política <c>fiscal-integration</c>, CA 18).</summary>
    public int TestsPerMinute { get; set; } = 5;
}

/// <summary>Conferência do endereço do Emissor (CA 7): HTTPS, porta padrão e host da lista; o endereço vem só da configuração.</summary>
public static class FiscalBaseUrl
{
    public const string Message = "O endereço do Emissor Fiscal é definido pelo sistema (https://api.fiscal.bemaquiapp.com.br) e não pode ser trocado aqui.";

    /// <summary>O endereço da configuração, conferido; configuração fora da regra não é usada (nenhuma chamada sai).</summary>
    public static Result<Uri> Configured(FiscalIntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Validate(options.BaseUrl, options);
    }

    /// <summary>
    /// Endereço pedido na tela: vazio ou igual ao da configuração (o campo é só de leitura); qualquer outro é recusado. Devolve o da
    /// configuração.
    /// </summary>
    public static Result<Uri> Resolve(string? requested, FiscalIntegrationOptions options)
    {
        var configured = Configured(options);
        if (configured.IsFailure || string.IsNullOrWhiteSpace(requested))
        {
            return configured;
        }

        var asked = Validate(requested, options);
        return asked.IsSuccess && asked.Value == configured.Value ? configured : Invalid();
    }

    public static Result<Uri> Validate(string? value, FiscalIntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is 0 or > Domain.FiscalIntegrationSettings.BaseUrlMaxLength
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/"
            || IPAddress.TryParse(uri.Host.Trim('[', ']'), out _)
            || uri.IsLoopback
            || !options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return Invalid();
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static Error Invalid() =>
        Error.Validation(Error.Codes.Validation, Message, new Dictionary<string, string[]> { ["baseUrl"] = [Message] });
}
