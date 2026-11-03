using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// PDFs da OS de instalação (EXP-04 CA 7 e 8): a OS impressa e o termo de aceite técnico, gerados na hora pelo mesmo
/// <see cref="InstallationOrderService.GetAsync"/> da tela (404 para OS de outro tenant ou de fabricação). O valor combinado com o
/// instalador não é lido para o modelo. O equipamento do termo vem dos romaneios expedidos do projeto (séries); sem romaneio, dos itens da OS.
/// </summary>
public sealed class InstallationDocumentService(
    InstallationOrderService orders, IProductionDb db, IProjects projects, IParties parties, IItems items, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit,
    TimeProvider clock)
{
    public const string AuditEntity = "ProductionOrder";
    public const string TermAuditEntity = "InstallationAcceptanceTerm";

    public static Error CancelledTerm() => Error.Conflict("installation_cancelled", "OS de instalação cancelada não tem termo de aceite.");

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await orders.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var o = detail.Value;
        if (o.Steps.Count + o.Checklist.Count > InstallationPdf.MaxLines)
        {
            return TooLong();
        }

        var (customer, document, site) = await CustomerAsync(o.ProjectId, cancellationToken);
        var done = o.Steps.Select(s => s.CompletedAt is not null).ToList();
        var model = new InstallationPdfModel(
            o.Number, o.Status, InstallationPdf.StatusLabel(o.Status), o.ProjectNumber, o.ProjectName, customer, document, site, o.InstallerName, o.PlannedStart, o.PlannedEnd,
            o.StartedOn, o.AcceptedOn, o.AcceptedByName, o.CancelReason, InstallationPdf.Letters(o.Steps.Select(s => s.Name)), InstallationPdf.CurrentIndex(done),
            o.Steps.Select(s => new InstallationPdfStep(s.Sequence, s.Name, s.CompletedAt is null ? "Pendente" : "Concluída", s.CompletedAt is { } at ? Day(at) : null)).ToList(),
            o.Checklist.Select(c => new InstallationPdfCheck(c.Text, InstallationPdf.ResultLabel(c.Result))).ToList(), BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new InstallationOrderPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, o.Number + ".pdf");
    }

    /// <summary>Termo de aceite (CA 8): em branco em Aberta e Em instalação; preenchido na concluída; 409 na cancelada.</summary>
    public async Task<Result<(byte[] Content, string FileName)>> AcceptanceTermAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await orders.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var o = detail.Value;
        if (o.Status == ProductionOrderStatus.Cancelled)
        {
            return CancelledTerm();
        }

        var (equipment, fromShipment) = await EquipmentAsync(o, cancellationToken);
        if (o.Checklist.Count + equipment.Count > InstallationPdf.MaxLines)
        {
            return TooLong();
        }

        var completed = o.Status == ProductionOrderStatus.Completed;
        var (customer, document, site) = await CustomerAsync(o.ProjectId, cancellationToken);
        var model = new AcceptanceTermPdfModel(
            o.Number, o.Status, o.ProjectNumber, o.ProjectName, customer, document, site, o.InstallerName, fromShipment, equipment,
            o.Checklist.Select(c => new AcceptanceTermCheck(c.Text, completed ? c.Result : null)).ToList(), completed ? o.AcceptedOn : null, completed ? o.AcceptedByName : null,
            BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new AcceptanceTermPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(TermAuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, o.Number + "-termo-de-aceite.pdf");
    }

    /// <summary>Linhas dos romaneios expedidos do projeto, com as séries; sem romaneio, os itens cobertos pela OS (séries em branco).</summary>
    private async Task<(IReadOnlyList<AcceptanceTermEquipment> Lines, bool FromShipment)> EquipmentAsync(InstallationDetail o, CancellationToken ct)
    {
        var shipped = await db.Shipments.AsNoTracking().Where(s => s.ProjectId == o.ProjectId && s.Status == ShipmentState.Shipped).Select(s => s.Id).ToListAsync(ct);
        if (shipped.Count > 0)
        {
            var lines = await db.ShipmentLines.AsNoTracking().Where(l => shipped.Contains(l.ShipmentId)).OrderBy(l => l.ShipmentId).ThenBy(l => l.Sequence).ToListAsync(ct);
            var serials = (await db.ShipmentLineSerials.AsNoTracking().Where(s => shipped.Contains(s.ShipmentId)).OrderBy(s => s.SerialNumber).ToListAsync(ct)).ToLookup(s => s.LineId);
            var names = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
            return (lines.Select(l =>
            {
                var item = names.GetValueOrDefault(l.ItemId);
                var description = item is null ? "Item" : $"{item.Code} · {item.Name}";
                return new AcceptanceTermEquipment(description, l.Quantity, item?.UnitCode, serials[l.Id].Select(s => s.SerialNumber).ToList());
            }).ToList(), true);
        }

        var projectItems = (await projects.ItemsAsync(o.ProjectId, ct)).Where(i => o.Items.Any(x => x.Id == i.Id)).OrderBy(i => i.Sequence).ToList();
        var units = (await items.FindAsync(projectItems.Where(i => i.ItemId is not null).Select(i => i.ItemId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id, i => i.UnitCode);
        return (projectItems.Select(i => new AcceptanceTermEquipment(i.Description, i.Quantity, i.ItemId is { } item ? units.GetValueOrDefault(item) : null, [])).ToList(), false);
    }

    private async Task<(string Name, string? Document, string? Site)> CustomerAsync(Guid projectId, CancellationToken ct)
    {
        var project = await projects.FindAsync(projectId, ct);
        if (project is null)
        {
            return (string.Empty, null, null);
        }

        var party = await parties.FindAsync(project.PartyId, ct);
        var addresses = await parties.AddressesAsync(project.PartyId, ct);
        var site = project.SiteAddressId is { } siteId ? addresses.FirstOrDefault(a => a.Id == siteId) : null;
        site ??= addresses.FirstOrDefault(a => a.Kind == AddressKind.Site);
        return (party is null ? string.Empty : party.LegalName, party?.Document is { Length: > 0 } doc ? BrazilianDocuments.Format(doc) : null,
            site is null ? null : ShipmentRules.FormatAddress(site));
    }

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime);

    private static Error TooLong()
    {
        var message = $"O PDF da OS de instalação aceita até {InstallationPdf.MaxLines} linhas (etapas, checklist e equipamento).";
        return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["lines"] = [message] });
    }
}
