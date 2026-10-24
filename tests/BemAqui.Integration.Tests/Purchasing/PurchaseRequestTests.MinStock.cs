using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Platform.Domain;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

public partial class PurchaseRequestTests
{
    /// <summary>O job como o Hangfire o roda: só o tenantId nos argumentos (CA 10).</summary>
    private async Task RunMinStockJobAsync(Tenant? tenant = null)
    {
        var job = new MinStockPurchaseRequestJob(Factory.Services.GetRequiredService<ITenantDirectory>(), Factory.Services.GetRequiredService<ITenantScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MinStockPurchaseRequestJob>.Instance);
        await job.ExecuteAsync((tenant ?? tenancy.TenantA).Id, new MinStockArgs(), CancellationToken.None);
    }

    /// <summary>
    /// CA 9: posição = disponível + linhas "Estoque geral" não pedidas (fora a própria linha do mínimo); abaixo do mínimo, uma linha até o
    /// mínimo, com a necessidade = hoje + 15; atualizada quando a posição muda; cancelada quando o item volta ao mínimo.
    /// </summary>
    [Fact]
    public async Task Minimo_cria_atualiza_e_cancela_uma_linha_por_item()
    {
        await SetReleasedAsync(true);
        var (_, buyer) = await ActorAsync(Buyer);
        var item = await ItemAsync(minStock: 10m);
        Available[item] = 3.5m;
        await RunMinStockJobAsync();
        var line = (await LinesOfItemAsync(item)).Should().ContainSingle().Subject;
        line.Origin.Should().Be(PurchaseRequestOrigin.MinStock);
        line.Quantity.Should().Be(7m, "10 − 3,5 arredondado para cima");
        line.NeededBy.Should().Be(Today.AddDays(15));
        line.ProjectId.Should().BeNull("estoque geral");

        // Uma SC manual "Estoque geral" de 2 entra na posição; a própria linha do mínimo não.
        await buyer.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item, 2m)));
        await RunMinStockJobAsync();
        (await LinesOfItemAsync(item)).Single(l => l.Origin == PurchaseRequestOrigin.MinStock).Quantity.Should().Be(5m);

        Available[item] = 12m;
        await RunMinStockJobAsync();
        var back = (await LinesOfItemAsync(item)).Single(l => l.Origin == PurchaseRequestOrigin.MinStock);
        back.Status.Should().Be(PurchaseRequestStatus.Cancelled);
        back.CancelReason.Should().Be("Estoque voltou ao mínimo");
    }

    /// <summary>ADR 0019 §12, teste 1 (CA 10): rodar duas vezes no mesmo dia não duplica a linha, a SC nem o aviso.</summary>
    [Fact]
    public async Task Job_do_minimo_duas_vezes_no_mesmo_dia_nao_duplica()
    {
        await SetReleasedAsync(true);
        var (buyerId, _) = await ActorAsync(Buyer);
        var a = await ItemAsync(minStock: 4m);
        var b = await ItemAsync(minStock: 2m);
        await RunMinStockJobAsync();
        await RunMinStockJobAsync();

        var lines = (await LinesOfItemAsync(a)).Concat(await LinesOfItemAsync(b)).ToList();
        lines.Should().HaveCount(2);
        lines.Select(l => l.RequestId).Distinct().Should().ContainSingle("as linhas do dia ficam numa SC da execução");
        var request = await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == lines[0].RequestId));
        request.Origin.Should().Be(PurchaseRequestOrigin.MinStock);
        request.RunDate.Should().Be(Today);

        // CA 11: um aviso consolidado por dia para quem tem purchasing.edit, com o link para a lista filtrada.
        var notices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == buyerId && n.Type == MinStockPurchaseRequests.NoticeType).ToListAsync());
        var notice = notices.Should().ContainSingle().Subject;
        notice.Title.Should().MatchRegex(@"^\d+ (item|itens) abaixo do mínimo\.$");
        notice.Link.Should().Be(MinStockPurchaseRequests.ListLink);
    }

    /// <summary>ADR 0019 §12, teste 2 (CA 10): dois tenants, cada um vê só o seu.</summary>
    [Fact]
    public async Task Job_do_minimo_em_dois_tenants_nao_mistura()
    {
        await SetReleasedAsync(true);
        await SetReleasedAsync(true, tenancy.TenantB);
        var inA = await ItemAsync(minStock: 3m);
        var inB = await ItemAsync(minStock: 6m, tenant: tenancy.TenantB);
        await RunMinStockJobAsync(tenancy.TenantB);

        (await LinesOfItemAsync(inB, tenancy.TenantB)).Should().ContainSingle().Which.Quantity.Should().Be(6m);
        (await LinesOfItemAsync(inA)).Should().BeEmpty("o job do tenant B não toca o A");
        (await LinesOfItemAsync(inA, tenancy.TenantB)).Should().BeEmpty();

        await RunMinStockJobAsync();
        (await LinesOfItemAsync(inA)).Should().ContainSingle().Which.Quantity.Should().Be(3m);
        (await LinesOfItemAsync(inB)).Should().BeEmpty("o item do B não existe no A");
    }

    /// <summary>CA 13 no mínimo: linha do mínimo editada à mão não é mais atualizada pelo job.</summary>
    [Fact]
    public async Task Linha_do_minimo_editada_a_mao_nao_muda_no_job()
    {
        await SetReleasedAsync(true);
        var (_, buyer) = await ActorAsync(Buyer);
        var item = await ItemAsync(minStock: 8m);
        await RunMinStockJobAsync();
        var line = (await LinesOfItemAsync(item)).Single();
        (await buyer.PutAsJsonAsync($"{Base}/purchase-requests/{line.RequestId}/lines/{line.Id}", new { quantity = 20m, neededBy = Today.AddDays(30), version = line.Version }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Available[item] = 1m;
        await RunMinStockJobAsync();
        var lines = await LinesOfItemAsync(item);
        lines.Should().ContainSingle().Which.Quantity.Should().Be(20m);
        lines[0].ManuallyEdited.Should().BeTrue();
    }

    /// <summary>CA 2 (D8): o Almoxarifado ganha view + receive, sem create/edit/approve; o Financeiro, view; uma vez só.</summary>
    [Fact]
    public async Task Seed_patch_purchasing_permissions_uma_vez()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.SeedPatches.CountAsync(p => p.Key == "purchasing-permissions")).Should().Be(1);
        (await db.CatalogModuleActions.AnyAsync(a => a.ModuleCode == "purchasing" && a.Action == "receive")).Should().BeTrue("o catálogo declara purchasing.receive (CA 1)");
        var warehouse = await db.Groups.SingleAsync(g => g.SeedKey == "warehouse");
        (await db.GroupPermissions.Where(p => p.GroupId == warehouse.Id && p.ModuleCode == "purchasing").Select(p => p.Action).ToListAsync())
            .Should().BeEquivalentTo(["view", "receive"]);
        var finance = await db.Groups.SingleAsync(g => g.SeedKey == "finance");
        (await db.GroupPermissions.Where(p => p.GroupId == finance.Id && p.ModuleCode == "purchasing").Select(p => p.Action).ToListAsync()).Should().Equal("view");
        var others = await db.Groups.Where(g => g.SeedKey != null && g.SeedKey != "warehouse" && g.SeedKey != "finance" && !g.IsSystem).Select(g => g.Id).ToListAsync();
        (await db.GroupPermissions.AnyAsync(p => others.Contains(p.GroupId) && p.ModuleCode == "purchasing")).Should().BeFalse("nenhum outro grupo do seed tem Compras");

        foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await db.SeedPatches.CountAsync(p => p.Key == "purchasing-permissions")).Should().Be(1, "idempotente");
        (await db.GroupPermissions.CountAsync(p => p.GroupId == warehouse.Id && p.ModuleCode == "purchasing")).Should().Be(2);
    }
}
