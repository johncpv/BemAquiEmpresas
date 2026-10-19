using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Conta bancária ou caixa (<c>finance.bank_accounts</c>, CAD-05b, R-FIN-2). O saldo inicial e a data são opcionais, mas só
/// valem juntos; a conta padrão é única entre as ativas (índice único parcial). Banco, agência e conta só existem no tipo Banco.
/// </summary>
public sealed class BankAccount : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public BankAccountKind Kind { get; set; }

    /// <summary>Código COMPE de 3 dígitos (lista estática do sistema).</summary>
    public string? BankCode { get; set; }

    /// <summary>Agência com dígito, se houver (ex.: <c>1234-5</c>).</summary>
    public string? Agency { get; set; }

    /// <summary>Conta com dígito (ex.: <c>12345678-9</c>).</summary>
    public string? Account { get; set; }

    public decimal? OpeningBalance { get; set; }

    public DateOnly? OpeningDate { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    /// <summary>Origem da importação (ex.: <c>dolibarr:llx_bank_account:3</c>), gravada só pelo <c>IBankAccountImport</c> (MIG-01b).</summary>
    public string? ImportSource { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Banco, agência ou conta mudaram: troca protegida e notificada (CA 9).</summary>
    public bool BankDataDiffers(string? bankCode, string? agency, string? account) =>
        !string.Equals(BankCode, bankCode, StringComparison.Ordinal) || !string.Equals(Agency, agency, StringComparison.Ordinal) || !string.Equals(Account, account, StringComparison.Ordinal);

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
        IsDefault = false;
    }
}

/// <summary>Condição de pagamento (<c>finance.payment_terms</c>, ADR 0016): parcelas em jsonb, somando exatamente 100%.</summary>
public sealed class PaymentTerm : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public List<Installment> Installments { get; set; } = [];

    public bool IsActive { get; set; } = true;

    /// <summary>Chave do seed (À vista, 30, 30/60, 30/60/90); null para as criadas pelo tenant.</summary>
    public string? SeedKey { get; init; }

    public string? ImportSource { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}
