using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.GoLive;

/// <summary>
/// GOL-01a CA 8: o <c>conferencia.sql</c> roda só leitura no banco do tenant e devolve, nesta ordem, saldo por conta, títulos em aberto,
/// folha do mês, estoque por local, contagens e a asserção de nenhuma nota de homologação nem boleto de sandbox, sem coluna de dado pessoal.
/// </summary>
[Collection(GoLiveGroup.Name)]
public class ConferenciaScriptTests(GoLiveFixture golive)
{
    private static readonly string[][] ExpectedColumns =
    [
        ["conta", "tipo", "ativa", "saldo", "transacoes", "nao_conciliadas"],
        ["tipo", "quantidade", "saldo_em_aberto", "vencidos", "saldo_vencido"],
        ["mes", "grupo", "situacao", "quantidade", "valor", "saldo_em_aberto"],
        ["local_codigo", "local", "itens_com_saldo", "quantidade", "valor"],
        ["terceiros", "itens", "contratos", "contratos_nao_cancelados", "projetos"],
        ["notas_homologacao", "notas_rascunho", "boletos_sandbox", "integracoes_bancarias_sandbox", "emissor_em_homologacao", "resultado"],
    ];

    [Fact]
    public async Task Devolve_as_colunas_esperadas_o_saldo_da_conta_e_a_assercao_OK_num_tenant_limpo()
    {
        var tenant = await golive.ProvisionAsync("confere");
        var info = (await golive.Tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using (var scope = await golive.Tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var account = new BankAccount { Name = "Conta conferência", Kind = BankAccountKind.Bank, OpeningBalance = 100.00m, CreatedAt = DateTimeOffset.UtcNow };
            db.Set<BankAccount>().Add(account);
            db.Set<FinancialTransaction>().Add(new FinancialTransaction
            {
                BankAccountId = account.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow), Amount = 50.25m, Type = TransactionType.Receipt, Description = "Entrada", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var results = await GoLiveFixture.RunScriptAsync(golive.Tenancy.Decrypt(tenant), "conferencia.sql", new Dictionary<string, string> { ["bqe.mes"] = "2026-12" });

        results.Select(r => r.Columns.ToArray()).Should().BeEquivalentTo(ExpectedColumns, o => o.WithStrictOrdering());
        results[0].Rows.Single(r => (string)r[0]! == "Conta conferência")[3].Should().Be(150.25m);
        results[1].Rows.Select(r => (string)r[0]!).Should().Equal("A receber", "A pagar");
        results[5].Rows.Single()[5].Should().Be("OK");
        results.SelectMany(r => r.Columns).Should().NotContain(c => new[] { "email", "document", "cpf", "nome" }.Any(p => c.Contains(p, StringComparison.Ordinal)));
    }
}
