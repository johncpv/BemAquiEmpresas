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
        ["Identity/BemAqui.Identity.Application/Shopfloor/RegisterDeviceService.cs"] = "segredo do caixa do PDV: rotação por UPDATE condicional ao segredo lido (duas sincronizações simultâneas, uma ganha) e a aposentadoria do segredo anterior; não é dado de negócio e a sincronização é a trilha (PDV-01, ADR 0048 §2)",
        ["Identity/BemAqui.Identity.Application/Kiosk/KioskSessionService.cs"] = "último acesso da TV (last_seen_at e o prazo de 30 dias), no máximo 1 vez por minuto; não é dado de negócio (PNL-01a CA 19)",
        // INT-01 (ADR 0053): credenciais de uso único do OAuth e tokens opacos não são dado de negócio nem auditáveis; a trilha é a conexão (rastreada e
        // auditada) e as entradas explícitas da auditoria (consentimento, conexão, revogação).
        ["Identity/BemAqui.Identity.Application/ConnectedApps/OAuthAuthorizeService.cs"] = "intenção de conexão consumida por UPDATE condicional (used_at IS NULL) na transação do código: dois consentimentos simultâneos, um ganha (INT-01 CA 7); o consentimento é auditado pela entrada AppConsentGranted",
        ["Identity/BemAqui.Identity.Application/ConnectedApps/OAuthTokenService.cs"] = "código de autorização resgatado por UPDATE condicional (used_at IS NULL), uso único sob concorrência (INT-01 CA 11); a conexão nasce rastreada e auditada",
        ["Identity/BemAqui.Identity.Application/ConnectedApps/AppConnectionLifecycle.cs"] = "tokens opacos da conexão revogados juntos (revoked_at) na transação que revoga a conexão rastreada e auditada (INT-01 CA 13, 21 e 22)",
        ["Identity/BemAqui.Identity.Application/ConnectedApps/AppTokenAuthenticator.cs"] = "último uso e IP da conexão, no máximo 1 vez por minuto, fora da auditoria como o last_seen_at da TV (INT-01 CA 18)",
        ["Identity/BemAqui.Identity.Application/ConnectedApps/ConnectedAppJobs.cs"] = "expurgo diário de tokens, códigos e intenções vencidos ou revogados há 30 dias (INT-01, LGPD); o IP da conexão sai pela entidade rastreada",
        ["Parties/BemAqui.Parties.Application/CnpjLookupService.cs"] = "expurgo do cache de consultas de CNPJ",
        ["Migrations/BemAqui.Migrations.Application/StagingService.cs"] = "expurgo da staging da importação (ADR 0017)",
        ["Migrations/BemAqui.Migrations.Application/ImportSession.cs"] = "xmin gravado na staging da importação",
        ["Production/BemAqui.Production.Application/ScheduleService.cs"] = "substituição do plano (schedule_allocations e schedule_step_forecasts) numa transação; as tabelas do plano ficam fora da auditoria e o histórico é a schedule_runs (PLN-01a, ADR 0029)",
        ["Inventory/BemAqui.Inventory.Application/StockLedger.cs"] = "trava do motor de estoque: INSERT … ON CONFLICT DO NOTHING da posição, do lote e do saldo por lote antes do SELECT … FOR UPDATE (ADR 0023 §2); o movimento é a trilha",
        ["Finance/BemAqui.Finance.Application/Statements/BankStatementImportService.Commands.cs"] = "\"Desfazer importação\" (BNK-03a CA 9): as linhas do lote viram Undone e o saldo trazido pelo lote sai numa transação; o lote desfeito (quem, quando, motivo) é a trilha auditada",
        ["Catalog/BemAqui.Catalog.Application/ProductImportService.cs"] = "situação e progresso da importação por planilha (CAT-02): a troca Previewed → Running é atômica (confirma uma vez só) e o progresso do job é gravado por bloco; import_runs não é auditável e a confirmação grava o Imported pelo IAuditWriter",
        ["Parties/BemAqui.Parties.Application/IdentityChallengeService.cs"] = "tentativas e situação do código de identidade (INT-04 CA 13): o UPDATE atômico gasta a tentativa antes de comparar (chamadas paralelas não ganham tentativas extras); cada desafio e conferência vai para a auditoria do cliente pelo IAuditWriter.RecordEventAsync",
        ["Sales/BemAqui.Sales.Application/SaleCustomerDocumentService.cs"] = "chave do índice cego criada uma vez por tenant (INSERT … WHERE NOT EXISTS) e expurgo do documento do consumidor em 5 anos (PP-26, OFF-02); não é dado de negócio auditável, o documento é cifrado e fica fora da auditoria",
        ["Pos/BemAqui.Pos.Application/PosEventIngestion.cs"] = "registro imutável dos eventos do caixa (OFF-02, ADR 0050): INSERT … ON CONFLICT (event_id) DO NOTHING em pos.device_events, que não é auditado porque é a própria trilha; as pendências que nascem dele são auditadas",
        ["Pos/BemAqui.Pos.Application/PosCatalogProjector.cs"] = "modelo de leitura do caixa (OFF-01): pos.catalog_items é projeção do Catálogo e das formas de pagamento, regravada em lote (upsert e marca de removido com change_seq); não é dado de negócio nem auditável, a trilha fica nos donos",
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
