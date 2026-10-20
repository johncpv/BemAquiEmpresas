namespace BemAqui.Finance.Application;

/// <summary>Filtros do fluxo de caixa (FIN-05): visão daily/weekly/monthly, horizonte em dias (até 365), conta e início.</summary>
public sealed record CashFlowQuery(string? View, int? Days, Guid? BankAccountId, DateOnly? From);

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
    CashFlowUndated UndatedReceivables);

/// <summary>Item que compõe um dia (CA 10). <c>Amount</c> com sinal: entrada positiva, saída negativa.</summary>
public sealed record CashFlowItem(string Kind, Guid Id, string? Number, string Description, string? PartyName, decimal Amount, DateOnly? DueDate, bool Overdue, bool Estimate, string? Link);

public sealed record CashFlowDay(DateOnly Date, bool Projected, IReadOnlyList<CashFlowItem> Items);
