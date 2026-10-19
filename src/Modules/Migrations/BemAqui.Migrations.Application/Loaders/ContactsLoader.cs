using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Parties.Contracts;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>Contatos do <c>llx_socpeople</c> (MIG-01b CA 11): só de terceiro importado e ativos no Dolibarr.</summary>
public sealed class ContactsLoader(IPartyImport parties) : IDolibarrLoader, IRowImporter
{
    public const string Target = "party_contact";

    public string Entity => "contacts";

    public int Order => 40;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.SocPeople];

    private ImportSession? _session;

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        await foreach (var row in session.ReadAsync(SnapshotTables.SocPeople, cancellationToken))
        {
            if (row.WholeNumber("statut") == 0)
            {
                session.Skip(row, "Contato inativo no Dolibarr.");
            }
            else if (row.WholeNumber("fk_soc") is not { } soc || await session.FindRefAsync(SnapshotTables.Societe.Name, soc.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken) is null)
            {
                session.Skip(row, "Terceiro não importado.");
            }
            else
            {
                await session.ProcessAsync(row, this, cancellationToken);
            }
        }
    }

    private static (ContactImportData? Data, string? Warning) Map(SourceRow row)
    {
        var name = string.Join(' ', new[] { row.Text("firstname"), row.Text("lastname") }.OfType<string>()).Trim();
        if (name.Length == 0)
        {
            return (null, null);
        }

        var warnings = new List<string>();
        var email = DolibarrText.Email(row.Text("email"));
        if (email is null && row.Text("email") is not null)
        {
            warnings.Add("e-mail inválido, não importado");
        }

        var phone = DolibarrText.Phone(row.Text("phone")) ?? DolibarrText.Phone(row.Text("phone_mobile"));
        var role = row.Text("poste") is { Length: > 100 } long_ ? long_[..100] : row.Text("poste");
        return (new ContactImportData(name.Length > 150 ? name[..150] : name, role, email, phone), warnings.Count == 0 ? null : string.Join("; ", warnings));
    }

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var (data, warning) = Map(row);
        if (data is null)
        {
            return RowResult.Fail($"Contato sem nome no Dolibarr (id {row.Id}).");
        }

        var party = await _session!.FindRefAsync(SnapshotTables.Societe.Name, row.WholeNumber("fk_soc")!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        var created = await parties.AddContactAsync(party!.TargetId, data, cancellationToken);
        return created.IsFailure ? RowResult.Fail(created.Error!.Message) : RowResult.Created(Target, created.Value.Id, created.Value.Xmin, warning);
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var (data, warning) = Map(row);
        if (data is null)
        {
            return RowResult.Fail($"Contato sem nome no Dolibarr (id {row.Id}).");
        }

        var updated = await parties.UpdateContactAsync(targetId, data, cancellationToken);
        return updated.IsFailure ? RowResult.Fail(updated.Error!.Message) : RowResult.Changed(Target, targetId, updated.Value.Xmin, warning);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => parties.ContactVersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var source = Map(row).Data;
        var current = await parties.CurrentContactAsync(targetId, cancellationToken);
        if (source is null || current is null)
        {
            return [];
        }

        (string Field, string? Source, string? Target)[] fields = [("name", source.Name, current.Name), ("email", source.Email, current.Email), ("phone", source.Phone, current.Phone), ("role", source.Role, current.Role)];
        return fields.Where(f => f.Source != f.Target).Select(f => new FieldDifference(f.Field, f.Source, f.Target)).ToList();
    }
}
