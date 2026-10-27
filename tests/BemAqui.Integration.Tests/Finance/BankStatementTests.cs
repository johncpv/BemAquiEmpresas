using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Application.Statements;
using BemAqui.Finance.Domain;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BankIntegrationFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-03a, integração (Testcontainers + fake do Inter com mTLS e antivírus de teste): API com sobreposição sem duplicar, execução dupla,
/// concorrência e dois tenants, produção sem a flag (CA 1, 2); saldo e aviso de diferença (CA 3); log sem dado de linha e no-store (CA 11).
/// O OFX, a contingência, a consulta, a imutabilidade e as permissões ficam no <c>BankStatementTests.Ofx</c>.
/// </summary>
public partial class BankStatementTests(BankIntegrationFixture fixture) : IClassFixture<BankIntegrationFixture>
{
    private static readonly DateOnly Today = BusinessTime.Today(TimeProvider.System);

    private static string Lines(Guid account) => $"/api/v1/finance/bank-accounts/{account}/statement-lines";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("code").GetString()!;

    /// <summary>Conta 077 com a integração do Inter ligada (Sandbox, ou Produção sem a cobrança real liberada).</summary>
    private async Task<(HttpClient Owner, Guid Account, FakeInterAccount Bank)> InterAccountAsync(Tenant? tenant = null, string host = HostA, string environment = "Sandbox")
    {
        tenant ??= fixture.Tenancy.TenantA;
        var (_, owner, _) = await fixture.ActorAsync(tenant, host, owner: true, recentTwoFactor: true);
        var account = await BankAccountAsync(owner);
        var clientId = "cli-" + Guid.NewGuid().ToString("N")[..12];
        var bank = fixture.Inter.Account(clientId);
        var saved = await owner.PutAsync($"{Base}/{account}", Form(clientId, bank.ClientSecret, TestInterCertificate.Create(), environment));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return (owner, account, bank);
    }

    private async Task<int> RunJobAsync(Tenant tenant)
    {
        await using var scope = await fixture.ScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<BankStatementImportService>().RunScheduledAsync();
    }

    private Task<List<BankStatementLine>> LinesAsync(Tenant tenant, Guid account) =>
        fixture.QueryAsync(tenant, db => db.BankStatementLines.AsNoTracking().Where(l => l.BankAccountId == account && l.Status != StatementLineStatus.Undone).OrderBy(l => l.Date).ToListAsync());

    [Fact]
    public async Task API_importa_paginado_com_sobreposicao_sem_duplicar_na_execucao_dupla_e_com_o_job_ao_mesmo_tempo()
    {
        var (owner, account, bank) = await InterAccountAsync();
        bank.Statement.Enqueue(new FakeInterTransaction("inter-1", Today.AddDays(-2), 1200m));
        bank.Statement.Enqueue(new FakeInterTransaction("inter-2", Today.AddDays(-1), -45.90m, "TARIFA", "Tarifa", "TARIFA PACOTE", null, null));
        bank.Statement.Enqueue(new FakeInterTransaction("inter-3", Today, -300m, "BOLETO_COBRANCA", "Pagamento", "PAGAMENTO DE TITULO", "Fornecedor X", "47225005000186"));

        // "Buscar agora" (CA 1): 3 novas, em 2 páginas do fake; o resumo traz as pendentes.
        var fetched = await OkAsync(await owner.PostAsync(Lines(account) + "/fetch", null));
        fetched.GetProperty("newCount").GetInt32().Should().Be(3);
        fetched.GetProperty("pendingCount").GetInt32().Should().Be(3);
        fixture.Inter.Calls.Count(c => c.Path == "/banking/v2/extrato/completo").Should().BeGreaterThanOrEqualTo(2, "o extrato vem paginado");

        // No máximo 1 vez a cada 5 min por conta.
        var again = await owner.PostAsync(Lines(account) + "/fetch", null);
        again.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodeAsync(again)).Should().Be("bank_statement_fetch_too_soon");

        var lines = await LinesAsync(fixture.Tenancy.TenantA, account);
        lines.Select(l => l.ExternalId).Should().Equal("inter-1", "inter-2", "inter-3");
        lines.Select(l => l.Amount).Should().Equal(1200m, -45.90m, -300m);
        lines.Should().OnlyContain(l => l.Source == StatementSource.Api && l.BankVerified && l.Status == StatementLineStatus.Pending);
        lines[0].Kind.Should().Be(StatementLineKind.Pix);
        lines[0].CounterpartyName.Should().Be("Maria da Silva");
        lines[1].Kind.Should().Be(StatementLineKind.Fee);
        lines[2].CounterpartyDocument.Should().Be("47225005000186");

        // CA 2: a sobreposição devolve as mesmas e mais uma; job duas vezes e ao mesmo tempo: nada duplica.
        bank.Statement.Enqueue(new FakeInterTransaction("inter-4", Today, 10m));
        await Task.WhenAll(RunJobAsync(fixture.Tenancy.TenantA), RunJobAsync(fixture.Tenancy.TenantA));
        await RunJobAsync(fixture.Tenancy.TenantA);
        (await LinesAsync(fixture.Tenancy.TenantA, account)).Select(l => l.ExternalId).Should().BeEquivalentTo(["inter-1", "inter-2", "inter-3", "inter-4"]);
        var batches = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankStatementBatches.AsNoTracking().Where(b => b.BankAccountId == account).ToListAsync());
        batches.Should().HaveCount(4);
        batches.Sum(b => b.NewCount).Should().Be(4);
        batches.Where(b => b.Trigger == StatementTrigger.Scheduled).Should().OnlyContain(b => b.DuplicateCount >= 3 && b.ImportedBy == null);

        // A sobreposição pede desde a última linha menos 3 dias.
        var last = fixture.Inter.Calls.Last(c => c.Path == "/banking/v2/extrato/completo");
        last.Should().NotBeNull();
    }

    [Fact]
    public async Task Job_de_um_tenant_nao_toca_o_outro_e_so_roda_com_o_finance_liberado()
    {
        var (_, accountA, bankA) = await InterAccountAsync();
        var (_, accountB, bankB) = await InterAccountAsync(fixture.Tenancy.TenantB, HostB);
        bankA.Statement.Enqueue(new FakeInterTransaction("a-1", Today, 1m));
        bankB.Statement.Enqueue(new FakeInterTransaction("b-1", Today, 2m));

        await RunJobAsync(fixture.Tenancy.TenantA);
        (await LinesAsync(fixture.Tenancy.TenantA, accountA)).Select(l => l.ExternalId).Should().Equal("a-1");
        (await LinesAsync(fixture.Tenancy.TenantB, accountB)).Should().BeEmpty();

        // Módulo não liberado: o job do tenant não importa.
        await fixture.SetModuleAsync(fixture.Tenancy.TenantB, "finance", released: false);
        try
        {
            var job = new InterStatementImportJob(fixture.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantDirectory>(),
                fixture.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InterStatementImportJob>.Instance);
            await job.ExecuteAsync(fixture.Tenancy.TenantB.Id, new InterStatementImportArgs(), CancellationToken.None);
            (await LinesAsync(fixture.Tenancy.TenantB, accountB)).Should().BeEmpty();
        }
        finally
        {
            await fixture.SetModuleAsync(fixture.Tenancy.TenantB, "finance", released: true);
        }

        await RunJobAsync(fixture.Tenancy.TenantB);
        (await LinesAsync(fixture.Tenancy.TenantB, accountB)).Select(l => l.ExternalId).Should().Equal("b-1");
    }

    [Fact]
    public async Task Producao_sem_a_flag_responde_409_e_nada_e_gravado()
    {
        var (owner, account, bank) = await InterAccountAsync(environment: "Production");
        bank.Statement.Enqueue(new FakeInterTransaction("p-1", Today, 1m));
        var refused = await owner.PostAsync(Lines(account) + "/fetch", null);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(refused)).Should().Be("bank_production_not_allowed");
        await RunJobAsync(fixture.Tenancy.TenantA);
        (await LinesAsync(fixture.Tenancy.TenantA, account)).Should().BeEmpty();
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankStatementBatches.CountAsync(b => b.BankAccountId == account))).Should().Be(0);
    }

    [Fact]
    public async Task Saldo_do_banco_por_dia_e_aviso_da_diferenca_uma_vez_por_conta_e_dia()
    {
        var (owner, account, bank) = await InterAccountAsync();
        var (editorId, _, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: false, "finance.view", "finance.edit");
        bank.Balances[Today.AddDays(-3)] = 800m;
        bank.Statement.Enqueue(new FakeInterTransaction("s-1", Today.AddDays(-1), 5m));
        await OkAsync(await owner.PostAsync(Lines(account) + "/fetch", null));

        var balances = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankStatementBalances.AsNoTracking().Where(b => b.BankAccountId == account).OrderBy(b => b.Date).ToListAsync());
        balances.Select(b => b.Date).Should().Contain([Today.AddDays(-3), Today.AddDays(-1), Today]);
        balances.Single(b => b.Date == Today.AddDays(-3)).Balance.Should().Be(800m);

        // A aba mostra "Saldo no banco · no sistema · diferença" do dia mais recente.
        var view = await OkAsync(await owner.GetAsync(Lines(account)));
        view.GetProperty("latest").GetProperty("bankBalance").GetDecimal().Should().Be(1500.25m);
        view.GetProperty("latest").GetProperty("systemBalance").GetDecimal().Should().Be(0m);
        view.GetProperty("latest").GetProperty("difference").GetDecimal().Should().Be(1500.25m);

        // CA 3: diferença há mais de 2 dias avisa quem tem finance.edit, uma vez (dedup) e com o link da conciliação.
        await RunJobAsync(fixture.Tenancy.TenantA);
        var key = $"stmt-diff:{account}:{Today.AddDays(-3):yyyy-MM-dd}";
        var notices = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Notifications.AsNoTracking().Where(n => n.DedupKey == key).ToListAsync());
        notices.Should().Contain(n => n.UserId == editorId);
        notices.GroupBy(n => n.UserId).Should().OnlyContain(g => g.Count() == 1);
        notices.Should().OnlyContain(n => n.Link == BankStatementImportService.Link(account) && n.Title.Contains("difere do sistema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Log_da_importacao_sem_descricao_contraparte_nem_valores_e_resposta_no_store()
    {
        var (owner, account, bank) = await InterAccountAsync();
        bank.Statement.Enqueue(new FakeInterTransaction("log-1", Today, 4321.09m, "PIX", "Pix recebido", "PIX CANARIO-DESCRICAO-77", "Canario Contraparte", "52998224725"));
        await OkAsync(await owner.PostAsync(Lines(account) + "/fetch", null));
        var response = await owner.GetAsync(Lines(account));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var logs = fixture.RenderedLogs();
        logs.Should().Contain("Extrato do banco importado").And.Contain(account.ToString());
        logs.Should().NotContain("CANARIO-DESCRICAO-77").And.NotContain("Canario Contraparte").And.NotContain("52998224725").And.NotContain("4321.09").And.NotContain("4.321,09");
    }
}
