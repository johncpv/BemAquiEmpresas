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
                (await history.GetWithChildrenAsync(nameof(IncomingInvoice), id.ToString(), [nameof(IncomingInvoiceItem)], "invoiceId", new PageRequest(page ?? 1, pageSize ?? 20).Normalize(), ct)).ToHttpResult())
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
        return api;
    }
}
