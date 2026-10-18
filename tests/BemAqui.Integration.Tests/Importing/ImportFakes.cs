using System.Runtime.CompilerServices;
using BemAqui.Host.Persistence;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>Snapshot em memória: as linhas de <c>llx_societe</c> que o teste monta, com o CNPJ da empresa no MAIN_INFO_SIREN.</summary>
internal sealed class FakeSnapshot(string? siren = "47.225.005/0001-86") : ISnapshotSource
{
    public List<Dictionary<string, object?>> Societe { get; } = [];

    public static Dictionary<string, object?> Company(int rowid, string name, string? document, string? email = null, int entity = 1) => new()
    {
        ["rowid"] = rowid, ["nom"] = name, ["siren"] = document, ["email"] = email, ["client"] = 1, ["fournisseur"] = 0, ["entity"] = entity,
        ["tms"] = DateTime.UtcNow,
    };

    public Task<DateTimeOffset?> TakenAtAsync(CancellationToken cancellationToken = default) => Task.FromResult<DateTimeOffset?>(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    public Task<string?> CompanyDocumentAsync(CancellationToken cancellationToken = default) => Task.FromResult(siren);

    public async IAsyncEnumerable<SourceRow> ReadAsync(SnapshotTable table, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var values in Societe.OrderBy(v => (int)v["rowid"]!))
        {
            yield return new SourceRow(table, Convert.ToString(values["rowid"], System.Globalization.CultureInfo.InvariantCulture)!, new Dictionary<string, object?>(values));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Carregador de teste (o real é da MIG-01b): grava terceiros pelo <see cref="PartyService"/>, como o <c>IPartyImport</c> fará,
/// e lê o <c>xmin</c> da linha de destino. <see cref="FailAfter"/> simula uma falha de infraestrutura no meio da entidade.
/// </summary>
internal sealed class TestPartyLoader(PartyService parties, TenantDbContext db) : IDolibarrLoader, IRowImporter
{
    public const string Name = "parties";

    public string Entity => Name;

    public int Order => 10;

    public int? FailAfter { get; set; }

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.Societe];

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        var processed = 0;
        await foreach (var row in session.ReadAsync(SnapshotTables.Societe, cancellationToken))
        {
            if (FailAfter == processed)
            {
                throw new InvalidOperationException("conexão perdida com o snapshot");
            }

            if (row.WholeNumber("entity") != 1)
            {
                session.Skip(row, "Outra entidade do Dolibarr (entity ≠ 1).");
            }
            else
            {
                await session.ProcessAsync(row, this, cancellationToken);
            }

            processed++;
        }
    }

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var created = await parties.CreateAsync(new CreatePartyRequest(PartyKind.PJ, row.Text("siren"), row.Text("nom") ?? string.Empty, null, [PartyRoleKind.Customer], Email: row.Text("email")), cancellationToken);
        return created.IsFailure ? RowResult.Fail(created.Error!.Message) : RowResult.Created("party", created.Value.Id, await XminAsync(created.Value.Id, cancellationToken) ?? 0);
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var updated = await parties.UpdateAsync(targetId, new UpdatePartyRequest(PartyKind.PJ, row.Text("siren"), row.Text("nom") ?? string.Empty, null, [PartyRoleKind.Customer], null, null, TaxRegime.NotInformed, row.Text("email"), null, null), cancellationToken);
        return updated.IsFailure ? RowResult.Fail(updated.Error!.Message) : RowResult.Changed("party", targetId, await XminAsync(targetId, cancellationToken) ?? 0);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => XminAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var current = await db.Parties.AsNoTracking().Where(p => p.Id == targetId).Select(p => new { p.LegalName, p.Email }).SingleAsync(cancellationToken);
        return [new FieldDifference("legalName", row.Text("nom"), current.LegalName), new FieldDifference("email", row.Text("email"), current.Email)];
    }

    private async Task<long?> XminAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM parties.parties WHERE id = {id} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken);
}
