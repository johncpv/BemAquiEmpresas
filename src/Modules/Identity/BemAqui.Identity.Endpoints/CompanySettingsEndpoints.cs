using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace BemAqui.Identity.Endpoints;

/// <summary>
/// Configurações da empresa (CAD-06): dados e logo, numeração e parâmetros, todos no módulo <c>settings</c> (ver com
/// <c>settings.view</c>, gravar com <c>settings.edit</c>); e a rota pública do logo, sem login, só do tenant do host (CA 7).
/// </summary>
internal static class CompanySettingsEndpoints
{
    public const string PublicLogoPath = "/public/tenant-logo";

    public static IEndpointRouteBuilder MapCompanySettingsEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings").WithTags("Configurações").RequireModule("settings");

        settings.MapGet("/company", async (CompanySettings company, CancellationToken ct) => Results.Ok(await company.GetAsync(ct)))
            .RequirePermission("settings.view").WithName("GetCompanyProfile").Produces<CompanyProfileDto>();
        settings.MapPut("/company", async (CompanyProfileInput input, CompanySettings company, CancellationToken ct) => (await company.SaveAsync(input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SaveCompanyProfile").Produces<CompanyProfileDto>();
        settings.MapPost("/company/logo", async (IFormFile file, CompanySettings company, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await company.UploadLogoAsync(file.FileName, stream, ct)).ToHttpResult();
            })
            .RequirePermission("settings.edit").DisableAntiforgery().WithName("UploadCompanyLogo").Produces<CompanyLogo>();

        // CEP do endereço da empresa (CA 2), pelo mesmo ICepLookup dos terceiros (CAD-01b), sem exigir permissão de terceiros.
        settings.MapGet("/company/cep/{cep}", async (string cep, BemAqui.BuildingBlocks.Lookups.ICepLookup lookup, CancellationToken ct) => (await lookup.LookupAsync(cep, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("LookupCompanyCep").Produces<BemAqui.BuildingBlocks.Lookups.AddressData>();

        settings.MapGet("/numbering", async (INumberFormatAdmin numbering, CancellationToken ct) => Results.Ok(await numbering.ListAsync(ct)))
            .RequirePermission("settings.view").WithName("ListNumberFormats").Produces<IReadOnlyList<NumberFormatRow>>();
        settings.MapPut("/numbering/{docType}", async (string docType, NumberFormatInput input, INumberFormatAdmin numbering, CancellationToken ct) =>
                (await numbering.SaveAsync(docType, input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SaveNumberFormat").Produces<NumberFormatRow>();

        settings.MapGet("/parameters", async (TenantParametersService parameters, CancellationToken ct) => Results.Ok(await parameters.GetAsync(ct)))
            .RequirePermission("settings.view").WithName("GetTenantParameters").Produces<TenantParametersDto>();
        settings.MapPut("/parameters/payroll-charges", async (PayrollChargesInput input, TenantParametersService parameters, CancellationToken ct) =>
                (await parameters.SaveChargesAsync(input.Pct, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SavePayrollCharges").Produces<TenantParametersDto>();
        settings.MapPut("/parameters/pricing-mode", async (PricingModeInput input, TenantParametersService parameters, CancellationToken ct) =>
                (await parameters.SavePricingAsync(input.Mode, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SavePricingMode").Produces<TenantParametersDto>();

        // Rota anônima da ADR 0016: o logo publicado do tenant do host, nunca outro anexo, com cache por ETag.
        api.MapGet(PublicLogoPath, async (HttpContext http, CompanySettings company, IFileStorage storage, CancellationToken ct) =>
            {
                if (await company.PublishedLogoAsync(ct) is not { } logo)
                {
                    return Results.NotFound();
                }

                var etag = new EntityTagHeaderValue("\"" + logo.Sha256 + "\"");
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
                http.Response.Headers.CacheControl = "public, max-age=300";
                if (http.Request.Headers.IfNoneMatch.ToString().Contains(logo.Sha256, StringComparison.Ordinal))
                {
                    http.Response.Headers.ETag = etag.ToString();
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                return Results.Stream(await storage.OpenReadAsync(logo.StoragePath, ct), logo.ContentType, entityTag: etag);
            })
            .AllowAnonymous().WithTags("Plataforma").WithName("GetTenantLogo").ExcludeFromDescription();
        return api;
    }
}
