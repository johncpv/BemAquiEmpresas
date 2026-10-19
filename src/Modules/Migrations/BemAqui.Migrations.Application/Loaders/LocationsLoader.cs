using System.Globalization;
using System.Text;
using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Depósitos do <c>llx_entrepot</c> (MIG-01c CA 11 e 12). Um único depósito ativo é ligado ao local padrão (ALM). Com vários,
/// cada um vira local, com o código saneado do <c>ref</c> e o sub-depósito como "{pai} / {filho}"; o de-para <c>locations</c>
/// (id do Dolibarr → código de um local do BemAqui) liga a um local existente. A carga nunca troca o local padrão.
/// </summary>
public sealed class LocationsLoader(ILocationImport locations) : IDolibarrLoader, IRowImporter
{
    public const string Target = "location";

    private ImportSession? _session;
    private Dictionary<string, SourceRow> _all = new(StringComparer.Ordinal);
    private bool _single;
    private string? _suggested;

    public string Entity => "locations";

    public int Order => 52;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.Entrepot];

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        var rows = new List<SourceRow>();
        await foreach (var row in session.ReadAsync(SnapshotTables.Entrepot, cancellationToken))
        {
            if (row.WholeNumber("entity") is { } entity && entity != 1)
            {
                session.Skip(row, "Outra empresa do Dolibarr (entity " + entity.ToString(CultureInfo.InvariantCulture) + ").");
                continue;
            }

            rows.Add(row);
        }

        _all = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        _single = rows.Count(r => r.WholeNumber("statut") == 1) == 1;
        // Sugestão de padrão para o relatório: o primeiro depósito ativo de nível mais alto.
        _suggested = rows.Where(r => r.WholeNumber("statut") == 1 && r.WholeNumber("fk_parent") is null or 0).OrderBy(r => r.WholeNumber("rowid")).FirstOrDefault()?.Id;
        foreach (var row in rows.OrderBy(r => r.WholeNumber("fk_parent") is > 0 ? 1 : 0).ThenBy(r => r.WholeNumber("rowid")))
        {
            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    /// <summary>Código do local: o <c>ref</c> em maiúsculas, só letras, números e hífen, até 20; sem nada aproveitável, DEP-{id}.</summary>
    public static string Code(SourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var text = BemAqui.SharedKernel.SearchText.Normalize(row.Text("ref") ?? row.Text("label")).ToUpperInvariant();
        var builder = new StringBuilder();
        foreach (var c in text)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        var code = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        code = code.Length > 20 ? code[..20].TrimEnd('-') : code;
        return code.Length == 0 ? "DEP-" + row.Id : code;
    }

    /// <summary>Nome: o <c>label</c> (ou <c>ref</c>); o sub-depósito leva o nome do pai na frente (CA 12).</summary>
    public static string Name(SourceRow row, IReadOnlyDictionary<string, SourceRow> all)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(all);
        var own = row.Text("label") ?? row.Text("ref") ?? row.Id;
        var parentId = row.WholeNumber("fk_parent") is > 0 and var p ? p.ToString(CultureInfo.InvariantCulture) : null;
        var name = parentId is not null && all.TryGetValue(parentId, out var parent) ? (parent.Text("label") ?? parent.Text("ref") ?? parentId) + " / " + own : own;
        return name.Length > 100 ? name[..100] : name;
    }

    private LocationImportData Data(SourceRow row) => new(Code(row), Name(row, _all), row.WholeNumber("statut") == 1, ExternalRef.ImportSource(row.Table.Name, row.Id));

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var data = Data(row);
        if (_session!.Mapping.Get("locations", row.Id) is { } mapped)
        {
            return await LinkAsync(await locations.FindAsync(mapped, cancellationToken), mapped, data, $"ligado ao local {mapped.ToUpperInvariant()} pelo de-para", cancellationToken);
        }

        if (_single && data.IsActive)
        {
            return await LinkAsync(await locations.DefaultAsync(cancellationToken), "padrão", data, "único depósito: ligado ao local padrão (ALM)", cancellationToken);
        }

        if (await locations.FindAsync(data.Code, cancellationToken) is { } existing)
        {
            return await LinkAsync(existing, data.Code, data, $"ligado ao local {data.Code}, que já existia", cancellationToken);
        }

        var created = await locations.CreateAsync(data, cancellationToken);
        if (created.IsFailure)
        {
            return RowResult.Fail($"Depósito {row.Text("ref") ?? row.Id}: {created.Error!.Message}");
        }

        var warning = row.Id == _suggested ? $"sugerido como local padrão; a carga não troca o padrão: confira em Estoque → Locais" : null;
        return RowResult.Created(Target, created.Value.Id, created.Value.Xmin, warning);
    }

    private async Task<RowResult> LinkAsync(Guid? target, string name, LocationImportData data, string message, CancellationToken ct)
    {
        if (target is not { } id)
        {
            return RowResult.Fail($"Depósito {data.Code}: o local {name} não existe no BemAqui.");
        }

        var linked = await locations.LinkAsync(id, data.ImportSource, ct);
        return linked.IsFailure ? RowResult.Fail(linked.Error!.Message) : RowResult.LinkedTo(Target, id, linked.Value.Xmin, message);
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        // Ligado a um local do BemAqui (ALM, de-para ou código existente): a origem não sobrescreve o cadastro.
        var current = await locations.CurrentAsync(targetId, cancellationToken);
        if (current is null || current.ImportSource != ExternalRef.ImportSource(row.Table.Name, row.Id))
        {
            return await locations.VersionAsync(targetId, cancellationToken) is { } xmin ? RowResult.Changed(Target, targetId, xmin) : RowResult.Fail("Local não encontrado.");
        }

        var updated = await locations.UpdateAsync(targetId, Data(row), cancellationToken);
        return updated.IsFailure ? RowResult.Fail(updated.Error!.Message) : RowResult.Changed(Target, targetId, updated.Value.Xmin);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => locations.VersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var current = await locations.CurrentAsync(targetId, cancellationToken);
        if (current is null)
        {
            return [];
        }

        var source = Data(row);
        (string Field, string? Source, string? Target)[] fields = [("code", source.Code, current.Code), ("name", source.Name, current.Name), ("isActive", source.IsActive.ToString(), current.IsActive.ToString())];
        return fields.Where(f => f.Source != f.Target).Select(f => new FieldDifference(f.Field, f.Source, f.Target)).ToList();
    }
}
