using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application.Statements;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Formulário de "Importar extrato" (<c>multipart/form-data</c>): o arquivo OFX, até 5 MB.</summary>
public sealed class StatementOfxForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    public override string ToString() => "StatementOfxForm(***)";
}

/// <summary>
/// Extrato do banco da conta (BNK-03a CA 10): ver com <c>finance.view</c> (contraparte mascarada); importar, buscar agora, ignorar crédito
/// e desfazer com <c>finance.edit</c>; ignorar débito (no serviço) e liberar OFX em conta com API com <c>finance.approve</c>. Linha ou
/// lote de outra conta: 404. Todas as respostas com <c>no-store</c> (filtro do grupo <c>/finance</c>, CA 11).
/// </summary>
internal static partial class FinanceEndpoints
{
    private static void MapBankStatementLines(RouteGroupBuilder finance)
    {
        var lines = finance.MapGroup("/bank-accounts/{id:guid}/statement-lines");
        lines.MapGet(string.Empty, async (Guid id, DateOnly? from, DateOnly? to, string? status, int? page, int? pageSize, BankStatementImportService service, CancellationToken ct) =>
                (await service.ViewAsync(id, from, to, status, new PageRequest(page ?? 1, pageSize ?? 100).Normalize(500), ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetBankStatementLines").Produces<BankStatementView>();
        lines.MapGet("/batches", async (Guid id, BankStatementImportService service, CancellationToken ct) => (await service.BatchesAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("ListBankStatementBatches").Produces<IReadOnlyList<BankStatementBatchView>>();
        lines.MapPost("/import-ofx", async (Guid id, [FromForm] StatementOfxForm form, BankStatementImportService service, CancellationToken ct) =>
            {
                if (form.File is not { Length: > 0 } file)
                {
                    return BemAqui.SharedKernel.Error.Validation("file", "Escolha o arquivo OFX do extrato.", new Dictionary<string, string[]> { ["file"] = ["Escolha o arquivo OFX do extrato."] }).ToProblem();
                }

                if (file.Length > 5 * 1024 * 1024)
                {
                    return BemAqui.SharedKernel.Error.Validation("ofx_size", "O arquivo precisa ter até 5 MB.", new Dictionary<string, string[]> { ["file"] = ["O arquivo precisa ter até 5 MB."] }).ToProblem();
                }

                using var memory = new MemoryStream();
                await file.CopyToAsync(memory, ct);
                return (await service.ImportOfxAsync(id, file.FileName, memory.ToArray(), ct)).ToHttpResult();
            })
            .RequirePermission("finance.edit").DisableAntiforgery().Accepts<StatementOfxForm>("multipart/form-data")
            .WithName("ImportBankStatementOfx").Produces<BankStatementImportResult>();
        lines.MapPost("/fetch", async (Guid id, BankStatementImportService service, CancellationToken ct) => (await service.FetchAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("FetchBankStatement").Produces<BankStatementImportResult>();
        lines.MapPost("/ofx-contingency", async (Guid id, OfxContingencyInput input, BankStatementImportService service, CancellationToken ct) => (await service.GrantContingencyAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.approve").WithName("GrantOfxContingency").Produces<OfxContingencyView>();
        lines.MapPost("/{lineId:guid}/ignore", async (Guid id, Guid lineId, IgnoreStatementLineInput input, BankStatementImportService service, CancellationToken ct) =>
                (await service.IgnoreAsync(id, lineId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("IgnoreBankStatementLine").Produces<BankStatementLineView>();
        lines.MapPost("/batches/{batchId:guid}/undo", async (Guid id, Guid batchId, UndoStatementBatchInput input, BankStatementImportService service, CancellationToken ct) =>
                (await service.UndoAsync(id, batchId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UndoBankStatementBatch").Produces<BankStatementBatchView>();
    }
}
