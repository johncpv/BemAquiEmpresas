namespace BemAqui.Dashboard.Application;

/// <summary>Card do painel e o módulo dono do dado: sem o módulo liberado e a permissão, o card não é montado (CA 4 e 5).</summary>
public sealed record CardRule(string Code, string? OwnerModule, string? OwnerPermission);

/// <summary>
/// Cards do painel do proprietário (DSH-01a CA 5), na ordem da tela a 400 px: Caixa → Aguardando sua decisão → Faturamento →
/// Vencido → Comercial, com o Saldo em contas na linha de KPIs. A DSH-01b acrescenta os de margem, carteira e produção.
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

    public static IReadOnlyList<CardRule> All { get; } =
    [
        new(Balances, "finance", "finance.view"),
        new(CashForecast, "finance", "finance.view"),
        new(Decisions, null, null), // cada item da pauta já vem filtrado pela permissão própria (DSH-04)
        new(Revenue, "finance", "finance.view"),
        new(Overdue, "finance", "finance.view"),
        new(Commercial, "quotes", "quotes.view"),
    ];

    public static CardRule? Find(string code) => All.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.Ordinal));
}
