using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Application.Incoming;
using BemAqui.Fiscal.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Fiscal.Endpoints;

/// <summary>Formulário de "Importar XML" (<c>multipart/form-data</c>): um arquivo por requisição (o front envia cada um e mostra o resultado de cada).</summary>
public sealed class IncomingXmlForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    public override string ToString() => "IncomingXmlForm(***)";
}

/// <summary>
/// Fiscal → Notas de entrada (FIS-04a CA 1, 11 e 13): o módulo <c>fiscal</c> liberado; ver (lista, nota, XML) com <c>fiscal.view</c>;
/// importar, de-para, pedido, conferir e recusar com <c>fiscal.import_incoming</c> (com <c>fiscal.create</c> sozinha, 403, D12); a
/// importação tem a política <c>fiscal-incoming-import</c> (60 arquivos por hora por usuário). Item de outra nota: 404 (IDOR). Respostas
/// sem cache.
/// </summary>
internal static class FiscalIncomingEndpoints
{
    private const string Import = "fiscal.import_incoming";

    public static IEndpointRouteBuilder MapFiscalIncomingEndpoints(this IEndpointRouteBuilder api)
    {
        var invoices = api.MapGroup("/fiscal/incoming-invoices").WithTags("Fiscal").RequireModule("fiscal")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        invoices.MapGet(string.Empty, async (IncomingInvoiceStatus? status, int? page, int? pageSize, IncomingInvoiceService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(status, new PageRequest(page ?? 1, pageSize ?? 50).Normalize(), ct)))
            .RequirePermission("fiscal.view").WithName("ListIncomingInvoices").Produces<IncomingInvoiceList>();

        invoices.MapPost("/import", async ([FromForm] IncomingXmlForm form, IncomingInvoiceService service, CancellationToken ct) =>
            {
                if (form.File is not { Length: > 0 } file)
                {
                    return BemAqui.SharedKernel.Error.Validation("file", "Escolha o arquivo XML da nota.", new Dictionary<string, string[]> { ["file"] = ["Escolha o arquivo XML da nota."] }).ToProblem();
                }

                if (file.Length > IncomingInvoiceRules.MaxBytes)
                {
                    return BemAqui.SharedKernel.Error.Validation("incoming_xml_size", "O arquivo precisa ter até 2 MB.", new Dictionary<string, string[]> { ["file"] = ["O arquivo precisa ter até 2 MB."] }).ToProblem();
                }

                using var memory = new MemoryStream();
                await file.CopyToAsync(memory, ct);
                return (await service.ImportAsync(file.FileName, memory.ToArray(), ct)).ToHttpResult();
            })
            .RequirePermission(Import).RequireRateLimiting(IncomingInvoiceService.RateLimitPolicy).DisableAntiforgery().Accepts<IncomingXmlForm>("multipart/form-data")
            .WithName("ImportIncomingInvoice").Produces<IncomingImportResult>();

        invoices.MapGet("/item-options", async (string? q, IncomingInvoiceService service, CancellationToken ct) => Results.Ok(await service.ItemOptionsAsync(q, ct)))
            .RequirePermission(Import).WithName("ListIncomingItemOptions").Produces<IReadOnlyList<IncomingItemOption>>();
        invoices.MapGet("/account-options", async (IncomingInvoiceService service, CancellationToken ct) => Results.Ok(await service.AccountOptionsAsync(ct)))
            .RequirePermission(Import).WithName("ListIncomingAccountOptions").Produces<IReadOnlyList<IncomingAccountOption>>();

        invoices.MapGet("/{id:guid}", async (Guid id, IncomingInvoiceService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetIncomingInvoice").Produces<IncomingInvoiceView>();
        invoices.MapGet("/{id:guid}/xml", async (Guid id, IncomingInvoiceService service, CancellationToken ct) =>
            {
                var file = await service.XmlAsync(id, ct);
                return file.IsSuccess ? Results.File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : file.Error!.ToProblem();
            })
            .RequirePermission("fiscal.view").WithName("DownloadIncomingInvoiceXml");

        invoices.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(IncomingInvoice), id.ToString(), [nameof(IncomingInvoiceItem), nameof(IncomingInvoiceReading)], "invoiceId",
                    new PageRequest(page ?? 1, pageSize ?? 20).Normalize(), ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetIncomingInvoiceHistory").Produces<PagedList<RelatedAuditEntryDto>>();

        invoices.MapPost("/{id:guid}/supplier", async (Guid id, IncomingInvoiceService service, CancellationToken ct) => (await service.RefreshSupplierAsync(id, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("RefreshIncomingInvoiceSupplier").Produces<IncomingInvoiceView>();
        invoices.MapPut("/{id:guid}/items/{itemId:guid}/mapping", async (Guid id, Guid itemId, IncomingItemMappingRequest request, IncomingInvoiceService service, CancellationToken ct) =>
                (await service.MapItemAsync(id, itemId, request, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("MapIncomingInvoiceItem").Produces<IncomingInvoiceView>();
        invoices.MapPut("/{id:guid}/order", async (Guid id, IncomingOrderRequest request, IncomingInvoiceService service, CancellationToken ct) => (await service.SetOrderAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("SetIncomingInvoiceOrder").Produces<IncomingInvoiceView>();
        invoices.MapPost("/{id:guid}/check", async (Guid id, CheckIncomingInvoiceRequest request, IncomingInvoiceService service, CancellationToken ct) => (await service.CheckAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("CheckIncomingInvoice").Produces<IncomingInvoiceView>();
        invoices.MapPost("/{id:guid}/reject", async (Guid id, RejectIncomingInvoiceRequest request, IncomingInvoiceService service, CancellationToken ct) => (await service.RejectAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("RejectIncomingInvoice").Produces<IncomingInvoiceView>();

        MapReadingEndpoints(invoices);
        return api;
    }

    /// <summary>
    /// FIS-05 (CA 1, 6 a 11): "Importar PDF da DANFE" com a política <c>fiscal-pdf-reading</c> (30 por hora por usuário), "Digitar a nota",
    /// a leitura assistida (<c>/{id}/pdf-reading</c>: rascunho de outro tenant ou inexistente é 404), o PDF só por endpoint autorizado,
    /// "Pedir o XML ao fornecedor", "Conferir nota" e "Substituir pelo XML"; tudo que grava pede <c>fiscal.import_incoming</c>.
    /// </summary>
    private static void MapReadingEndpoints(RouteGroupBuilder invoices)
    {
        invoices.MapPost("/pdf", async ([FromForm] IncomingXmlForm form, IncomingInvoiceService service, CancellationToken ct) =>
            {
                if (form.File is not { Length: > 0 } file)
                {
                    return BemAqui.SharedKernel.Error.Validation("file", "Escolha o PDF da nota.", new Dictionary<string, string[]> { ["file"] = ["Escolha o PDF da nota."] }).ToProblem();
                }

                if (file.Length > IncomingReadingRules.MaxPdfBytes)
                {
                    return BemAqui.SharedKernel.Error.Validation("file", "O PDF precisa ter até 5 MB.", new Dictionary<string, string[]> { ["file"] = ["O PDF precisa ter até 5 MB."] }).ToProblem();
                }

                using var memory = new MemoryStream();
                await file.CopyToAsync(memory, ct);
                return (await service.UploadPdfAsync(file.FileName, memory.ToArray(), ct)).ToHttpResult();
            })
            .RequirePermission(Import).RequireRateLimiting(IncomingReadingRules.RateLimitPolicy).DisableAntiforgery().Accepts<IncomingXmlForm>("multipart/form-data")
            .WithName("UploadIncomingInvoicePdf").Produces<IncomingReadingView>();

        invoices.MapPost("/manual", async (IncomingInvoiceService service, CancellationToken ct) => (await service.StartManualAsync(ct)).ToHttpResult())
            .RequirePermission(Import).WithName("StartManualIncomingInvoice").Produces<IncomingReadingView>();

        invoices.MapGet("/{id:guid}/pdf-reading", async (Guid id, IncomingInvoiceService service, CancellationToken ct) => (await service.GetReadingAsync(id, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("GetIncomingInvoiceReading").Produces<IncomingReadingView>();
        invoices.MapGet("/{id:guid}/pdf-reading/pdf", async (Guid id, IncomingInvoiceService service, CancellationToken ct) =>
            {
                var file = await service.ReadingPdfAsync(id, ct);
                return file.IsSuccess ? Results.File(file.Value.Content, "application/pdf", file.Value.FileName) : file.Error!.ToProblem();
            })
            .RequirePermission(Import).WithName("DownloadIncomingInvoiceReadingPdf");
        invoices.MapPost("/{id:guid}/pdf-reading/manual", async (Guid id, IncomingInvoiceService service, CancellationToken ct) => (await service.SwitchToManualAsync(id, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("SwitchIncomingInvoiceReadingToManual").Produces<IncomingReadingView>();
        invoices.MapPost("/{id:guid}/pdf-reading/xml-request", async (Guid id, IncomingInvoiceService service, CancellationToken ct) => (await service.RequestXmlAsync(id, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("RequestIncomingInvoiceXml").Produces<XmlRequestResult>();
        invoices.MapPost("/{id:guid}/pdf-reading/check", async (Guid id, CheckReadingRequest request, IncomingInvoiceService service, CancellationToken ct) =>
                (await service.CheckReadingAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Import).WithName("CheckIncomingInvoiceReading").Produces<CheckReadingResult>();

        invoices.MapGet("/{id:guid}/pdf", async (Guid id, IncomingInvoiceService service, CancellationToken ct) =>
            {
                var file = await service.InvoicePdfAsync(id, ct);
                return file.IsSuccess ? Results.File(file.Value.Content, "application/pdf", file.Value.FileName) : file.Error!.ToProblem();
            })
            .RequirePermission("fiscal.view").WithName("DownloadIncomingInvoicePdf");
        invoices.MapPost("/{id:guid}/replace-xml", async (Guid id, [FromForm] IncomingXmlForm form, IncomingInvoiceService service, CancellationToken ct) =>
            {
                if (form.File is not { Length: > 0 } file)
                {
                    return BemAqui.SharedKernel.Error.Validation("file", "Escolha o arquivo XML da nota.", new Dictionary<string, string[]> { ["file"] = ["Escolha o arquivo XML da nota."] }).ToProblem();
                }

                if (file.Length > IncomingInvoiceRules.MaxBytes)
                {
                    return BemAqui.SharedKernel.Error.Validation("incoming_xml_size", "O arquivo precisa ter até 2 MB.", new Dictionary<string, string[]> { ["file"] = ["O arquivo precisa ter até 2 MB."] }).ToProblem();
                }

                using var memory = new MemoryStream();
                await file.CopyToAsync(memory, ct);
                return (await service.ReplaceWithXmlAsync(id, file.FileName, memory.ToArray(), ct)).ToHttpResult();
            })
            .RequirePermission(Import).RequireRateLimiting(IncomingInvoiceService.RateLimitPolicy).DisableAntiforgery().Accepts<IncomingXmlForm>("multipart/form-data")
            .WithName("ReplaceIncomingInvoiceWithXml").Produces<IncomingInvoiceView>();
    }
}
