using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// "Baixar PDF" do contrato e do aditivo (EXP-04 CA 1 a 6): gerados na hora no modelo único pelos mesmos <see cref="ContractService.GetAsync"/> e
/// <see cref="AmendmentService.GetAsync"/> da tela (404 para outro tenant; aditivo de outro contrato também é 404, IDOR). Nenhum campo de
/// custo, margem ou orçado é copiado para o modelo. Cada download fica na auditoria de exportação.
/// </summary>
public sealed class ContractDocumentService(
    ContractService contracts, AmendmentService amendments, ProjectService projects, IParties parties, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit,
    TimeProvider clock)
{
    public const string ContractEntity = "Contract";
    public const string AmendmentEntity = "ContractAmendment";

    public async Task<Result<(byte[] Content, string FileName)>> ContractPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await contracts.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var contract = detail.Value;
        var list = await amendments.ListAsync(id, cancellationToken);
        if (ContractPdf.Lines(contract, list.Count) > ContractPdf.MaxLines)
        {
            return Error.Validation(Error.Codes.Validation, ContractPdf.LinesMessage, new Dictionary<string, string[]> { ["lines"] = [ContractPdf.LinesMessage] });
        }

        var party = await parties.FindAsync(contract.PartyId, cancellationToken);
        var address = await parties.DefaultAddressAsync(contract.PartyId, AddressKind.Billing, cancellationToken);
        var project = contract.ProjectId is { } projectId && await projects.GetAsync(projectId, cancellationToken) is { IsSuccess: true } p ? p.Value : null;
        var model = ContractPdf.Build(contract, party?.LegalName ?? contract.PartyName, address is null ? null : Address(address), project, list, BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new ContractPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(ContractEntity, id.ToString(), "pdf", model.Items.Count, cancellationToken);
        return (bytes, contract.Number + ".pdf");
    }

    public async Task<Result<(byte[] Content, string FileName)>> AmendmentPdfAsync(Guid contractId, Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await amendments.GetAsync(contractId, id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var amendment = detail.Value;
        if (amendment.Milestones.Count + amendment.Reductions.Count > ContractPdf.MaxLines)
        {
            return Error.Validation(Error.Codes.Validation, ContractPdf.LinesMessage, new Dictionary<string, string[]> { ["lines"] = [ContractPdf.LinesMessage] });
        }

        var contract = (await contracts.GetAsync(contractId, cancellationToken)).Value;
        var party = await parties.FindAsync(contract.PartyId, cancellationToken);
        var model = ContractPdf.Build(amendment, party?.LegalName ?? contract.PartyName, contract.PartyDocument, contract.ProjectNumber, BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        DateOnly? date = amendment.ApprovedAt is { } at ? DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime) : null;
        var bytes = await renderer.RenderAsync(new AmendmentPdfDocument(frame, model, date), cancellationToken);
        await audit.RecordExportAsync(AmendmentEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, $"{contract.Number}-aditivo-{amendment.Number}.pdf");
    }

    private static string Address(PartyAddressInfo a) =>
        $"{a.Street}, {a.Number}{(string.IsNullOrWhiteSpace(a.Complement) ? string.Empty : " " + a.Complement)} — {a.District}, {a.City}/{a.State} · CEP {a.ZipCode}";
}
