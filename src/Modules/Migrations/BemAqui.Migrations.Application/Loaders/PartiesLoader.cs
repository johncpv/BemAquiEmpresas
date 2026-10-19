using System.Globalization;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;
using BemAqui.Parties.Contracts;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Terceiros do <c>llx_societe</c> (MIG-01b CA 2 a 10, 14, 18). As linhas vão da mais antiga para a mais recente (<c>tms</c>):
/// documento repetido na carga é mesclado no terceiro já importado, e os dados principais ficam os do registro mais recente
/// (CA 6); documento que já existe sem chave externa é vinculado, sem sobrescrever (CA 7).
/// </summary>
public sealed class PartiesLoader(IPartyImport parties) : IDolibarrLoader, IRowImporter
{
    public const string Target = "party";

    private PartyLookups _lookups = new(new Dictionary<int, string>(), new Dictionary<int, string>(), new Dictionary<int, IReadOnlyList<int>>());
    private ImportSession? _session;

    public string Entity => "parties";

    public int Order => 30;

    public IReadOnlyList<SnapshotTable> SourceTables =>
        [SnapshotTables.Societe, SnapshotTables.Departements, SnapshotTables.TypeEnt, SnapshotTables.CategorieFournisseur, SnapshotTables.PaymentTerm];

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _lookups = await LookupsAsync(session, cancellationToken);
        var rows = new List<SourceRow>();
        await foreach (var row in session.ReadAsync(SnapshotTables.Societe, cancellationToken))
        {
            rows.Add(row);
        }

        foreach (var row in rows.OrderBy(r => r["tms"] as DateTime? ?? DateTime.MinValue).ThenBy(r => r.WholeNumber("rowid")))
        {
            if (row.WholeNumber("entity") is { } entity && entity != 1)
            {
                session.Skip(row, "Outra empresa do Dolibarr (entity " + entity.ToString(CultureInfo.InvariantCulture) + ").");
                continue;
            }

            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    public static async Task<PartyLookups> LookupsAsync(ImportSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        var states = (await session.LookupAsync(SnapshotTables.Departements, ct))
            .Where(r => r.WholeNumber("rowid") is not null && r.Text("code_departement") is not null)
            .ToDictionary(r => r.WholeNumber("rowid")!.Value, r => r.Text("code_departement")!.ToUpperInvariant());
        var types = (await session.LookupAsync(SnapshotTables.TypeEnt, ct))
            .Where(r => r.WholeNumber("id") is not null && r.Text("code") is not null)
            .GroupBy(r => r.WholeNumber("id")!.Value).ToDictionary(g => g.Key, g => g.First().Text("code")!);
        var categories = (await session.LookupAsync(SnapshotTables.CategorieFournisseur, ct))
            .Where(r => r.WholeNumber("fk_soc") is not null && r.WholeNumber("fk_categorie") is not null)
            .GroupBy(r => r.WholeNumber("fk_soc")!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(r => r.WholeNumber("fk_categorie")!.Value).ToList());
        return new PartyLookups(states, types, categories);
    }

    private async Task<MappedParty> MapAsync(SourceRow row, CancellationToken ct)
    {
        var session = _session ?? throw new InvalidOperationException("Carregador usado fora da sessão.");
        Guid? term = null;
        if (row.WholeNumber("cond_reglement") is { } condition)
        {
            term = (await session.FindRefAsync(SnapshotTables.PaymentTerm.Name, condition.ToString(CultureInfo.InvariantCulture), ct))?.TargetId;
        }

        return PartyRowMapper.Map(row, _lookups, session.Mapping, term);
    }

    private static string? Join(IReadOnlyList<string> warnings) => warnings.Count == 0 ? null : string.Join("; ", warnings);

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var mapped = await MapAsync(row, cancellationToken);
        if (mapped.Data is not { } data)
        {
            return RowResult.Fail(mapped.Error!);
        }

        if (data.Document is { } document && await parties.FindByDocumentAsync(document, cancellationToken) is { } existing)
        {
            if (await _session!.TargetHasRefAsync(Target, existing, cancellationToken))
            {
                var merged = await parties.UpdateAsync(existing, data, cancellationToken);
                return merged.IsFailure ? RowResult.Fail(merged.Error!.Message)
                    : RowResult.MergedInto(Target, existing, merged.Value.Xmin, "Mesclado com outro registro do Dolibarr de mesmo documento.") with { Warning = Join(mapped.Warnings) };
            }

            var linked = await parties.LinkAsync(existing, data.Roles, data.ImportSource, cancellationToken);
            return linked.IsFailure ? RowResult.Fail(linked.Error!.Message)
                : RowResult.LinkedTo(Target, existing, linked.Value.Xmin, "Vinculado a cadastro existente no BemAqui, sem sobrescrever os dados.") with { Warning = Join(mapped.Warnings) };
        }

        var created = await parties.CreateAsync(data, cancellationToken);
        return created.IsFailure ? RowResult.Fail($"Não importado ({row.Id}, {data.LegalName}): {created.Error!.Message}")
            : RowResult.Created(Target, created.Value.Id, created.Value.Xmin, Join(mapped.Warnings));
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var mapped = await MapAsync(row, cancellationToken);
        if (mapped.Data is not { } data)
        {
            return RowResult.Fail(mapped.Error!);
        }

        var updated = await parties.UpdateAsync(targetId, data, cancellationToken);
        return updated.IsFailure ? RowResult.Fail(updated.Error!.Message) : RowResult.Changed(Target, targetId, updated.Value.Xmin, Join(mapped.Warnings));
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => parties.VersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var source = (await MapAsync(row, cancellationToken)).Data;
        var current = await parties.CurrentAsync(targetId, cancellationToken);
        if (source is null || current is null)
        {
            return [];
        }

        (string Field, string? Source, string? Target)[] fields =
        [
            ("legalName", source.LegalName, current.LegalName), ("tradeName", source.TradeName, current.TradeName), ("document", source.Document, current.Document),
            ("email", source.Email, current.Email), ("phone", source.Phone, current.Phone), ("isActive", source.IsActive ? "ativo" : "inativo", current.IsActive ? "ativo" : "inativo"),
        ];
        return fields.Where(f => f.Source != f.Target).Select(f => new FieldDifference(f.Field, f.Source, f.Target)).ToList();
    }
}
