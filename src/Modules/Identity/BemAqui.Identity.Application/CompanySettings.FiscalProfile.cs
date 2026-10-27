using BemAqui.Identity.Contracts;

namespace BemAqui.Identity.Application;

public sealed partial class CompanySettings : ICompanyFiscalProfile
{
    /// <summary>Dados da empresa para Configurações → Fiscal (FIS-02a CA 3 e 7): o Fiscal lê daqui e não copia.</summary>
    public async Task<CompanyFiscalProfile> GetFiscalProfileAsync(CancellationToken cancellationToken = default)
    {
        var profile = await GetAsync(cancellationToken);
        var street = string.Join(", ", new[] { profile.Street, profile.Number, profile.Complement }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var city = string.Join(" — ", new[] { profile.District, profile.City is null ? null : $"{profile.City}/{profile.State}", profile.ZipCode is null ? null : "CEP " + profile.ZipCode }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var address = string.Join(" · ", new[] { street, city }.Where(s => s.Length > 0));
        return new CompanyFiscalProfile(profile.LegalName, profile.Document, Blank(profile.StateRegistration), Blank(profile.MunicipalRegistration),
            address.Length > 0 ? address : null, profile.City, profile.State, Blank(profile.CityIbge));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
