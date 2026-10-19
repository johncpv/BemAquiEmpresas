using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>
/// Criação e edição de conta (CA 1 a 3). No tipo Caixa, banco, agência e conta são ignorados. <c>IsDefault</c> quer dizer
/// "tornar padrão": falso não desmarca, porque a padrão só muda quando outra conta vira padrão (CA 6).
/// </summary>
public sealed record BankAccountInput(
    string Name,
    BankAccountKind Kind,
    string? BankCode,
    string? Agency,
    string? Account,
    decimal? OpeningBalance,
    DateOnly? OpeningDate,
    bool IsDefault = false);

/// <summary>
/// Conta para a lista e o detalhe. Agência e conta vêm mascaradas do servidor para quem não tem <c>finance.edit</c> (CA 10);
/// <c>Masked</c> diz à tela que os dados não são editáveis.
/// </summary>
public sealed record BankAccountDetail(
    Guid Id,
    string Name,
    BankAccountKind Kind,
    string? BankCode,
    string? BankName,
    string? Agency,
    string? Account,
    decimal? OpeningBalance,
    DateOnly? OpeningDate,
    bool IsActive,
    bool IsDefault,
    string? ImportSource,
    bool Masked,
    DateTimeOffset CreatedAt);

public sealed record BankAccountListQuery(string? Q, bool IncludeInactive);

public sealed record PaymentTermInput(string Name, IReadOnlyList<Installment> Installments);

public sealed record PaymentTermDetail(Guid Id, string Name, IReadOnlyList<Installment> Installments, bool IsActive, string? SeedKey, string? ImportSource, DateTimeOffset CreatedAt);

/// <summary>Prévia da divisão (CA 13) com as parcelas da tela, antes de salvar.</summary>
public sealed record SplitPreviewRequest(IReadOnlyList<Installment> Installments, decimal Amount, DateOnly BaseDate);
