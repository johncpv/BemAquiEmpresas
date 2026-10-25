namespace BemAqui.Finance.Application;

/// <summary>Filtros do fluxo de caixa (FIN-05): visão daily/weekly/monthly, horizonte em dias (até 365), conta e início.</summary>
public sealed record CashFlowQuery(string? View, int? Days, Guid? BankAccountId, DateOnly? From, string? Scenario = null);

/// <summary>
/// Um período da série: dias antes de hoje são realizado (transações), hoje em diante é projetado. <c>Overdue*</c> são os
/// vencidos em aberto que entram no dia de hoje (CA 3).
/// </summary>
public sealed record CashFlowPeriod(DateOnly Start, DateOnly End, decimal Inflow, decimal Outflow, decimal Balance, bool Projected, bool ContainsToday, decimal OverdueInflow, decimal OverdueOutflow);

public sealed record CashFlowPoint(DateOnly Date, decimal Balance);

public sealed record CashFlowUndated(int Count, decimal Total);

public sealed record CashFlowResult(
    DateOnly Today,
    DateOnly From,
    DateOnly To,
    string View,
    Guid? BankAccountId,
    decimal CurrentBalance,
    bool Consolidated,
    IReadOnlyList<CashFlowPeriod> Periods,
    CashFlowPoint? Lowest,
    CashFlowPoint? FirstNegative,
    CashFlowUndated UndatedReceivables,
    CashFlowAdjusted? Adjusted = null);

/// <summary>Recebíveis do ajustado que saíram do período (FIN-07 CA 5), pelo saldo.</summary>
public sealed record CashFlowShifted(int Count, decimal Total);

/// <summary>Cliente acima do limite de inadimplência (FIN-07 CA 3): o que provavelmente não entra no período.</summary>
public sealed record CashFlowRiskCustomer(Guid PartyId, string? Name, decimal DefaultPct, decimal AtRisk, int Count);

/// <summary>
/// Cenário ajustado (FIN-07): os mesmos períodos da base, com cada recebível deslocado pelo atraso médio e ponderado pela
/// inadimplência acima do limite. <c>LowestDifference</c> = menor saldo base − menor saldo ajustado. Só no consolidado.
/// </summary>
public sealed record CashFlowAdjusted(
    IReadOnlyList<CashFlowPeriod> Periods,
    CashFlowPoint? Lowest,
    CashFlowPoint? FirstNegative,
    decimal? LowestDifference,
    CashFlowShifted ShiftedBeyond,
    decimal AtRiskTotal,
    IReadOnlyList<CashFlowRiskCustomer> AtRisk,
    bool TenantHasHistory,
    decimal TenantAvgDelayDays,
    decimal ThresholdPct);

/// <summary>Item que compõe um dia (CA 10). <c>Amount</c> com sinal: entrada positiva, saída negativa.</summary>
/// <remarks>
/// No ajustado (FIN-07 CA 9), o recebível traz a <c>AdjustedDate</c>, o <c>Reason</c> ("Atraso médio de 10 dias de {cliente}"
/// ou "Média da empresa: 6 dias", e a ponderação) e, quando só saiu do dia por deslocamento, <c>ShiftedOut</c> (fora do total).
/// </remarks>
public sealed record CashFlowItem(
    string Kind, Guid Id, string? Number, string Description, string? PartyName, decimal Amount, DateOnly? DueDate, bool Overdue, bool Estimate, string? Link,
    DateOnly? AdjustedDate = null, string? Reason = null, bool ShiftedOut = false, decimal? OriginalAmount = null);

/// <summary>Dia do fluxo; <c>Total</c> = soma dos itens que entram (sem os deslocados), igual à célula do cenário.</summary>
public sealed record CashFlowDay(DateOnly Date, bool Projected, IReadOnlyList<CashFlowItem> Items, string Scenario = CashFlowService.ScenarioBase, decimal Total = 0);
