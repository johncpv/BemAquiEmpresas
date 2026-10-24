using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-01a CA 1, 2, 4 e 6 a 11: saldo inicial na janela aberta, entrada, saída, transferência e a segregação da D7.</summary>
public partial class StockTests
{
    [Fact]
    public async Task Saldo_inicial_grava_em_D_23_59_59_e_bloqueia_item_com_movimento_no_local()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, location) = (await ItemAsync(tracksLot: true), await LocationAsync());
        var cutoff = Today.AddDays(-1);
        var body = new { cutoffDate = Iso(cutoff), itemId = item, locationId = location, quantity = 12m, unitId = (Guid?)null, unitCost = 8.5m, lotNumber = "L-OPEN" };
        var created = await OkAsync(await warehouse.SendAsync(Command($"{Base}/opening-balances", body)), HttpStatusCode.Created);
        created.GetProperty("kind").GetString().Should().Be("OpeningBalance");
        created.GetProperty("status").GetString().Should().Be("Posted");
        created.GetProperty("number").GetString().Should().MatchRegex(@"^MOV-\d{4}-\d{6}$");
        var movement = await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.ItemId == item));
        movement.At.Should().Be(ManualPostingService.OpeningAt(cutoff), "ADR 0016: D 23:59:59 de São Paulo, em UTC");
        movement.LotId.Should().NotBeNull();
        (await StateAsync(item, location)).Should().Be((12m, 8.5m));

        var again = await warehouse.SendAsync(Command($"{Base}/opening-balances", body));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 2");
        var problem = await JsonAsync(again);
        Detail(problem).Should().Be("Este item já tem movimentos neste local. Use um ajuste de estoque.");
        problem.GetProperty("recovery").GetString().Should().Be("Lançar ajuste", "EST-01b: o caminho para corrigir é o ajuste de estoque");

        var tomorrow = await warehouse.SendAsync(Command($"{Base}/opening-balances", new { cutoffDate = Iso(Today.AddDays(1)), itemId = await ItemAsync(), locationId = location, quantity = 1m, unitId = (Guid?)null, unitCost = 1m, lotNumber = (string?)null }));
        FieldErrors(await JsonAsync(tomorrow), "cutoffDate").Should().ContainSingle();
        var noLot = await warehouse.SendAsync(Command($"{Base}/opening-balances", new { cutoffDate = Iso(cutoff), itemId = await ItemAsync(tracksLot: true), locationId = location, quantity = 1m, unitId = (Guid?)null, unitCost = 1m, lotNumber = (string?)null }));
        FieldErrors(await JsonAsync(noLot), "lotNumber").Should().ContainSingle("item com lote exige o número");
    }

    [Fact]
    public async Task Saldo_inicial_em_lote_e_atomico_com_caminho_da_linha_invalida_e_um_numero()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (a, b, location) = (await ItemAsync(), await ItemAsync(), await LocationAsync());
        object Line(Guid item, decimal cost) => new { itemId = item, locationId = location, quantity = 2m, unitId = (Guid?)null, unitCost = cost, lotNumber = (string?)null };
        var invalid = await warehouse.SendAsync(Command($"{Base}/opening-balances/batch", new { cutoffDate = Iso(Today), lines = new[] { Line(a, 1m), Line(b, 0m) } }));
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        FieldErrors(await JsonAsync(invalid), "lines[1].unitCost").Should().ContainSingle();
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ItemId == a))).Should().Be(0, "nada grava (CA 4)");

        var key = Guid.NewGuid();
        var body = new { cutoffDate = Iso(Today), lines = new[] { Line(a, 1m), Line(b, 2m) } };
        var created = await OkAsync(await warehouse.SendAsync(Command($"{Base}/opening-balances/batch", body, key)), HttpStatusCode.Created);
        var replay = await OkAsync(await warehouse.SendAsync(Command($"{Base}/opening-balances/batch", body, key)), HttpStatusCode.Created);
        replay.GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid(), "Idempotency-Key");
        var numbers = await WithDbAsync(db => db.StockMovements.Where(m => m.ItemId == a || m.ItemId == b).Select(m => m.Number).Distinct().ToListAsync());
        numbers.Should().ContainSingle("um número MOV para o lançamento");

        var repeated = await warehouse.SendAsync(Command($"{Base}/opening-balances/batch", new { cutoffDate = Iso(Today), lines = new[] { Line(await ItemAsync(), 1m), Line(a, 1m) } }));
        repeated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        FieldErrors(await JsonAsync(repeated), "lines[1].itemId").Single().Should().StartWith("Este item já tem movimentos neste local");
    }

    [Fact]
    public async Task Entrada_saida_e_transferencia_abaixo_dos_limites_gravam_na_hora()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, a, b) = (await ItemAsync(), await LocationAsync(), await LocationAsync());
        var receipt = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, a, 10m, 20m))), HttpStatusCode.Created);
        receipt.GetProperty("status").GetString().Should().Be("Posted");
        (await StateAsync(item, a)).Should().Be((10m, 20m));

        var transfer = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Transfer", item, a, 4m, to: b))), HttpStatusCode.Created);
        var transferId = transfer.GetProperty("id").GetGuid();
        var legs = await WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.SourceId == transferId).ToListAsync());
        legs.Should().HaveCount(2);
        legs.Should().OnlyContain(m => m.Type == StockMovementType.Transfer && m.Number == legs[0].Number && m.UnitCost == 20m, "CA 8: duas pernas, um número, custo do momento");
        (await StateAsync(item, a)).OnHand.Should().Be(6m);
        (await StateAsync(item, b)).Should().Be((4m, 20m), "transferência não muda o custo médio global");

        var issue = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, b, 3m))), HttpStatusCode.Created);
        var issueId = issue.GetProperty("id").GetGuid();
        var exit = await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.SourceId == issueId));
        exit.Quantity.Should().Be(-3m);
        exit.UnitCost.Should().Be(20m, "a saída grava o custo médio do momento (CA 7)");
        exit.TotalCost.Should().Be(-60m);

        var noReason = await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, b, 1m, reason: " ")));
        FieldErrors(await JsonAsync(noReason), "reason").Should().ContainSingle();
        var tooMuch = await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, b, 2m)));
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(tooMuch)).Should().Be("insufficient_stock");
        (await WithDbAsync(db => db.ManualPostingLines.CountAsync(l => l.ItemId == item && l.LocationId == b && l.Quantity == 2m)))
            .Should().Be(0, "a recusa desfaz o lançamento inteiro");
    }

    [Fact]
    public async Task Acima_de_mil_reais_vira_pendente_sem_mexer_no_saldo_e_outra_pessoa_aprova()
    {
        var (authorId, warehouse) = await ActorAsync("warehouse");
        var (approverId, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        foreach (var quantity in new[] { 15m, 10m })
        {
            (await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, quantity, 50m)))).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var pending = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, location, 20.5m))), HttpStatusCode.Created);
        var id = pending.GetProperty("id").GetGuid();
        pending.GetProperty("status").GetString().Should().Be("Pending");
        pending.GetProperty("approvalReason").GetString().Should().Be("Acima de R$ 1.000,00");
        pending.GetProperty("number").GetString().Should().StartWith("MOV-", "o pendente já nasce com o número");
        (await StateAsync(item, location)).OnHand.Should().Be(25m, "pendente não mexe no saldo (CA 9)");
        var notices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.Type == ManualPostingService.ApprovalType).ToListAsync());
        notices.Where(n => n.Link.EndsWith(id.ToString(), StringComparison.Ordinal)).Select(n => n.UserId).Should().Contain(approverId).And.NotContain(authorId);
        notices.Single(n => n.UserId == approverId && n.Link.EndsWith(id.ToString(), StringComparison.Ordinal)).Title.Should().Contain("aguardando aprovação: saída, R$ 1.025,00.");

        var own = await warehouse.PostAsJsonAsync($"{Base}/manual-postings/{id}/approve", new { comment = (string?)null, version = pending.GetProperty("version").GetUInt32() });
        own.StatusCode.Should().Be(HttpStatusCode.Forbidden, "o Almoxarifado não tem inventory.approve");
        var edited = await owner.SendAsync(Command($"{Base}/manual-postings/{id}/approve", new { comment = (string?)null, version = pending.GetProperty("version").GetUInt32(), quantity = 1m }));
        edited.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "quem aprova decide sem editar (CA 10)");
        Code(await JsonAsync(edited)).Should().Be("decision_only");

        var approved = await OkAsync(await owner.SendAsync(Command($"{Base}/manual-postings/{id}/approve", new { comment = "Ok", version = pending.GetProperty("version").GetUInt32() })));
        approved.GetProperty("status").GetString().Should().Be("Posted");
        approved.GetProperty("decidedByName").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined);
        (await StateAsync(item, location)).OnHand.Should().Be(4.5m);

        var big = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, location, 24m))), HttpStatusCode.Created);
        var short_ = await owner.SendAsync(Command($"{Base}/manual-postings/{big.GetProperty("id").GetGuid()}/approve", new { comment = (string?)null, version = big.GetProperty("version").GetUInt32() }));
        short_.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(short_)).Should().Be("insufficient_stock");
        (await OkAsync(await owner.GetAsync($"{Base}/manual-postings/{big.GetProperty("id").GetGuid()}"))).GetProperty("status").GetString().Should().Be("Pending", "nada é gravado");
    }

    [Fact]
    public async Task Desvio_de_custo_reprovacao_cancelamento_e_decisao_simultanea()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (_, otherOwner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        (await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 10m, 50m)))).StatusCode.Should().Be(HttpStatusCode.Created);

        var deviation = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 1m, 70m))), HttpStatusCode.Created);
        deviation.GetProperty("approvalReason").GetString().Should().Be("Custo 40% acima do médio");
        var rejectNoComment = await owner.PostAsJsonAsync($"{Base}/manual-postings/{deviation.GetProperty("id").GetGuid()}/reject", new { comment = " ", version = deviation.GetProperty("version").GetUInt32() });
        FieldErrors(await JsonAsync(rejectNoComment), "comment").Should().ContainSingle();
        var rejected = await OkAsync(await owner.PostAsJsonAsync($"{Base}/manual-postings/{deviation.GetProperty("id").GetGuid()}/reject", new { comment = "Custo errado", version = deviation.GetProperty("version").GetUInt32() }));
        rejected.GetProperty("status").GetString().Should().Be("Rejected");
        (await StateAsync(item, location)).Should().Be((10m, 50m), "reprovado não tem movimento");

        var toCancel = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 1m, 10m))), HttpStatusCode.Created);
        (await owner.PostAsJsonAsync($"{Base}/manual-postings/{toCancel.GetProperty("id").GetGuid()}/cancel", new { version = toCancel.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "só o autor cancela");
        (await OkAsync(await warehouse.PostAsJsonAsync($"{Base}/manual-postings/{toCancel.GetProperty("id").GetGuid()}/cancel", new { version = toCancel.GetProperty("version").GetUInt32() })))
            .GetProperty("status").GetString().Should().Be("Cancelled");

        var race = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 30m, 50m))), HttpStatusCode.Created);
        var body = new { comment = (string?)null, version = race.GetProperty("version").GetUInt32() };
        var raceId = race.GetProperty("id").GetGuid();
        var decisions = await Task.WhenAll(
            owner.SendAsync(Command($"{Base}/manual-postings/{raceId}/approve", body)),
            otherOwner.SendAsync(Command($"{Base}/manual-postings/{raceId}/approve", body)));
        decisions.Count(d => d.StatusCode == HttpStatusCode.OK).Should().Be(1, "duas decisões simultâneas: só uma grava (xmin)");
        decisions.Count(d => d.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await StateAsync(item, location)).OnHand.Should().Be(40m);
    }

    [Fact]
    public async Task Autor_nao_aprova_o_proprio_lancamento_salvo_o_Proprietario_unico()
    {
        var (authorId, author) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var pending = await OkAsync(await author.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 100m, 20m))), HttpStatusCode.Created);
        var id = pending.GetProperty("id").GetGuid();
        var refused = await author.SendAsync(Command($"{Base}/manual-postings/{id}/approve", new { comment = (string?)null, version = pending.GetProperty("version").GetUInt32() }));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Detail(await JsonAsync(refused)).Should().Be("Quem lança não aprova o próprio lançamento.");

        // Proprietário único (SoleOwnerRule): só o autor fica ativo; a aprovação passa com "Autoaprovado" e o aviso ao Proprietário.
        var others = await WithDbAsync(db => db.Users.Where(u => u.Id != authorId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync());
        await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Inactive)));
        try
        {
            var self = await OkAsync(await author.SendAsync(Command($"{Base}/manual-postings/{id}/approve", new { comment = (string?)null, version = pending.GetProperty("version").GetUInt32() })));
            self.GetProperty("selfApproved").GetBoolean().Should().BeTrue("marca Autoaprovado (CA 11)");
            self.GetProperty("status").GetString().Should().Be("Posted");
            (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == authorId && n.Type == ManualPostingService.SelfApprovedType).Select(n => n.Title).ToListAsync()))
                .Should().ContainSingle().Which.Should().EndWith($"aprovou o próprio lançamento {pending.GetProperty("number").GetString()} (Autoaprovado).");
        }
        finally
        {
            await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Active)));
        }
    }

    [Fact]
    public async Task Ids_do_corpo_sao_validados_e_lote_e_linha_conferem_o_dono()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, lotted, location) = (await ItemAsync(), await ItemAsync(tracksLot: true), await LocationAsync());
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", Guid.NewGuid(), location, 1m, 1m)))), "itemId").Should().ContainSingle();
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, Guid.NewGuid(), 1m, 1m)))), "locationId").Should().ContainSingle();
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 1m, 1m, project: Guid.NewGuid())))), "projectId").Should().ContainSingle();
        var closed = Guid.NewGuid();
        Projects[closed] = "Closed";
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, location, 1m, project: closed)))), "projectId")
            .Single().Should().Contain("em andamento");

        var receipt = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", lotted, location, 5m, 2m, lotNumber: "L-A"))), HttpStatusCode.Created);
        var lotId = await WithDbAsync(db => db.Lots.Where(l => l.ItemId == lotted && l.LotNumber == "L-A").Select(l => l.Id).SingleAsync());
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", lotted, location, 1m, lotId: Guid.NewGuid())))), "lotId").Should().ContainSingle();
        FieldErrors(await JsonAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, location, 1m, lotId: lotId)))), "lotId").Should().ContainSingle("lote de outro item");
        (await warehouse.GetAsync($"{Base}/items/{lotted}/lots/{lotId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await warehouse.GetAsync($"{Base}/items/{item}/lots/{lotId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR");

        var line = receipt.GetProperty("lines")[0].GetProperty("id").GetGuid();
        (await warehouse.GetAsync($"{Base}/manual-postings/{receipt.GetProperty("id").GetGuid()}/lines/{line}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var other = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 1m, 1m))), HttpStatusCode.Created);
        (await warehouse.GetAsync($"{Base}/manual-postings/{other.GetProperty("id").GetGuid()}/lines/{line}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR");

        var open = Guid.NewGuid();
        Projects[open] = "InProgress";
        var withProject = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, location, 1m, project: open))), HttpStatusCode.Created);
        withProject.GetProperty("projectId").GetGuid().Should().Be(open, "saída avulsa com projeto em andamento (ponto 12)");
    }
}
