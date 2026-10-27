using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>O que vai entrar de cada arquivo (sem valores).</summary>
public sealed record AccountantSectionPreview(string Key, string? FileName, bool Included, string Summary, int Rows);

/// <summary>Folha da competência ainda em rascunho (aviso com o atalho para a folha).</summary>
public sealed record AccountantDraftRun(Guid Id, PayrollRunType Type);

/// <summary>Exportação anterior (CA 5): quando, quem, a competência e a versão do layout, lida da auditoria (<c>SensitiveRead</c>).</summary>
public sealed record AccountantExportRecord(Guid Id, DateTimeOffset At, string? UserName, DateOnly Period, string Layout);

/// <summary>
/// Tela "Exportar para a contadora" (RH-03): a competência (sugerida = o mês anterior), o layout e se a contadora já conferiu, o que vai entrar
/// ("Movimentação: 3 eventos"), o que fica de fora, as folhas em rascunho, a exportação anterior da mesma competência (CA 6) e as
/// exportações anteriores.
/// </summary>
public sealed record AccountantExportOverview(
    DateOnly Period,
    DateOnly SuggestedPeriod,
    string Layout,
    string LayoutName,
    bool LayoutConfirmed,
    IReadOnlyList<AccountantSectionPreview> Sections,
    IReadOnlyList<string> NotIncluded,
    IReadOnlyList<AccountantDraftRun> DraftRuns,
    AccountantExportRecord? Previous,
    IReadOnlyList<AccountantExportRecord> History);

/// <summary>O ZIP gerado na hora: nunca fica guardado no servidor (CA 5).</summary>
public sealed record AccountantPackage(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Pacote mensal para a contadora (RH-03, R-RH-8, P-06): o ZIP com os arquivos do layout escolhido no parâmetro do RH (padrão <c>v1</c>, "A
/// confirmar com a contadora", ADR 0042) e o <c>leia-me.txt</c>. Só com <c>hr.view_sensitive</c> (na rota e aqui); cada geração grava
/// <c>SensitiveRead</c> (entidade <c>AccountantExport</c>, a competência, o layout, quem e o IP), sem valores. Nada vai para log nem
/// outbox, e o arquivo não é gravado em lugar nenhum.
/// </summary>
public sealed class AccountantExportService(
    IHrDb db,
    IEnumerable<IAccountantExport> layouts,
    IPermissionChecker permissions,
    IAuditWriter audit,
    IAuditReader auditReader,
    IUserDirectory users,
    TimeProvider clock)
{
    public const string AuditEntity = "AccountantExport";
    public const string ContentType = "application/zip";
    public const int HistorySize = 50;

    /// <summary>Quantas gerações são lidas para achar a anterior da mesma competência (CA 6).</summary>
    public const int LookupSize = 300;

    public async Task<Result<AccountantExportOverview>> OverviewAsync(string? period, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var today = BusinessTime.Today(clock);
        var suggested = AccountantLayoutV1.SuggestedPeriod(today);
        var chosen = string.IsNullOrWhiteSpace(period) ? suggested : PayrollRules.ParsePeriod(period);
        if (chosen is not { } start || !AccountantLayoutV1.Exportable(start, today))
        {
            return InvalidPeriod();
        }

        var (export, confirmed) = await LayoutAsync(cancellationToken);
        var sections = await export.BuildAsync(new AccountantExportContext(start, await CanHealthAsync(cancellationToken)), cancellationToken);
        var drafts = await db.PayrollRuns.AsNoTracking().Where(r => r.Period == start && r.Status == PayrollRunStatus.Draft).OrderBy(r => r.Type)
            .Select(r => new AccountantDraftRun(r.Id, r.Type)).ToListAsync(cancellationToken);
        var history = await HistoryAsync(cancellationToken);
        return new AccountantExportOverview(start, suggested, export.Layout, LayoutName(export.Layout), confirmed,
            sections.Select(s => new AccountantSectionPreview(s.Key, s.FileName, s.Included, s.Summary, s.Rows)).ToList(),
            sections.SelectMany(s => s.NotIncluded).ToList(), drafts, history.FirstOrDefault(h => h.Period == start), history.Take(HistorySize).ToList());
    }

    /// <summary>Gera o pacote (CA 1 a 7): o ZIP em memória, o <c>SensitiveRead</c> gravado antes de devolver.</summary>
    public async Task<Result<AccountantPackage>> PackageAsync(string? period, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var today = BusinessTime.Today(clock);
        if (PayrollRules.ParsePeriod(period) is not { } start || !AccountantLayoutV1.Exportable(start, today))
        {
            return InvalidPeriod();
        }

        var (export, confirmed) = await LayoutAsync(cancellationToken);
        var sections = await export.BuildAsync(new AccountantExportContext(start, await CanHealthAsync(cancellationToken)), cancellationToken);
        var previous = (await HistoryAsync(cancellationToken)).FirstOrDefault(h => h.Period == start);
        var now = clock.GetUtcNow();
        var readme = AccountantLayoutV1.Readme(export.Layout, confirmed, start, BusinessTime.ToBusiness(now).DateTime,
            previous is null ? null : BusinessTime.ToBusiness(previous.At).DateTime,
            sections.Where(s => s.Included).Select(s => $"{s.FileName}: {s.Summary}."),
            sections.SelectMany(s => s.NotIncluded));

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var section in sections.Where(s => s.Included && s.Content is not null))
            {
                await WriteAsync(zip, section.FileName!, section.Content!, now, cancellationToken);
            }

            await WriteAsync(zip, AccountantLayoutV1.ReadmeFile, [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(readme)], now, cancellationToken);
        }

        await audit.RecordReadAsync(AuditEntity, start.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), export.Layout, cancellationToken);
        return new AccountantPackage($"contadora-{start:yyyy-MM}-{export.Layout}.zip", ContentType, buffer.ToArray());
    }

    private static async Task WriteAsync(ZipArchive zip, string name, byte[] content, DateTimeOffset at, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = BusinessTime.ToBusiness(at);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, ct);
    }

    /// <summary>O layout do parâmetro <c>hr.accountant_export_layout</c> (padrão <c>v1</c>) e se a contadora já conferiu.</summary>
    private async Task<(IAccountantExport Export, bool Confirmed)> LayoutAsync(CancellationToken ct)
    {
        var parameter = await db.HrParameters.AsNoTracking().Where(p => p.Key == HrParametersService.AccountantExportLayout).Select(p => new { p.Text, p.ConfirmedAt }).FirstOrDefaultAsync(ct);
        var all = layouts.ToList();
        var export = all.FirstOrDefault(l => l.Layout == (parameter?.Text ?? AccountantLayouts.Default)) ?? all.First(l => l.Layout == AccountantLayouts.Default);
        return (export, parameter?.ConfirmedAt is not null && export.Layout == parameter.Text);
    }

    private static string LayoutName(string layout) => AccountantLayouts.Available.FirstOrDefault(l => l.Code == layout).Name ?? layout;

    private async Task<List<AccountantExportRecord>> HistoryAsync(CancellationToken ct)
    {
        var entries = await auditReader.ListAsync(AuditEntity, AuditActions.SensitiveRead, LookupSize, ct);
        var names = new Dictionary<Guid, string?>();
        foreach (var by in entries.Select(e => e.UserId).OfType<Guid>().Distinct())
        {
            names[by] = await users.DisplayNameAsync(by, ct);
        }

        var records = new List<AccountantExportRecord>();
        foreach (var e in entries)
        {
            if (PayrollRules.ParsePeriod(e.EntityId) is { } period)
            {
                records.Add(new AccountantExportRecord(e.Id, e.At, e.UserId is { } by ? names.GetValueOrDefault(by) : null, period, LayoutOf(e.Changes)));
            }
        }

        return records;
    }

    /// <summary>A versão do layout gravada na referência do <c>SensitiveRead</c>.</summary>
    private static string LayoutOf(string changes)
    {
        try
        {
            using var json = JsonDocument.Parse(changes);
            return json.RootElement.TryGetProperty("reference", out var reference) && reference.TryGetProperty("new", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? AccountantLayouts.Default
                : AccountantLayouts.Default;
        }
        catch (JsonException)
        {
            return AccountantLayouts.Default;
        }
    }

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    private ValueTask<bool> CanHealthAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", EmployeeService.HealthAction, ct);

    private static Error InvalidPeriod() => PayrollRules.Field("period", "Escolha o mês atual ou um mês anterior.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso à exportação para a contadora. Fale com o administrador.");
}
