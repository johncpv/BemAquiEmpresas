using System.Globalization;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Application.Invoices.Nfse;
using BemAqui.Fiscal.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Fiscal.Endpoints;

/// <summary>
/// Formulário de "Registrar NFS-e emitida na prefeitura" (<c>multipart/form-data</c>, FIS-06 CA 6): os valores como saíram na nota da
/// prefeitura (valores com ponto decimal) e o PDF ou o XML dela.
/// </summary>
public sealed class RegisterNfseForm
{
    [FromForm(Name = "number")]
    public string? Number { get; set; }

    [FromForm(Name = "verificationCode")]
    public string? VerificationCode { get; set; }

    /// <summary>Data de emissão (<c>yyyy-MM-dd</c>).</summary>
    [FromForm(Name = "issuedOn")]
    public string? IssuedOn { get; set; }

    [FromForm(Name = "servicesAmount")]
    public string? ServicesAmount { get; set; }

    [FromForm(Name = "issAmount")]
    public string? IssAmount { get; set; }

    [FromForm(Name = "version")]
    public string? Version { get; set; }

    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    public override string ToString() => "RegisterNfseForm(***)";
}

/// <summary>
/// NFS-e da instalação (FIS-06 CA 10): o módulo <c>fiscal</c> liberado (403 <c>module_not_released</c>); ver com <c>fiscal.view</c>; a fila,
/// criar (com <c>Idempotency-Key</c>), conferir, emitir, registrar a nota da prefeitura e reenviar com <c>fiscal.create</c>; salvar, corrigir e
/// descartar o rascunho com <c>fiscal.edit</c>; registrar o cancelamento feito na prefeitura com <c>fiscal.approve</c> e o 2FA de novo. A NFS-e
/// de outra rota (NF-e) dá 404 (IDOR). XML e PDF só pelos endpoints da nota (<c>/xml</c> e <c>/danfe</c>). Nenhuma rota DELETE. Sem cache.
/// </summary>
internal static class FiscalNfseEndpoints
{
    public static IEndpointRouteBuilder MapFiscalNfseEndpoints(this IEndpointRouteBuilder api)
    {
        var invoices = api.MapGroup("/fiscal/outgoing-invoices").WithTags("Fiscal").RequireModule("fiscal")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        invoices.MapGet("/installations-to-invoice", async (NfseInvoiceService service, CancellationToken ct) => Results.Ok(await service.ToIssueAsync(ct)))
            .RequirePermission("fiscal.create").WithName("ListInstallationsToInvoice").Produces<IReadOnlyList<InstallationQueueRow>>();
        invoices.MapPost("/nfse", async (CreateNfseRequest request, NfseInvoiceService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToHttpResult(view => Results.Created("/api/v1/fiscal/outgoing-invoices/" + view.Id + "/nfse", view)))
            .RequirePermission("fiscal.create").RequireIdempotencyKey().WithName("CreateNfse").Produces<NfseView>(StatusCodes.Status201Created);
        invoices.MapGet("/{id:guid}/nfse", async (Guid id, NfseInvoiceService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetNfse").Produces<NfseView>();
        invoices.MapPut("/{id:guid}/nfse", async (Guid id, UpdateNfseRequest request, NfseInvoiceService service, CancellationToken ct) => (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("UpdateNfse").Produces<NfseView>();
        invoices.MapGet("/{id:guid}/nfse/issue-check", async (Guid id, NfseInvoiceService service, CancellationToken ct) => (await service.IssueCheckAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.create").WithName("CheckNfseIssue").Produces<NfseIssueCheckView>();
        invoices.MapPost("/{id:guid}/nfse/issue", async (Guid id, OutgoingInvoiceVersionRequest request, NfseInvoiceService service, CancellationToken ct) =>
                (await service.IssueAsync(id, request, ct)).ToHttpResult(result => result.Status == OutgoingInvoiceStatus.Authorized ? Results.Ok(result) : Results.Accepted(null, result)))
            .RequirePermission("fiscal.create").WithName("IssueNfse").Produces<NfseIssueResultView>().Produces<NfseIssueResultView>(StatusCodes.Status202Accepted);
        invoices.MapPost("/{id:guid}/nfse/register", async (Guid id, [FromForm] RegisterNfseForm form, NfseInvoiceService service, CancellationToken ct) =>
            {
                byte[]? content = null;
                if (form.File is { Length: > 0 } file)
                {
                    if (file.Length > 5 * 1024 * 1024)
                    {
                        return BemAqui.SharedKernel.Error.Validation("attachment.size", "O arquivo da nota precisa ter até 5 MB.",
                            new Dictionary<string, string[]> { ["file"] = ["O arquivo da nota precisa ter até 5 MB."] }).ToProblem();
                    }

                    using var memory = new MemoryStream();
                    await file.CopyToAsync(memory, ct);
                    content = memory.ToArray();
                }

                var input = new RegisterNfseInput(form.Number, form.VerificationCode, Date(form.IssuedOn), Amount(form.ServicesAmount), Amount(form.IssAmount),
                    uint.TryParse(form.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : 0, form.File?.FileName, content);
                return (await service.RegisterAsync(id, input, ct)).ToHttpResult();
            })
            .RequirePermission("fiscal.create").DisableAntiforgery().Accepts<RegisterNfseForm>("multipart/form-data").WithName("RegisterNfse").Produces<NfseRegisterResultView>();
        invoices.MapPost("/{id:guid}/nfse/register-cancellation", async (Guid id, RegisterNfseCancellationRequest request, NfseInvoiceService service, CancellationToken ct) =>
                (await service.RegisterCancellationAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.approve").WithName("RegisterNfseCancellation").Produces<NfseView>();
        invoices.MapPost("/{id:guid}/nfse/reopen", async (Guid id, OutgoingInvoiceVersionRequest request, NfseInvoiceService service, CancellationToken ct) =>
                (await service.ReopenAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("ReopenNfse").Produces<NfseView>();
        invoices.MapPost("/{id:guid}/nfse/discard", async (Guid id, OutgoingInvoiceVersionRequest request, NfseInvoiceService service, CancellationToken ct) =>
                (await service.DiscardAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("DiscardNfse").Produces<NfseView>();
        invoices.MapPost("/{id:guid}/nfse/resend", async (Guid id, NfseInvoiceService service, CancellationToken ct) => (await service.ResendAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.create").WithName("ResendNfse").Produces<NfseView>();
        return api;
    }

    private static DateOnly? Date(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal Amount(string? value) =>
        decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? Math.Round(d, 2, MidpointRounding.AwayFromZero) : -1m;
}
