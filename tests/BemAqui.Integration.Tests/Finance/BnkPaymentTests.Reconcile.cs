using System.Net;
using System.Net.Http.Headers;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BillingSlipFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-02: conferência diária com o aviso perdido, execução dupla e os dois tenants (CA 11), falhas seguidas com alerta e aviso (CA 12) e o
/// modo <c>PathToken</c> do token (CA 2).
/// </summary>
public partial class BnkPaymentTests
{
    private async Task<IReadOnlyList<ReconcileReport>> ReconcileAsync()
    {
        await using var scope = await fixture.ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<InterBillingReconciler>().RunAsync();
    }

    private Task<BankIntegration> IntegrationAsync(Guid account) => fixture.QueryAsync(db => db.BankIntegrations.AsNoTracking().SingleAsync(i => i.BankAccountId == account));

    [Fact]
    public async Task Conferencia_diaria_baixa_o_boleto_pago_sem_aviso_uma_vez_so()
    {
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(1000m, Today); // o aviso se perdeu: nada chegou no webhook

        var first = await ReconcileAsync();
        var second = await ReconcileAsync();

        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Paid);
        (await ReceivableAsync(s.ReceivableId)).PaidAmount.Should().Be(1000m);
        (await fixture.QueryAsync(db => db.ReceivableSettlements.CountAsync(x => x.ReceivableId == s.ReceivableId))).Should().Be(1, "execução dupla não baixa duas vezes");
        first.Should().ContainSingle().Which.Settled.Should().BeGreaterThanOrEqualTo(1);
        second.Should().ContainSingle().Which.Settled.Should().Be(0);
        var integration = await IntegrationAsync(s.Account);
        integration.LastReconcileAt.Should().NotBeNull();
        InterWebhookService.Report(integration.LastReconcileReport)!.From.Should().Be(DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(integration.LastReconcileAt!.Value).DateTime).AddDays(-2),
            "a próxima execução começa 2 dias antes da última");

        // Actor de sistema da conferência e a origem sem evento.
        var settlement = await fixture.QueryAsync(db => db.ReceivableSettlements.AsNoTracking().SingleAsync(x => x.ReceivableId == s.ReceivableId));
        settlement.WebhookEventId.Should().BeNull();
        (await fixture.QueryAsync(db => db.AuditEntries.AsNoTracking().AnyAsync(a => a.EntityId == settlement.Id.ToString() && a.Origin == BankPaymentService.ReconcileActor))).Should().BeTrue();

        // A seção "Aviso de pagamento" mostra a última conferência e o relatório.
        var view = await JsonAsync(await s.Owner.GetAsync(Notice(string.Empty, s.Account)));
        view.GetProperty("lastReconcile").GetProperty("consulted").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Recorrente_enfileira_a_conferencia_por_tenant_com_o_financeiro_liberado()
    {
        var before = fixture.Jobs.Created.Count;
        var recurring = ActivatorUtilities.CreateInstance<InterBillingReconcileRecurringJob>(fixture.Factory.Services, fixture.Jobs);

        await recurring.ExecuteAsync(CancellationToken.None);

        var tenants = fixture.Jobs.Created.Skip(before).Where(c => c.Job.Type == typeof(InterBillingReconcileJob)).Select(c => (Guid)c.Job.Args[0]).ToList();
        tenants.Should().Contain([fixture.TenantA.Id, fixture.Tenancy.TenantB.Id]).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Tres_falhas_seguidas_da_conferencia_alertam_sem_alterar_titulo()
    {
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(1000m, Today);
        fixture.Inter.Billing.Unavailable = true;
        try
        {
            for (var i = 0; i < InterBillingReconciler.FailuresToAlert; i++)
            {
                (await ReconcileAsync()).Should().BeEmpty();
            }

            (await IntegrationAsync(s.Account)).ReconcileFailures.Should().Be(InterBillingReconciler.FailuresToAlert);
            (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Issued);
            (await ReceivableAsync(s.ReceivableId)).PaidAmount.Should().Be(0m, "nenhum título é alterado");
            (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.UserId == s.OwnerId && n.Type == InterBillingReconciler.FailureNotification && n.Link == BankIntegrationService.Link(s.Account))))
                .Should().BeTrue();
        }
        finally
        {
            fixture.Inter.Billing.Unavailable = false;
        }

        await ReconcileAsync();
        (await IntegrationAsync(s.Account)).ReconcileFailures.Should().Be(0, "a execução com sucesso zera a contagem");
    }

    [Fact]
    public async Task Modo_PathToken_poe_o_token_no_ultimo_segmento_e_recusa_o_da_query()
    {
        var (s, slipId, charge) = await IssuedAsync();
        // Como a subida faz com Inter:WebhookMode = PathToken: o provedor inter registrado no modo do caminho.
        await using var pathMode = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Inter:WebhookMode", "PathToken").ConfigureTestServices(services =>
        {
            foreach (var registered in services.Where(d => d.ServiceType == typeof(WebhookProvider) && d.ImplementationInstance is WebhookProvider { Code: InterWebhook.ProviderCode }).ToList())
            {
                services.Remove(registered);
            }

            services.AddSingleton(InterWebhook.Provider(UrlTokenMode.PathToken));
        }));
        var owner = pathMode.CreateClient();
        owner.DefaultRequestHeaders.Host = HostA;
        owner.DefaultRequestHeaders.Authorization = s.Owner.DefaultRequestHeaders.Authorization;
        await ActiveUrlAsync(s);

        var rotated = await owner.PostAsync(Notice("/rotate", s.Account), null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());
        var url = (await JsonAsync(rotated)).GetProperty("url").GetString()!;
        var token = url[(url.LastIndexOf('/') + 1)..];
        url.Should().NotContain("?t=");
        token.Should().HaveLength(UrlTokenValidator.TokenLength);
        (await IntegrationAsync(s.Account)).WebhookMode.Should().Be("PathToken");

        charge.Pay(1000m, Today);
        var platform = pathMode.CreateClient();
        platform.DefaultRequestHeaders.Host = PlatformHost;
        var body = Body(charge, at: "caminho-" + Guid.NewGuid().ToString("N"));
        var path = new Uri(url).AbsolutePath;
        (await platform.PostAsync(path, new StringContent(body, System.Text.Encoding.UTF8, new MediaTypeHeaderValue("application/json")))).StatusCode.Should().Be(HttpStatusCode.OK);
        var misplaced = path[..path.LastIndexOf('/')] + "?t=" + token;
        (await platform.PostAsync(misplaced, new StringContent(body + " ", System.Text.Encoding.UTF8, new MediaTypeHeaderValue("application/json")))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "no modo PathToken o token na query é recusado");

        (await ProcessAsync((await EventAsync(body))!.Id)).Status.Should().Be(WebhookEventStatus.Processed);
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Paid);

        // Volta ao modo da classe (Query) para os outros testes.
        _url = null;
        await fixture.QueryAsync(db => db.BankIntegrations.Where(i => i.BankAccountId == s.Account).ExecuteUpdateAsync(u => u.SetProperty(i => i.WebhookRegisteredAt, (DateTimeOffset?)null)));
        await ActiveUrlAsync(s);
    }
}
