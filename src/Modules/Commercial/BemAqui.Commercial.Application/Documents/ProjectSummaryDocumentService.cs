using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// "Baixar PDF" do resumo do projeto (EXP-05 CA 5), com <c>projects.view</c>: o projeto pelo mesmo <see cref="ProjectService.GetAsync"/> da tela
/// (404 fora do tenant), os marcos pelo contrato e, como a tela, as OS só com <c>production.view</c>, o bloco Custos só com
/// <c>projects.view_cost</c> e o Financeiro só com <c>finance.view</c>: sem a permissão, a consulta do bloco nem é feita.
/// </summary>
public sealed class ProjectSummaryDocumentService(
    ProjectService projects, ContractService contracts, ProjectMarginService margins, IProductionOrders productionOrders, IPermissionChecker permissions,
    PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "Project";

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await projects.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var project = detail.Value;
        var contract = await contracts.GetAsync(project.ContractId, cancellationToken);
        IReadOnlyList<ProductionOrderInfo>? orders = await permissions.HasPermissionAsync("production", "view", cancellationToken)
            ? await productionOrders.ByProjectAsync(id, cancellationToken)
            : null;
        ProjectCostView? costs = null;
        if (await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken) && await margins.CostsAsync(id, cancellationToken) is { IsSuccess: true } cost)
        {
            costs = cost.Value;
        }

        ProjectFinancial? financial = null;
        if (await permissions.HasPermissionAsync("finance", "view", cancellationToken) && await projects.FinancialAsync(id, cancellationToken) is { IsSuccess: true } fin)
        {
            financial = fin.Value;
        }

        var model = ProjectSummaryPdf.Build(project, contract.IsSuccess ? contract.Value : null, orders, costs, financial, BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new ProjectSummaryPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, project.Number + ".pdf");
    }
}
