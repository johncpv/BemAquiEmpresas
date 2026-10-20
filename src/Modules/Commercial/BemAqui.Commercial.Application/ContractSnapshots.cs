using System.Text.Json;
using BemAqui.Commercial.Domain;

namespace BemAqui.Commercial.Application;

// Snapshot do contrato (COM-04a CA 12), esquema "v1": o cliente só por id e nome; custo só com projects.view_cost.

public sealed record SnapshotParty(Guid Id, string Name);

public sealed record SnapshotCost(string CostType, string? Description, Guid? ItemId, decimal Quantity, decimal UnitCost, decimal Total);

public sealed record SnapshotItem(int Sequence, string Kind, Guid? ItemId, string Description, decimal Quantity, decimal UnitPrice, decimal Total, decimal? UnitCost, IReadOnlyList<SnapshotCost>? Costs);

public sealed record SnapshotMilestone(int Sequence, string Description, decimal Percent, decimal Amount, string Trigger, string? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent);

public sealed record ContractSnapshot(
    string Schema, string QuoteNumber, int QuoteRevision, SnapshotParty Party, int? DeliveryDays, string? Notes, string PricingMode, IReadOnlyList<SnapshotItem> Items,
    IReadOnlyList<SnapshotMilestone> Milestones, decimal TotalPrice, decimal? TotalCost, decimal? MarginPct);

public static class ContractSnapshots
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(Quote quote, string partyName, IReadOnlyList<QuoteItem> items, IReadOnlyList<QuoteItemCost> costs, IReadOnlyList<QuoteMilestone> milestones)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return JsonSerializer.Serialize(new ContractSnapshot(
            "v1", quote.Number, quote.Revision, new SnapshotParty(quote.PartyId, partyName), quote.DeliveryDays, quote.Notes, quote.PricingMode.ToString(),
            items.Select(i => new SnapshotItem(i.Sequence, i.Kind.ToString(), i.ItemId, i.Description, i.Quantity, i.UnitPrice, QuotePricing.Money(i.Quantity * i.UnitPrice), i.UnitCost,
                costs.Where(c => c.QuoteItemId == i.Id).Select(c => new SnapshotCost(c.CostType.ToString(), c.Description, c.ItemId, c.Quantity, c.UnitCost, c.Total)).ToList())).ToList(),
            milestones.Select(m => new SnapshotMilestone(m.Sequence, m.Description, m.Percent, m.Amount, m.Trigger.ToString(), m.TriggerEvent?.ToString(), m.DueDate, m.DaysAfterEvent)).ToList(),
            quote.TotalPrice, quote.TotalCost, quote.MarginPct), Json);
    }

    /// <summary>Lê o snapshot; sem <c>projects.view_cost</c>, tira composições, custo e margem (D7).</summary>
    public static ContractSnapshot Read(string json, bool withCost)
    {
        var snapshot = JsonSerializer.Deserialize<ContractSnapshot>(json, Json) ?? throw new InvalidOperationException("Snapshot do contrato inválido.");
        return withCost ? snapshot : snapshot with
        {
            Items = snapshot.Items.Select(i => i with { UnitCost = null, Costs = null }).ToList(), TotalCost = null, MarginPct = null,
        };
    }
}
