namespace BemAqui.BuildingBlocks.Lookups;

/// <summary>Seção <c>Lookups</c>: consultas públicas de CNPJ e CEP (CAD-01b). A URL base é fixa da plataforma, nunca do tenant.</summary>
public sealed class LookupOptions
{
    public const string Section = "Lookups";

    public Uri BrasilApiBaseUrl { get; set; } = new("https://brasilapi.com.br/");

    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Resposta maior que isso é descartada (CA 13).</summary>
    public int MaxResponseBytes { get; set; } = 64 * 1024;

    /// <summary>Teto de consultas por instância (o IP da VPS é compartilhado e a BrasilAPI limita por IP).</summary>
    public int GlobalPerMinute { get; set; } = 120;

    /// <summary>Falhas seguidas que abrem o circuito, e por quanto tempo ele fica aberto.</summary>
    public int CircuitFailures { get; set; } = 5;

    public int CircuitOpenSeconds { get; set; } = 60;

    /// <summary>Por usuário + tenant (CA 12).</summary>
    public int PerUserPerMinute { get; set; } = 30;

    public int CacheDays { get; set; } = 30;
}

/// <summary>Resultado bruto de uma consulta: encontrado (com o JSON), não encontrado ou indisponível.</summary>
public enum LookupStatus
{
    Found,
    NotFound,
    Unavailable,
}
