namespace BemAqui.Identity.Contracts;

/// <summary>
/// Dados da empresa que o Fiscal lê sem copiar (FIS-02a CA 3 e 7, fonte única <c>settings.company_profile</c>, CAD-06): razão social,
/// CNPJ do catálogo, IE, IM, endereço e o código IBGE do município.
/// </summary>
public sealed record CompanyFiscalProfile(
    string LegalName, string? Document, string? StateRegistration, string? MunicipalRegistration, string? Address, string? City, string? State, string? CityIbge);

public interface ICompanyFiscalProfile
{
    Task<CompanyFiscalProfile> GetFiscalProfileAsync(CancellationToken cancellationToken = default);
}
