using System.Globalization;
using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Categorias de produto do <c>llx_categorie</c> (tipo 0) viram a árvore de categorias de itens (MIG-01c CA 2): os pais vêm
/// antes dos filhos. As de fornecedor e cliente já foram lidas na MIG-01b e ficam como ignoradas.
/// </summary>
public sealed class ItemCategoriesLoader(IItemImport items) : IDolibarrLoader, IRowImporter
{
    public const string Target = "item_category";

    private ImportSession? _session;

    public string Entity => "item-categories";

    public int Order => 51;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.Categorie];

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        var rows = new List<SourceRow>();
        await foreach (var row in session.ReadAsync(SnapshotTables.Categorie, cancellationToken))
        {
            if (row.WholeNumber("type") != 0)
            {
                session.Skip(row, "Categoria de fornecedor ou cliente (não é de produto).");
            }
            else if (row.WholeNumber("entity") is { } entity && entity != 1)
            {
                session.Skip(row, "Outra empresa do Dolibarr (entity " + entity.ToString(CultureInfo.InvariantCulture) + ").");
            }
            else
            {
                rows.Add(row);
            }
        }

        foreach (var row in Ordered(rows))
        {
            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    /// <summary>Pais antes dos filhos (profundidade na árvore), depois pelo <c>rowid</c>; ciclo na origem fica no fim.</summary>
    public static IEnumerable<SourceRow> Ordered(IReadOnlyList<SourceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var parents = rows.ToDictionary(r => r.Id, r => r.WholeNumber("fk_parent") is > 0 and var p ? p.ToString(CultureInfo.InvariantCulture) : null, StringComparer.Ordinal);
        int Depth(string id)
        {
            var depth = 0;
            for (var at = parents.GetValueOrDefault(id); at is not null && parents.ContainsKey(at) && depth < rows.Count; at = parents.GetValueOrDefault(at))
            {
                depth++;
            }

            return depth;
        }

        return rows.OrderBy(r => Depth(r.Id)).ThenBy(r => r.WholeNumber("rowid"));
    }

    private async Task<Guid?> ParentAsync(SourceRow row, CancellationToken ct) =>
        row.WholeNumber("fk_parent") is > 0 and var parent
            ? (await _session!.FindRefAsync(row.Table.Name, parent.ToString(CultureInfo.InvariantCulture), ct))?.TargetId
            : null;

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var created = await items.CreateCategoryAsync(row.Text("label") ?? row.Id, await ParentAsync(row, cancellationToken), ExternalRef.ImportSource(row.Table.Name, row.Id), cancellationToken);
        return created.IsFailure ? RowResult.Fail($"Categoria {row.Text("label") ?? row.Id}: {created.Error!.Message}") : RowResult.Created(Target, created.Value.Id, created.Value.Xmin);
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var updated = await items.UpdateCategoryAsync(targetId, row.Text("label") ?? row.Id, await ParentAsync(row, cancellationToken), cancellationToken);
        return updated.IsFailure ? RowResult.Fail(updated.Error!.Message) : RowResult.Changed(Target, targetId, updated.Value.Xmin);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => items.CategoryVersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var current = await items.CategoryNameAsync(targetId, cancellationToken);
        var source = row.Text("label");
        return current == source ? [] : [new FieldDifference("name", source, current)];
    }
}
