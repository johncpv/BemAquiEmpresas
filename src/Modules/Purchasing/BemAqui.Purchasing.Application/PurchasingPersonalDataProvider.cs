using BemAqui.BuildingBlocks.Privacy;
using BemAqui.SharedKernel;
using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Onde o titular aparece em Compras (LGPD-01a CA 7 e 9): os pedidos de compra do terceiro PF fornecedor (número, situação e data, sem o
/// conteúdo) e os envios do pedido por e-mail a ele (<c>purchasing.purchase_order_dispatches</c>), com os outros endereços mascarados.
/// </summary>
[PersonalDataCovers(typeof(PurchaseOrderDispatch))]
public sealed class PurchasingPersonalDataProvider(IPurchasingDb db) : IPersonalDataProvider
{
    public string Module => "purchasing";

    public string Title => "Compras";

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var orders = subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party
            ? await db.PurchaseOrders.AsNoTracking().Where(o => o.PartyId == party).OrderBy(o => o.CreatedAt).Select(o => new { o.Number, o.Status, o.CreatedAt }).ToListAsync(cancellationToken)
            : [];
        var seen = new HashSet<Guid>();
        var dispatches = new List<PersonalDataRecord>();
        foreach (var email in subject.Emails)
        {
            var pattern = "%" + email.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var rows = await (from d in db.PurchaseOrderDispatches.AsNoTracking()
                              join o in db.PurchaseOrders.AsNoTracking() on d.OrderId equals o.Id
                              where EF.Functions.ILike(d.To, PersonalDataQuery.Exact(email)) || (d.Cc != null && EF.Functions.ILike(d.Cc, pattern))
                              orderby d.CreatedAt
                              select new { d.Id, d.To, d.Cc, d.CreatedAt, o.Number }).ToListAsync(cancellationToken);
            dispatches.AddRange(rows.Where(r => seen.Add(r.Id)).Select(r => PersonalDataRecord.Of(r.CreatedAt, ("Pedido", r.Number),
                ("Destinatário", PersonalDataMask.Recipients(r.To, subject.Emails)), ("Cópia", PersonalDataMask.Recipients(r.Cc, subject.Emails)), ("Enviado em", PersonalDataMask.At(r.CreatedAt)))));
        }

        return new PersonalDataSection(Module, Title,
        [
            new PersonalDataBlock("Pedidos de compra", "purchasing.purchase_orders", orders.Select(o => PersonalDataRecord.Of(o.CreatedAt,
                ("Número", o.Number), ("Situação", o.Status.ToString()), ("Criado em", PersonalDataMask.At(o.CreatedAt)))).ToList()),
            new PersonalDataBlock("Pedidos enviados por e-mail", "purchasing.purchase_order_dispatches", dispatches),
        ]);
    }

    /// <summary>Data do último pedido de compra do terceiro PF fornecedor, para a retenção (CA 3).</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> RetentionAnchorsAsync(PersonalDataSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Kind != PersonalDataSubjectKind.Party || subject.PartyId is not { } party)
        {
            return new Dictionary<string, DateOnly>();
        }

        var dates = (await db.PurchaseOrders.Where(o => o.PartyId == party).Select(o => o.CreatedAt).ToListAsync(cancellationToken))
            .Select(at => DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(at).DateTime)).ToList();
        return dates.Count == 0 ? new Dictionary<string, DateOnly>() : new Dictionary<string, DateOnly> { [RetentionAnchors.LastDocument] = dates.Max() };
    }

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>LGPD-01b CA 2 e 9: pedidos de compra ficam; nos envios por e-mail, o endereço do titular vira o marcador.</summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var plan = new PersonalDataPlanBuilder(Module, Title);
        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            plan.Keep("Pedidos de compra: mantidos", "purchasing.purchase_orders", await db.PurchaseOrders.CountAsync(o => o.PartyId == party, ct));
        }

        var dispatches = new Dictionary<Guid, PurchaseOrderDispatch>();
        foreach (var email in subject.Emails)
        {
            var pattern = PersonalDataQuery.Contains(email);
            foreach (var d in await db.PurchaseOrderDispatches.Where(d => EF.Functions.ILike(d.To, PersonalDataQuery.Exact(email)) || (d.Cc != null && EF.Functions.ILike(d.Cc, pattern))).ToListAsync(ct))
            {
                dispatches[d.Id] = d;
            }
        }

        var decision = context.Decide(context.ContactGroup);
        if (apply && !decision.Retained && dispatches.Count > 0)
        {
            foreach (var d in dispatches.Values)
            {
                var entry = db.Entry(d);
                entry.Property(x => x.To).CurrentValue = PersonalDataMarker.Recipients(d.To, subject.Emails, context.Marker)!;
                entry.Property(x => x.Cc).CurrentValue = PersonalDataMarker.HasAny(d.Cc, subject.Emails) ? PersonalDataMarker.Recipients(d.Cc, subject.Emails, context.Marker) : d.Cc;
            }

            await db.SaveChangesAsync(ct);
        }

        var built = plan.Fields(decision, "Destinatário dos pedidos enviados por e-mail", "purchasing.purchase_order_dispatches", dispatches.Count).Build();
        return built.Items.Count == 0 ? null : built;
    }
}
