using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Commercial.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

public partial class PurchaseOrderTests
{
    /// <summary>CA 5 e 8: o envio resolve pela regra padrão própria do tipo e avisa o nível 1 por sino e e-mail, só com número, fornecedor, total e link.</summary>
    [Fact]
    public async Task Enviar_resolve_a_regra_padrao_e_avisa_o_nivel_1_com_e_mail_minimo()
    {
        var (ownerId, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var order = await CreateAsync(buyer, OrderBody(supplier, null, null, (item, 2m, 150m, null)));
        var id = order.GetProperty("id").GetGuid();

        var submitted = await SubmitAsync(buyer, order);

        submitted.GetProperty("status").GetString().Should().Be("PendingApproval");
        submitted.GetProperty("attempt").GetInt32().Should().Be(1);
        submitted.GetProperty("referenceAmount").GetDecimal().Should().Be(300m);
        var level = submitted.GetProperty("levels")[0];
        level.GetProperty("level").GetInt32().Should().Be(1);
        var rule = await WithDbAsync(db => db.ApprovalRules.AsNoTracking().SingleAsync(r => r.SeedKey == "owner-approves-all-purchase"));
        level.GetProperty("ruleId").GetGuid().Should().Be(rule.Id, "sem regra específica vale a padrão do tipo PurchaseOrder");
        rule.DocumentType.Should().Be("PurchaseOrder");

        var emails = await WithDbAsync(db => db.EmailLogs.AsNoTracking().Where(e => e.Template == "purchase-order-pending" && e.RelatedId == id).ToListAsync());
        emails.Should().NotBeEmpty();
        foreach (var email in emails)
        {
            var model = JsonDocument.Parse(email.Model).RootElement;
            model.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["number", "supplier", "total", "link"], "só número, fornecedor, total e o link autenticado (CA 8)");
            model.GetProperty("link").GetString().Should().Contain("/app/compras/pedidos/" + id);
        }

        (await WithDbAsync(db => db.Notifications.AsNoTracking().CountAsync(n => n.UserId == ownerId && n.Type == "purchasing.purchase_order"))).Should().BeGreaterThan(0);
        var outbox = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("PurchaseOrderSubmitted")).ToListAsync());
        outbox.Select(m => JsonDocument.Parse(m.Payload).RootElement.GetProperty("purchaseOrderId").GetGuid()).Should().Contain(id);
    }

    /// <summary>CA 10 (ADR 0024 §4): quem tem finance.approve e não tem purchasing.approve não aprova nem reprova pedido.</summary>
    [Fact]
    public async Task Finance_approve_sem_purchasing_approve_recebe_403()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, financeApprover) = await ActorAsync("purchasing:view;finance:view,approve");
        var (_, financeOnly) = await ActorAsync("finance:view,approve");
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 80m, null))));

        (await DecideAsync(financeApprover, order, true)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DecideAsync(financeApprover, order, false, "Não")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var none = await DecideAsync(financeOnly, order, true);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden", "sem grupo que habilite Compras");
        (await GetAsync(owner, order.GetProperty("id").GetGuid())).GetProperty("status").GetString().Should().Be("PendingApproval");
    }

    /// <summary>
    /// CA 12 (ADR 0024 §1 e §2): a aprovação do último nível gera um título Planned por parcela (30/60/90 sobre o total, a última absorve a
    /// diferença), com a data estimada a partir da previsão de entrega, a conta 2.1, a natureza Material e a descrição "Pedido … — parcela n/m".
    /// </summary>
    [Fact]
    public async Task Aprovar_gera_um_titulo_previsto_por_parcela_na_mesma_transacao()
    {
        var (_, owner) = await ActorAsync("owner");
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner, "30/60/90 dias");
        var project = Project();
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, project, null, (await ItemAsync(), 10m, 100m, null))));
        var id = order.GetProperty("id").GetGuid();
        var expected = DateOnly.Parse(order.GetProperty("expectedDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        var approved = await DecideAsync(owner, order, true);

        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await JsonAsync(approved)).GetProperty("status").GetString().Should().Be("Approved");
        var payables = await PayablesOfAsync(id);
        payables.Should().HaveCount(3);
        payables.Select(p => p.Amount).Should().Equal(333.33m, 333.33m, 333.34m);
        payables.Select(p => p.DueDate).Should().Equal(expected.AddDays(30), expected.AddDays(60), expected.AddDays(90));
        payables.Should().OnlyContain(p => p.Status == PayableStatus.Planned && p.IsEstimate && p.PartyId == supplier && p.ProjectId == project && p.CostType == CostType.Material);
        payables.Select(p => p.Installment).Should().Equal((short)1, (short)2, (short)3);
        var number = order.GetProperty("number").GetString();
        payables.Select(p => p.Description).Should().Equal($"Pedido {number} — parcela 1/3", $"Pedido {number} — parcela 2/3", $"Pedido {number} — parcela 3/3");
        var account = await WithDbAsync(db => db.Accounts.Where(a => a.Code == "2.1").Select(a => a.Id).SingleAsync());
        payables.Should().OnlyContain(p => p.AccountId == account, "conta 2.1 Materiais, padrão de purchasing.parameters (CA 19)");
        (await WithDbAsync(db => db.Notifications.AsNoTracking().CountAsync(n => n.UserId == buyerId && n.Title.Contains("Previsão lançada no caixa")))).Should().Be(1);

        // CA 13 (D2, ADR 0025 §1): no custo do projeto, a compra de item estocável fica em Comprometido, fora do realizado.
        await using var scope = await ScopeAsync();
        var byProject = await scope.ServiceProvider.GetRequiredService<IPayables>().ByProjectAsync(project);
        var costs = ProjectCosts.Compute(0m, new Dictionary<CostType, decimal>(), byProject.Select(p => new CostPayable(p.CostType, p.Origin, p.Status, p.Amount)), []);
        var material = costs.Lines.Single(l => l.CostType == CostType.Material);
        material.Realized.Should().Be(0m);
        material.Committed.Should().Be(1_000m);
    }

    /// <summary>CA 9: quem pediu não aprova o próprio pedido quando há outro aprovador (403 own_purchase_order).</summary>
    [Fact]
    public async Task Quem_pediu_nao_aprova_o_proprio_pedido()
    {
        var (_, owner) = await ActorAsync("owner");
        await ActorAsync("owner");
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(owner, await CreateAsync(owner, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 50m, null))));

        var own = await DecideAsync(owner, order, true);

        own.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(own)).Should().Be("own_purchase_order");
        (await GetAsync(owner, order.GetProperty("id").GetGuid())).GetProperty("decideBlocker").GetString().Should().Be("Quem pediu não aprova o próprio pedido.");
    }

    /// <summary>CA 14: reprovar exige o motivo, volta o pedido a rascunho com o histórico da tentativa; o reenvio abre a tentativa 2.</summary>
    [Fact]
    public async Task Reprovar_volta_a_rascunho_e_o_reenvio_resolve_de_novo()
    {
        var (_, owner) = await ActorAsync("owner");
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 70m, null))));
        var id = order.GetProperty("id").GetGuid();

        (await DecideAsync(owner, order, false)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");
        var rejected = await DecideAsync(owner, order, false, "Preço acima da tabela.");
        rejected.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await JsonAsync(rejected);
        draft.GetProperty("status").GetString().Should().Be("Draft");
        draft.GetProperty("rejectionComment").GetString().Should().Be("Preço acima da tabela.");
        (await WithDbAsync(db => db.Notifications.AsNoTracking().CountAsync(n => n.UserId == buyerId && n.Title.Contains("reprovado")))).Should().Be(1);
        (await PayablesOfAsync(id)).Should().BeEmpty("reprovado não lança previsão");

        var resubmitted = await SubmitAsync(buyer, draft);

        resubmitted.GetProperty("attempt").GetInt32().Should().Be(2);
        var decisions = await JsonAsync(await buyer.GetAsync($"{Base}/{id}/decisions"));
        decisions.GetProperty("levels").EnumerateArray().Select(l => (l.GetProperty("attempt").GetInt32(), l.GetProperty("status").GetString()))
            .Should().BeEquivalentTo([(2, "Pending"), (1, "Rejected")], "os níveis da tentativa 1 ficam no histórico");
        decisions.GetProperty("events").EnumerateArray().Select(e => (e.GetProperty("attempt").GetInt32(), e.GetProperty("decision").GetString()))
            .Should().Contain([(1, "Submitted"), (1, "Rejected"), (2, "Submitted")]);
    }

    /// <summary>CA 23: as decisões trazem a tentativa, o nível, a regra e o valor de referência; o histórico tem o pedido e as linhas.</summary>
    [Fact]
    public async Task Decisoes_e_historico_de_alteracoes()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 3m, 20m, null))));
        var id = order.GetProperty("id").GetGuid();
        (await DecideAsync(owner, order, true, "Ok")).StatusCode.Should().Be(HttpStatusCode.OK);

        var decisions = await JsonAsync(await buyer.GetAsync($"{Base}/{id}/decisions"));
        var approvedEvent = decisions.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("decision").GetString() == "Approved");
        approvedEvent.GetProperty("attempt").GetInt32().Should().Be(1);
        approvedEvent.GetProperty("level").GetInt32().Should().Be(1);
        approvedEvent.GetProperty("referenceAmount").GetDecimal().Should().Be(60m);
        approvedEvent.GetProperty("comment").GetString().Should().Be("Ok");
        approvedEvent.GetProperty("selfApproved").GetBoolean().Should().BeFalse();

        var history = await JsonAsync(await buyer.GetAsync($"{Base}/{id}/history"));
        history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entity").GetString()).Should().Contain(["PurchaseOrder", "PurchaseOrderLine"]);
    }

    /// <summary>CA 21: o módulo não liberado responde module_not_released; sem grupo, forbidden; cada ação pede a permissão dela.</summary>
    [Fact]
    public async Task Tres_camadas_e_IDOR_da_linha()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, viewer) = await ActorAsync("purchasing:view");
        var (_, nobody) = await ActorAsync(null);
        var supplier = await SupplierAsync(owner);
        var first = await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 10m, null)));
        var second = await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 10m, null)));
        var firstLine = first.GetProperty("lines")[0].GetProperty("id").GetGuid();

        Code(await JsonAsync(await nobody.GetAsync(Base))).Should().Be("forbidden");
        (await viewer.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(Post(Base, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 10m, null))))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "criar exige purchasing.create");
        (await viewer.PostAsJsonAsync($"{Base}/{first.GetProperty("id").GetGuid()}/cancel", new { reason = "x", version = Version(first) })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "cancelar exige purchasing.edit");
        (await buyer.PostAsJsonAsync($"{Base}/{first.GetProperty("id").GetGuid()}/approve", new { comment = (string?)null, version = Version(first) })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "aprovar exige purchasing.approve");

        var idor = await buyer.PutAsJsonAsync($"{Base}/{second.GetProperty("id").GetGuid()}/lines/{firstLine}", new { quantity = 5m, unitPrice = 1m, version = Version(second) });
        idor.StatusCode.Should().Be(HttpStatusCode.NotFound, "a linha é lida pelo pedido da URL");

        using var pdf = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4\n%%EOF"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "file", "proposta.pdf" } };
        (await viewer.PostAsync($"/api/v1/attachments/PurchaseOrder/{first.GetProperty("id").GetGuid()}", pdf)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "anexar exige purchasing.edit");
        (await viewer.GetAsync($"/api/v1/attachments/PurchaseOrder/{first.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.OK, "ler anexo com purchasing.view");
        (await buyer.GetAsync($"/api/v1/attachments/PurchaseOrder/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "OwnerExists");

        await SetReleasedAsync(false);
        try
        {
            var blocked = await buyer.GetAsync(Base);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }
    }
}
