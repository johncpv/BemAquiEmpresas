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
}
