namespace BemAqui.Dashboard.Application;

// DTOs dos cards do painel do proprietário (DSH-01a CA 5): só o que a permissão do card deixa ver. Nenhum card desta story
// leva custo, orçado ou margem (CA 15, ADR 0028 §2); o teste de arquitetura confere os nomes das propriedades.

public sealed record BalanceAccountItem(Guid Id, string Name, decimal Balance);

/// <summary>Saldo em contas (FIN-05): consolidado e por conta; <see cref="OpeningMissing"/> se alguma conta não tem saldo inicial.</summary>
public sealed record BalancesCard(DateOnly Date, decimal Consolidated, IReadOnlyList<BalanceAccountItem> Accounts, bool OpeningMissing, DateTimeOffset UpdatedAt);

public sealed record CashWeekItem(DateOnly WeekEnd, decimal BaseBalance, decimal AdjustedBalance);

public sealed record CashHorizonItem(int Days, decimal BaseBalance, decimal AdjustedBalance);

public sealed record CashPointItem(DateOnly Date, decimal Balance);

/// <summary>
/// Caixa projetado das próximas 12 semanas (FIN-07): base × ajustado, saldo em 30/60/90 dias, menor saldo de cada cenário e a
/// diferença causada pelos atrasos. <see cref="Available"/> falso enquanto a projeção ajustada não está ligada.
/// </summary>
public sealed record CashForecastCard(
    bool Available, decimal? CurrentBalance, IReadOnlyList<CashWeekItem> Weeks, IReadOnlyList<CashHorizonItem> Horizons, CashPointItem? LowestBase,
    CashPointItem? LowestAdjusted, decimal? Difference, CashPointItem? FirstAdjustedNegative, DateTimeOffset UpdatedAt);

/// <summary>
/// Faturamento × meta (FIN-08 e CA 9): receita do mês e do ano, metas, % atingido e dias úteis que faltam no mês.
/// <see cref="Available"/> falso enquanto o faturamento não está ligado (as metas aparecem mesmo assim).
/// </summary>
public sealed record RevenueCard(
    int Year, int Month, bool Available, decimal? MonthAmount, decimal? YearAmount, decimal? MonthTarget, decimal? YearTarget, decimal? MonthPct,
    decimal? YearPct, bool Provisional, int WorkingDaysLeft, bool CanEditTargets, DateTimeOffset UpdatedAt);

public sealed record OverdueBucketItem(string Label, int FromDays, int? ToDays, decimal Total, int Count);

public sealed record OverdueCustomerItem(Guid PartyId, string PartyName, decimal Total, int MaxDaysLate, decimal? AvgDelayDays);

public sealed record OverdueLargestItem(Guid ReceivableId, string Number, Guid PartyId, string PartyName, decimal Balance, int DaysLate, decimal? CustomerAvgDelayDays);

/// <summary>A receber vencido (FIN-06a): total, aging, os 5 maiores clientes, o maior atraso e o maior título vencido.</summary>
public sealed record OverdueCard(
    DateOnly Today, decimal Total, int Count, int Customers, IReadOnlyList<OverdueBucketItem> Buckets, IReadOnlyList<OverdueCustomerItem> TopCustomers,
    int MaxDaysLate, OverdueLargestItem? Largest, DateTimeOffset UpdatedAt);

public sealed record FunnelStageItem(string Status, string Situation, int Count, decimal Amount);

/// <summary>Comercial (COM-08): em aberto, conversão dos últimos 90 dias, vencendo em 7 dias e o funil, só com preço.</summary>
public sealed record CommercialCard(
    DateOnly From, DateOnly To, int OpenCount, decimal OpenAmount, decimal? ConversionByCount, decimal? ConversionByAmount, int Decided, int ExpiringCount,
    decimal ExpiringAmount, IReadOnlyList<FunnelStageItem> Stages, DateTimeOffset UpdatedAt);

/// <summary>"Aguardando sua decisão" (DSH-04): o nível 1 da pauta do usuário; <see cref="Available"/> falso enquanto não está ligada.</summary>
public sealed record DecisionsCard(bool Available, int Total, decimal TotalAmount, DateOnly? FirstDue, IReadOnlyList<DecisionItem> Items, DateTimeOffset UpdatedAt);

/// <summary>Alerta da faixa "Atenção" (CA 10): tom (<c>crit</c> ou <c>warn</c>), o que houve, o detalhe e o link para a ação.</summary>
public sealed record AttentionAlert(string Code, string Tone, string Title, string Detail, string Url);

/// <summary>
/// Cabeçalho do painel (CA 2 e 10): saudação pelo horário, primeiro nome, hoje, frase-resumo dos cards visíveis, faixa
/// "Atenção" e a lista dos cards que o usuário vê, na ordem da tela. Sem card visível, a lista vem vazia (CA 4).
/// </summary>
public sealed record DashboardOverview(string Greeting, string FirstName, DateOnly Today, string? Summary, IReadOnlyList<AttentionAlert> Attention, IReadOnlyList<string> Cards, DateTimeOffset GeneratedAt);
