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

    /// <summary>Data do último romaneio do terceiro PF cliente, para a retenção (CA 3).</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> RetentionAnchorsAsync(PersonalDataSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Kind != PersonalDataSubjectKind.Party || subject.PartyId is not { } party)
        {
            return new Dictionary<string, DateOnly>();
        }

        var dates = (await db.Shipments.Where(s => s.PartyId == party).Select(s => s.ShippedAt ?? s.CreatedAt).ToListAsync(cancellationToken))
            .Select(DateOf).ToList();
        return dates.Count == 0 ? new Dictionary<string, DateOnly>() : new Dictionary<string, DateOnly> { [RetentionAnchors.LastDocument] = dates.Max() };
    }

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 2 e 4: o nome de quem aceitou a OS de instalação e o motorista (nome e placa) do romaneio são prova de entrega: seguem a
    /// retenção do nome do titular com a data do próprio documento (fim do exercício + 5 anos no terceiro; desligamento + 5 anos no
    /// funcionário). O endereço de entrega dos romaneios do cliente segue a retenção do endereço. A OS e o romaneio ficam.
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var name = subject.Name.Trim();
        var plan = new PersonalDataPlanBuilder(Module, Title);
        var orders = name.Length == 0 || PersonalDataMarker.IsMarker(name) ? []
            : await db.ProductionOrders.Where(o => o.AcceptedByName != null && EF.Functions.ILike(o.AcceptedByName, PersonalDataQuery.Exact(name))).ToListAsync(ct);
        foreach (var group in orders.GroupBy(o => Decide(context, o.AcceptedOn)))
        {
            if (apply && !group.Key.Retained)
            {
                group.ToList().ForEach(o => o.AcceptedByName = context.Marker);
            }

            plan.Fields(group.Key, "Nome de quem aceitou a OS de instalação", "production.production_orders", group.Count());
        }

        var drivers = name.Length == 0 || PersonalDataMarker.IsMarker(name) ? []
            : await db.Shipments.Where(s => s.DriverName != null && EF.Functions.ILike(s.DriverName, PersonalDataQuery.Exact(name))).ToListAsync(ct);
        foreach (var group in drivers.GroupBy(s => Decide(context, DateOf(s.ShippedAt ?? s.CreatedAt))))
        {
            if (apply && !group.Key.Retained)
            {
                group.ToList().ForEach(s => { s.DriverName = context.Marker; s.VehiclePlate = null; });
            }

            plan.Fields(group.Key, "Motorista e placa nos romaneios", "production.shipments", group.Count());
        }

        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            var shipments = await db.Shipments.Where(s => s.PartyId == party).ToListAsync(ct);
            plan.Keep("Romaneios: mantidos", "production.shipments", shipments.Count);
            foreach (var group in shipments.Where(s => s.DeliveryAddress.Length > 0 && s.DeliveryAddress != context.Marker)
                         .GroupBy(s => context.Decide(RetentionGroups.Address, DateOf(s.ShippedAt ?? s.CreatedAt))))
            {
                if (apply && !group.Key.Retained)
                {
                    group.ToList().ForEach(s => s.DeliveryAddress = context.Marker);
                }

                plan.Fields(group.Key, "Endereço de entrega nos romaneios", "production.shipments", group.Count());
            }
        }

        if (apply)
        {
            await db.SaveChangesAsync(ct);
        }

        var built = plan.Build();
        return built.Items.Count == 0 ? null : built;
    }

    private static RetentionDecision Decide(PersonalDataAnonymizationContext context, DateOnly? date) =>
        context.Subject.Kind == PersonalDataSubjectKind.Employee ? context.Decide(RetentionGroups.Labor) : context.Decide(context.IdentityGroup, date);

    private static DateOnly DateOf(DateTimeOffset at) => DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(at).DateTime);
}
