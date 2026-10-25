using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

/// <summary>Período do DRE (CA 2): meses inteiros; padrão os 12 meses até o mês corrente; até 24 meses.</summary>
public sealed record DreQuery(DateOnly? From, DateOnly? To);

/// <summary>Coluna de mês: "Provisório" (receita pelos títulos, CA 3) e "Mudou" desde a última consulta (CA 9).</summary>
public sealed record DreMonth(DateOnly Month, bool IsProvisional, bool Changed);

/// <summary>Valor em conta "A classificar" (1.9 e 3.9) no período (CA 10, R-PRP-5).</summary>
public sealed record DreClassifyNotice(Guid AccountId, string Code, string Name, decimal Total);

/// <summary>"Mudou desde a sua última consulta em {data}: {n} documentos em {meses}." (CA 9).</summary>
public sealed record DreChangeSummary(DateTimeOffset Since, int Count, IReadOnlyList<DateOnly> Months);

/// <summary>
/// DRE gerencial mensal (FIN-08): linhas já na ordem e com o sinal no rótulo (<see cref="DreRow"/>), meses com as marcas, avisos
/// acionáveis (a classificar, mudou desde, parâmetros a confirmar com a contadora).
/// </summary>
public sealed record DreReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DreMonth> Months,
    IReadOnlyList<DreRow> Rows,
    bool IsProvisional,
    bool HasEntries,
    IReadOnlyList<DreClassifyNotice> ToClassify,
    DreChangeSummary? Changes,
    DateTimeOffset? LastViewedAt,
    int PendingParameters);

/// <summary>
/// Célula do drill-down (CA 13): conta (<c>AccountId</c>) ou grupo (<c>Group</c>) × mês (<c>Month</c>; nulo = o período todo, a
/// coluna Total). <c>Sort</c> só pela allowlist: <c>date</c>, <c>-date</c>, <c>amount</c>, <c>-amount</c>, <c>number</c>.
/// </summary>
public sealed record DreCellQuery(Guid? AccountId, string? Group, DateOnly? Month, DateOnly? From, DateOnly? To, string? Sort);

/// <summary>
/// Documento de uma célula (CA 13 e 14). <c>Kind</c>: <c>Receivable</c>, <c>DownPayment</c> (sinal reconhecido), <c>Payable</c>,
/// <c>BankEntry</c>, <c>ReceivableSettlement</c>, <c>PayableSettlement</c> ou <c>Payroll</c> (linha agregada "Folha de {mês}" sem
/// <c>hr.view_sensitive</c>: sem número, favorecido nem valor por funcionário). O valor vem como a célula mostra.
/// </summary>
public sealed record DreCellItem(string Kind, Guid? Id, string? Number, string? Counterparty, string? Description, DateOnly Date, decimal Amount, string? Link, string? Note, int? Count, bool Protected);

/// <summary>Drill-down de uma célula: a soma dos itens é igual ao valor da célula (CA 13).</summary>
public sealed record DreCell(string Label, DateOnly From, DateOnly To, decimal Total, IReadOnlyList<DreCellItem> Items, bool Truncated, bool HasProtected);

/// <summary>
/// Documento que mudou desde a última consulta (CA 9): <c>Effect</c> "Lançado", "Cancelado" ou "Estornado" e o efeito com sinal no
/// resultado. Folha sem <c>hr.view_sensitive</c> vem sem número e sem valor.
/// </summary>
public sealed record DreChange(string Kind, Guid Id, string? Number, DateOnly Date, DateOnly Month, string Effect, decimal? Amount, DateTimeOffset At, string? Link);

public sealed record DreChanges(DateTimeOffset? Since, int Count, IReadOnlyList<DreChange> Items);
