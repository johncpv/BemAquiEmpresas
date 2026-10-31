using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Application.Certificates;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Fiscal.Endpoints;

/// <summary>Formulário do envio do A1 (CA 11): arquivo, senha e o código do autenticador, só no corpo (nunca na URL).</summary>
public sealed class FiscalCertificateForm
{
    [FromForm(Name = "pfx")]
    public IFormFile? Pfx { get; set; }

    [FromForm(Name = "password")]
    public string? Password { get; set; }

    [FromForm(Name = "code")]
    public string? Code { get; set; }
}

/// <summary>
/// Configurações → Fiscal e Certificado digital (FIS-02a CA 3 a 16): o módulo <c>fiscal</c> liberado; ver com <c>settings.view</c> ou
/// <c>fiscal.view</c>; editar, conferir, baixar o PDF e enviar o certificado com <c>settings.edit</c>; envio do A1 com a política
/// <c>fiscal-certificate</c> (5 por hora por tenant). Respostas sem cache.
/// </summary>
internal static class FiscalSettingsEndpoints
{
    public static IEndpointRouteBuilder MapFiscalSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/fiscal/settings").WithTags("Fiscal").RequireModule("fiscal")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        settings.MapGet(string.Empty, async (FiscalSettingsService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequireAnyPermissionOf("settings.view", "fiscal.view").WithName("GetFiscalSettings").Produces<FiscalSettingsView>();
        settings.MapPut(string.Empty, async (SaveFiscalSettingsRequest request, FiscalSettingsService service, CancellationToken ct) => (await service.SaveAsync(request, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SaveFiscalSettings").Produces<FiscalSettingsSaved>();
        settings.MapPost("/confirmations", async (ConfirmFiscalSettingsRequest request, FiscalSettingsService service, CancellationToken ct) => (await service.ConfirmAsync(request, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("ConfirmFiscalSettings").Produces<FiscalSettingsView>();
        settings.MapDelete("/confirmations/{key}", async (string key, FiscalSettingsService service, CancellationToken ct) => (await service.UndoAsync(key, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("UndoFiscalSettingConfirmation").Produces<FiscalSettingsView>();
        settings.MapGet("/export-pdf", async (FiscalSettingsExport export, HttpContext http, CancellationToken ct) =>
            {
                var pdf = await export.RenderAsync(ct);
                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(pdf, "application/pdf", "configuracao-fiscal-para-a-contadora.pdf");
            })
            .RequirePermission("settings.edit").WithName("ExportFiscalSettingsPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");

        settings.MapGet("/certificate", async (bool? refresh, FiscalCertificateService service, CancellationToken ct) => Results.Ok(await service.GetAsync(refresh == true, ct)))
            .RequireAnyPermissionOf("settings.view", "fiscal.view").WithName("GetFiscalCertificate").Produces<FiscalCertificateView>();
        settings.MapPost("/certificate", async ([FromForm] FiscalCertificateForm form, FiscalCertificateService service, CancellationToken ct) =>
            {
                var pfx = await ReadAsync(form.Pfx, ct);
                if (pfx is null)
                {
                    return Error.Validation(Error.Codes.Validation, InvalidFile, new Dictionary<string, string[]> { ["pfx"] = [InvalidFile] }).ToProblem();
                }

                if (string.IsNullOrEmpty(form.Password))
                {
                    return Error.Validation(Error.Codes.Validation, MissingPassword, new Dictionary<string, string[]> { ["password"] = [MissingPassword] }).ToProblem();
                }

                return (await service.UploadAsync(new UploadCertificateCommand(pfx, new Secret(form.Password), form.Code), ct)).ToHttpResult();
            })
            .RequirePermission("settings.edit").RequireRateLimiting(FiscalCertificateService.RateLimitPolicy).DisableAntiforgery()
            .Accepts<FiscalCertificateForm>("multipart/form-data").WithName("UploadFiscalCertificate").Produces<FiscalCertificateView>();
        return api;
    }

    private const string InvalidFile = "Escolha o arquivo .pfx ou .p12 do certificado A1 (até 50 KB).";

    private const string MissingPassword = "Informe a senha do certificado.";

    /// <summary>O arquivo só em memória (CA 12): até 50 KB, extensão <c>.pfx</c> ou <c>.p12</c>.</summary>
    private static async Task<byte[]?> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length is 0 or > FiscalCertificateRules.MaxBytes
            || !(file.FileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || file.FileName.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        Array.Clear(memory.GetBuffer());
        return bytes;
    }
}
