using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected const string Boms = "/api/v1/engineering/boms";

    private WebApplicationFactory<Program>? _bomFactory;

    /// <summary>Mesma composição da classe com o bloqueio real da ENG-02 (as outras partes desta classe usam o NoBomGate).</summary>
    protected WebApplicationFactory<Program> BomFactory => _bomFactory ??= Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IBomGate, EngineeringBomGate>()));

    /// <summary>O mesmo usuário (token) num cliente do <see cref="BomFactory"/>.</summary>
    protected HttpClient WithBomGate(HttpClient client)
    {
        var gated = BomFactory.CreateClient();
        gated.DefaultRequestHeaders.Host = Host;
        gated.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        return gated;
    }

    /// <summary>
    /// OS de fabricação com o roteiro C (outro centro) → M → Q e a quantidade dada; sem <paramref name="withProduct"/>, o item do orçamento
    /// não tem item de estoque (CA 4).
    /// </summary>
    protected async Task<(HttpClient Owner, Guid Order, Guid? Product, Guid MyCenter)> BomOrderAsync(decimal quantity = 2m, bool withProduct = true, Guid? product = null)
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var mine = await CenterAsync("Montagem", "Linha 1");
        var other = await CenterAsync("Corte", "Linha 2");
        var routing = await RoutingAsync(owner, mine, null, null, Step("Corte", "C", other, 1m), Step("Montagem", "M", mine, 2m), Step("Qualidade", "Q", mine, 0.5m, quality: true));
        Guid? item = withProduct ? product ?? await ProductAsync() : null;
        var (_, _, items) = await ApprovedAsync(owner, [("Equipment", item, quantity)], routingOnItems: routing);
        return (owner, (await OrderOfAsync(items[0]))!.Id, item, mine);
    }

    /// <summary>Material ativo direto no estoque, na unidade de estoque dada (o cadastro tem os próprios testes).</summary>
    protected async Task<Guid> MaterialAsync(string name, ItemType type = ItemType.RawMaterial, string unit = "KG", bool active = true)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var unitId = await db.Units.Where(u => u.Code == unit).Select(u => u.Id).SingleAsync();
        var item = new BemAqui.Inventory.Domain.Item { Type = type, Code = "MP-" + Guid.NewGuid().ToString("N")[..6], Name = name, UnitId = unitId, Ncm = "72085100", IsActive = active, CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    /// <summary>Conversão do item: 1 {de} = {fator} {para} (CAD-02b).</summary>
    protected async Task ConversionAsync(Guid itemId, string from, string to, decimal factor)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var units = await db.Units.Where(u => u.Code == from || u.Code == to).ToDictionaryAsync(u => u.Code, u => u.Id);
        db.ItemUnitConversions.Add(new BemAqui.Inventory.Domain.ItemUnitConversion { ItemId = itemId, FromUnitId = units[from], ToUnitId = units[to], Factor = factor, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    protected async Task<Guid> UnitIdAsync(string code) => await WithDbAsync(db => db.Units.Where(u => u.Code == code).Select(u => u.Id).SingleAsync());

    protected static async Task<JsonElement> CreatedBomAsync(HttpClient client, Guid order, Guid? copyFrom = null)
    {
        var created = await client.SendAsync(Post(Boms, new { productionOrderId = order, copyFromBomId = copyFrom }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await JsonAsync(created);
    }

    protected static object BomLine(Guid item, decimal quantity, decimal scrap = 0m, Guid? unit = null, Guid? id = null, string? notes = null) =>
        new { id, itemId = item, quantity, unitId = unit, scrapPct = scrap, notes };

    protected static async Task<HttpResponseMessage> SaveBomAsync(HttpClient client, JsonElement bom, params object[] lines) =>
        await client.PutAsJsonAsync($"{Boms}/{bom.GetProperty("id").GetGuid()}", new { lines, version = Version(bom) });

    protected static async Task<JsonElement> ReleaseAsync(HttpClient client, JsonElement bom) =>
        await OkAsync(await client.PostAsJsonAsync($"{Boms}/{bom.GetProperty("id").GetGuid()}/release", new { version = Version(bom) }));

    /// <summary>BOM liberada com as linhas dadas (cria, salva e libera).</summary>
    protected static async Task<JsonElement> ReleasedBomAsync(HttpClient client, Guid order, params object[] lines)
    {
        var bom = await CreatedBomAsync(client, order);
        if (lines.Length > 0)
        {
            bom = await OkAsync(await SaveBomAsync(client, bom, lines));
        }

        return await ReleaseAsync(client, bom);
    }

    protected async Task<List<Bom>> BomsOfAsync(Guid order) => await WithDbAsync(db => db.Boms.AsNoTracking().Where(b => b.ProductionOrderId == order).OrderBy(b => b.Revision).ToListAsync());

    /// <summary>Entrada de estoque pelo motor (EST-01a), numa transação própria, como faria o recebimento.</summary>
    protected async Task ReceiveStockAsync(Guid item, decimal quantity, decimal unitCost)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        var location = new BemAqui.Inventory.Domain.Location { Code = Unique("L"), Name = "Local " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var posted = await scope.ServiceProvider.GetRequiredService<IStockMovements>()
            .PostAsync(new StockPosting(StockMovementType.Receipt, item, location.Id, null, quantity, unitCost, StockSources.GoodsReceipt, Guid.NewGuid()));
        posted.IsSuccess.Should().BeTrue(posted.Error?.Message);
        await transaction.CommitAsync();
    }

    /// <summary>Processa o outbox do tenant até não sobrar mensagem pendente.</summary>
    protected async Task DrainOutboxAsync()
    {
        var info = (await Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 40; round++)
        {
            await using (var scope = await ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Messaging.OutboxProcessor>().ProcessBatchAsync(info);
            }

            if (await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1))) == 0)
            {
                return;
            }

            await Task.Delay(250);
        }
    }
}

/// <summary>
/// ENG-02, BOM da OS: seed patch (CA 1), criar, copiar, salvar e descartar sem efeito em estoque (CA 2 a 7), liberar com o evento no
/// outbox (CA 8, 9), OS fechada (CA 11), revisão e Superseded (CA 12, 13), bloqueio das etapas sem BOM (CA 16), filtro do deploy (CA 17),
/// IBoms (CA 18), três camadas e IDOR (CA 19), concorrência (CA 20) e auditoria (CA 21). Cada teste usa as próprias OS.
/// </summary>
public partial class FabricationOrdersTests
{
    [Fact]
    public async Task Seed_patch_engineering_release_bom_uma_vez_com_perm_version_e_2FA()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.SeedPatches.CountAsync(p => p.Key == "engineering-release-bom")).Should().Be(1, "o provisionamento aplicou o patch (CA 1)");
        var engineering = await db.Groups.SingleAsync(g => g.SeedKey == "engineering");
        (await db.GroupPermissions.CountAsync(p => p.GroupId == engineering.Id && p.ModuleCode == "engineering" && p.Action == "approve")).Should().Be(1);

        // Tenant existente no próximo deploy: o patch ainda não aplicado roda uma vez e sobe a versão de permissão dos membros.
        var (member, _) = await ActorAsync("engineering");
        await db.GroupPermissions.Where(p => p.GroupId == engineering.Id && p.ModuleCode == "engineering" && p.Action == "approve").ExecuteDeleteAsync();
        await db.SeedPatches.Where(p => p.Key == "engineering-release-bom").ExecuteDeleteAsync();
        var before = await db.Users.AsNoTracking().Where(u => u.Id == member).Select(u => u.PermVersion).SingleAsync();
        foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await db.SeedPatches.CountAsync(p => p.Key == "engineering-release-bom")).Should().Be(1);
        (await db.GroupPermissions.CountAsync(p => p.GroupId == engineering.Id && p.ModuleCode == "engineering" && p.Action == "approve")).Should().Be(1);
        (await db.Users.AsNoTracking().Where(u => u.Id == member).Select(u => u.PermVersion).SingleAsync()).Should().BeGreaterThan(before);

        // Rodar de novo não duplica nada; quem tem engineering.approve passa a ter 2FA obrigatório no login (FND-06c CA 12).
        foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await db.GroupPermissions.CountAsync(p => p.GroupId == engineering.Id && p.ModuleCode == "engineering" && p.Action == "approve")).Should().Be(1);
        var user = await db.Users.SingleAsync(u => u.Id == member);
        (await scope.ServiceProvider.GetRequiredService<TwoFactorRequirement>().IsRequiredAsync(user)).Should().BeTrue();
    }

    [Fact]
    public async Task Criar_e_salvar_o_rascunho_calcula_a_necessidade_sem_efeito_em_estoque()
    {
        var (_, order, product, _) = await BomOrderAsync(quantity: 2m);
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa de aço 2 mm");
        var bolt = await MaterialAsync("Parafuso M8");
        var service = await MaterialAsync("Pintura terceirizada", ItemType.Service, "UN");
        var meter = await MaterialAsync("Cabo de aço", unit: "KG");
        await ConversionAsync(bolt, "PC", "KG", 0.025m);

        var bom = await CreatedBomAsync(engineer, order);
        bom.GetProperty("revision").GetInt32().Should().Be(0);
        bom.GetProperty("status").GetString().Should().Be("Draft");
        bom.GetProperty("lines").GetArrayLength().Should().Be(0);

        var pc = await UnitIdAsync("PC");
        var saved = await OkAsync(await SaveBomAsync(engineer, bom, BomLine(steel, 1.5m, 10m, notes: "Cortar no sentido da fibra"), BomLine(bolt, 40m, 0m, pc)));
        var lines = saved.GetProperty("lines").EnumerateArray().ToList();
        lines[0].GetProperty("requiredQuantity").GetDecimal().Should().Be(3.3m, "1,5 × 2 × 1,10, na unidade de estoque (CA 3)");
        lines[0].GetProperty("unitCode").GetString().Should().Be("KG", "a unidade de estoque é o padrão");
        lines[1].GetProperty("requiredQuantity").GetDecimal().Should().Be(2m, "40 PC × 2 × 0,025 kg");
        lines[1].GetProperty("unitCode").GetString().Should().Be("PC");
        saved.GetProperty("stockConnected").GetBoolean().Should().BeTrue("o saldo vem do IStockBalances da EST-01a");
        lines[0].GetProperty("available").GetDecimal().Should().Be(0m, "item sem posição no estoque");
        lines[0].GetProperty("shortage").GetDecimal().Should().Be(3.3m, "no rascunho, a falta é a necessidade acima do disponível");

        var duplicated = await SaveBomAsync(engineer, saved, BomLine(steel, 1m), BomLine(steel, 2m));
        duplicated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(duplicated)).GetProperty("errors").GetProperty("lines[1].itemId")[0].GetString().Should().Be("O item já está na linha 1.");
        var noConversion = await SaveBomAsync(engineer, saved, BomLine(meter, 1m, unit: await UnitIdAsync("M")));
        (await JsonAsync(noConversion)).GetProperty("errors").GetProperty("lines[0].unitId")[0].GetString().Should().Be("Cadastre a conversão de M para KG no item.");
        (await JsonAsync(await SaveBomAsync(engineer, saved, BomLine(service, 1m)))).GetProperty("errors").GetProperty("lines[0].itemId")[0].GetString().Should().StartWith("Serviço não entra na BOM");
        (await JsonAsync(await SaveBomAsync(engineer, saved, BomLine(product!.Value, 1m)))).GetProperty("errors").GetProperty("lines[0].itemId")[0].GetString().Should().Be("O produto da OS não entra na própria BOM.");
        (await SaveBomAsync(engineer, saved, BomLine(steel, 0m))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SaveBomAsync(engineer, saved, BomLine(steel, 1m, 100.5m))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Salvar com a versão antiga: 409 (ADR 0019 §2); o rascunho muda à vontade e nada vai para o outbox (CA 6).
        (await SaveBomAsync(engineer, bom, BomLine(steel, 1m))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OutboxAsync<BomReleased>(order)).Should().Be(0);
        (await engineer.SendAsync(Post(Boms, new { productionOrderId = order, copyFromBomId = (Guid?)null }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "um rascunho por vez");
    }

    [Fact]
    public async Task Saldo_e_custo_previsto_vem_do_motor_de_estoque_e_a_previa_diz_quanto_vai_reservar()
    {
        var (owner, order, _, _) = await BomOrderAsync(quantity: 2m);
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa 7 mm");
        await ReceiveStockAsync(steel, 2m, 10m);
        await ReceiveStockAsync(steel, 1m, 10m);

        var bom = await CreatedBomAsync(engineer, order);
        bom = await OkAsync(await SaveBomAsync(engineer, bom, BomLine(steel, 2.5m)));
        var line = bom.GetProperty("lines")[0];
        line.GetProperty("onHand").GetDecimal().Should().Be(3m, "somado nos locais (CA 3, padrão 8)");
        line.GetProperty("reserved").GetDecimal().Should().Be(0m);
        line.GetProperty("available").GetDecimal().Should().Be(3m);
        line.GetProperty("shortage").GetDecimal().Should().Be(2m, "necessidade 5 − disponível 3");
        bom.GetProperty("shortageCount").GetInt32().Should().Be(1);
        line.GetProperty("estimatedCost").ValueKind.Should().Be(JsonValueKind.Null, "a Engenharia do seed não tem projects.view_cost");

        var withCost = await OkAsync(await owner.GetAsync($"{Boms}/{bom.GetProperty("id").GetGuid()}"));
        withCost.GetProperty("lines")[0].GetProperty("estimatedCost").GetDecimal().Should().Be(50m, "necessidade 5 × custo médio 10, sem gravar");
        withCost.GetProperty("estimatedCost").GetDecimal().Should().Be(50m);

        var preview = await OkAsync(await engineer.GetAsync($"{Boms}/{bom.GetProperty("id").GetGuid()}/release-preview"));
        preview.GetProperty("stockConnected").GetBoolean().Should().BeTrue();
        preview.GetProperty("reservationsConnected").GetBoolean().Should().BeTrue("as reservas da EST-02 estão ligadas");
        preview.GetProperty("shortageCount").GetInt32().Should().Be(1);
        var first = preview.GetProperty("lines")[0];
        first.GetProperty("available").GetDecimal().Should().Be(3m);
        first.GetProperty("shortage").GetDecimal().Should().Be(2m);
        first.GetProperty("toReserve").GetDecimal().Should().Be(3m, "vai reservar o disponível (3) da necessidade 5");
        first.GetProperty("issued").ValueKind.Should().Be(JsonValueKind.Null, "na primeira liberação não há baixado");
    }

    /// <summary>
    /// EST-02 integrada à ENG-02: liberar e revisar a BOM reconcilia as reservas pelo IBoms real (consumidores de BomReleased e
    /// BomRevised pela inbox) e quem libera BOM recebe "Material reservado: {n} itens, {f} em falta." (padrão 7).
    /// </summary>
    [Fact]
    public async Task Liberar_e_revisar_a_BOM_reserva_pelo_IBoms_e_avisa_o_fim_da_reserva()
    {
        var (_, order, _, _) = await BomOrderAsync(quantity: 2m);
        var (engineerId, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa 9 mm");
        await ReceiveStockAsync(steel, 3m, 10m);

        var draft = await CreatedBomAsync(engineer, order);
        draft = await OkAsync(await SaveBomAsync(engineer, draft, BomLine(steel, 2.5m)));
        await ReleaseAsync(engineer, draft);
        await DrainOutboxAsync();

        await using (var scope = await ScopeAsync())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<IStockReservations>().ForOrderAsync(order);
            (rows.Sum(r => r.Requested), rows.Sum(r => r.Reserved), rows.Sum(r => r.Shortage)).Should().Be((5m, 3m, 2m), "necessidade 5, disponível 3");
        }

        var notices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == engineerId && n.Type == BemAqui.Inventory.Application.ReservationConsumers.NoticeType).ToListAsync());
        notices.Should().Contain(n => n.Title == "Material reservado: 1 item, 1 em falta." && n.Link.Contains(order.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task OS_sem_item_de_estoque_tem_BOM_e_a_BOM_vazia_e_liberada_sem_reservas()
    {
        var (_, order, _, _) = await BomOrderAsync(withProduct: false);
        var (_, engineer) = await ActorAsync("engineering");

        var view = await OkAsync(await engineer.GetAsync($"{Boms}/orders/{order}"));
        view.GetProperty("order").GetProperty("productId").ValueKind.Should().Be(JsonValueKind.Null, "chip Sem item de estoque (CA 4)");
        var released = await ReleasedBomAsync(engineer, order);
        released.GetProperty("status").GetString().Should().Be("Released");
        released.GetProperty("releasedBy").GetGuid().Should().NotBeEmpty();

        var events = (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(BomReleased).FullName).ToListAsync()))
            .Select(m => JsonSerializer.Deserialize<BomReleased>(m.Payload)!).Where(e => e.ProductionOrderId == order).ToList();
        events.Should().ContainSingle();
        events[0].Lines.Should().BeEmpty("OS só de mão de obra: BomReleased com lines vazio (CA 9)");
        events[0].Revision.Should().Be(0);
    }

    [Fact]
    public async Task Liberar_publica_o_BomReleased_na_mesma_transacao_com_a_necessidade_calculada()
    {
        var (_, order, _, _) = await BomOrderAsync(quantity: 3m);
        var (engineerId, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa 3 mm");
        var bom = await CreatedBomAsync(engineer, order);
        bom = await OkAsync(await SaveBomAsync(engineer, bom, BomLine(steel, 2.5m, 10m)));

        var preview = await OkAsync(await engineer.GetAsync($"{Boms}/{bom.GetProperty("id").GetGuid()}/release-preview"));
        preview.GetProperty("isRevision").GetBoolean().Should().BeFalse();
        preview.GetProperty("lines")[0].GetProperty("required").GetDecimal().Should().Be(8.25m);

        var stale = await engineer.PostAsJsonAsync($"{Boms}/{bom.GetProperty("id").GetGuid()}/release", new { version = Version(bom) + 1 });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OutboxAsync<BomReleased>(order)).Should().Be(0, "a liberação recusada não publica");

        var released = await ReleaseAsync(engineer, bom);
        released.GetProperty("releasedBy").GetGuid().Should().Be(engineerId);
        var stored = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(BomReleased).FullName).ToListAsync());
        var evt = stored.Select(m => JsonSerializer.Deserialize<BomReleased>(m.Payload)!).Single(e => e.ProductionOrderId == order);
        evt.BomId.Should().Be(bom.GetProperty("id").GetGuid());
        evt.Lines.Should().ContainSingle().Which.Should().Match<BomReleasedLine>(l => l.ItemId == steel && l.RequiredQty == 8.25m);
        evt.ProjectId.Should().NotBeEmpty();

        // Liberada não é editada nem descartada (CA 21).
        (await SaveBomAsync(engineer, released, BomLine(steel, 1m))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await engineer.PostAsJsonAsync($"{Boms}/{bom.GetProperty("id").GetGuid()}/discard", new { version = Version(released) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Copiar_de_outra_OS_sugere_a_ultima_do_mesmo_produto_e_marca_o_item_inativo()
    {
        var product = await ProductAsync("Cabina padrão");
        var (_, source, _, _) = await BomOrderAsync(quantity: 1m, product: product);
        var (_, target, _, _) = await BomOrderAsync(quantity: 4m, product: product);
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa 1,5 mm");
        var paint = await MaterialAsync("Tinta epóxi");
        var released = await ReleasedBomAsync(engineer, source, BomLine(steel, 2m, 5m), BomLine(paint, 0.5m));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.items SET is_active = false WHERE id = {paint}"));

        var sources = (await OkAsync(await engineer.GetAsync($"{Boms}/orders/{target}/copy-sources"))).EnumerateArray().ToList();
        var suggested = sources.Single(s => s.GetProperty("suggested").GetBoolean());
        suggested.GetProperty("bomId").GetGuid().Should().Be(released.GetProperty("id").GetGuid(), "a última OS do mesmo produto com BOM liberada (CA 5, R-PRP-1)");
        sources[0].GetProperty("suggested").GetBoolean().Should().BeTrue("o Sugerido vem no topo");

        var copy = await CreatedBomAsync(engineer, target, released.GetProperty("id").GetGuid());
        copy.GetProperty("copiedFromBomId").GetGuid().Should().Be(released.GetProperty("id").GetGuid());
        var lines = copy.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().HaveCount(2);
        lines.Single(l => l.GetProperty("itemId").GetGuid() == steel).GetProperty("requiredQuantity").GetDecimal().Should().Be(8.4m, "2 × 4 × 1,05 com a quantidade desta OS");
        lines.Single(l => l.GetProperty("itemId").GetGuid() == paint).GetProperty("itemActive").GetBoolean().Should().BeFalse("item que ficou inativo vem marcado para revisão");

        // Salvar o rascunho com o item inativo copiado não bloqueia a edição.
        (await SaveBomAsync(engineer, copy, BomLine(steel, 2m, 5m, id: lines[0].GetProperty("id").GetGuid()), BomLine(paint, 0.5m, id: lines[1].GetProperty("id").GetGuid())))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await OutboxAsync<BomReleased>(target)).Should().Be(0, "copiar não reserva nada");
    }

    [Fact]
    public async Task Descartar_rascunho_exige_engineering_delete_e_fica_no_historico()
    {
        var (_, order, _, _) = await BomOrderAsync();
        var (engineerId, engineer) = await ActorAsync("engineering");
        var (_, editor) = await ActorAsync("engineering:view,create,edit");
        var steel = await MaterialAsync("Chapa 4 mm");
        var released = await ReleasedBomAsync(engineer, order, BomLine(steel, 1m));
        var draft = await JsonAsync(await engineer.SendAsync(Post($"{Boms}/{released.GetProperty("id").GetGuid()}/revise", new { version = Version(released) })));
        var id = draft.GetProperty("id").GetGuid();

        (await editor.PostAsJsonAsync($"{Boms}/{id}/discard", new { version = Version(draft) })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem engineering.delete (CA 7)");
        (await engineer.PostAsJsonAsync($"{Boms}/{id}/discard", new { version = Version(draft) + 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await engineer.PostAsJsonAsync($"{Boms}/{id}/discard", new { version = Version(draft) })).IsSuccessStatusCode.Should().BeTrue();

        var boms = await BomsOfAsync(order);
        boms.Should().ContainSingle("o rascunho foi apagado").Which.Status.Should().Be(BomStatus.Released, "a liberada anterior continua valendo");
        var history = await OkAsync(await engineer.GetAsync($"{Boms}/orders/{order}/history"));
        var entries = history.GetProperty("items").EnumerateArray().ToList();
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "Bom" && e.GetProperty("entityId").GetString() == id.ToString() && e.GetProperty("action").GetString() == "Deleted"
            && e.GetProperty("userId").GetGuid() == engineerId, "o histórico registra quem descartou");
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "BomLine", "as linhas também entram no histórico (CA 21)");
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "Bom" && e.GetProperty("action").GetString() == "Updated", "a liberação aparece com antes e depois");
    }

    [Fact]
    public async Task OS_cancelada_ou_concluida_nao_muda_a_BOM()
    {
        var (_, order, _, _) = await BomOrderAsync();
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa 5 mm");
        var released = await ReleasedBomAsync(engineer, order, BomLine(steel, 1m));
        var (_, other, _, _) = await BomOrderAsync();
        var draft = await CreatedBomAsync(engineer, other);
        var number = (await OrderAsync(order)).Number;

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {order}"));
        var revise = await engineer.SendAsync(Post($"{Boms}/{released.GetProperty("id").GetGuid()}/revise", new { version = Version(released) }));
        revise.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(revise)).GetProperty("detail").GetString().Should().Be($"A OS {number} está cancelada: a BOM não muda.");

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Completed' WHERE id = {other}"));
        var release = await engineer.PostAsJsonAsync($"{Boms}/{draft.GetProperty("id").GetGuid()}/release", new { version = Version(draft) });
        release.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(release)).GetProperty("detail").GetString().Should().EndWith("está concluída: a BOM não muda.");
        (await engineer.SendAsync(Post(Boms, new { productionOrderId = order, copyFromBomId = (Guid?)null }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Revisao_substitui_a_liberada_publica_o_BomRevised_e_o_indice_parcial_garante_uma_liberada()
    {
        var (_, order, _, _) = await BomOrderAsync(quantity: 2m);
        var (_, engineer) = await ActorAsync("engineering");
        var (steel, bolt, paint) = (await MaterialAsync("Chapa 6 mm"), await MaterialAsync("Parafuso M10"), await MaterialAsync("Tinta PU"));
        var released = await ReleasedBomAsync(engineer, order, BomLine(steel, 5m), BomLine(bolt, 1m));

        var revision = await JsonAsync(await engineer.SendAsync(Post($"{Boms}/{released.GetProperty("id").GetGuid()}/revise", new { version = Version(released) })));
        revision.GetProperty("revision").GetInt32().Should().Be(1);
        revision.GetProperty("status").GetString().Should().Be("Draft");
        revision.GetProperty("lines").GetArrayLength().Should().Be(2, "nasce com as linhas da liberada (CA 12)");
        (await BomsOfAsync(order)).Single(b => b.Revision == 0).Status.Should().Be(BomStatus.Released, "a liberada continua valendo até a nova ser liberada");

        var steelLine = revision.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == steel).GetProperty("id").GetGuid();
        revision = await OkAsync(await SaveBomAsync(engineer, revision, BomLine(steel, 6m, id: steelLine), BomLine(paint, 0.25m)));
        var preview = await OkAsync(await engineer.GetAsync($"{Boms}/{revision.GetProperty("id").GetGuid()}/release-preview"));
        preview.GetProperty("isRevision").GetBoolean().Should().BeTrue();
        var byItem = preview.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("itemId").GetGuid());
        byItem[steel].GetProperty("previousRequired").GetDecimal().Should().Be(10m);
        byItem[steel].GetProperty("moreToReserve").GetDecimal().Should().Be(2m);
        byItem[bolt].GetProperty("change").GetString().Should().Be("Removed");
        byItem[bolt].GetProperty("toRelease").GetDecimal().Should().Be(2m);
        byItem[paint].GetProperty("change").GetString().Should().Be("Added");
        byItem[steel].GetProperty("issuedWarning").ValueKind.Should().Be(JsonValueKind.Null, "o baixado vem da EST-02 (CA 14)");

        await ReleaseAsync(engineer, revision);
        var boms = await BomsOfAsync(order);
        boms.Select(b => (b.Revision, b.Status)).Should().Equal((0, BomStatus.Superseded), (1, BomStatus.Released));
        var revised = (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(BomRevised).FullName).ToListAsync()))
            .Select(m => JsonSerializer.Deserialize<BomRevised>(m.Payload)!).Single(e => e.ProductionOrderId == order);
        revised.PreviousBomId.Should().Be(released.GetProperty("id").GetGuid());
        revised.Revision.Should().Be(1);
        revised.Lines.Single(l => l.ItemId == steel).Should().Match<BomRevisedLine>(l => l.RequiredQty == 12m && l.PreviousRequiredQty == 10m);
        revised.Lines.Single(l => l.ItemId == bolt).Should().Match<BomRevisedLine>(l => l.RequiredQty == 0m && l.PreviousRequiredQty == 2m);
        revised.Lines.Single(l => l.ItemId == paint).PreviousRequiredQty.Should().Be(0m);

        // O índice único parcial (production_order_id) WHERE status = 'Released' recusa uma segunda liberada.
        var act = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE engineering.boms SET status = 'Released' WHERE production_order_id = {order} AND revision = 0"));
        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ux_boms_released");

        var compare = await OkAsync(await engineer.GetAsync($"{Boms}/compare?from={boms[0].Id}&to={boms[1].Id}"));
        compare.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("change").GetString()).Should().BeEquivalentTo(["Changed", "Removed", "Added"]);
    }

    [Fact]
    public async Task Etapa_depois_da_primeira_so_inicia_ou_conclui_com_a_BOM_liberada()
    {
        var (owner, order, _, mine) = await BomOrderAsync();
        var (_, engineer) = await ActorAsync("engineering");
        var (_, _, operatorClient, _) = await OperatorAsync(mine);
        var gatedOperator = WithBomGate(operatorClient);
        var gatedOwner = WithBomGate(owner);
        var first = await StepIdAsync(order, 1);
        var second = await StepIdAsync(order, 2);

        // A primeira etapa inicia sem BOM (D12).
        await OkAsync(await gatedOperator.SendAsync(Tap($"{Shopfloor}/steps/{first}/start")));
        await MarkStepAsync(order, 1, ProductionStepStatus.Done);
        await MarkStepAsync(order, 2, ProductionStepStatus.Ready);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.labor_entries SET ended_at = now() WHERE order_id = {order} AND ended_at IS NULL"));

        var blocked = await gatedOperator.SendAsync(Tap($"{Shopfloor}/steps/{second}/start"));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(blocked);
        Code(problem).Should().Be("bom_not_released");
        problem.GetProperty("detail").GetString().Should().Be("A BOM desta OS ainda não foi liberada. Fale com a Engenharia.");

        // "Concluir etapa" do supervisor também recusa (CA 16).
        var detail = await GetOrderAsync(owner, order);
        var complete = await gatedOwner.PostAsJsonAsync($"{Base}/{order}/steps/{second}/complete", new { reason = "Corte feito fora do sistema.", version = Version(detail) });
        complete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(complete)).Should().Be("bom_not_released");

        // CA 17: a OS aparece no filtro das que vão parar sem BOM.
        var list = await OkAsync(await engineer.GetAsync($"{Boms}?situation=will_stop&pageSize=100"));
        list.GetProperty("items").EnumerateArray().Should().Contain(r => r.GetProperty("productionOrderId").GetGuid() == order && r.GetProperty("willStop").GetBoolean());

        // Liberada a BOM (mesmo vazia), a etapa inicia e a OS sai do filtro.
        await ReleasedBomAsync(engineer, order);
        await OkAsync(await gatedOperator.SendAsync(Tap($"{Shopfloor}/steps/{second}/start")));
        list = await OkAsync(await engineer.GetAsync($"{Boms}?situation=will_stop&pageSize=100"));
        list.GetProperty("items").EnumerateArray().Should().NotContain(r => r.GetProperty("productionOrderId").GetGuid() == order);
    }

    [Fact]
    public async Task Etapa_ja_em_andamento_no_deploy_nao_e_interrompida()
    {
        var (owner, order, _, mine) = await BomOrderAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(mine);
        await MarkStepAsync(order, 1, ProductionStepStatus.Done);
        await MarkStepAsync(order, 2, ProductionStepStatus.InProgress);
        var second = await StepIdAsync(order, 2);

        await OkAsync(await WithBomGate(operatorClient).SendAsync(Tap($"{Shopfloor}/steps/{second}/start")));
        var detail = await GetOrderAsync(owner, order);
        var (_, supervisor) = await ActorAsync("production:view,edit,approve");
        await OkAsync(await WithBomGate(supervisor).PostAsJsonAsync($"{Base}/{order}/steps/{second}/complete", new { reason = "Etapa iniciada antes da ENG-02.", version = Version(detail) }));
    }

    [Fact]
    public async Task IBoms_devolve_a_revisao_liberada_vigente_com_a_necessidade()
    {
        var (_, order, _, _) = await BomOrderAsync(quantity: 2m);
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa 8 mm");
        await using (var scope = await ScopeAsync())
        {
            var boms = scope.ServiceProvider.GetRequiredService<IBoms>();
            (await boms.ReleasedForOrderAsync(order)).Should().BeNull();
            (await boms.IsReleasedAsync(order)).Should().BeFalse();
        }

        var released = await ReleasedBomAsync(engineer, order, BomLine(steel, 1.25m, 20m));
        var revision = await JsonAsync(await engineer.SendAsync(Post($"{Boms}/{released.GetProperty("id").GetGuid()}/revise", new { version = Version(released) })));
        await using (var scope = await ScopeAsync())
        {
            var info = await scope.ServiceProvider.GetRequiredService<IBoms>().ReleasedForOrderAsync(order);
            info!.Revision.Should().Be(0, "o rascunho da revisão 1 ainda não vale");
            info.Lines.Should().ContainSingle().Which.Should().Match<BomRequirement>(l => l.ItemId == steel && l.RequiredQty == 3m);
        }

        await ReleaseAsync(engineer, revision);
        await using (var scope = await ScopeAsync())
        {
            var boms = scope.ServiceProvider.GetRequiredService<IBoms>();
            (await boms.ReleasedForOrderAsync(order))!.Revision.Should().Be(1, "a fonte já está na revisão nova: o evento da revisão 0 é ignorado (ADR 0023 §6)");
            (await boms.IsReleasedAsync(order)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Tres_camadas_e_IDOR_na_BOM()
    {
        var (_, order, _, _) = await BomOrderAsync();
        var (_, engineer) = await ActorAsync("engineering");
        var (_, viewer) = await ActorAsync("engineering:view");
        var (_, nobody) = await ActorAsync(null);
        var steel = await MaterialAsync("Chapa 10 mm");
        var bom = await CreatedBomAsync(engineer, order);
        bom = await OkAsync(await SaveBomAsync(engineer, bom, BomLine(steel, 1m)));
        var id = bom.GetProperty("id").GetGuid();
        var line = bom.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var noGroup = await nobody.GetAsync($"{Boms}/orders/{order}");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden");
        (await viewer.GetAsync($"{Boms}/orders/{order}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(Post(Boms, new { productionOrderId = order, copyFromBomId = (Guid?)null }))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "criar sem engineering.create");
        (await SaveBomAsync(viewer, bom, BomLine(steel, 2m))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "editar sem engineering.edit");
        (await viewer.PostAsJsonAsync($"{Boms}/{id}/release", new { version = Version(bom) })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "liberar sem engineering.approve");
        (await viewer.PostAsJsonAsync($"{Boms}/{id}/discard", new { version = Version(bom) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // IDOR: a linha de outra BOM e a OS que não existe respondem 404.
        (await viewer.GetAsync($"{Boms}/{id}/lines/{line}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, otherOrder, _, _) = await BomOrderAsync();
        var otherBom = await CreatedBomAsync(engineer, otherOrder);
        (await viewer.GetAsync($"{Boms}/{otherBom.GetProperty("id").GetGuid()}/lines/{line}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await engineer.SendAsync(Post(Boms, new { productionOrderId = Guid.CreateVersion7(), copyFromBomId = (Guid?)null }))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignLine = await SaveBomAsync(engineer, await OkAsync(await engineer.GetAsync($"{Boms}/{otherBom.GetProperty("id").GetGuid()}")), BomLine(steel, 1m, id: line));
        foreignLine.StatusCode.Should().Be(HttpStatusCode.NotFound, "linha com id de outra BOM no salvar");

        await SetModuleReleasedAsync("engineering", false);
        try
        {
            var notReleased = await engineer.GetAsync($"{Boms}/orders/{order}");
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("engineering", true);
        }
    }

    [Fact]
    public async Task Duas_liberacoes_juntas_gravam_uma_e_o_evento_sai_uma_vez()
    {
        var (_, order, _, _) = await BomOrderAsync();
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa 12 mm");
        var bom = await CreatedBomAsync(engineer, order);
        bom = await OkAsync(await SaveBomAsync(engineer, bom, BomLine(steel, 1m)));
        var body = new { version = Version(bom) };
        var url = $"{Boms}/{bom.GetProperty("id").GetGuid()}/release";

        var both = await Task.WhenAll(engineer.PostAsJsonAsync(url, body), engineer.PostAsJsonAsync(url, body));
        both.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict], "xmin: só uma grava (CA 20)");
        (await OutboxAsync<BomReleased>(order)).Should().Be(1);

        // Duas criações juntas na mesma OS: 201 e 409 (índice parcial do rascunho), nunca 500.
        var (_, other, _, _) = await BomOrderAsync();
        var creates = await Task.WhenAll(
            engineer.SendAsync(Post(Boms, new { productionOrderId = other, copyFromBomId = (Guid?)null })),
            engineer.SendAsync(Post(Boms, new { productionOrderId = other, copyFromBomId = (Guid?)null })));
        creates.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await BomsOfAsync(other)).Should().ContainSingle();
    }
}