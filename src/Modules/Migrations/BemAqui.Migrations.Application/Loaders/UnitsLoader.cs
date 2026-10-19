using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Unidades do <c>llx_c_units</c> usadas pelos produtos ou pelos preços de fornecedor (MIG-01c CA 1). De-para <c>units</c>
/// (código ou id do Dolibarr → código do BemAqui, ou <c>criar</c>); sem de-para, vale o mesmo código quando o BemAqui já o
/// tem (e <c>U</c> → UN). A chave <c>none</c> do mesmo mapa diz a unidade do produto que não tem nenhuma.
/// </summary>
public sealed class UnitsLoader(IItemImport items) : IDolibarrLoader, IRowImporter
{
    public const string Target = "unit";
    public const string Create = "criar";

    private ImportSession? _session;

    public string Entity => "units";

    public int Order => 50;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.Units, SnapshotTables.Product, SnapshotTables.SupplierPrice];

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        var used = (await session.LookupAsync(SnapshotTables.Product, cancellationToken)).Select(r => r.WholeNumber("fk_unit"))
            .Concat((await session.LookupAsync(SnapshotTables.SupplierPrice, cancellationToken)).Select(r => r.WholeNumber("fk_unit")))
            .Where(u => u is > 0).Select(u => u!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        await foreach (var row in session.ReadAsync(SnapshotTables.Units, cancellationToken))
        {
            if (!used.Contains(row.Id))
            {
                session.Skip(row, "Não usada pelos produtos.");
                continue;
            }

            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    /// <summary>Código do BemAqui para a unidade do Dolibarr, <c>criar</c>, ou null sem de-para.</summary>
    public static string? TargetCode(SourceRow row, ImportMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);
        var code = (row.Text("code") ?? string.Empty).Trim();
        return mapping.Get("units", code) ?? mapping.Get("units", row.Id) ?? (code.Equals("U", StringComparison.OrdinalIgnoreCase) ? "UN" : code.Length > 0 ? code.ToUpperInvariant() : null);
    }

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var code = (row.Text("code") ?? row.Id).Trim();
        var target = TargetCode(row, _session!.Mapping);
        var source = ExternalRef.ImportSource(row.Table.Name, row.Id);
        if (target is null)
        {
            return RowResult.Fail($"Unidade {code} sem de-para: informe no mapa units.");
        }

        if (target.Equals(Create, StringComparison.OrdinalIgnoreCase))
        {
            var created = await items.CreateUnitAsync(code, row.Text("label") ?? code, source, cancellationToken);
            return created.IsFailure ? RowResult.Fail(created.Error!.Message) : RowResult.Created(Target, created.Value.Id, created.Value.Xmin);
        }

        if (await items.FindUnitAsync(target, cancellationToken) is not { } existing)
        {
            return RowResult.Fail($"Unidade {code}: o BemAqui não tem a unidade {target}. Cadastre em Estoque → Unidades ou use \"{Create}\" no mapa units.");
        }

        var linked = await items.LinkUnitAsync(existing, source, cancellationToken);
        return linked.IsFailure ? RowResult.Fail(linked.Error!.Message) : RowResult.LinkedTo(Target, existing, linked.Value.Xmin, $"ligada à unidade {target.ToUpperInvariant()}");
    }

    /// <summary>A unidade só é ligada ou criada; mudança no Dolibarr não altera o cadastro do BemAqui.</summary>
    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken) =>
        await items.UnitVersionAsync(targetId, cancellationToken) is { } xmin ? RowResult.Changed(Target, targetId, xmin) : RowResult.Fail("Unidade não encontrada.");

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => items.UnitVersionAsync(targetId, cancellationToken);

    public Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FieldDifference>>([]);
}
