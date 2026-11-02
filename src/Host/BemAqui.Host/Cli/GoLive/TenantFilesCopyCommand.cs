using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Host.Cli.GoLive;

/// <summary>
/// <c>tenant copy-files</c> (GOL-01a, ADR 0034 §2): o volume de arquivos da homologação mistura o <c>demo</c> e o <c>konebras</c>, e parte
/// dos arquivos não tem o prefixo do tenant. A lista sai do banco do tenant: <c>shared.attachments.storage_path</c> é o único lugar que
/// guarda chave do storage (todo arquivo passa pelo <c>AttachmentService</c>). Copia exatamente esses arquivos, no mesmo caminho, para o
/// volume novo, confere o SHA-256 gravado no anexo na origem e no destino, e relata os órfãos (no banco sem arquivo) e os arquivos sem
/// dono (na origem sem anexo do tenant nem da lista dos outros tenants). Nunca apaga nada; idempotente.
/// </summary>
internal static class TenantFilesCopyCommand
{
    public const int ListLimit = 20;

    public const string Usage =
        "Uso: tenant copy-files --tenant <chave> --from <raiz-do-volume-antigo> --to <raiz-do-volume-novo> [--other-tenants-paths <arquivo>]\n"
        + "       (--dry-run | --confirm-tenant <chave>)";

    public static async Task<int> RunAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var key = args.Get("tenant");
        var dryRun = args.Has("dry-run");
        if (string.IsNullOrWhiteSpace(key) || args.Get("from") is not { } fromArg || args.Get("to") is not { } toArg)
        {
            await output.WriteLineAsync("Informe --tenant, --from e --to. Nada foi copiado.\n" + Usage);
            return 1;
        }

        if (!dryRun && args.Get("confirm-tenant") != key)
        {
            await output.WriteLineAsync("Para copiar, repita a chave em --confirm-tenant " + key + " (ou rode antes com --dry-run). Nada foi copiado.");
            return 1;
        }

        var from = Path.GetFullPath(fromArg);
        var to = Path.GetFullPath(toArg);
        if (!Directory.Exists(from) || string.Equals(from, to, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("A origem precisa existir e ser diferente do destino. Nada foi copiado.");
            return 1;
        }

        var others = new HashSet<string>(StringComparer.Ordinal);
        if (args.Get("other-tenants-paths") is { } othersFile)
        {
            if (!File.Exists(othersFile))
            {
                await output.WriteLineAsync("Lista dos outros tenants não encontrada: " + othersFile + ". Nada foi copiado.");
                return 1;
            }

            others.UnionWith((await File.ReadAllLinesAsync(othersFile)).Select(l => l.Trim()).Where(l => l.Length > 0));
        }

        var tenant = await services.GetRequiredService<CatalogDbContext>().Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key);
        if (tenant is null || tenant.Status != TenantLifecycle.Active)
        {
            await output.WriteLineAsync("Tenant não encontrado ou não ativo: " + key + ". Nada foi copiado.");
            return 1;
        }

        await using var scope = await services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(new TenantInfo(tenant.Id, tenant.Key, TenantStatus.Active));
        var attachments = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Attachments.AsNoTracking()
            .Select(a => new { a.Id, a.StoragePath, a.Sha256, Removed = a.ContentRemovedAt != null })
            .ToListAsync();

        var report = new Report();
        var mine = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in attachments.Where(a => !a.Removed && !string.IsNullOrEmpty(a.StoragePath)))
        {
            mine.Add(a.StoragePath);
            var source = Resolve(from, a.StoragePath);
            var target = Resolve(to, a.StoragePath);
            if (source is null || target is null)
            {
                report.Invalid.Add(a.Id.ToString());
                continue;
            }

            if (!File.Exists(source))
            {
                report.Orphans.Add(a.Id.ToString());
                continue;
            }

            if (!string.Equals(await HashAsync(source), a.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                report.Divergent.Add(a.Id.ToString());
                continue;
            }

            if (File.Exists(target))
            {
                if (string.Equals(await HashAsync(target), a.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    report.Already++;
                }
                else
                {
                    report.Conflicts.Add(a.Id.ToString());
                }

                continue;
            }

            if (!dryRun && !await CopyAsync(source, target, a.Sha256))
            {
                report.Divergent.Add(a.Id.ToString());
                continue;
            }

            report.Copied++;
        }

        var sourceFiles = Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(from, f).Replace('\\', '/')).ToList();
        var ownerless = sourceFiles.Where(f => !mine.Contains(f) && !others.Contains(f)).Order(StringComparer.Ordinal).ToList();

        await output.WriteLineAsync("Cópia dos arquivos do tenant " + key + (dryRun ? " (simulação: nada é copiado)." : "."));
        await output.WriteLineAsync($"  Anexos no banco: {attachments.Count} ({attachments.Count(a => a.Removed)} com o conteúdo apagado pela LGPD, sem arquivo a copiar)");
        await output.WriteLineAsync($"  {(dryRun ? "A copiar" : "Copiados e conferidos")}: {report.Copied}   já no destino com o mesmo hash: {report.Already}");
        await WriteListAsync(output, "Órfãos (no banco, sem arquivo na origem)", report.Orphans);
        await WriteListAsync(output, "Hash diferente do gravado no anexo", report.Divergent);
        await WriteListAsync(output, "Destino já tem outro arquivo no mesmo caminho (não sobrescrito)", report.Conflicts);
        await WriteListAsync(output, "Caminho inválido (fora da raiz)", report.Invalid);
        await output.WriteLineAsync($"  Arquivos da origem de outros tenants (lista informada): {sourceFiles.Count(others.Contains)}");
        await WriteListAsync(output, "Arquivos sem dono (na origem, sem anexo deste tenant nem da lista dos outros)", ownerless);
        var problems = report.Orphans.Count + report.Divergent.Count + report.Conflicts.Count + report.Invalid.Count;
        await output.WriteLineAsync(problems == 0
            ? (dryRun ? "Simulação concluída. Para copiar, rode sem --dry-run e com --confirm-tenant " + key + "." : "Cópia concluída: todo anexo do tenant está no destino com o hash conferido.")
            : $"Atenção: {problems} anexo(s) com problema (listados acima por id). O resto foi {(dryRun ? "simulado" : "copiado")}; corrija e rode de novo.");
        return problems == 0 ? 0 : 2;
    }

    private static async Task WriteListAsync(TextWriter output, string title, List<string> items)
    {
        await output.WriteLineAsync($"  {title}: {items.Count}");
        foreach (var item in items.Take(ListLimit))
        {
            await output.WriteLineAsync("    " + item);
        }

        if (items.Count > ListLimit)
        {
            await output.WriteLineAsync($"    ... e mais {items.Count - ListLimit}");
        }
    }

    /// <summary>Copia para um temporário no destino, confere o hash e só então dá o nome final (nunca sobrescreve).</summary>
    private static async Task<bool> CopyAsync(string source, string target, string sha256)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".copiando";
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        await using (var copy = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await input.CopyToAsync(copy);
        }

        if (!string.Equals(await HashAsync(temporary), sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporary);
            return false;
        }

        File.Move(temporary, target, overwrite: false);
        return true;
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }

    /// <summary>Mesma defesa do <c>LocalFileStorage</c>: o caminho final precisa ficar dentro da raiz.</summary>
    private static string? Resolve(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    private sealed class Report
    {
        public int Copied { get; set; }

        public int Already { get; set; }

        public List<string> Orphans { get; } = [];

        public List<string> Divergent { get; } = [];

        public List<string> Conflicts { get; } = [];

        public List<string> Invalid { get; } = [];
    }
}
