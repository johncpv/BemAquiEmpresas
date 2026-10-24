using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Regra de alçada (<c>finance.approval_rules</c>, FIN-03b): faixa de valor (e conta, opcional, com teto) que diz quem aprova
/// em cada nível (1 a 3). Aprovador é um grupo ou um usuário. A padrão "Proprietário aprova tudo" vem do seed.
/// </summary>
[DocumentName("A regra de alçada")]
public sealed class ApprovalRule : Entity, IAuditable, ISoftDeletable
{
    public const string PaymentOrder = Contracts.ApprovalDocumentTypes.PaymentOrder;
    public const string PurchaseOrder = Contracts.ApprovalDocumentTypes.PurchaseOrder;
    public const string DefaultSeedKey = "owner-approves-all";

    /// <summary>Regra padrão própria do pedido de compra (CMP-03a CA 5, ponto 44): "Proprietário aprova tudo".</summary>
    public const string PurchaseOrderSeedKey = "owner-approves-all-purchase";

    public static string SeedKeyOf(string documentType) => documentType == PurchaseOrder ? PurchaseOrderSeedKey : DefaultSeedKey;

    public string DocumentType { get; init; } = PaymentOrder;

    public decimal MinAmount { get; set; }

    /// <summary>Teto; nulo = sem teto (obrigatório quando há conta).</summary>
    public decimal? MaxAmount { get; set; }

    public Guid? AccountId { get; set; }

    public Guid? ApproverGroupId { get; set; }

    public Guid? ApproverUserId { get; set; }

    public int Level { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }

    public bool Covers(decimal amount) => amount >= MinAmount && (MaxAmount is null || amount <= MaxAmount);

    public bool Overlaps(ApprovalRule other) => MinAmount <= (other.MaxAmount ?? decimal.MaxValue) && other.MinAmount <= (MaxAmount ?? decimal.MaxValue);
}

[JsonConverter(typeof(JsonStringEnumConverter<LevelStatus>))]
public enum LevelStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>Nível resolvido no envio da OP (<c>finance.payment_order_levels</c>, FIN-03b CA 6, 10): vale até reenviar.</summary>
public sealed class PaymentOrderLevel : Entity, IAuditable
{
    public Guid PaymentOrderId { get; init; }

    public int Level { get; init; }

    public Guid ApprovalRuleId { get; init; }

    public decimal ReferenceAmount { get; init; }

    public string? SplitWarning { get; init; }

    public LevelStatus Status { get; set; } = LevelStatus.Pending;
}

/// <summary>Resolução da alçada (FIN-03b CA 6 a 8), sem banco: uma regra por nível; conta vence faixa dentro do teto.</summary>
public static class ApprovalRules
{
    /// <param name="singleAccountId">A conta de todos os títulos, quando todos são da mesma; senão nulo (CA 7).</param>
    public static IReadOnlyList<ApprovalRule> Resolve(IEnumerable<ApprovalRule> rules, decimal reference, Guid? singleAccountId)
    {
        var active = rules.Where(r => r.IsActive && !r.IsDeleted).ToList();
        var result = new List<ApprovalRule>();
        foreach (var level in active.Select(r => r.Level).Distinct().Order())
        {
            var byAccount = singleAccountId is { } account
                ? active.FirstOrDefault(r => r.Level == level && r.AccountId == account && r.Covers(reference))
                : null;
            var byRange = active.Where(r => r.Level == level && r.AccountId is null && r.Covers(reference)).OrderByDescending(r => r.MinAmount).FirstOrDefault();
            if ((byAccount ?? byRange) is { } chosen)
            {
                result.Add(chosen);
            }
        }

        return result;
    }

    /// <summary>
    /// Valor de referência (CA 8): o maior entre o total da OP, o maior valor original dos títulos e a soma das OPs do mesmo
    /// fornecedor em 30 dias (incluindo esta); evita o fracionamento.
    /// </summary>
    public static decimal Reference(decimal total, decimal largestPayable, decimal supplierSum30d) => Math.Max(total, Math.Max(largestPayable, supplierSum30d));
}
