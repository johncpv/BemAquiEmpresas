namespace BemAqui.Finance.Application.Inter;

/// <summary>
/// Seção <c>Inter</c> da configuração. Os nomes dos escopos são os da documentação pública do Inter e vão ser conferidos no sandbox
/// (runbook <c>docs/operacao/banco-inter.md</c>); por isso ficam na configuração, com estes valores padrão. As URLs base NÃO ficam
/// aqui: são fixas no código por ambiente (CA 9, contra SSRF).
/// </summary>
public sealed class InterOptions
{
    public const string Section = "Inter";

    /// <summary>Escopos necessários (CA 7): cobrança leitura e escrita, extrato leitura e webhook.</summary>
    public string[] RequiredScopes { get; set; } = ["boleto-cobranca.read", "boleto-cobranca.write", "extrato.read", "webhook.read", "webhook.write"];

    /// <summary>Famílias permitidas (D13): qualquer escopo concedido fora delas recusa a credencial (CA 8).</summary>
    public string[] AllowedScopePrefixes { get; set; } = ["boleto-cobranca.", "extrato.", "webhook."];

    /// <summary>
    /// Escopos de pagamento e de envio de Pix pedidos junto, só no token de prova (sem cache): se o Inter conceder algum, a credencial
    /// pode pagar e é recusada (CA 8). O token de uso pede só os necessários.
    /// </summary>
    public string[] ProbeScopes { get; set; } = ["pagamento-pix.write", "pagamento-boleto.write", "pagamento-lote.write", "pagamento-darf.write", "pix.write", "cob.write", "cobv.write"];

    /// <summary>Tempo máximo de cada chamada ao Inter.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Vida das conexões do handler mTLS (<c>PooledConnectionLifetime</c>).</summary>
    public TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Espera antes de descartar o handler antigo, para as chamadas em curso terminarem (ADR 0032 §2).</summary>
    public TimeSpan HandlerDisposeDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Consultas seguidas da emissão dentro do mesmo job (BNK-01b, a emissão no Inter é assíncrona).</summary>
    public int BillingPollsPerRun { get; set; } = 3;

    /// <summary>Espera entre as consultas do mesmo job.</summary>
    public TimeSpan BillingPollDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Sem confirmação no job, nova consulta depois deste tempo (padrão 6: "Demorando").</summary>
    public TimeSpan BillingRescheduleDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Rodadas de consulta até a emissão virar falha ("O Inter não confirmou a emissão"): 20 × 30 s ≈ 10 min.</summary>
    public int BillingMaxPolls { get; set; } = 20;

    /// <summary>
    /// Dias depois do vencimento para o Inter baixar o boleto não pago (<c>numDiasAgenda</c>, 0 a 60): o padrão do Inter, conferido no
    /// sandbox (runbook). Depois disso, a reconsulta vê "Expirado" (CA 18).
    /// </summary>
    public int BillingExpiryDays { get; set; } = 60;
}

/// <summary>Avaliação dos escopos concedidos (CA 7 e 8).</summary>
public sealed record ScopeEvaluation(IReadOnlyList<string> Granted, IReadOnlyList<string> Missing, IReadOnlyList<string> NotAllowed)
{
    public bool CanPay => NotAllowed.Count > 0;
}

public static class InterScopes
{
    public const string PaymentScopesMessage = "Esta credencial pode fazer pagamentos. Gere no Inter uma credencial só com cobrança, extrato e webhook.";

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["boleto-cobranca.read"] = "Cobrança (consultar boletos)",
        ["boleto-cobranca.write"] = "Cobrança (emitir e cancelar boletos)",
        ["extrato.read"] = "Extrato (consultar saldo e extrato)",
        ["webhook.read"] = "Webhook (consultar)",
        ["webhook.write"] = "Webhook (cadastrar)",
    };

    public static string Label(string scope) => Labels.GetValueOrDefault(scope, scope);

    /// <summary>Lê o <c>scope</c> do token (separado por espaço) sem repetição.</summary>
    public static IReadOnlyList<string> Parse(string? scope) =>
        (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    public static ScopeEvaluation Evaluate(IReadOnlyList<string> granted, InterOptions options)
    {
        ArgumentNullException.ThrowIfNull(granted);
        ArgumentNullException.ThrowIfNull(options);
        var missing = options.RequiredScopes.Where(r => !granted.Contains(r, StringComparer.Ordinal)).ToList();
        var notAllowed = granted.Where(g => !options.AllowedScopePrefixes.Any(p => g.StartsWith(p, StringComparison.Ordinal))).ToList();
        return new ScopeEvaluation(granted, missing, notAllowed);
    }

    /// <summary>Texto do que falta e como liberar (CA 7).</summary>
    public static string MissingMessage(IReadOnlyList<string> missing) =>
        "Faltam escopos na credencial: " + string.Join(", ", missing.Select(Label)) + ".";

    public const string MissingAction = "Libere esses escopos na integração do Internet Banking do Inter (Soluções para sua empresa → Nova integração) e teste de novo.";
}
