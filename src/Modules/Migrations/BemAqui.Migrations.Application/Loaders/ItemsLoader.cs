using System.Globalization;
using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Produtos do <c>llx_product</c> viram itens (MIG-01c CA 3 a 9): unidade pelo de-para das unidades (ou a chave <c>none</c>
/// do mapa <c>units</c>), primeira categoria pela ordem do <c>rowid</c>, garantia do <c>elev_garantia</c>. Item de produção
/// sem NCM entra inativo (invariante da CAD-02a). Nada de custo, saldo ou lote é lido (ADR 0017).
/// </summary>
public sealed partial class ItemsLoader(IItemImport items) : IDolibarrLoader, IRowImporter
{
    public const string Target = "item";

    private ImportSession? _session;
    private Dictionary<string, string?> _warranty = new(StringComparer.Ordinal);
    private Dictionary<string, List<int>> _categories = new(StringComparer.Ordinal);

    public string Entity => "items";

    public int Order => 53;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.Product, SnapshotTables.ProductExtrafields, SnapshotTables.CategorieProduct];

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _warranty = (await session.LookupAsync(SnapshotTables.ProductExtrafields, cancellationToken))
            .Where(r => r.WholeNumber("fk_object") is not null).GroupBy(r => r.WholeNumber("fk_object")!.Value.ToString(CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => DolibarrConvert.Text(g.First()["elev_garantia"]), StringComparer.Ordinal);
        _categories = (await session.LookupAsync(SnapshotTables.CategorieProduct, cancellationToken))
            .Where(r => r.WholeNumber("fk_product") is not null && r.WholeNumber("fk_categorie") is not null)
            .GroupBy(r => r.WholeNumber("fk_product")!.Value.ToString(CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.Select(r => r.WholeNumber("fk_categorie")!.Value).Order().ToList(), StringComparer.Ordinal);

        var rows = new List<SourceRow>();
        await foreach (var row in session.ReadAsync(SnapshotTables.Product, cancellationToken))
        {
            rows.Add(row);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.OrderBy(r => r.WholeNumber("rowid")))
        {
            if (row.WholeNumber("entity") is { } entity && entity != 1)
            {
                session.Skip(row, "Outra empresa do Dolibarr (entity " + entity.ToString(CultureInfo.InvariantCulture) + ").");
                continue;
            }

            var reference = (row.Text("ref") ?? row.Id).Trim();
            if (!seen.Add(reference) && await session.FindRefAsync(row.Table.Name, row.Id, cancellationToken) is null)
            {
                await session.ProcessAsync(row, new Failing("Código repetido no Dolibarr: " + reference + "."), cancellationToken);
                continue;
            }

            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    /// <summary>Registra o erro pela sessão (para a soma do relatório fechar), sem gravar nada.</summary>
    private sealed class Failing(string reason) : IRowImporter
    {
        public Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken) => Task.FromResult(RowResult.Fail(reason));

        public Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken) => Task.FromResult(RowResult.Fail(reason));

        public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);

        public Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FieldDifference>>([]);
    }
}
