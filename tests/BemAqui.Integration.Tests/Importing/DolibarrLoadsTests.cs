using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Domain;
using BemAqui.Platform.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01b: as quatro cargas reais (condições, contas, terceiros e contatos) com uma fixture anonimizada.</summary>
public partial class DolibarrLoadsTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    public const string CnpjA = "47960950000121";
    public const string CnpjB = "60701190000104";
    public const string CnpjC = "33000167000101";
    public const string CnpjD = "00000000000191";

    private static readonly string[] All = ["payment-terms", "bank-accounts", "parties", "contacts"];

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Uma execução com os carregadores do DI, como a CLI.</summary>
    private async Task<ImportRunResult> RunAsync(FakeSnapshot source, string mapping = "{}", ImportMode mode = ImportMode.Real)
    {
        await using var scope = await ScopeAsync();
        var sp = scope.ServiceProvider;
        var runner = new ImportRunner(sp.GetRequiredService<IMigrationDb>(), sp.GetServices<IDolibarrLoader>(), sp.GetRequiredService<ITenantProfile>(),
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<AuditOrigin>(), TimeProvider.System);
        return await runner.RunAsync(source, new ImportRequest(mode, All, "operador@konebras.com.br", ImportMapping.Parse(mapping).Value));
    }

    private async Task<T> QueryAsync<T>(Func<TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private Task<Guid?> TargetAsync(string table, string id) =>
        QueryAsync(db => db.ExternalRefs.AsNoTracking().Where(r => r.SourceTable == table && r.SourceId == id).Select(r => (Guid?)r.TargetId).FirstOrDefaultAsync());

    private static Dictionary<string, object?> Societe(int rowid, string name, string? document, int client = 1, int supplier = 0, int status = 1, int entity = 1, DateTime? tms = null) => new()
    {
        ["rowid"] = rowid, ["nom"] = name, ["siren"] = document, ["client"] = client, ["fournisseur"] = supplier, ["status"] = status, ["entity"] = entity,
        ["tms"] = tms ?? new DateTime(2026, 1, 1),
    };

    /// <summary>
    /// Fixture da story: prospect PF sem documento, fornecedor encerrado, documento inválido, dois registros do mesmo CNPJ,
    /// CNPJ já cadastrado à mão, outra entidade, contatos ativo e inativo, condições com e sem equivalente e três contas.
    /// </summary>
    private static FakeSnapshot Fixture()
    {
        var s = new FakeSnapshot();
        s.Table("llx_c_departements").Add(new() { ["rowid"] = 151, ["code_departement"] = "SP" });
        s.Table("llx_c_typent").Add(new() { ["id"] = 8, ["code"] = "TE_PRIVATE" });
        s.Table("llx_c_payment_term").AddRange([
            new() { ["rowid"] = 1, ["code"] = "RECEP", ["libelle"] = "Na entrega", ["nbjour"] = 1, ["type_cdr"] = 0, ["active"] = 1 },
            new() { ["rowid"] = 2, ["code"] = "30D", ["libelle"] = "30 dias", ["nbjour"] = 30, ["type_cdr"] = 0, ["active"] = 1 },
            new() { ["rowid"] = 3, ["code"] = "30DENDMONTH", ["libelle"] = "30 dias, fim do mês", ["nbjour"] = 30, ["type_cdr"] = 1, ["active"] = 1 },
        ]);
        s.Table("llx_bank_account").AddRange([
            new() { ["rowid"] = 1, ["ref"] = "BB", ["label"] = "Banco sem código", ["courant"] = 1, ["number"] = "12345", ["clos"] = 0 },
            new() { ["rowid"] = 2, ["ref"] = "CX", ["label"] = "Caixa da fábrica", ["courant"] = 2, ["clos"] = 0 },
            new() { ["rowid"] = 3, ["ref"] = "INTER", ["label"] = "Inter PJ", ["courant"] = 1, ["code_banque"] = "077", ["code_guichet"] = "0001", ["number"] = "12345678", ["cle_rib"] = "9", ["clos"] = 0 },
        ]);
        var a = Societe(1, "Elevadores Alfa Ltda", "47.960.950/0001-21");
        a["address"] = "Rua das Flores, 120 - Sala 3"; a["zip"] = "12345-678"; a["town"] = "Caçapava"; a["fk_departement"] = 151; a["cond_reglement"] = 2;
        a["note_private"] = "<p>Bom &amp; pagador</p><br>Sem‮ atraso"; a["email"] = "compras@alfa.com.br"; a["phone"] = "+55 (12) 3653-1000";
        var prospect = Societe(3, "Maria Prospect", null, client: 2);
        prospect["fk_typent"] = 8;
        s.Societe.AddRange([a, Societe(2, "Beta Peças Ltda", CnpjB, client: 0, supplier: 1, status: 0), prospect, Societe(4, "Documento errado", "11.111.111/1111-11"),
            Societe(5, "Gama Antigo", CnpjC, tms: new DateTime(2025, 1, 1)), Societe(6, "Gama Elevadores", CnpjC, client: 0, supplier: 1, tms: new DateTime(2026, 6, 1)),
            Societe(7, "Delta do Dolibarr", CnpjD), Societe(8, "Outra empresa", CnpjA, entity: 2)]);
        s.Table("llx_socpeople").AddRange([
            new() { ["rowid"] = 10, ["fk_soc"] = 1, ["firstname"] = "João", ["lastname"] = "Silva", ["poste"] = "Compras", ["email"] = "joao@alfa", ["phone_mobile"] = "12999998888", ["statut"] = 1, ["entity"] = 1 },
            new() { ["rowid"] = 11, ["fk_soc"] = 1, ["firstname"] = "Ex", ["lastname"] = "Funcionário", ["statut"] = 0, ["entity"] = 1 },
            new() { ["rowid"] = 12, ["fk_soc"] = 4, ["firstname"] = "Sem", ["lastname"] = "Terceiro", ["statut"] = 1, ["entity"] = 1 },
        ]);
        return s;
    }
}
