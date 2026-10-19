using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>Tabelas de apoio da carga de terceiros: UF por <c>fk_departement</c>, tipo por <c>fk_typent</c> e categorias de fornecedor.</summary>
public sealed record PartyLookups(IReadOnlyDictionary<int, string> States, IReadOnlyDictionary<int, string> Types, IReadOnlyDictionary<int, IReadOnlyList<int>> SupplierCategories);

/// <summary>Resultado do mapeamento: os dados ou o erro (CA 5), com os avisos que não impedem a gravação.</summary>
public sealed record MappedParty(PartyImportData? Data, string? Error, IReadOnlyList<string> Warnings);

/// <summary>
/// <c>llx_societe</c> para terceiro (MIG-01b CA 2 a 10). De-para: mapa <c>special-roles</c> (id → Installer/Carrier) e
/// <c>special-role-categories</c> (categoria de fornecedor → papel).
/// </summary>
public static class PartyRowMapper
{
    public const string District = "Não informado";

    public static MappedParty Map(SourceRow row, PartyLookups lookups, ImportMapping mapping, Guid? paymentTermId)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(mapping);
        var warnings = new List<string>();
        var name = row.Text("nom");
        var label = $"id {row.Id}, {name ?? "sem nome"}";
        var (roles, prospect) = Roles(row, lookups, mapping, warnings);

        var document = BrazilianDocuments.Normalize(row.Text("siren"));
        PartyKind kind;
        if (document.Length > 0)
        {
            var docKind = BrazilianDocuments.KindOf(document);
            if (docKind is null || !BrazilianDocuments.IsValid(document))
            {
                return new MappedParty(null, (docKind == DocumentKind.Cpf ? "CPF" : "CNPJ") + $" inválido no Dolibarr ({label}).", warnings);
            }

            kind = docKind == DocumentKind.Cpf ? PartyKind.PF : PartyKind.PJ;
        }
        else
        {
            kind = row.WholeNumber("fk_typent") is { } t && lookups.Types.GetValueOrDefault(t) == "TE_PRIVATE" ? PartyKind.PF : PartyKind.PJ;
            warnings.Add("sem documento no Dolibarr");
        }

        if (name is null || name.Length < 2)
        {
            return new MappedParty(null, $"Terceiro sem nome no Dolibarr ({label}).", warnings);
        }

        var email = DolibarrText.Email(row.Text("email"));
        if (email is null && row.Text("email") is not null)
        {
            warnings.Add("e-mail inválido, não importado");
        }

        var phone = DolibarrText.Phone(row.Text("phone"));
        if (phone is null && row.Text("phone") is not null)
        {
            warnings.Add("telefone sem DDD válido, não importado");
        }

        var data = new PartyImportData(kind, document.Length > 0 ? document : null, Truncate(name, 200)!, kind == PartyKind.PJ ? Truncate(row.Text("name_alias"), 200) : null,
            roles, email, phone, Notes(row, prospect, warnings), row.WholeNumber("status") != 0, paymentTermId, Address(row, lookups, warnings),
            Domain.ExternalRef.ImportSource(row.Table.Name, row.Id));
        return new MappedParty(data, null, warnings);
    }

    private static (List<PartyRoleKind> Roles, bool Prospect) Roles(SourceRow row, PartyLookups lookups, ImportMapping mapping, List<string> warnings)
    {
        var roles = new List<PartyRoleKind>();
        var client = row.WholeNumber("client") ?? 0;
        if (client is 1 or 2 or 3)
        {
            roles.Add(PartyRoleKind.Customer);
        }

        var supplier = row.WholeNumber("fournisseur") == 1;
        if (supplier)
        {
            roles.Add(PartyRoleKind.Supplier);
        }

        var special = new List<string?> { mapping.Get("special-roles", row.Id) };
        if (row.WholeNumber("rowid") is { } id && lookups.SupplierCategories.TryGetValue(id, out var categories))
        {
            special.AddRange(categories.Select(c => mapping.Get("special-role-categories", c.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        foreach (var value in special.OfType<string>())
        {
            if (Enum.TryParse<PartyRoleKind>(value, out var role) && role is PartyRoleKind.Installer or PartyRoleKind.Carrier)
            {
                roles.Add(role);
            }
        }

        if (supplier && mapping.Map("special-roles").Count == 0 && mapping.Map("special-role-categories").Count == 0)
        {
            warnings.Add("papel especial não informado no de-para");
        }

        if (roles.Count == 0)
        {
            roles.Add(PartyRoleKind.Customer);
            warnings.Add("sem papel no Dolibarr; entrou como Cliente");
        }

        return (roles.Distinct().ToList(), client == 2);
    }

    private static string? Notes(SourceRow row, bool prospect, List<string> warnings)
    {
        var parts = new List<string>();
        if (prospect)
        {
            parts.Add("Prospect no Dolibarr");
        }

        if (DolibarrText.HtmlToText(row.Text("note_private")) is { } privateNote)
        {
            parts.Add("Dolibarr — nota privada: " + privateNote);
        }

        if (DolibarrText.HtmlToText(row.Text("note_public")) is { } publicNote)
        {
            parts.Add("Dolibarr — nota pública: " + publicNote);
        }

        var notes = parts.Count == 0 ? null : string.Join("\n\n", parts);
        if (notes is { Length: > 2000 })
        {
            warnings.Add("observações cortadas em 2.000 caracteres");
            notes = notes[..1999] + "…";
        }

        return notes;
    }

    private static PartyImportAddress? Address(SourceRow row, PartyLookups lookups, List<string> warnings)
    {
        var street = row.Text("address");
        var zip = DolibarrText.Digits(row.Text("zip"));
        var city = row.Text("town");
        var state = row.WholeNumber("fk_departement") is { } dep ? lookups.States.GetValueOrDefault(dep) : null;
        if (street is null && zip.Length == 0 && city is null)
        {
            return null;
        }

        if (street is null || zip.Length != 8 || city is null || state is null)
        {
            warnings.Add("endereço incompleto no Dolibarr (logradouro, CEP, cidade ou UF); cadastre no BemAqui");
            return null;
        }

        var split = DolibarrText.Street(street);
        if (!split.Separated)
        {
            warnings.Add("endereço sem número separado");
        }

        warnings.Add("bairro não informado no Dolibarr");
        return new PartyImportAddress(zip, Truncate(split.Street, 200)!, Truncate(split.Number, 20)!, Truncate(split.Complement, 100), District, Truncate(city, 100)!, state);
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
