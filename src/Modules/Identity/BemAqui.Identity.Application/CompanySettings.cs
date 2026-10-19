using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>
/// Configurações → Empresa (CAD-06 CA 1 a 8). O perfil nasce dos dados do catálogo e, salvo, passa a ser a fonte da verdade.
/// O logo é um anexo <c>TenantLogo</c> do perfil: o publicado é o mais recente já verificado pelo antivírus, então o
/// anterior continua valendo até o novo ficar <c>Clean</c>.
/// </summary>
public sealed partial class CompanySettings(IIdentityDb db, BemAquiDbContext shared, TenantContextHolder tenant, ITenantProfile catalog, AttachmentService attachments, TimeProvider clock)
    : ITenantBranding
{
    public const long LogoMaxSize = 1024 * 1024;
    public const int LogoMaxSide = 1024;
    public static readonly string[] LogoTypes = ["image/png", "image/jpeg", "image/webp"];

    [GeneratedRegex(@"^(ISENTO|\d{2,14})$", RegexOptions.IgnoreCase)]
    private static partial Regex StateRegistrationPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    private Task<CompanyProfile?> ProfileAsync(CancellationToken ct) => db.CompanyProfiles.FirstOrDefaultAsync(ct);

    public async Task<CompanyProfileDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var profile = await ProfileAsync(cancellationToken);
        var resolved = tenant.Resolved;
        var document = resolved is null ? null : await catalog.DocumentAsync(resolved.Tenant.Id, cancellationToken);
        if (profile is null)
        {
            // Propositivo (CA 1): razão social e nome fantasia do catálogo, para só confirmar.
            return new CompanyProfileDto(false, document, resolved?.LegalName ?? string.Empty, resolved?.TradeName, null, null, null, null, null, null, null, null, null, null, null, null, null,
                new CompanyLogo(LogoStatus.None, null, false));
        }

        return new CompanyProfileDto(true, document, profile.LegalName, profile.TradeName, profile.StateRegistration, profile.MunicipalRegistration, profile.ZipCode, profile.Street,
            profile.Number, profile.Complement, profile.District, profile.City, profile.CityIbge, profile.State, profile.Phone, profile.Email, profile.Id, await LogoAsync(profile.Id, cancellationToken));
    }

    public async Task<Result<CompanyProfileDto>> SaveAsync(CompanyProfileInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        var profile = await ProfileAsync(cancellationToken);
        if (profile is null)
        {
            profile = new CompanyProfile();
            db.CompanyProfiles.Add(profile);
        }

        profile.LegalName = input.LegalName.Trim();
        profile.TradeName = Clean(input.TradeName);
        profile.StateRegistration = Clean(input.StateRegistration)?.ToUpperInvariant();
        profile.MunicipalRegistration = Clean(input.MunicipalRegistration);
        profile.ZipCode = Digits(input.ZipCode);
        profile.Street = Clean(input.Street);
        profile.Number = Clean(input.Number);
        profile.Complement = Clean(input.Complement);
        profile.District = Clean(input.District);
        profile.City = Clean(input.City);
        profile.CityIbge = Digits(input.CityIbge);
        profile.State = Clean(input.State)?.ToUpperInvariant();
        profile.Phone = Digits(input.Phone);
        profile.Email = Clean(input.Email)?.ToLowerInvariant();
        profile.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>"Trocar logo" (CA 5 e 6): as regras do dono <c>TenantLogo</c> recusam o que não é PNG, JPG ou WebP de até 1 MB e 1024 px.</summary>
    public async Task<Result<CompanyLogo>> UploadLogoAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var profile = await ProfileAsync(cancellationToken);
        if (profile is null)
        {
            return Error.Conflict("company.profile_missing", "Salve os dados da empresa antes de enviar o logo.");
        }

        var uploaded = await attachments.UploadAsync(CompanyProfile.LogoOwnerType, profile.Id, fileName, content, cancellationToken);
        return uploaded.IsFailure ? uploaded.Error! : await LogoAsync(profile.Id, cancellationToken);
    }

    /// <summary>Logo publicado: o mais recente já verificado. Nenhum outro anexo sai pela rota pública (CA 7).</summary>
    public async Task<Attachment?> PublishedLogoAsync(CancellationToken cancellationToken = default)
    {
        var profile = await db.CompanyProfiles.AsNoTracking().Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        return profile is null ? null : await shared.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == CompanyProfile.LogoOwnerType && a.OwnerId == profile && a.ScanStatus == ScanStatus.Clean)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }

    async Task<TenantBrand> ITenantBranding.GetAsync(CancellationToken cancellationToken) => await BrandAsync(cancellationToken);

    private async Task<TenantBrand> BrandAsync(CancellationToken ct)
    {
        var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var logo = await PublishedLogoAsync(ct);
        // CA 4: nome fantasia do perfil; sem perfil, o nome do catálogo.
        var name = profile is null ? tenant.Resolved?.TradeName ?? tenant.Resolved?.LegalName : profile.TradeName ?? profile.LegalName;
        return new TenantBrand(name, logo?.Sha256[..16]);
    }

    private async Task<CompanyLogo> LogoAsync(Guid profileId, CancellationToken ct)
    {
        var logos = await shared.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == CompanyProfile.LogoOwnerType && a.OwnerId == profileId && a.ScanStatus != ScanStatus.Infected)
            .OrderByDescending(a => a.CreatedAt).Select(a => new { a.ScanStatus, a.Sha256 }).ToListAsync(ct);
        var clean = logos.FirstOrDefault(l => l.ScanStatus == ScanStatus.Clean);
        var pendingNew = logos.Count > 0 && logos[0].ScanStatus != ScanStatus.Clean;
        return clean is null
            ? new CompanyLogo(pendingNew ? LogoStatus.Pending : LogoStatus.None, null, pendingNew)
            : new CompanyLogo(LogoStatus.Clean, clean.Sha256[..16], pendingNew);
    }
}
