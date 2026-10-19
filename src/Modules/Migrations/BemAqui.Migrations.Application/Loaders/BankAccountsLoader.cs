using BemAqui.Finance.Contracts;
using BemAqui.Migrations.Application.Snapshot;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Contas do <c>llx_bank_account</c> (MIG-01b CA 15 e 16): só o cadastro, sem saldo nem data. De-para <c>bank-codes</c> e
/// <c>bank-agencies</c> (id da conta → código COMPE e agência) para o que o Dolibarr não tem.
/// </summary>
public sealed class BankAccountsLoader(IBankAccountImport accounts) : IDolibarrLoader, IRowImporter
{
    public const string Target = "bank_account";

    public string Entity => "bank-accounts";

    public int Order => 20;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.BankAccount];

    private ImportSession? _session;

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        await foreach (var row in session.ReadAsync(SnapshotTables.BankAccount, cancellationToken))
        {
            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    /// <summary>Monta a conta; null com o motivo quando falta o banco (CA 15).</summary>
    public static (BankAccountImportData? Data, string? Error) Map(SourceRow row, ImportMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);
        var reference = row.Text("ref") ?? row.Id;
        var kind = row.WholeNumber("courant") == 2 ? BankAccountKind.Cash : BankAccountKind.Bank;
        var name = row.Text("label") ?? reference;
        var source = Domain.ExternalRef.ImportSource(row.Table.Name, row.Id);
        var active = row.WholeNumber("clos") != 1;
        if (kind == BankAccountKind.Cash)
        {
            return (new BankAccountImportData(name, kind, null, null, null, active, source), null);
        }

        var digits = DolibarrText.Digits(row.Text("code_banque"));
        var code = mapping.Get("bank-codes", row.Id) ?? (digits.Length == 3 ? digits : null);
        if (code is null)
        {
            return (null, $"Conta {reference} sem código de banco: informe no de-para.");
        }

        var agency = mapping.Get("bank-agencies", row.Id) ?? row.Text("code_guichet");
        var number = row.Text("number");
        var key = row.Text("cle_rib");
        var account = number is not null && key is not null && !number.Contains('-', StringComparison.Ordinal) ? number + "-" + key : number;
        return (new BankAccountImportData(name, kind, code, agency, account, active, source), null);
    }

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var (data, error) = Map(row, _session!.Mapping);
        if (data is null)
        {
            return RowResult.Fail(error!);
        }

        var created = await accounts.CreateAsync(data, cancellationToken);
        return created.IsFailure ? RowResult.Fail($"Conta {row.Text("ref") ?? row.Id}: {created.Error!.Message}")
            : RowResult.Created(Target, created.Value.Id, created.Value.Xmin, "entrou sem saldo inicial; informe em Financeiro → Contas e caixas");
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var (data, error) = Map(row, _session!.Mapping);
        if (data is null)
        {
            return RowResult.Fail(error!);
        }

        var updated = await accounts.UpdateAsync(targetId, data, cancellationToken);
        return updated.IsFailure ? RowResult.Fail($"Conta {row.Text("ref") ?? row.Id}: {updated.Error!.Message}") : RowResult.Changed(Target, targetId, updated.Value.Xmin);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => accounts.VersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var current = await accounts.CurrentAsync(targetId, cancellationToken);
        var source = Map(row, _session!.Mapping).Data;
        if (current is null || source is null)
        {
            return [];
        }

        (string Field, string? Source, string? Target)[] fields = [("name", source.Name, current.Name), ("bankCode", source.BankCode, current.BankCode), ("agency", source.Agency, current.Agency), ("account", source.Account, current.Account)];
        return fields.Where(f => f.Source != f.Target).Select(f => new FieldDifference(f.Field, f.Source, f.Target)).ToList();
    }
}
