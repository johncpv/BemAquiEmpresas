using System.Globalization;
using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application.Loaders;

public sealed partial class ItemsLoader
{
    /// <summary>Monta o item; null com o motivo quando falta a unidade (CA 1).</summary>
    private async Task<(ItemImportData? Data, string? Error, string? Warning)> DataAsync(SourceRow row, CancellationToken ct)
    {
        var mapped = ItemRowMapper.Map(row, _session!.Mapping, _warranty.GetValueOrDefault(row.Id));
        var warnings = mapped.Warnings.ToList();
        Guid unit;
        if (row.WholeNumber("fk_unit") is > 0 and var unitRow)
        {
            var found = (await _session.FindRefAsync(SnapshotTables.Units.Name, unitRow.ToString(CultureInfo.InvariantCulture), ct))?.TargetId;
            if (found is null)
            {
                return (null, "Produto " + mapped.Code + ": a unidade " + unitRow + " do Dolibarr não foi importada (veja as pendências de unidades).", null);
            }

            unit = found.Value;
        }
        else if (_session.Mapping.Get("units", "none") is { } fallback && await items.FindUnitAsync(fallback, ct) is { } byCode)
        {
            unit = byCode;
        }
        else
        {
            return (null, "Produto " + mapped.Code + " sem unidade: informe a chave none no mapa units (ex.: none = UN).", null);
        }

        Guid? category = null;
        if (_categories.TryGetValue(row.Id, out var cats))
        {
            category = (await _session.FindRefAsync(SnapshotTables.Categorie.Name, cats[0].ToString(CultureInfo.InvariantCulture), ct))?.TargetId;
            if (cats.Count > 1)
            {
                warnings.Add("em " + cats.Count + " categorias no Dolibarr: ficou na primeira");
            }
        }

        var months = mapped.UseDefaultWarranty ? await items.DefaultWarrantyAsync(mapped.Type, ct) : mapped.WarrantyMonths;
        var requiresNcm = mapped.Type is ItemType.RawMaterial or ItemType.Component or ItemType.Finished;
        var active = mapped.SellOrBuy;
        if (active && requiresNcm && mapped.Ncm is null)
        {
            active = false;
            warnings.Add("sem NCM: ativar depois de preencher");
        }

        var data = new ItemImportData(mapped.Type, mapped.Code, mapped.Name, unit, category, mapped.Ncm, mapped.MinStock, mapped.TracksLot, mapped.TracksSerial, months, active, ExternalRef.ImportSource(row.Table.Name, row.Id));
        return (data, null, warnings.Count == 0 ? null : string.Join(", ", warnings));
    }

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var (data, error, warning) = await DataAsync(row, cancellationToken);
        if (data is null)
        {
            return RowResult.Fail(error!);
        }

        if (await items.FindItemAsync(data.Code, cancellationToken) is { } existing)
        {
            var linked = await items.LinkItemAsync(existing, data.ImportSource, cancellationToken);
            return linked.IsFailure ? RowResult.Fail(linked.Error!.Message) : RowResult.LinkedTo(Target, existing, linked.Value.Xmin, "ligado ao item " + data.Code + ", que já existia no BemAqui");
        }

        var created = await items.CreateItemAsync(data, cancellationToken);
        return created.IsFailure ? RowResult.Fail("Produto " + data.Code + ": " + created.Error!.Message) : RowResult.Created(Target, created.Value.Id, created.Value.Xmin, warning);
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var (data, error, warning) = await DataAsync(row, cancellationToken);
        if (data is null)
        {
            return RowResult.Fail(error!);
        }

        var updated = await items.UpdateItemAsync(targetId, data, cancellationToken);
        return updated.IsFailure ? RowResult.Fail("Produto " + data.Code + ": " + updated.Error!.Message) : RowResult.Changed(Target, targetId, updated.Value.Xmin, warning);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => items.ItemVersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var current = await items.CurrentItemAsync(targetId, cancellationToken);
        var source = (await DataAsync(row, cancellationToken)).Data;
        if (current is null || source is null)
        {
            return [];
        }

        (string Field, string? Source, string? Target)[] fields =
        [
            ("code", source.Code, current.Code), ("name", source.Name, current.Name), ("type", source.Type.ToString(), current.Type.ToString()), ("ncm", source.Ncm, current.Ncm),
            ("minStock", source.MinStock.ToString(CultureInfo.InvariantCulture), current.MinStock.ToString(CultureInfo.InvariantCulture)),
            ("warrantyMonths", source.WarrantyMonths?.ToString(CultureInfo.InvariantCulture), current.WarrantyMonths?.ToString(CultureInfo.InvariantCulture)),
        ];
        return fields.Where(f => f.Source != f.Target).Select(f => new FieldDifference(f.Field, f.Source, f.Target)).ToList();
    }
}
