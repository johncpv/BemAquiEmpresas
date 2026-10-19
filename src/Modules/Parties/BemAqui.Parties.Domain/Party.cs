using System.Globalization;
using System.Text;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Parties.Domain;

/// <summary>
/// Terceiro (<c>parties.parties</c>, PRD §5.2, R-TER-1): um cadastro só, com papéis. Documento opcional (tabela 1.1);
/// quando existe, é válido e único entre os não excluídos. Dado pessoal marcado com <see cref="PersonalDataAttribute"/>
/// sai mascarado na auditoria (ADR 0018).
/// </summary>
public sealed class Party : Entity, IAuditable, ISoftDeletable
{
    private string _legalName = string.Empty;
    private string? _tradeName;

    public PartyKind Kind { get; set; }

    /// <summary>CPF ou CNPJ normalizado (maiúsculas, sem pontuação). Sempre dado pessoal na auditoria, mesmo de PJ.</summary>
    [PersonalData]
    public string? Document { get; set; }

    public string LegalName
    {
        get => _legalName;
        set { _legalName = value; SearchText = BuildSearch(); }
    }

    public string? TradeName
    {
        get => _tradeName;
        set { _tradeName = value; SearchText = BuildSearch(); }
    }

    /// <summary>Nomes em minúsculas e sem acento, para a busca com pg_trgm (CA 18).</summary>
    public string SearchText { get; private set; } = string.Empty;

    public string? StateRegistration { get; set; }

    public string? MunicipalRegistration { get; set; }

    public TaxRegime TaxRegime { get; set; } = TaxRegime.NotInformed;

    [PersonalData]
    public string? Email { get; set; }

    [PersonalData]
    public string? Phone { get; set; }

    [PersonalData]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Condição de pagamento padrão (CAD-05b CA 16): só o id de <c>finance.payment_terms</c>, validado por <c>IPaymentTerms</c>.</summary>
    public Guid? DefaultPaymentTermId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public List<PartyRole> Roles { get; } = [];

    public List<PartyAddress> Addresses { get; } = [];

    public List<PartyContact> Contacts { get; } = [];

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        IsActive = true;
    }

    public static string Normalize(string? text)
    {
        var decomposed = (text ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private string BuildSearch() => Normalize(_legalName + " " + _tradeName);
}

/// <summary>Papel do terceiro (<c>parties.party_roles</c>), único por terceiro e papel.</summary>
public sealed class PartyRole : Entity, IAuditable
{
    public Guid PartyId { get; init; }

    public PartyRoleKind Role { get; init; }
}

/// <summary>Endereço (<c>parties.party_addresses</c>): cobrança, entrega ou obra; um padrão por tipo.</summary>
public sealed class PartyAddress : Entity, IAuditable
{
    public Guid PartyId { get; init; }

    public AddressKind Kind { get; set; }

    public string ZipCode { get; set; } = string.Empty;

    public string Street { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string? Complement { get; set; }

    public string District { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string? IbgeCode { get; set; }

    public string State { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
}

/// <summary>Contato (<c>parties.party_contacts</c>). Nome, e-mail e telefone são dado pessoal.</summary>
public sealed class PartyContact : Entity, IAuditable
{
    public Guid PartyId { get; init; }

    [PersonalData]
    public string Name { get; set; } = string.Empty;

    public string? Role { get; set; }

    [PersonalData]
    public string? Email { get; set; }

    [PersonalData]
    public string? Phone { get; set; }

    public bool ReceivesBilling { get; set; }
}
