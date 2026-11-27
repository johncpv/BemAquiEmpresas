using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Forma de pagamento da venda (<c>finance.payment_methods</c>, PAG-01): tipo, conta de destino, adquirente (um terceiro com o papel
/// Adquirente), o <c>tPag</c> da NFC-e e o selo de conferência (<c>ConfirmedAt</c> nulo = "A confirmar", só nas que vieram das formas
/// sugeridas). Nenhum campo guarda dado de cartão de cliente (regra 14 do PRD do PDV).
/// </summary>
public sealed class PaymentMethod : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public PaymentMethodType Type { get; set; }

    public Guid DestinationAccountId { get; set; }

    public Guid? AcquirerPartyId { get; set; }

    /// <summary>Código do meio de pagamento da NFC-e (<c>tPag</c>, 2 dígitos).</summary>
    public string NfePaymentCode { get; set; } = string.Empty;

    /// <summary>Dá troco: só Dinheiro (gravado a cada salvamento, a partir do tipo).</summary>
    public bool AllowsChange { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Chave das formas sugeridas (dinheiro, pix, debito...); null para as criadas pelo tenant.</summary>
    public string? SeedKey { get; init; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
        IsActive = false;
    }

    /// <summary>Débito e crédito exigem a adquirente; vale pode ter (vale-refeição na maquininha) ou não.</summary>
    public static bool RequiresAcquirer(PaymentMethodType type) => type is PaymentMethodType.Debit or PaymentMethodType.Credit or PaymentMethodType.CreditInstallments;

    public static bool AcceptsAcquirer(PaymentMethodType type) => RequiresAcquirer(type) || type == PaymentMethodType.Voucher;

    /// <summary>O <c>tPag</c> mais provável para o tipo (o formulário vem preenchido com ele).</summary>
    public static string DefaultNfeCode(PaymentMethodType type) => type switch
    {
        PaymentMethodType.Cash => "01",
        PaymentMethodType.Credit or PaymentMethodType.CreditInstallments => "03",
        PaymentMethodType.Debit => "04",
        PaymentMethodType.StoreCredit or PaymentMethodType.OnAccount => "05",
        PaymentMethodType.Voucher => "10",
        PaymentMethodType.Pix => "17",
        PaymentMethodType.BankSlip => "15",
        _ => "99",
    };

    /// <summary>Códigos de <c>tPag</c> aceitos (NT 2020.006 e seguintes).</summary>
    public static readonly IReadOnlySet<string> NfeCodes = new HashSet<string>(StringComparer.Ordinal)
        { "01", "02", "03", "04", "05", "10", "11", "12", "13", "15", "16", "17", "18", "19", "20", "90", "99" };

    /// <summary>Parcelas aceitas na taxa: só o crédito parcelado tem de 2 a 24; os demais, 1.</summary>
    public static (int Min, int Max) InstallmentRange(PaymentMethodType type) => type == PaymentMethodType.CreditInstallments ? (2, 24) : (1, 1);
}

/// <summary>
/// Taxa e prazo da adquirente (<c>finance.acquirer_fees</c>, PAG-01 CA 2 e 3) por forma, bandeira e "a partir de N parcelas", com vigência.
/// Taxa passada nunca é editada: a nova encerra a anterior (<c>ValidTo</c> = início da nova, exclusivo) e entra no histórico com quem e quando.
/// </summary>
public sealed class AcquirerFee : Entity, IAuditable
{
    public Guid PaymentMethodId { get; init; }

    /// <summary>A adquirente da forma quando a taxa foi cadastrada; trocar a adquirente da forma deixa as taxas antigas sem efeito.</summary>
    public Guid AcquirerPartyId { get; init; }

    public CardBrand Brand { get; init; }

    public int Installments { get; init; }

    public decimal FeePercent { get; init; }

    public int SettlementDays { get; init; }

    public DateOnly ValidFrom { get; init; }

    public DateOnly? ValidTo { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public bool InForce(DateOnly on) => ValidFrom <= on && (ValidTo is null || on < ValidTo);

    /// <summary>Encerra a vigência quando a nova entra (só a data de fim muda; taxa e prazo ficam como estavam).</summary>
    public void CloseAt(DateOnly end) => ValidTo = end;
}

/// <summary>Escolha da taxa de uma venda (PAG-01 CA 5): bandeira exata, senão "qualquer bandeira"; a faixa de parcelas mais alta que cabe.</summary>
public static class AcquirerFeeRules
{
    public static AcquirerFee? Pick(IEnumerable<AcquirerFee> fees, Guid acquirerPartyId, CardBrand brand, int installments, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(fees);
        var candidates = fees.Where(f => f.AcquirerPartyId == acquirerPartyId && f.InForce(on) && f.Installments <= installments).ToList();
        return Best(candidates, brand) ?? (brand == CardBrand.Any ? null : Best(candidates, CardBrand.Any));
    }

    private static AcquirerFee? Best(List<AcquirerFee> candidates, CardBrand brand) =>
        candidates.Where(f => f.Brand == brand).OrderByDescending(f => f.Installments).ThenByDescending(f => f.ValidFrom).FirstOrDefault();
}
