using System.Text.Json.Serialization;

namespace BemAqui.Finance.Contracts;

/// <summary>Tipo da conta (CAD-05a): o da raiz vale para todos os descendentes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountType>))]
public enum AccountType
{
    Revenue,
    Expense,
    Asset,
    Liability,
}

/// <summary>Grupo da DRE (lista fixa do sistema, usada pela FIN-08).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DreGroup>))]
public enum DreGroup
{
    GrossRevenue,
    Deductions,
    Costs,
    OperatingExpenses,
    FinancialResult,
    OtherResults,
    OutsideDre,
}

/// <summary>Conta para seleção nos lançamentos (CA 17).</summary>
public sealed record AccountOption(Guid Id, string Code, string Name, AccountType Type, DreGroup DreGroup, bool AcceptsEntries, bool IsActive);

/// <summary>
/// Plano de contas para os módulos que lançam valores (CA 17): a lista só tem contas analíticas, ativas e não excluídas; a
/// resolução por id devolve qualquer conta (documento antigo continua mostrando o nome).
/// </summary>
public interface IChartOfAccounts
{
    Task<IReadOnlyList<AccountOption>> PostableAsync(AccountType? type = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>Quem lança em contas (FIN-01, FIN-02, recorrentes, CA 15): com lançamento, a conta não é excluída nem vira agrupadora.</summary>
public interface IAccountUsageProvider
{
    Task<bool> HasEntriesAsync(Guid accountId, CancellationToken cancellationToken = default);
}

/// <summary>Quem usa um centro de custo (CA 16): com uso, o centro não é excluído, só inativado.</summary>
public interface ICostCenterUsageProvider
{
    Task<bool> HasEntriesAsync(Guid costCenterId, CancellationToken cancellationToken = default);
}
