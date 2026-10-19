using System.Globalization;
using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Códigos do fornecedor do <c>llx_product_fournisseur_price</c> (MIG-01c CA 10), sem preço nenhum. A unidade do fornecedor é o
/// código da <c>fk_unit</c> (ou o de-para <c>supplier-units</c>, id da linha → texto) e o fator vem do de-para
/// <c>supplier-factors</c> (id da linha → número), padrão 1. Fornecedor ou item não importado ficam como ignorados.
/// </summary>
public sealed class SupplierCodesLoader(IItemImport items) : IDolibarrLoader, IRowImporter
{
    public const string Target = "item_supplier_code";

    private ImportSession? _session;
    private Dictionary<string, string> _unitCodes = new(StringComparer.Ordinal);

    public string Entity => "supplier-codes";

    public int Order => 54;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.SupplierPrice, SnapshotTables.Units];

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _unitCodes = (await session.LookupAsync(SnapshotTables.Units, cancellationToken)).Where(r => r.Text("code") is not null)
            .ToDictionary(r => r.Id, r => r.Text("code")!.Trim().ToUpperInvariant(), StringComparer.Ordinal);
        await foreach (var row in session.ReadAsync(SnapshotTables.SupplierPrice, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(row.Text("ref_fourn")))
            {
                session.Skip(row, "Sem o código do fornecedor (ref_fourn vazio).");
                continue;
            }

            if (await RefAsync(SnapshotTables.Product.Name, row.WholeNumber("fk_product"), cancellationToken) is null)
            {
                session.Skip(row, "Ignorado: produto não importado.");
                continue;
            }

            if (await RefAsync(SnapshotTables.Societe.Name, row.WholeNumber("fk_soc"), cancellationToken) is null)
            {
                session.Skip(row, "Ignorado: fornecedor não importado.");
                continue;
            }

            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    private async Task<Guid?> RefAsync(string table, int? id, CancellationToken ct) =>
        id is > 0 ? (await _session!.FindRefAsync(table, id.Value.ToString(CultureInfo.InvariantCulture), ct))?.TargetId : null;

    /// <summary>Unidade do fornecedor em texto e fator (CA 10).</summary>
    public static (string? Unit, decimal Factor, string? Error) UnitAndFactor(SourceRow row, ImportMapping mapping, IReadOnlyDictionary<string, string> unitCodes)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(unitCodes);
        var unit = mapping.Get("supplier-units", row.Id)
            ?? (row.WholeNumber("fk_unit") is > 0 and var u ? unitCodes.GetValueOrDefault(u.ToString(CultureInfo.InvariantCulture)) : null);
        if (mapping.Get("supplier-factors", row.Id) is not { } text)
        {
            return (unit, 1m, null);
        }

        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var factor) && factor > 0
            ? (unit, factor, null)
            : (unit, 1m, "Fator \"" + text + "\" do de-para supplier-factors não é um número maior que zero.");
    }

    private async Task<(SupplierCodeImportData? Data, string? Error)> DataAsync(SourceRow row, CancellationToken ct)
    {
        var item = await RefAsync(SnapshotTables.Product.Name, row.WholeNumber("fk_product"), ct);
        var party = await RefAsync(SnapshotTables.Societe.Name, row.WholeNumber("fk_soc"), ct);
        if (item is null || party is null)
        {
            return (null, "Produto ou fornecedor não importado.");
        }

        var (unit, factor, error) = UnitAndFactor(row, _session!.Mapping, _unitCodes);
        return error is not null ? (null, error)
            : (new SupplierCodeImportData(item.Value, party.Value, row.Text("ref_fourn")!.Trim(), unit, factor, ExternalRef.ImportSource(row.Table.Name, row.Id)), null);
    }

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var (data, error) = await DataAsync(row, cancellationToken);
        if (data is null)
        {
            return RowResult.Fail(error!);
        }

        var created = await items.CreateSupplierCodeAsync(data, cancellationToken);
        return created.IsFailure ? RowResult.Fail(created.Error!.Message) : RowResult.Created(Target, created.Value.Id, created.Value.Xmin);
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var (data, error) = await DataAsync(row, cancellationToken);
        if (data is null)
        {
            return RowResult.Fail(error!);
        }

        var updated = await items.UpdateSupplierCodeAsync(targetId, data, cancellationToken);
        return updated.IsFailure ? RowResult.Fail(updated.Error!.Message) : RowResult.Changed(Target, targetId, updated.Value.Xmin);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => items.SupplierCodeVersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var current = await items.CurrentSupplierCodeAsync(targetId, cancellationToken);
        var source = (await DataAsync(row, cancellationToken)).Data;
        if (current is null || source is null)
        {
            return [];
        }

        (string Field, string? Source, string? Target)[] fields =
        [
            ("supplierCode", source.SupplierCode, current.SupplierCode), ("supplierUnit", source.SupplierUnit, current.SupplierUnit),
            ("conversionFactor", source.ConversionFactor.ToString(CultureInfo.InvariantCulture), current.ConversionFactor.ToString(CultureInfo.InvariantCulture)),
        ];
        return fields.Where(f => f.Source != f.Target).Select(f => new FieldDifference(f.Field, f.Source, f.Target)).ToList();
    }
}
