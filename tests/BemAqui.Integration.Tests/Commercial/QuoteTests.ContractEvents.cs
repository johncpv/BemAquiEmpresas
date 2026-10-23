using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    private async Task ConsumeAsync<TConsumer>(Func<TConsumer, Task> handle)
        where TConsumer : notnull
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        await handle(scope.ServiceProvider.GetRequiredService<TConsumer>());
    }

    [Fact]
    public async Task Fabricacao_libera_parcela_e_evento_fora_de_ordem_ja_nasce_aberto()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, customer) = await SentQuoteAsync(owner, canary: false, milestones:
            [Milestone("Sinal", 30m), Milestone("Fabricação", 40m, triggerEvent: "FabricationDone", days: 10), Milestone("Aceite", 30m, triggerEvent: "TechnicalAcceptance", days: 10)]);
        var (contractId, projectId) = await ApproveAsync(owner, id, quote, Today);
        var fab = (await ParcelsAsync(contractId)).Single(p => p.TriggerEvent == TriggerEvent.FabricationDone);
        fab.Status.Should().Be(ReceivableStatus.Planned);

        // CA 10: com marco de fabricação, a entrega espera a fabricação.
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        (await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project) })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // CA 17: evento de outro projeto não libera nem grava gatilho.
        await ConsumeAsync<MilestoneReachedConsumer>(c => c.HandleAsync(new ProjectFabricated(Guid.NewGuid(), contractId, Today), CancellationToken.None));
        (await WithDbAsync(db => db.ProjectTriggerEvents.CountAsync(t => t.ContractId == contractId))).Should().Be(0);

        // PRD-06a1 CA 9: com OS de fabricação, a fabricação é concluída pelas OS e não se registra à mão.
        await DrainAsync();
        var byOrders = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/fabrication", new { date = Iso(Today.AddDays(-1)), version = Version(project) });
        byOrders.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(byOrders)).GetProperty("code").GetString().Should().Be("fabrication_by_orders");
        await WithDbAsync(db => db.ProductionOrders.Where(o => o.ProjectId == projectId && o.Type == ProductionOrderType.Fabrication)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.Status, ProductionOrderStatus.Cancelled)));

        // CA 9 e 6: sem OS de fabricação ativa, a fabricação registrada à mão libera com data + prazo, uma vez.
        var fabricated = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/fabrication", new { date = Iso(Today.AddDays(-1)), version = Version(project) });
        fabricated.StatusCode.Should().Be(HttpStatusCode.OK, await fabricated.Content.ReadAsStringAsync());
        await DrainAsync();
        fab = (await ParcelsAsync(contractId)).Single(p => p.Id == fab.Id);
        fab.Status.Should().Be(ReceivableStatus.Open);
        fab.DueDate.Should().Be(Today.AddDays(9));
        await ConsumeAsync<MilestoneReachedConsumer>(c => c.HandleAsync(new ProjectFabricated(projectId, contractId, Today), CancellationToken.None));
        (await ParcelsAsync(contractId)).Single(p => p.Id == fab.Id).DueDate.Should().Be(Today.AddDays(9), "o primeiro gatilho vale");

        // CA 7: parcela que nasce depois do evento já sai aberta.
        var account = (await ParcelsAsync(contractId))[0].AccountId;
        var late = new ContractSignedConsumerProbe(contractId, projectId, customer, account);
        await ConsumeAsync<ContractSignedConsumer>(c => c.HandleAsync(late.Event, CancellationToken.None));
        var created = await WithDbAsync(db => db.Receivables.AsNoTracking().SingleAsync(r => r.OriginId == late.MilestoneId));
        created.Status.Should().Be(ReceivableStatus.Open);
        created.DueDate.Should().Be(Today.AddDays(4));
    }

    private sealed record ContractSignedConsumerProbe(Guid ContractId, Guid ProjectId, Guid PartyId, Guid AccountId)
    {
        public Guid MilestoneId { get; } = Guid.CreateVersion7();

        public ContractSigned Event => new(ContractId, ProjectId, PartyId, Today, 100m, AccountId,
            [new ContractMilestoneInfo(MilestoneId, 9, "Atrasado", 100m, MilestoneTrigger.Event, TriggerEvent.FabricationDone, null, 5, 1m)]);
    }
}
