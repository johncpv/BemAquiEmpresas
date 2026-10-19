using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>
/// Perfil da empresa (<c>settings.company_profile</c>, CAD-06 CA 1 a 4): uma linha por tenant, fonte da verdade dos dados
/// que saem nos documentos. O CNPJ não fica aqui: vem do catálogo e só o suporte da plataforma altera. E-mail e telefone
/// são da empresa, não dado pessoal.
/// </summary>
public sealed class CompanyProfile : Entity, IAuditable
{
    public const string LogoOwnerType = "TenantLogo";

    public string LegalName { get; set; } = string.Empty;

    public string? TradeName { get; set; }

    public string? StateRegistration { get; set; }

    public string? MunicipalRegistration { get; set; }

    public string? ZipCode { get; set; }

    public string? Street { get; set; }

    public string? Number { get; set; }

    public string? Complement { get; set; }

    public string? District { get; set; }

    public string? City { get; set; }

    public string? CityIbge { get; set; }

    public string? State { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
