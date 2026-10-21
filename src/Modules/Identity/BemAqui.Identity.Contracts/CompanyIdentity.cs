namespace BemAqui.Identity.Contracts;

/// <summary>
/// Dados da empresa para documentos (PDF do orçamento, COM-03 CA 1 e 4): razão social, CNPJ do catálogo, endereço e contato do
/// perfil (CAD-06) e o logo publicado (só <c>Clean</c>), lido depois pelo storage.
/// </summary>
public sealed record CompanyIdentity(
    string LegalName, string? TradeName, string? Document, string? Address, string? Phone, string? Email, bool Complete, string? LogoStoragePath, string? LogoContentType);

public interface ICompanyIdentity
{
    Task<CompanyIdentity> GetAsync(CancellationToken cancellationToken = default);
}
