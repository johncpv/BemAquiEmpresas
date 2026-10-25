namespace BemAqui.Dashboard.Application;

/// <summary>
/// Card do painel e as permissões dos donos do dado: basta uma, com o módulo dela liberado; sem nenhuma, o card não é montado
/// (CA 4 e 5).
/// </summary>
public sealed record CardRule(string Code, IReadOnlyList<string> Permissions)
{
    public static string ModuleOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];
}

/// <summary>
/// Cards do painel do proprietário (DSH-01a CA 5), na ordem da tela a 400 px: Saldo em contas (linha de KPIs), Caixa →
/// Aguardando sua decisão → Faturamento → Vencido → Comercial. A DSH-01b acrescenta os de margem, carteira e produção.
/// </summary>
public static class DashboardCards
{
    public const string Module = "dashboard";
    public const string ViewPermission = "dashboard.view";

    public const string Balances = "balances";
    public const string CashForecast = "cash-forecast";
    public const string Revenue = "revenue";
    public const string Overdue = "overdue";
    public const string Commercial = "commercial";
    public const string Decisions = "decisions";

    /// <summary>Semanas do "Caixa projetado" (protótipo: "próximas 12 semanas").</summary>
    public const int ForecastWeeks = 12;

    /// <summary>
    /// Quem decide algo da pauta de nível 1 (OP, aditivo, qualidade, pedido de compra, ajuste de ponto): o card "Aguardando sua
    /// decisão" aparece com uma delas, e cada item continua filtrado pela permissão própria (DSH-04).
    /// </summary>
    public static readonly string[] DecisionPermissions = ["finance.approve", "projects.approve", "production.approve_quality", "purchasing.approve", "hr.approve"];

    public static IReadOnlyList<CardRule> All { get; } =
    [
        new(Balances, ["finance.view"]),
        new(CashForecast, ["finance.view"]),
        new(Decisions, DecisionPermissions),
        new(Revenue, ["finance.view"]),
        new(Overdue, ["finance.view"]),
        new(Commercial, ["quotes.view"]),
    ];

    public static CardRule? Find(string code) => All.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.Ordinal));
}
