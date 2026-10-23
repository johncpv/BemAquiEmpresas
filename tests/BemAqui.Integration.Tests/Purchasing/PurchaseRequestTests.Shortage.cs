using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

public partial class PurchaseRequestTests
{
    /// <summary>
    /// CA 5: o evento é só aviso; a linha vem da falta da fonte, numa SC Open por OS, com OS, projeto, linha da BOM e a necessidade =
    /// data prometida − 15 dias; reentrega, evento fora de ordem e falta repetida não duplicam.
    /// </summary>
    [Fact]
    public async Task Falta_da_BOM_vira_linha_pela_fonte_sem_duplicar()
    {
        await SetReleasedAsync(true);
        var promised = Today.AddDays(60);
        var project = Project(promised: promised);
        var order = Order(project);
        var cable = await ItemAsync();
        var bolt = await ItemAsync();
        var bomLine = Guid.CreateVersion7();
        Short(order, cable, 6m, bomLine);
        Short(order, bolt, 2m);

        await DeliverAsync(order, cable, 999m); // o valor do evento não importa
        await DeliverAsync(order, cable, 6m); // reentrega
        await DeliverAsync(order, bolt, 1m); // fora de ordem, com valor antigo

        var lines = await LinesOfOrderAsync(order);
        lines.Should().HaveCount(2);
        lines.Select(l => l.RequestId).Distinct().Should().ContainSingle("uma SC Open por OS");
        var cableLine = lines.Single(l => l.ItemId == cable);
        cableLine.Quantity.Should().Be(6m);
        cableLine.BomLineId.Should().Be(bomLine);
        cableLine.ProjectId.Should().Be(project);
        cableLine.NeededBy.Should().Be(promised.AddDays(-15));
        cableLine.Origin.Should().Be(PurchaseRequestOrigin.BomShortage);
        lines.Single(l => l.ItemId == bolt).Quantity.Should().Be(2m);
        var request = await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == cableLine.RequestId));
        request.Origin.Should().Be(PurchaseRequestOrigin.BomShortage);
        request.ProductionOrderId.Should().Be(order);
        request.Status.Should().Be(PurchaseRequestStatus.Open);

        // Sem data prometida: hoje + 15.
        var loose = Order(Project());
        Short(loose, cable, 1m);
        await DeliverAsync(loose, cable, 1m);
        (await LinesOfOrderAsync(loose)).Single().NeededBy.Should().Be(Today.AddDays(15));
    }

    /// <summary>CA 6: a falta diminui (reduz), zera (cancela com o motivo); linha em cotação não muda e o comprador é avisado.</summary>
    [Fact]
    public async Task Falta_menor_reduz_zerada_cancela_e_cotacao_fica_intacta()
    {
        await SetReleasedAsync(true);
        var (buyerId, _) = await ActorAsync(Buyer);
        var order = Order();
        var item = await ItemAsync();
        Short(order, item, 10m);
        await DeliverAsync(order, item, 10m);

        Short(order, item, 4m);
        await DeliverAsync(order, item, 4m);
        (await LinesOfOrderAsync(order)).Single().Quantity.Should().Be(4m, "chegou material: a linha é reduzida");

        Short(order, item, 0m);
        await DeliverAsync(order, item, 0m);
        var cancelled = (await LinesOfOrderAsync(order)).Single();
        cancelled.Status.Should().Be(PurchaseRequestStatus.Cancelled);
        cancelled.CancelReason.Should().Be("Falta resolvida pelo estoque");
        (await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == cancelled.RequestId))).Status.Should().Be(PurchaseRequestStatus.Cancelled);

        // Falta volta; a linha nova vai para cotação; a falta cai: a cotação não muda e o comprador é avisado.
        Short(order, item, 9m);
        await DeliverAsync(order, item, 9m);
        var quoted = (await LinesOfOrderAsync(order)).Single(l => l.Status == PurchaseRequestStatus.Open);
        quoted.RequestId.Should().NotBe(cancelled.RequestId, "a SC cancelada não recebe linha nova");
        await SetLineStatusAsync(quoted.Id, PurchaseRequestStatus.Quoting);
        Short(order, item, 5m);
        await DeliverAsync(order, item, 5m);
        var lines = await LinesOfOrderAsync(order);
        lines.Single(l => l.Id == quoted.Id).Quantity.Should().Be(9m);
        lines.Should().NotContain(l => l.Status == PurchaseRequestStatus.Open, "o que está em cotação cobre a falta");
        var itemCode = await WithDbAsync(db => db.Items.Where(i => i.Id == item).Select(i => i.Code + " " + i.Name).SingleAsync());
        var notices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == buyerId && n.Type == ShortageRecomposer.DropNoticeType).ToListAsync());
        notices.Should().ContainSingle(n => n.Title == $"A falta de {itemCode} na {Orders[order].Number} caiu para 5; a linha já está em cotação.");

        // A falta sobe além da cotação: só o excedente vira linha aberta.
        Short(order, item, 12m);
        await DeliverAsync(order, item, 12m);
        (await LinesOfOrderAsync(order)).Single(l => l.Status == PurchaseRequestStatus.Open).Quantity.Should().Be(3m);
    }

    /// <summary>CA 7 (D3): as faltas de antes da CMP-01 viram linhas com "Calculado na implantação", uma vez; o job repete sem duplicar.</summary>
    [Fact]
    public async Task Recomposicao_da_implantacao_marca_e_nao_duplica()
    {
        await SetReleasedAsync(true);
        var order = Order();
        var item = await ItemAsync();
        Short(order, item, 7m);
        await WithDbAsync(async db =>
        {
            await db.PurchasingParameters.Where(p => p.Key == PurchasingParametersService.ShortagesBackfilled).ExecuteDeleteAsync();
            return 0;
        });

        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<ShortageBackfill>().RunAsync()).Should().BeGreaterThan(0);
        }

        var line = (await LinesOfOrderAsync(order)).Single();
        line.CalculatedOnOnboarding.Should().BeTrue();
        line.Quantity.Should().Be(7m);
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(PurchaseRequestLine) && a.EntityId == line.Id.ToString()).ToListAsync()))
            .Should().Contain(a => a.Origin == ShortageRecomposer.OnboardingLabel);
        (await WithDbAsync(db => db.PurchasingParameters.AnyAsync(p => p.Key == PurchasingParametersService.ShortagesBackfilled))).Should().BeTrue();

        // Rede de segurança: roda de novo sem duplicar; a linha nova da rotina não leva a marca.
        var later = Order();
        Short(later, item, 2m);
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<ShortageBackfill>().RunAsync();
            await scope.ServiceProvider.GetRequiredService<ShortageBackfill>().RunAsync();
        }

        (await LinesOfOrderAsync(order)).Should().ContainSingle();
        (await LinesOfOrderAsync(later)).Should().ContainSingle().Which.CalculatedOnOnboarding.Should().BeFalse();
    }

    /// <summary>[Segurança] CA 8: módulo não liberado, nada é gravado (a mensagem conta como processada); liberado, o job recupera.</summary>
    [Fact]
    public async Task Modulo_nao_liberado_nao_cria_e_a_liberacao_recupera()
    {
        var order = Order();
        var item = await ItemAsync(minStock: 5m);
        Short(order, item, 3m);
        await SetReleasedAsync(false);
        try
        {
            await DeliverAsync(order, item, 3m);
            await using var scope = await ScopeAsync();
            (await scope.ServiceProvider.GetRequiredService<ShortageBackfill>().RunAsync()).Should().Be(0);
            (await scope.ServiceProvider.GetRequiredService<MinStockPurchaseRequests>().RunAsync()).Created.Should().Be(0);
        }
        finally
        {
            await SetReleasedAsync(true);
        }

        (await LinesOfOrderAsync(order)).Should().BeEmpty();
        (await LinesOfItemAsync(item)).Should().BeEmpty();

        await RunMinStockJobAsync();
        (await LinesOfOrderAsync(order)).Should().ContainSingle().Which.Quantity.Should().Be(3m, "o job diário recompõe as faltas abertas");
        (await LinesOfItemAsync(item)).Should().Contain(l => l.Origin == PurchaseRequestOrigin.MinStock);
    }

    /// <summary>CA 15: o contrato devolve as linhas da OS com número e situação (o link "SC gerada").</summary>
    [Fact]
    public async Task IPurchaseRequests_devolve_as_linhas_da_OS()
    {
        await SetReleasedAsync(true);
        var order = Order();
        var item = await ItemAsync();
        Short(order, item, 2m);
        await DeliverAsync(order, item, 2m);

        await using var scope = await ScopeAsync();
        var lines = await scope.ServiceProvider.GetRequiredService<IPurchaseRequests>().ForOrderAsync(order);
        var line = lines.Should().ContainSingle().Subject;
        line.ItemId.Should().Be(item);
        line.Status.Should().Be(PurchaseRequestStatus.Open);
        line.RequestNumber.Should().StartWith("SC-");
        (await scope.ServiceProvider.GetRequiredService<IPurchaseRequests>().ForOrdersAsync([order, Guid.NewGuid()])).Should().ContainSingle();
    }
}
