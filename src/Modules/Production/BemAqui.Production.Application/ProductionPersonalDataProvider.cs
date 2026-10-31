using BemAqui.BuildingBlocks.Privacy;
using BemAqui.SharedKernel;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Onde o titular aparece na Produção (LGPD-01a CA 9, ADR 0037 §2): a OS de instalação aceita por ele (<c>accepted_by_name</c>, pelo nome),
/// os romaneios entregues a ele (terceiro PF cliente) e os romaneios em que ele é o motorista (pelo nome ou como transportadora PF). Só
/// número, datas e o dado dele; o endereço de entrega sai só quando o titular é o cliente.
/// </summary>
[PersonalDataCovers(typeof(ProductionOrder), typeof(Shipment))]
public sealed class ProductionPersonalDataProvider(IProductionDb db) : IPersonalDataProvider
{
    public string Module => "production";

    public string Title => "Produção";

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var name = subject.Name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var accepted = await db.ProductionOrders.AsNoTracking().Where(o => o.AcceptedByName != null && EF.Functions.ILike(o.AcceptedByName, PersonalDataQuery.Exact(name)))
            .OrderBy(o => o.AcceptedOn).Select(o => new { o.Number, o.AcceptedOn, o.AcceptedByName }).ToListAsync(cancellationToken);
        var party = subject.Kind == PersonalDataSubjectKind.Party ? subject.PartyId : null;
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => (party != null && (s.PartyId == party || s.CarrierPartyId == party)) || (s.DriverName != null && EF.Functions.ILike(s.DriverName, PersonalDataQuery.Exact(name))))
            .OrderBy(s => s.CreatedAt).Select(s => new { s.Number, s.PartyId, s.DeliveryAddress, s.DriverName, s.VehiclePlate, s.ShippedAt, s.CreatedAt }).ToListAsync(cancellationToken);
        return new PersonalDataSection(Module, Title,
        [
            new PersonalDataBlock("OS de instalação aceitas", "production.production_orders", accepted.Select(o => PersonalDataRecord.Of(
                ("OS", o.Number), ("Aceita por", o.AcceptedByName), ("Aceita em", PersonalDataMask.Date(o.AcceptedOn)))).ToList()),
            new PersonalDataBlock("Romaneios", "production.shipments", shipments.Select(s =>
            {
                var driver = s.DriverName is not null && string.Equals(s.DriverName.Trim(), name, StringComparison.OrdinalIgnoreCase);
                var customer = party is not null && s.PartyId == party;
                return PersonalDataRecord.Of(s.CreatedAt, ("Romaneio", s.Number), ("Como", customer ? "Cliente" : driver ? "Motorista" : "Transportadora"),
                    ("Endereço de entrega", customer ? s.DeliveryAddress : null), ("Motorista", driver ? s.DriverName : PersonalDataMask.Name(s.DriverName)),
                    ("Placa", driver || !customer ? s.VehiclePlate : null), ("Expedido em", PersonalDataMask.At(s.ShippedAt)));
            }).ToList()),
        ]);
    }
}
