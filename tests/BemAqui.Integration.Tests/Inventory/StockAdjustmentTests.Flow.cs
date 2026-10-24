using System.Net;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-01b CA 1 a 13: seed, envio sem efeito, aviso, reservado, aprovação síncrona, recálculo, segregação, decisões e o modo Delta.</summary>
public partial class StockAdjustmentTests
{
    [Fact]
    public async Task Seed_patch_inventory_adjust_warehouse_uma_vez_com_perm_version_e_approve_so_no_Proprietario()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.SeedPatches.CountAsync(p => p.Key == "inventory-adjust-warehouse")).Should().Be(1, "o provisionamento aplicou o patch (CA 1)");
        var warehouse = await db.Groups.SingleAsync(g => g.SeedKey == "warehouse");
        (await db.GroupPermissions.CountAsync(p => p.GroupId == warehouse.Id && p.ModuleCode == "inventory" && p.Action == "adjust")).Should().Be(1);
        (await db.GroupPermissions.CountAsync(p => p.ModuleCode == "inventory" && p.Action == "approve" && db.Groups.Any(g => g.Id == p.GroupId && !g.IsSystem)))
            .Should().Be(0, "inventory.approve continua só no Proprietário (ADR 0008)");

        // Tenant existente no próximo deploy: o patch ainda não aplicado roda uma vez e sobe a versão de permissão dos membros.
        var (member, _) = await ActorAsync("warehouse");
        await db.GroupPermissions.Where(p => p.GroupId == warehouse.Id && p.ModuleCode == "inventory" && p.Action == "adjust").ExecuteDeleteAsync();
        await db.SeedPatches.Where(p => p.Key == "inventory-adjust-warehouse").ExecuteDeleteAsync();
        var before = await db.Users.AsNoTracking().Where(u => u.Id == member).Select(u => u.PermVersion).SingleAsync();
        foreach (var round in Enumerable.Range(0, 2))
        {
            foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
            }
        }

        (await db.SeedPatches.CountAsync(p => p.Key == "inventory-adjust-warehouse")).Should().Be(1);
        (await db.GroupPermissions.CountAsync(p => p.GroupId == warehouse.Id && p.ModuleCode == "inventory" && p.Action == "adjust")).Should().Be(1, "uma vez, mesmo rodando de novo");
        (await db.Users.AsNoTracking().Where(u => u.Id == member).Select(u => u.PermVersion).SingleAsync()).Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Ajuste_dentro_e_acima_do_limite_nasce_pendente_sem_mexer_no_saldo_com_numero_AJ_e_idempotencia()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 20m);

        var key = Guid.NewGuid();
        var small = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 9m)), key)), HttpStatusCode.Created);
        small.GetProperty("status").GetString().Should().Be("Pending", "CA 14: todo ajuste vai para aprovação, mesmo de R$ 20,00");
        small.GetProperty("number").GetString().Should().MatchRegex(@"^AJ-\d{4}-\d{4}$", "D15");
        var line = small.GetProperty("lines")[0];
        (line.GetProperty("systemQtyAtRequest").GetDecimal(), line.GetProperty("countedQty").GetDecimal(), line.GetProperty("deltaQty").GetDecimal()).Should().Be((10m, 9m, -1m));
        (line.GetProperty("onHand").GetDecimal(), line.GetProperty("reserved").GetDecimal(), line.GetProperty("available").GetDecimal()).Should().Be((10m, 0m, 10m));
        line.GetProperty("estimatedValue").GetDecimal().Should().Be(20m, "diferença × custo médio do momento");
        var replay = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 9m)), key)), HttpStatusCode.Created);
        Id(replay).Should().Be(Id(small), "Idempotency-Key: o mesmo envio não cria outro ajuste");

        var big = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Delta", Line(item, 200m)))), HttpStatusCode.Created);
        big.GetProperty("status").GetString().Should().Be("Pending", "acima do limite dos lançamentos manuais também é só aprovação");
        big.GetProperty("surplusValue").GetDecimal().Should().Be(4000m);
        (await StateAsync(item, location)).Should().Be((10m, 0m, 20m), "o saldo não muda no envio (CA 2)");
        (await MovementsAsync(Id(small))).Should().BeEmpty();
        (await WithDbAsync(db => db.StockAdjustments.CountAsync(a => a.LocationId == location))).Should().Be(2);
    }

    [Fact]
    public async Task O_modo_e_do_ajuste_inteiro_e_as_linhas_sao_validadas()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, lotted, location) = (await ItemAsync(), await ItemAsync(tracksLot: true), await LocationAsync());
        await ReceiveAsync(item, location, 5m, 1m);

        var perLine = await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", new { itemId = item, lotId = (Guid?)null, quantity = 3m, unitId = (Guid?)null, mode = "Delta" })));
        perLine.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 2: o modo é do ajuste, não da linha");
        FieldErrors(await JsonAsync(perLine), "lines[0].mode").Single().Should().StartWith("O modo é do ajuste inteiro");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, -1m))))), "lines[0].quantity")
            .Should().ContainSingle().Which.Should().Be("A quantidade contada não pode ser negativa.");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Delta", Line(item, 0m))))), "lines[0].quantity").Should().ContainSingle();
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 5m))))), "lines[0].quantity")
            .Single().Should().StartWith("Sem diferença");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 4m), Line(item, 3m))))), "lines[1].itemId")
            .Single().Should().Be("Item repetido neste ajuste.");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(lotted, 1m))))), "lines[0].lotId").Should().ContainSingle();
        var otherLot = await LotAsync(await ItemAsync(tracksLot: true), "L-OUTRO");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(lotted, 1m, otherLot))))), "lines[0].lotId")
            .Single().Should().Be("Lote não encontrado neste item.", "lote de outro item");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(Guid.NewGuid(), "Counted", Line(item, 4m))))), "locationId").Should().ContainSingle();
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(Guid.NewGuid(), 4m))))), "lines[0].itemId").Should().ContainSingle();
        var other = await warehouse.SendAsync(Command(Adjustments, new { locationId = location, mode = "Counted", reason = "Other", reasonDetail = " ", lines = new[] { Line(item, 4m) } }));
        FieldErrors(await JsonAsync(other), "reasonDetail").Should().ContainSingle("\"Outro\" exige a descrição");
        (await WithDbAsync(db => db.StockAdjustments.CountAsync(a => a.LocationId == location))).Should().Be(0, "nada grava");
    }

    [Fact]
    public async Task Aviso_aos_aprovadores_elegiveis_sem_o_autor_uma_vez_e_com_link()
    {
        var (authorId, warehouse) = await ActorAsync("warehouse");
        var (approverId, _) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 12.5m);
        var created = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 8m)))), HttpStatusCode.Created);
        var id = Id(created);
        var notices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.Type == StockAdjustmentService.ApprovalType && n.Link == StockAdjustmentService.Link + id).ToListAsync());
        notices.Select(n => n.UserId).Should().Contain(approverId).And.NotContain(authorId, "CA 3: o autor não é avisado");
        notices.Should().OnlyHaveUniqueItems(n => n.UserId);
        var location_ = await WithDbAsync(db => db.Locations.Where(l => l.Id == location).Select(l => l.Name).SingleAsync());
        notices.Single(n => n.UserId == approverId).Title.Should().Be($"Ajuste {created.GetProperty("number").GetString()} de {location_} aguardando aprovação: 1 item, R$ 25,00 de diferença.");
        notices.Single(n => n.UserId == approverId).DedupKey.Should().Be($"inventory-adjustment:{id}");
    }

    [Fact]
    public async Task Ajuste_abaixo_do_reservado_e_recusado_na_linha_com_Ver_reservas_do_item()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 10m, 5m);
        await ReserveAsync(item, 4m);
        (await StateAsync(item, alm)).Reserved.Should().Be(4m);

        var refused = await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Counted", Line(item, 2m))));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await refused.Content.ReadAsStringAsync());
        var problem = await JsonAsync(refused);
        FieldErrors(problem, "lines[0].quantity").Single().Should().Be("O saldo ficaria 2,0000 UN, abaixo do reservado (4,0000 UN) para OS.", "CA 4");
        problem.GetProperty("recovery").GetString().Should().Be("Ver reservas do item");
        problem.GetProperty("itemId").GetGuid().Should().Be(item);
        problem.GetProperty("link").GetString().Should().Be($"/app/estoque/itens/{item}?aba=reservas");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Delta", Line(item, -7m))))), "lines[0].quantity")
            .Single().Should().Contain("abaixo do reservado", "o modo Delta segue a mesma regra");
        (await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Counted", Line(item, 4m)))), HttpStatusCode.Created))
            .GetProperty("status").GetString().Should().Be("Pending", "até o reservado pode");

        var lotted = await ItemAsync(tracksLot: true);
        var lot = await LotAsync(lotted, "L-AJ");
        await ReceiveAsync(lotted, alm, 3m, 2m, lot);
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Delta", Line(lotted, -4m, lot))))), "lines[0].quantity")
            .Single().Should().Be("O saldo do lote ficaria -1,0000 UN, abaixo de zero.");
    }

    [Fact]
    public async Task Aprovacao_por_outra_pessoa_grava_os_movimentos_pelo_custo_medio_e_avisa_o_autor()
    {
        var (authorId, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (a, b, location) = (await ItemAsync(), await ItemAsync(), await LocationAsync());
        await ReceiveAsync(a, location, 10m, 7.5m);
        await ReceiveAsync(b, location, 4m, 3m);
        var created = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(a, 12m), Line(b, 1m)))), HttpStatusCode.Created);

        var approved = await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created, "Conferido"))));
        approved.GetProperty("status").GetString().Should().Be("Approved", "CA 5");
        approved.GetProperty("decidedByName").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined);
        approved.GetProperty("decisionComment").GetString().Should().Be("Conferido");
        var movements = await MovementsAsync(Id(created));
        movements.Should().HaveCount(2).And.OnlyContain(m => m.Type == StockMovementType.Adjustment && m.SourceNumber == created.GetProperty("number").GetString() && m.Origin == StockOrigins.Document);
        movements.Select(m => m.Number).Distinct().Should().ContainSingle("um número MOV para o ajuste");
        var surplus = movements.Single(m => m.ItemId == a);
        (surplus.Quantity, surplus.UnitCost, surplus.TotalCost).Should().Be((2m, 7.5m, 15m), "a sobra entra pelo custo médio do momento");
        var shortage = movements.Single(m => m.ItemId == b);
        (shortage.Quantity, shortage.UnitCost, shortage.TotalCost).Should().Be((-3m, 3m, -9m), "a falta sai pelo custo médio do momento");
        (await StateAsync(a, location)).Should().Be((12m, 0m, 7.5m), "a sobra não muda o custo médio");
        (await StateAsync(b, location)).Should().Be((1m, 0m, 3m));
        (approved.GetProperty("surplusValue").GetDecimal(), approved.GetProperty("shortageValue").GetDecimal()).Should().Be((15m, 9m));
        approved.GetProperty("lines").EnumerateArray().Should().OnlyContain(l => l.GetProperty("movementId").ValueKind == System.Text.Json.JsonValueKind.String);
        (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == authorId && n.Type == StockAdjustmentService.DecisionType).Select(n => n.Title).ToListAsync()))
            .Should().Contain($"Ajuste {created.GetProperty("number").GetString()} aprovado.");

        var again = await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(approved)));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(again)).Should().Be("stock_adjustment_decided");
    }

    [Fact]
    public async Task Ajuste_aprovado_nao_se_edita_e_o_aprovador_decide_sem_editar()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var created = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 8m)))), HttpStatusCode.Created);
        var pendingEdit = await warehouse.PutAsync($"{Adjustments}/{Id(created)}", System.Net.Http.Json.JsonContent.Create(new { }));
        pendingEdit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(pendingEdit)).Should().Be("stock_adjustment_not_editable");

        foreach (var extra in new object[]
                 {
                     new { comment = (string?)null, version = created.GetProperty("version").GetUInt32(), quantity = 9m },
                     new { comment = (string?)null, version = created.GetProperty("version").GetUInt32(), mode = "Delta" },
                     new { comment = (string?)null, version = created.GetProperty("version").GetUInt32(), lines = new[] { Line(item, 9m) } },
                 })
        {
            var edited = await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", extra));
            edited.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 6: só a decisão e o comentário");
            Code(await JsonAsync(edited)).Should().Be("decision_only");
        }

        (await StateAsync(item, location)).OnHand.Should().Be(10m);
        await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created))));
        (await StateAsync(item, location)).OnHand.Should().Be(8m, "aprovado o que foi enviado");
        var approvedEdit = await warehouse.PutAsync($"{Adjustments}/{Id(created)}", System.Net.Http.Json.JsonContent.Create(new { }));
        approvedEdit.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 12");
        Detail(await JsonAsync(approvedEdit)).Should().Be("Ajuste aprovado não muda. Para corrigir, lance outro ajuste ou estorne este.");
    }

    [Fact]
    public async Task Saldo_mudado_entre_envio_e_aprovacao_recalcula_no_Counted_mantem_no_Delta_e_recusa_abaixo_do_reservado()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (counted, delta, alm) = (await ItemAsync(), await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(counted, alm, 10m, 2m);
        await ReceiveAsync(delta, alm, 10m, 2m);
        var a = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Counted", Line(counted, 7m)))), HttpStatusCode.Created);
        var b = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Delta", Line(delta, -3m)))), HttpStatusCode.Created);
        await ReceiveAsync(counted, alm, 5m, 2m);
        await ReceiveAsync(delta, alm, 5m, 2m);

        var view = await OkAsync(await owner.GetAsync($"{Adjustments}/{Id(a)}"));
        view.GetProperty("impact").GetProperty("changedSinceRequest").GetBoolean().Should().BeTrue("o diálogo mostra \"O saldo mudou desde o envio\" (CA 7)");
        var now = view.GetProperty("lines")[0];
        (now.GetProperty("systemQtyNow").GetDecimal(), now.GetProperty("deltaNow").GetDecimal(), now.GetProperty("onHandAfter").GetDecimal()).Should().Be((15m, -8m, 7m));

        await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(a)}/approve", Decision(a))));
        (await StateAsync(counted, alm)).OnHand.Should().Be(7m, "no modo Counted o contado vale sobre o saldo de agora");
        (await MovementsAsync(Id(a))).Single().Quantity.Should().Be(-8m);
        await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(b)}/approve", Decision(b))));
        (await StateAsync(delta, alm)).OnHand.Should().Be(12m, "no modo Delta a diferença se soma ao saldo de agora");

        // Reserva depois do envio: a aprovação confere de novo e recusa sem gravar nada.
        var c = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Counted", Line(delta, 4m)))), HttpStatusCode.Created);
        await ReserveAsync(delta, 6m);
        var refused = await owner.SendAsync(Command($"{Adjustments}/{Id(c)}/approve", Decision(c)));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("insufficient_stock");
        Detail(problem).Should().Be("Linha 1: O saldo ficaria 4,0000 UN, abaixo do reservado (6,0000 UN) para OS.");
        problem.GetProperty("field").GetString().Should().Be("lines[0].quantity");
        problem.GetProperty("recovery").GetString().Should().Be("Ver reservas do item");
        (await OkAsync(await owner.GetAsync($"{Adjustments}/{Id(c)}"))).GetProperty("status").GetString().Should().Be("Pending", "nada é gravado");
        (await MovementsAsync(Id(c))).Should().BeEmpty();
        (await StateAsync(delta, alm)).Should().Be((12m, 6m, 2m));
    }

    [Fact]
    public async Task Autor_nao_aprova_o_proprio_ajuste_salvo_o_Proprietario_unico_com_Autoaprovado()
    {
        var (authorId, author) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var created = await OkAsync(await author.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 9m)))), HttpStatusCode.Created);
        var refused = await author.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created)));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Detail(await JsonAsync(refused)).Should().Be("Quem lança o ajuste não o aprova.", "CA 8");
        (await OkAsync(await author.GetAsync($"{Adjustments}/{Id(created)}"))).GetProperty("decideBlockedReason").GetString().Should().Be("Quem lança o ajuste não o aprova.");

        await AloneAsync(authorId, async () =>
        {
            var self = await OkAsync(await author.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created))));
            self.GetProperty("selfApproved").GetBoolean().Should().BeTrue("marca Autoaprovado (SoleOwnerRule)");
            self.GetProperty("status").GetString().Should().Be("Approved");
            (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == authorId && n.Type == StockAdjustmentService.SelfApprovedType).Select(n => n.Title).ToListAsync()))
                .Should().ContainSingle(t => t.EndsWith($"aprovou o próprio ajuste {created.GetProperty("number").GetString()} (Autoaprovado).", StringComparison.Ordinal));
        });
        (await StateAsync(item, location)).OnHand.Should().Be(9m);
    }

    [Fact]
    public async Task Reprovar_com_comentario_e_cancelar_pelo_autor_nao_geram_movimento()
    {
        var (authorId, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (_, colleague) = await ActorAsync("warehouse");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var toReject = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 9m)))), HttpStatusCode.Created);
        FieldErrors(await JsonAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(toReject)}/reject", Decision(toReject, " ")))), "comment").Should().ContainSingle();
        var rejected = await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(toReject)}/reject", Decision(toReject, "Conte de novo o corredor B"))));
        rejected.GetProperty("status").GetString().Should().Be("Rejected", "CA 9");
        (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == authorId && n.Type == StockAdjustmentService.DecisionType).Select(n => n.Title).ToListAsync()))
            .Should().Contain($"Ajuste {toReject.GetProperty("number").GetString()} reprovado: Conte de novo o corredor B");

        var toCancel = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 8m)))), HttpStatusCode.Created);
        (await colleague.SendAsync(Command($"{Adjustments}/{Id(toCancel)}/cancel", new { reason = "Não é meu", version = toCancel.GetProperty("version").GetUInt32() })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "só o autor cancela");
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command($"{Adjustments}/{Id(toCancel)}/cancel", new { reason = "", version = toCancel.GetProperty("version").GetUInt32() }))), "reason")
            .Should().ContainSingle("CA 10: com motivo");
        var cancelled = await OkAsync(await warehouse.SendAsync(Command($"{Adjustments}/{Id(toCancel)}/cancel", new { reason = "Contei errado", version = toCancel.GetProperty("version").GetUInt32() })));
        (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("cancelReason").GetString()).Should().Be(("Cancelled", "Contei errado"));
        (await MovementsAsync(Id(toReject))).Should().BeEmpty();
        (await MovementsAsync(Id(toCancel))).Should().BeEmpty();
        (await StateAsync(item, location)).OnHand.Should().Be(10m);
    }

    [Fact]
    public async Task Duas_aprovacoes_simultaneas_so_uma_grava_e_os_movimentos_nao_duplicam()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (_, otherOwner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var created = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Delta", Line(item, 5m)))), HttpStatusCode.Created);
        var decisions = await Task.WhenAll(
            owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created))),
            otherOwner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created))));
        decisions.Count(d => d.StatusCode == HttpStatusCode.OK).Should().Be(1, "CA 11: a transição pelo xmin deixa só uma passar");
        decisions.Count(d => d.StatusCode == HttpStatusCode.Conflict).Should().Be(1, "a outra é 409, não 500");
        (await MovementsAsync(Id(created))).Should().ContainSingle("idempotência por (StockAdjustment, ajuste, linha, Adjustment)");
        (await StateAsync(item, location)).OnHand.Should().Be(15m);
    }

    [Fact]
    public async Task Estorno_do_ajuste_pelo_documento_por_quem_nao_lancou_gera_os_inversos()
    {
        var (authorId, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 4m);
        var created = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 6m)))), HttpStatusCode.Created);
        var approved = await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created))));
        (await warehouse.SendAsync(Command($"{Adjustments}/{Id(created)}/reverse", new { reason = "Achei o material", version = approved.GetProperty("version").GetUInt32() })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "estornar exige inventory.approve");
        var movement = (await MovementsAsync(Id(created))).Single();
        var fromMovements = await owner.SendAsync(Command($"{Base}/movements/{movement.Id}/reverse", new { reason = "Pela tela de movimentos" }));
        fromMovements.StatusCode.Should().Be(HttpStatusCode.Conflict, "EST-01a CA 23: estorne pelo documento de origem");
        Detail(await JsonAsync(fromMovements)).Should().Contain(created.GetProperty("number").GetString()!);

        FieldErrors(await JsonAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/reverse", new { reason = " ", version = approved.GetProperty("version").GetUInt32() }))), "reason").Should().ContainSingle();
        var reversed = await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/reverse", new { reason = "Achei o material", version = approved.GetProperty("version").GetUInt32() })));
        reversed.GetProperty("reversedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String, "CA 12");
        reversed.GetProperty("canReverse").GetBoolean().Should().BeFalse();
        var all = await MovementsAsync(Id(created));
        all.Should().HaveCount(2);
        all.Single(m => m.ReversesMovementId == movement.Id).Quantity.Should().Be(4m, "o inverso pelo custo do original");
        (await StateAsync(item, location)).Should().Be((10m, 0m, 4m));
        var twice = await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/reverse", new { reason = "De novo", version = reversed.GetProperty("version").GetUInt32() }));
        Code(await JsonAsync(twice)).Should().Be("stock_adjustment_reversed");
        (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == authorId && n.Type == StockAdjustmentService.DecisionType).Select(n => n.Title).ToListAsync()))
            .Should().Contain(t => t.StartsWith($"Ajuste {created.GetProperty("number").GetString()} estornado", StringComparison.Ordinal));

        // O autor com inventory.approve não estorna o próprio ajuste (há outro aprovador).
        var (_, ownerAuthor) = await ActorAsync("owner");
        var own = await OkAsync(await ownerAuthor.SendAsync(Command(Adjustments, Adjustment(location, "Delta", Line(item, 1m)))), HttpStatusCode.Created);
        var ownApproved = await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(own)}/approve", Decision(own))));
        var ownReverse = await ownerAuthor.SendAsync(Command($"{Adjustments}/{Id(own)}/reverse", new { reason = "Meu", version = ownApproved.GetProperty("version").GetUInt32() }));
        ownReverse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Detail(await JsonAsync(ownReverse)).Should().Be("Quem lança o ajuste não o estorna.");
    }

    [Fact]
    public async Task Estorno_de_sobra_ja_reservada_e_recusado_e_nada_e_gravado()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 2m, 3m);
        var created = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(alm, "Counted", Line(item, 6m)))), HttpStatusCode.Created);
        var approved = await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created))));
        await ReserveAsync(item, 5m);
        var refused = await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/reverse", new { reason = "Sobra lançada errada", version = approved.GetProperty("version").GetUInt32() }));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(refused)).Should().Be("insufficient_stock");
        (await OkAsync(await owner.GetAsync($"{Adjustments}/{Id(created)}"))).GetProperty("reversedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "nada gravado");
        (await StateAsync(item, alm)).Should().Be((6m, 5m, 3m));
    }

    [Fact]
    public async Task Ajuste_Delta_do_inventario_pelo_servico_segue_a_aprovacao_e_aplica_sobre_o_saldo_de_agora()
    {
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 2m);
        var count = Guid.CreateVersion7();
        // O inventário bloqueia o local enquanto conta: o ajuste dele nasce mesmo assim (CA 18).
        Counting[location] = new LocationCountBlock(location, "Local", count, "INV-2026-0042");
        StockAdjustmentDetail detail;
        try
        {
            // O fechamento do inventário (EST-05) chama o serviço direto, sem tela nesta story.
            await using var scope = await ScopeAsync();
            var service = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
            var result = await service.CreateFromCountAsync(new CountAdjustmentInput(count, "INV-2026-0042", location, [new CountAdjustmentLine(item, null, -2m)]));
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            detail = result.Value;
        }
        finally
        {
            Counting.TryRemove(location, out _);
        }

        (detail.Mode, detail.Origin, detail.OriginId, detail.OriginNumber, detail.Reason, detail.Status)
            .Should().Be((StockAdjustmentMode.Delta, StockAdjustmentOrigin.InventoryCount, count, "INV-2026-0042", StockAdjustmentReason.Count, StockAdjustmentStatus.Pending), "CA 13");
        detail.OriginLink.Should().Be("/app/estoque/inventarios/" + count);
        detail.ReasonDetail.Should().Be("Inventário INV-2026-0042");
        (await StateAsync(item, location)).OnHand.Should().Be(10m, "o saldo só muda na aprovação");

        await ReceiveAsync(item, location, 3m, 2m);
        var view = await OkAsync(await owner.GetAsync($"{Adjustments}/{detail.Id}"));
        await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{detail.Id}/approve", Decision(view))));
        (await StateAsync(item, location)).OnHand.Should().Be(11m, "a diferença da contagem se soma ao saldo do momento da aprovação");
    }
}
