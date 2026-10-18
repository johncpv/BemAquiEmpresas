using System.Net.Mail;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Host.Cli;

/// <summary>
/// <c>tenant import-dolibarr</c> (MIG-01a, ADR 0017): só pela CLI, em container avulso na VPS. Recusa antes de gravar
/// quando o tenant não existe, está suspenso, tem schema defasado ou a chave não foi repetida em <c>--confirm-tenant</c>
/// (CA 6 e 7); o CNPJ do snapshot é conferido pelo executor. Com <c>--purge-staging</c>, expurga a staging (CA 22).
/// </summary>
internal static class DolibarrImportCommand
{
    public const string Usage = "Uso: tenant import-dolibarr --tenant <chave> --confirm-tenant <chave> (--dolibarr-api <arquivo-de-secret> | --snapshot <arquivo-de-secret>) --entities <lista> --operator <email> [--dry-run] [--mapping <arquivo>]\n"
        + "     tenant import-dolibarr --purge-staging --tenant <chave> [--accepted]";

    public static async Task<int> RunAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var key = args.Get("tenant");
        if (string.IsNullOrWhiteSpace(key))
        {
            await output.WriteLineAsync("Informe --tenant.\n" + Usage);
            return 1;
        }

        var catalog = services.GetRequiredService<CatalogDbContext>();
        var tenant = await catalog.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key);
        var refusal = tenant switch
        {
            null => "Tenant não encontrado: " + key + ".",
            { Status: TenantLifecycle.Suspended } => "O tenant " + key + " está suspenso. Reative-o antes de importar.",
            { Status: not TenantLifecycle.Active } => "O tenant " + key + " ainda não terminou o provisionamento.",
            _ when string.CompareOrdinal(tenant.SchemaVersion ?? string.Empty, TenantSchema.ExpectedVersion) < 0 => "O banco do tenant " + key + " está com o schema defasado. Rode o migrate antes de importar.",
            _ => null,
        };
        if (refusal is not null)
        {
            await output.WriteLineAsync(refusal + " Nada foi gravado.");
            return 1;
        }

        await using var scope = await services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(new TenantInfo(tenant!.Id, tenant.Key, TenantStatus.Active));
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MigrationOptions>>().Value;
        if (args.Has("purge-staging"))
        {
            var purged = await scope.ServiceProvider.GetRequiredService<StagingService>().PurgeAsync(args.Has("accepted"));
            await output.WriteLineAsync($"Staging expurgada: {purged.Runs} execução(ões), {purged.Items} linha(s) e {purged.Files} arquivo(s) do relatório. As chaves externas e os totais ficam.");
            return 0;
        }

        var request = await ParseAsync(args, key, output);
        if (request is null)
        {
            return 1;
        }

        ISnapshotSource source;
        try
        {
            var factory = scope.ServiceProvider.GetRequiredService<ISnapshotSourceFactory>();
            source = args.Get("dolibarr-api") is { } api ? await factory.OpenApiAsync(api) : await factory.OpenAsync(args.Get("snapshot")!);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            await output.WriteLineAsync(ex.Message + " Nada foi gravado.");
            return 2;
        }

        await using (source)
        {
            var result = await scope.ServiceProvider.GetRequiredService<ImportRunner>().RunAsync(source, request);
            if (result.Report is null)
            {
                await output.WriteLineAsync(result.Message);
                return result.ExitCode;
            }

            await output.WriteAsync(ImportReportWriter.Console(result.Report));
            var files = await ImportReportWriter.WriteFilesAsync(options.ReportDirectory, result.Report, result.Items ?? []);
            await output.WriteLineAsync("Relatório em " + string.Join(" e ", files) + " (permissão 0600; apagado no --purge-staging).");
            return result.ExitCode;
        }
    }

    private static async Task<ImportRequest?> ParseAsync(CliArguments args, string key, TextWriter output)
    {
        if (args.Get("confirm-tenant") != key)
        {
            await output.WriteLineAsync("--confirm-tenant precisa repetir a chave de --tenant (" + key + "). Nada foi gravado.");
            return null;
        }

        // Uma origem só: a API REST do Dolibarr (padrão, ADR 0017 adendo 2) ou o snapshot no MariaDB temporário.
        var snapshot = args.Get("dolibarr-api") is { } api ? (args.Has("snapshot") ? null : api) : args.Get("snapshot");
        var entities = (args.Get("entities") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var @operator = args.Get("operator");
        if (string.IsNullOrWhiteSpace(snapshot) || entities.Length == 0 || !MailAddress.TryCreate(@operator, out _))
        {
            await output.WriteLineAsync("Informe uma origem (--dolibarr-api ou --snapshot), --entities e o e-mail de quem executa em --operator.\n" + Usage);
            return null;
        }

        var mapping = ImportMapping.Empty;
        if (args.Get("mapping") is { } mappingFile)
        {
            if (!File.Exists(mappingFile))
            {
                await output.WriteLineAsync("Arquivo de de-para não encontrado: " + mappingFile + ".");
                return null;
            }

            var parsed = ImportMapping.Parse(await File.ReadAllTextAsync(mappingFile));
            if (parsed.IsFailure)
            {
                await output.WriteLineAsync(parsed.Error!.Message);
                return null;
            }

            mapping = parsed.Value;
        }

        return new ImportRequest(args.Has("dry-run") ? ImportMode.DryRun : ImportMode.Real, entities, @operator!, mapping);
    }
}
