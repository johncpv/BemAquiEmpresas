using System.Text.RegularExpressions;
using BemAqui.Host.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Architecture.Tests;

/// <summary>FND-09e CA 14 e 17 (ADR 0016 e 0019): posse de tabela, SQL cru sem schema alheio e escrita em massa sob controle.</summary>
public partial class DataOwnershipTests
{
    /// <summary>
    /// Escrita em massa (ExecuteUpdate/ExecuteDelete/ExecuteSql*) passa por fora do interceptor de auditoria. Só nestes
    /// arquivos, que não mudam estado de documento auditável; um documento novo usa a entidade rastreada (status + xmin)
    /// ou grava a auditoria explícita por <c>IAuditWriter</c> e entra aqui com o motivo (CA 14).
    /// </summary>
    private static readonly Dictionary<string, string> BulkWriteAllowlist = new(StringComparer.Ordinal)
    {
        ["Platform/BemAqui.Platform.Application/Auth/PlatformAuthService.TwoFactor.cs"] = "anti-replay do TOTP e códigos de recuperação da plataforma",
        ["Identity/BemAqui.Identity.Application/TwoFactorService.cs"] = "troca dos códigos de recuperação",
        ["Identity/BemAqui.Identity.Application/TwoFactorService.Challenge.cs"] = "anti-replay do TOTP e uso do código de recuperação",
        ["Identity/BemAqui.Identity.Application/PermissionService.cs"] = "versão do cache de permissões (ADR 0008)",
        ["Parties/BemAqui.Parties.Application/CnpjLookupService.cs"] = "expurgo do cache de consultas de CNPJ",
        ["Migrations/BemAqui.Migrations.Application/StagingService.cs"] = "expurgo da staging da importação (ADR 0017)",
        ["Migrations/BemAqui.Migrations.Application/ImportSession.cs"] = "xmin gravado na staging da importação",
        ["Production/BemAqui.Production.Application/ScheduleService.cs"] = "substituição do plano (schedule_allocations e schedule_step_forecasts) numa transação; as tabelas do plano ficam fora da auditoria e o histórico é a schedule_runs (PLN-01a, ADR 0029)",
        ["Inventory/BemAqui.Inventory.Application/StockLedger.cs"] = "trava do motor de estoque: INSERT … ON CONFLICT DO NOTHING da posição, do lote e do saldo por lote antes do SELECT … FOR UPDATE (ADR 0023 §2); o movimento é a trilha",
    };

    [GeneratedRegex(@"\.(ExecuteUpdate|ExecuteDelete|ExecuteSql)\w*\(")]
    private static partial Regex BulkWrite();

    [GeneratedRegex(@"\b(?:from|join|into|update)\s+([a-z_]+)\.[a-z_]+", RegexOptions.IgnoreCase)]
    private static partial Regex SchemaReference();

    private static string ModulesDirectory => Path.Combine(Solution.RootDirectory, "src", "Modules");

    private static IEnumerable<(string Module, string RelativePath, string Text)> ModuleSources() =>
        Directory.GetFiles(ModulesDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(ModulesDirectory, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Select(rel => (rel.Split('/')[0], rel, File.ReadAllText(Path.Combine(ModulesDirectory, rel))));

    private static Dictionary<string, HashSet<string>> SchemasByModule()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql("Host=localhost").UseSnakeCaseNamingConvention().Options;
        using var db = new TenantDbContext(options);
        return db.Model.GetEntityTypes()
            .Select(t => (Assembly: t.ClrType.Assembly.GetName().Name ?? string.Empty, Schema: t.GetSchema() ?? "public"))
            .Select(x => (Module: Solution.Classify(x.Assembly).Module ?? "BuildingBlocks", x.Schema))
            .GroupBy(x => x.Module)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Schema).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public void Nenhum_schema_e_de_dois_modulos()
    {
        var schemas = SchemasByModule();
        schemas.Should().ContainKey("Finance").WhoseValue.Should().Equal("finance");
        schemas.SelectMany(m => m.Value.Select(s => (m.Key, Schema: s))).GroupBy(x => x.Schema).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Key))}").Should().BeEmpty("nenhum schema é de dois módulos (ADR 0016)");
    }

    [Fact]
    public void SQL_cru_dos_modulos_so_cita_o_proprio_schema()
    {
        var schemas = SchemasByModule();
        var owned = schemas.SelectMany(m => m.Value).ToHashSet(StringComparer.Ordinal);
        var violations = ModuleSources()
            .SelectMany(s => SchemaReference().Matches(s.Text).Select(m => (s.Module, s.RelativePath, Schema: m.Groups[1].Value)))
            .Where(x => owned.Contains(x.Schema) && !(schemas.TryGetValue(x.Module, out var own) && own.Contains(x.Schema)))
            .Select(x => $"{x.RelativePath} cita o schema {x.Schema}, de outro módulo")
            .ToList();
        violations.Should().BeEmpty("SQL cru não lê nem grava tabela de outro módulo (ADR 0006)");
    }

    [Fact]
    public void Escrita_em_massa_so_nos_arquivos_da_allowlist()
    {
        var found = ModuleSources().Where(s => BulkWrite().IsMatch(s.Text)).Select(s => s.RelativePath).ToList();
        found.Should().NotBeEmpty("a regra precisa enxergar os usos atuais");
        found.Where(f => !BulkWriteAllowlist.ContainsKey(f)).Should().BeEmpty("escrita em massa fura a auditoria: use a entidade rastreada ou IAuditWriter e registre aqui (CA 14)");
    }

    [Fact]
    public void Contracts_nao_referencia_outro_Contracts()
    {
        Rules.ContractsCrossReferences(Solution.Projects).Should().BeEmpty();
        Rules.ContractsCrossReferences([new ProjectInfo("BemAqui.A.Contracts", "A", "Contracts", ["BemAqui.B.Contracts"], [], [])]).Should().ContainSingle("a regra acusa o caso proibido");
    }
}
