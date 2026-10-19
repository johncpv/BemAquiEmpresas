using BemAqui.Finance.Contracts;
using BemAqui.Migrations.Application.Snapshot;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>
/// Condições do <c>llx_c_payment_term</c> (MIG-01b CA 13). De-para <c>payment-terms</c> (id → <c>seed_key</c> ou nome de uma
/// condição do BemAqui); sem ele, <c>nbjour</c> 0 vai para "À vista" e 30 para "30 dias". Roda antes dos terceiros, que
/// apontam para a condição padrão (CA 14).
/// </summary>
public sealed class PaymentTermsLoader(IPaymentTermImport terms) : IDolibarrLoader, IRowImporter
{
    public const string Target = "payment_term";
    private const string MonthRule = "regra de fim de mês não suportada; conferir";

    public string Entity => "payment-terms";

    public int Order => 10;

    public IReadOnlyList<SnapshotTable> SourceTables => [SnapshotTables.PaymentTerm];

    private ImportSession? _session;

    public async Task LoadAsync(ImportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        await foreach (var row in session.ReadAsync(SnapshotTables.PaymentTerm, cancellationToken))
        {
            await session.ProcessAsync(row, this, cancellationToken);
        }
    }

    public static bool HasMonthRule(SourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return (row.WholeNumber("type_cdr") ?? 0) != 0 || (row.WholeNumber("decalage") ?? 0) != 0;
    }

    private static PaymentTermImportData Map(SourceRow row) =>
        new(row.Text("libelle") ?? row.Text("code") ?? "Condição " + row.Id, [new Installment(Math.Max(0, row.WholeNumber("nbjour") ?? 0), 100m)], row.WholeNumber("active") != 0,
            Domain.ExternalRef.ImportSource(row.Table.Name, row.Id));

    /// <summary>Condição do BemAqui equivalente, pelo de-para ou pelo padrão (CA 13).</summary>
    private async Task<Guid?> EquivalentAsync(SourceRow row, CancellationToken ct)
    {
        if (_session!.Mapping.Get("payment-terms", row.Id) is { } target)
        {
            return await terms.FindBySeedKeyAsync(target, ct) ?? await terms.FindByNameAsync(target, ct);
        }

        return HasMonthRule(row) ? null : (row.WholeNumber("nbjour") ?? 0) switch
        {
            0 => await terms.FindBySeedKeyAsync("a-vista", ct),
            30 => await terms.FindBySeedKeyAsync("30-dias", ct),
            _ => null,
        };
    }

    public async Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken)
    {
        var warning = HasMonthRule(row) ? MonthRule : null;
        if (await EquivalentAsync(row, cancellationToken) is { } existing)
        {
            var linked = await terms.LinkAsync(existing, Domain.ExternalRef.ImportSource(row.Table.Name, row.Id), cancellationToken);
            return linked.IsFailure ? RowResult.Fail(linked.Error!.Message)
                : RowResult.LinkedTo(Target, existing, linked.Value.Xmin, "Ligada a uma condição do BemAqui pelo de-para.") with { Warning = warning };
        }

        var created = await terms.CreateAsync(Map(row), cancellationToken);
        return created.IsFailure ? RowResult.Fail(created.Error!.Message) : RowResult.Created(Target, created.Value.Id, created.Value.Xmin, warning);
    }

    public async Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var updated = await terms.UpdateAsync(targetId, Map(row), cancellationToken);
        return updated.IsFailure ? RowResult.Fail(updated.Error!.Message) : RowResult.Changed(Target, targetId, updated.Value.Xmin, HasMonthRule(row) ? MonthRule : null);
    }

    public Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken) => terms.VersionAsync(targetId, cancellationToken);

    public async Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken)
    {
        var current = await terms.CurrentAsync(targetId, cancellationToken);
        var source = Map(row);
        return current is null || current.Name == source.Name ? [] : [new FieldDifference("name", source.Name, current.Name)];
    }
}
