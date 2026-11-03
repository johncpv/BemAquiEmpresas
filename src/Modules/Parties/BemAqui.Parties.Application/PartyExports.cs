using BemAqui.BuildingBlocks.Export;
using BemAqui.Parties.Contracts;

namespace BemAqui.Parties.Application;

/// <summary>
/// Exportação de Cadastros → Terceiros (EXP-05 CA 18, ADR 0045 §3): as colunas da tela, na mesma consulta (<see cref="PartyService.ListAsync"/>),
/// com o CPF mascarado como a lista mostra. Os contatos não vão (ficam na ficha).
/// </summary>
public static class PartyExports
{
    public static readonly IReadOnlyDictionary<PartyRoleKind, string> RoleLabels = new Dictionary<PartyRoleKind, string>
    {
        [PartyRoleKind.Customer] = "Cliente",
        [PartyRoleKind.Supplier] = "Fornecedor",
        [PartyRoleKind.Installer] = "Instalador",
        [PartyRoleKind.Carrier] = "Transportadora",
    };

    public static readonly ExportSpec<PartyListItem> Spec = new ExportSpec<PartyListItem>("Party", "terceiros", "Terceiros")
        .Text("Nome/razão social", p => p.LegalName)
        .Text("Nome fantasia", p => p.TradeName)
        .Text("Pessoa", p => p.Kind == PartyKind.PF ? "Física" : "Jurídica").NotInPdf()
        .Text("CNPJ/CPF", p => p.Document ?? "Sem documento")
        .Text("Papéis", p => string.Join(", ", p.Roles.Select(r => RoleLabels.GetValueOrDefault(r, r.ToString()))))
        .Text("Cidade/UF", p => p.City is { Length: > 0 } city ? $"{city}/{p.State}" : null)
        .Text("Situação", p => p.IsActive ? "Ativo" : "Inativo");

    /// <summary>Os filtros da tela com os rótulos dela (vazio é "Todos").</summary>
    public static IReadOnlyList<ExportFilter> Filters(PartyListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return
        [
            new("Busca", string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim()),
            new("Papel", query.Role is { } role ? RoleLabels.GetValueOrDefault(role) : null),
            new("Inativos", query.IncludeInactive ? "Incluídos" : null),
        ];
    }
}
