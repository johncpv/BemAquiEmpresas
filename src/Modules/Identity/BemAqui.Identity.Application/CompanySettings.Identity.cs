using BemAqui.Identity.Contracts;

namespace BemAqui.Identity.Application;

public sealed partial class CompanySettings : ICompanyIdentity
{
    async Task<CompanyIdentity> ICompanyIdentity.GetAsync(CancellationToken cancellationToken)
    {
        var profile = await GetAsync(cancellationToken);
        var logo = await PublishedLogoAsync(cancellationToken);
        var street = string.Join(", ", new[] { profile.Street, profile.Number, profile.Complement }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var city = string.Join(" — ", new[] { profile.District, profile.City is null ? null : $"{profile.City}/{profile.State}", profile.ZipCode is null ? null : "CEP " + profile.ZipCode }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var address = string.Join(" · ", new[] { street, city }.Where(s => s.Length > 0));
        var complete = profile.Exists && !string.IsNullOrWhiteSpace(profile.Street) && !string.IsNullOrWhiteSpace(profile.City) && (profile.Phone is not null || profile.Email is not null);
        return new CompanyIdentity(profile.LegalName, profile.TradeName, profile.Document, address.Length > 0 ? address : null, profile.Phone, profile.Email, complete,
            logo?.StoragePath, logo?.ContentType);
    }
}
