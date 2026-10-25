using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// EST-04, rastreabilidade (lê estoque, Testcontainers): dataset com 2 fornecedores, 3 lotes, 2 OS (uma concluída antes das séries), as
/// séries e 1 romaneio; série → origem e lote → destino com a conciliação (CA 1 a 3); exportação CSV auditada e protegida (CA 4); abas do item,
/// lote vencido e correção do lote, com saída bloqueada e idempotente (CA 5 e 6); desempenho com 200 OS (CA 7); três camadas e IDOR (CA 8);
/// cliente PJ e PF com e sem <c>parties.view</c>, na tela e no CSV (CA 9); busca parametrizada (CA 10). Cada teste cria os próprios itens,
/// lotes, OS e projetos e filtra pelos ids dele.
/// </summary>
public partial class FabricationTests
{
    private static async Task<Guid> SupplierAsync(HttpClient owner, string name)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = name + " " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Material com lote recebido pelo motor (como o recebimento da CMP-04 faria, com o número REC), num local novo.</summary>
    private async Task<(Guid Item, Guid Location, Guid Lot, string Receipt)> ReceivedLotAsync(Guid? item, string lotNumber, decimal quantity, Guid? supplier, DateOnly? expiry = null)
    {
        var material = item ?? await MaterialAsync("Cabo rastreado " + Guid.NewGuid().ToString("N")[..4]);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.items SET tracks_lot = true WHERE id = {material}"));
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        var location = new Location { Code = Unique("L"), Name = "Almoxarifado " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        var stock = scope.ServiceProvider.GetRequiredService<IStockMovements>();
        var lot = await stock.EnsureLotAsync(material, lotNumber, supplier, expiry);
        var receipt = "REC-" + Year + "-" + Random.Shared.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var posted = await stock.PostAsync(new StockPosting(StockMovementType.Receipt, material, location.Id, lot, quantity, 10m, StockSources.GoodsReceipt, Guid.NewGuid(), SourceNumber: receipt));
        posted.IsSuccess.Should().BeTrue(posted.Error?.Message);
        await transaction.CommitAsync();
        return (material, location.Id, lot, receipt);
    }

    /// <summary>Baixa do lote para a OS pelo motor (como a requisição da EST-03 faria).</summary>
    private async Task IssueToOrderAsync(Guid item, Guid location, Guid lot, Guid order, Guid project, decimal quantity)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var posted = await scope.ServiceProvider.GetRequiredService<IStockMovements>()
            .PostAsync(new StockPosting(StockMovementType.Issue, item, location, lot, -quantity, null, StockSources.Requisition, Guid.NewGuid(), ProjectId: project, ProductionOrderId: order, SourceNumber: "REQ-" + Year + "-0001"));
        posted.IsSuccess.Should().BeTrue(posted.Error?.Message);
        await transaction.CommitAsync();
    }

    private async Task CustomerAsPersonAsync(Guid project, string name) => await WithDbAsync(db => db.Database.ExecuteSqlAsync(
        $"UPDATE parties.parties SET kind = 'PF', legal_name = {name}, document = {Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture)} WHERE id = (SELECT party_id FROM commercial.projects WHERE id = {project})"));

    private async Task<string?> CustomerDocumentAsync(Guid project) => await WithDbAsync(db => db.Database
        .SqlQuery<string?>($"SELECT document AS \"Value\" FROM parties.parties WHERE id = (SELECT party_id FROM commercial.projects WHERE id = {project})").SingleAsync());

    /// <summary>
    /// Dataset fixo: OS 1 com 2 séries consumiu o lote A (fornecedor 1) e o lote B (fornecedor 2) e foi expedida no romaneio; OS 2 consumiu o
    /// lote A e ficou "concluída antes das séries" (sem entrada nem série); o lote C (fornecedor 2) só tem entrada.
    /// </summary>
    private async Task<TraceDataset> TraceDatasetAsync()
    {
        var (owner, order, project, product) = await TracedOrderAsync();
        var supplier1 = await SupplierAsync(owner, "Aços Minas");
        var supplier2 = await SupplierAsync(owner, "Cabos do Vale");
        var a = await ReceivedLotAsync(null, "LA-" + Guid.NewGuid().ToString("N")[..6], 50m, supplier1, Today.AddDays(200));
        var b = await ReceivedLotAsync(null, "LB-" + Guid.NewGuid().ToString("N")[..6], 20m, supplier2);
        var c = await ReceivedLotAsync(null, "LC-" + Guid.NewGuid().ToString("N")[..6], 5m, supplier2, Today.AddDays(-1));
        await IssueToOrderAsync(a.Item, a.Location, a.Lot, order, project, 12m);
        await IssueToOrderAsync(b.Item, b.Location, b.Lot, order, project, 3m);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));

        var (_, legacy, legacyProject, _, _) = await FinishedDatasetAsync(serial: false, issue: false);
        await IssueToOrderAsync(a.Item, a.Location, a.Lot, legacy, legacyProject, 8m);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Completed', actual_end = now() WHERE id = {legacy}"));

        var shipment = await DraftShipmentAsync(owner, project);
        var shipped = await OkAsync(await ShipmentActionAsync(owner, shipment.GetProperty("id").GetGuid(), "confirm", Version(shipment)));
        return new TraceDataset(owner, order, project, product, legacy, legacyProject, a, b, c, shipped.GetProperty("number").GetString()!);
    }

    private async Task<(HttpClient Owner, Guid Order, Guid Project, Guid Product)> TracedOrderAsync()
    {
        var (owner, order, project, product, _) = await FinishedDatasetAsync(serial: true, quantity: 2m);
        return (owner, order, project, product);
    }

    private static string SerialTraceUrl(Guid item, Guid serial) => $"{Inventory}/items/{item}/serials/{serial}/trace";

    private static string LotTraceUrl(Guid item, Guid lot) => $"{Inventory}/items/{item}/lots/{lot}/trace";

    [Fact]
    public async Task Serie_mostra_a_OS_o_projeto_o_cliente_o_romaneio_e_os_lotes_da_OS_inteira()
    {
        var d = await TraceDatasetAsync();
        var serials = await SerialsAsync(d.Order);
        serials.Should().HaveCount(2);
        var trace = await OkAsync(await d.Owner.GetAsync(SerialTraceUrl(d.Product, serials[0].Id)));
        trace.GetProperty("serialNumber").GetString().Should().Be(serials[0].SerialNumber);
        trace.GetProperty("status").GetString().Should().Be("Shipped", "CA 1: situação Expedido");
        trace.GetProperty("productionOrderId").GetGuid().Should().Be(d.Order);
        trace.GetProperty("productionOrderNumber").GetString().Should().StartWith("OS-");
        trace.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.String, "CA 1: data de conclusão");
        trace.GetProperty("projectId").GetGuid().Should().Be(d.Project);
        trace.GetProperty("customer").GetProperty("kind").GetString().Should().Be("PJ");
        trace.GetProperty("shipmentNumber").GetString().Should().Be(d.Shipment, "CA 1: romaneio de expedição (PRD-06b)");
        trace.GetProperty("sharedLotsNote").GetString().Should().Be($"Lotes da OS {trace.GetProperty("productionOrderNumber").GetString()} (2 unidades): a OS não separa o material por unidade.", "CA 2");

        var lots = trace.GetProperty("lots").EnumerateArray().Where(l => l.GetProperty("lotId").ValueKind == JsonValueKind.String).ToList();
        var lotA = lots.Single(l => l.GetProperty("lotId").GetGuid() == d.A.Lot);
        lotA.GetProperty("quantity").GetDecimal().Should().Be(12m, "a OS inteira, não por unidade (ponto 87)");
        lotA.GetProperty("supplierName").GetString().Should().StartWith("Aços Minas");
        lotA.GetProperty("expiry").GetString().Should().Be(Iso(Today.AddDays(200)));
        var entry = lotA.GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        entry.GetProperty("label").GetString().Should().Be("Recebimento " + d.A.Receipt, "CA 1: recebimento com número e data");
        entry.GetProperty("at").ValueKind.Should().Be(JsonValueKind.String);
        lots.Single(l => l.GetProperty("lotId").GetGuid() == d.B.Lot).GetProperty("supplierName").GetString().Should().StartWith("Cabos do Vale");

        // A outra série da mesma OS herda os mesmos lotes.
        var other = await OkAsync(await d.Owner.GetAsync(SerialTraceUrl(d.Product, serials[1].Id)));
        other.GetProperty("lots").EnumerateArray().Select(l => l.GetProperty("lotId").ToString()).Should().BeEquivalentTo(trace.GetProperty("lots").EnumerateArray().Select(l => l.GetProperty("lotId").ToString()));
        trace.GetRawText().Should().NotContainAny(["\"document\"", "\"averageCost\"", "\"unitCost\"", "\"totalCost\""], "CA 8 e 9: sem documento e sem custo");
    }

    [Fact]
    public async Task Lote_mostra_cada_consumo_com_as_series_o_romaneio_a_OS_antiga_e_concilia_com_o_recebido()
    {
        var d = await TraceDatasetAsync();
        var trace = await OkAsync(await d.Owner.GetAsync(LotTraceUrl(d.A.Item, d.A.Lot)));
        trace.GetProperty("supplierName").GetString().Should().StartWith("Aços Minas", "CA 3: fornecedor");
        trace.GetProperty("entries").EnumerateArray().Should().ContainSingle().Which.GetProperty("number").GetString().Should().Be(d.A.Receipt);
        trace.GetProperty("totalReceived").GetDecimal().Should().Be(50m);
        trace.GetProperty("totalConsumed").GetDecimal().Should().Be(20m);
        trace.GetProperty("onHand").GetDecimal().Should().Be(30m);
        trace.GetProperty("balances").EnumerateArray().Should().ContainSingle().Which.GetProperty("locationId").GetGuid().Should().Be(d.A.Location);
        trace.GetProperty("balanced").GetBoolean().Should().BeTrue("CA 3: recebido − consumido + outras = saldo");

        var uses = trace.GetProperty("consumptions").EnumerateArray().ToList();
        uses.Should().HaveCount(2);
        var current = uses.Single(u => u.GetProperty("productionOrderId").GetGuid() == d.Order);
        current.GetProperty("quantity").GetDecimal().Should().Be(12m);
        current.GetProperty("projectId").GetGuid().Should().Be(d.Project);
        current.GetProperty("customer").GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        var serials = current.GetProperty("serials").EnumerateArray().ToList();
        serials.Should().HaveCount(2);
        serials.Should().OnlyContain(s => s.GetProperty("shipmentNumber").GetString() == d.Shipment, "CA 3: o romaneio de cada série expedida");
        var legacy = uses.Single(u => u.GetProperty("productionOrderId").GetGuid() == d.Legacy);
        legacy.GetProperty("serials").GetArrayLength().Should().Be(0);
        legacy.GetProperty("noSerialNote").GetString().Should().Be("Sem série: OS concluída antes do registro de séries.", "CA 3");

        // Uma saída avulsa (outra pessoa, sem OS) entra como linha explicada e a conta continua fechando.
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostAsync(new StockPosting(StockMovementType.Issue, d.A.Item, d.A.Location, d.A.Lot, -2m, null,
                StockSources.ManualPosting, Guid.NewGuid(), Reason: "Amostra para o laboratório"))).IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        var after = await OkAsync(await d.Owner.GetAsync(LotTraceUrl(d.A.Item, d.A.Lot)));
        after.GetProperty("others").EnumerateArray().Should().ContainSingle().Which.GetProperty("explanation").GetString().Should().StartWith("Saída avulsa").And.EndWith("Amostra para o laboratório");
        after.GetProperty("totalOther").GetDecimal().Should().Be(-2m);
        (after.GetProperty("onHand").GetDecimal(), after.GetProperty("balanced").GetBoolean()).Should().Be((28m, true));
    }

    [Fact]
    public async Task Exportar_o_lote_baixa_CSV_protegido_com_o_cliente_mascarado_e_fica_na_auditoria()
    {
        var d = await TraceDatasetAsync();
        await CustomerAsPersonAsync(d.Project, "=João Marcus Ribeiro");
        var document = await CustomerDocumentAsync(d.Project);
        var (viewerId, viewer) = await ActorAsync("inventory:view");

        var response = await viewer.GetAsync($"{LotTraceUrl(d.A.Item, d.A.Lot)}/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("rastreio-lote-");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(Encoding.UTF8.Preamble.ToArray(), "CA 4: UTF-8 com BOM");
        var text = Encoding.UTF8.GetString(bytes.AsSpan(3));
        text.Split("\r\n")[0].Should().Contain(";", "CA 4: separador ;");
        text.Should().Contain("J. M. R.", "CA 9: PF sem parties.view, só as iniciais no CSV").And.NotContain("João").And.NotContain(document!, "nenhum documento");
        text.Should().Contain(d.Shipment).And.Contain("Sem série: OS concluída antes do registro de séries.");

        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(Lot) && a.EntityId == d.A.Lot.ToString()).ToListAsync());
        audit.Should().ContainSingle(a => a.Action == AuditActions.Exported && a.UserId == viewerId, "CA 4: quem, quando e qual lote");

        // CA 9 com parties.view: por extenso, com o "=" do nome protegido no CSV e sem documento.
        var (_, full) = await ActorAsync("inventory:view;parties:view");
        var fullText = Encoding.UTF8.GetString((await full.GetByteArrayAsync($"{LotTraceUrl(d.A.Item, d.A.Lot)}/export")).AsSpan(3));
        fullText.Should().Contain("'=João Marcus Ribeiro", "CA 4: célula que começa com = recebe '").And.NotContain(document!);
        var screen = await OkAsync(await full.GetAsync(LotTraceUrl(d.A.Item, d.A.Lot)));
        var customer = screen.GetProperty("consumptions").EnumerateArray().Single(u => u.GetProperty("productionOrderId").GetGuid() == d.Order).GetProperty("customer");
        (customer.GetProperty("name").GetString(), customer.GetProperty("masked").GetBoolean(), customer.GetProperty("partyId").ValueKind).Should().Be(("=João Marcus Ribeiro", false, JsonValueKind.String));
        var masked = (await OkAsync(await viewer.GetAsync(SerialTraceUrl(d.Product, (await SerialsAsync(d.Order))[0].Id)))).GetProperty("customer");
        (masked.GetProperty("name").GetString(), masked.GetProperty("masked").GetBoolean(), masked.GetProperty("partyId").ValueKind).Should().Be(("J. M. R.", true, JsonValueKind.Null), "CA 9 na tela");
    }

    [Fact]
    public async Task Aba_Lotes_mostra_validade_saldo_e_local_e_a_correcao_respeita_a_saida()
    {
        var (owner, order, project, _) = await TracedOrderAsync();
        var supplier = await SupplierAsync(owner, "Aços Minas");
        var expired = await ReceivedLotAsync(null, "LV-" + Guid.NewGuid().ToString("N")[..6], 4m, supplier, Today.AddDays(-2));
        var expiring = await ReceivedLotAsync(expired.Item, "LX-" + Guid.NewGuid().ToString("N")[..6], 6m, supplier, Today.AddDays(10));

        var list = await OkAsync(await owner.GetAsync($"{Inventory}/items/{expired.Item}/lots"));
        var rows = list.EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        var v = rows.Single(r => r.GetProperty("id").GetGuid() == expired.Lot);
        (v.GetProperty("expiryState").GetString(), v.GetProperty("onHand").GetDecimal(), v.GetProperty("supplierName").GetString()!.StartsWith("Aços Minas", StringComparison.Ordinal))
            .Should().Be(("Expired", 4m, true), "CA 5: chip crítico Vencido, saldo e fornecedor");
        v.GetProperty("balances").EnumerateArray().Single().GetProperty("locationId").GetGuid().Should().Be(expired.Location);
        var x = rows.Single(r => r.GetProperty("id").GetGuid() == expiring.Lot);
        (x.GetProperty("expiryState").GetString(), x.GetProperty("daysToExpiry").GetInt32()).Should().Be(("Expiring", 10), "CA 5: alerta Vence em 10 dias");

        // CA 6: sem saída, corrige número e validade com motivo e auditoria; repetir não grava de novo (idempotente).
        var (_, viewer) = await ActorAsync("inventory:view");
        var url = $"{Inventory}/items/{expired.Item}/lots/{expired.Lot}";
        var fixedNumber = "LV-OK-" + Guid.NewGuid().ToString("N")[..4];
        var body = new { lotNumber = fixedNumber, expiry = Iso(Today.AddDays(90)), reason = "Digitado errado no recebimento" };
        (await viewer.PutAsJsonAsync(url, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "corrigir pede inventory.edit");
        var corrected = await OkAsync(await owner.PutAsJsonAsync(url, body));
        (corrected.GetProperty("lotNumber").GetString(), corrected.GetProperty("expiryState").GetString(), corrected.GetProperty("correctionReason").GetString())
            .Should().Be((fixedNumber, "Ok", "Digitado errado no recebimento"));
        await OkAsync(await owner.PutAsJsonAsync(url, body));
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(Lot) && a.EntityId == expired.Lot.ToString() && a.Action == AuditActions.Updated).ToListAsync());
        audit.Should().ContainSingle("a segunda correção igual não muda nada").Which.Changes.Should().Contain("Digitado errado no recebimento");
        (await owner.PutAsJsonAsync(url, new { lotNumber = fixedNumber, expiry = (string?)null, reason = " " })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");
        var duplicate = await owner.PutAsJsonAsync(url, new { lotNumber = await WithDbAsync(db => db.Lots.Where(l => l.Id == expiring.Lot).Select(l => l.LotNumber).SingleAsync()), expiry = (string?)null, reason = "Teste de número repetido" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "número repetido no item");

        // Com saída, só a validade.
        await IssueToOrderAsync(expiring.Item, expiring.Location, expiring.Lot, order, project, 1m);
        var lotUrl = $"{Inventory}/items/{expiring.Item}/lots/{expiring.Lot}";
        var number = await WithDbAsync(db => db.Lots.Where(l => l.Id == expiring.Lot).Select(l => l.LotNumber).SingleAsync());
        var refused = await owner.PutAsJsonAsync(lotUrl, new { lotNumber = number + "-X", expiry = Iso(Today.AddDays(10)), reason = "Troca do número" });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(refused);
        (Code(problem), problem.GetProperty("detail").GetString()).Should().Be(("lot_has_outflow", "Lote com saída só tem a validade corrigida."), "CA 6");
        var onlyExpiry = await OkAsync(await owner.PutAsJsonAsync(lotUrl, new { lotNumber = number, expiry = Iso(Today.AddDays(45)), reason = "Validade do certificado do fornecedor" }));
        (onlyExpiry.GetProperty("lotNumber").GetString(), onlyExpiry.GetProperty("expiry").GetString(), onlyExpiry.GetProperty("hasOutflow").GetBoolean()).Should().Be((number, Iso(Today.AddDays(45)), true));
    }

    [Fact]
    public async Task Lote_consumido_em_200_OS_responde_em_menos_de_1_segundo()
    {
        var (owner, _, project, _) = await TracedOrderAsync();
        var lot = await ReceivedLotAsync(null, "LP-" + Guid.NewGuid().ToString("N")[..6], 400m, await SupplierAsync(owner, "Aços Minas"));
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var postings = Enumerable.Range(0, 200).Select(_ => new StockPosting(StockMovementType.Issue, lot.Item, lot.Location, lot.Lot, -1m, null, StockSources.Requisition, Guid.NewGuid(),
                ProjectId: project, ProductionOrderId: Guid.CreateVersion7())).ToList();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostManyAsync(postings)).IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        await OkAsync(await owner.GetAsync(LotTraceUrl(lot.Item, lot.Lot)));
        var watch = Stopwatch.StartNew();
        var trace = await OkAsync(await owner.GetAsync(LotTraceUrl(lot.Item, lot.Lot)));
        watch.Stop();
        trace.GetProperty("consumptions").GetArrayLength().Should().Be(200);
        trace.GetProperty("balanced").GetBoolean().Should().BeTrue();
        watch.ElapsedMilliseconds.Should().BeLessThan(1000, "CA 7: índices de lot_id e source_id, nomes por lista");
    }

    [Fact]
    public async Task Rastreabilidade_tem_as_tres_camadas_e_IDOR_pelo_item()
    {
        var d = await TraceDatasetAsync();
        var serial = (await SerialsAsync(d.Order))[0].Id;
        var urls = new[] { $"{Inventory}/traceability?q=LA", SerialTraceUrl(d.Product, serial), LotTraceUrl(d.A.Item, d.A.Lot), $"{LotTraceUrl(d.A.Item, d.A.Lot)}/export", $"{Inventory}/items/{d.A.Item}/lots" };
        var (_, noGroup) = await ActorAsync(null);
        var (_, production) = await ActorAsync("production:view;inventory:create");
        foreach (var url in urls)
        {
            var forbidden = await noGroup.GetAsync(url);
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            Code(await JsonAsync(forbidden)).Should().Be("forbidden", "CA 8: sem grupo");
            (await production.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"CA 8: sem inventory.view ({url})");
        }

        // IDOR: série e lote lidos pelo item.
        (await d.Owner.GetAsync(SerialTraceUrl(d.A.Item, serial))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await d.Owner.GetAsync(LotTraceUrl(d.B.Item, d.A.Lot))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await d.Owner.GetAsync($"{LotTraceUrl(d.B.Item, d.A.Lot)}/export")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await d.Owner.GetAsync($"{Inventory}/items/{d.B.Item}/lots/{d.A.Lot}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await d.Owner.PutAsJsonAsync($"{Inventory}/items/{d.B.Item}/lots/{d.A.Lot}", new { lotNumber = "X", expiry = (string?)null, reason = "Teste IDOR" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var exports = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(Lot) && a.EntityId == d.A.Lot.ToString() && a.Action == AuditActions.Exported).CountAsync());
        exports.Should().Be(0, "exportação recusada não vai para a auditoria");

        await SetModuleReleasedAsync("inventory", false);
        try
        {
            foreach (var url in urls)
            {
                var blocked = await d.Owner.GetAsync(url);
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
                Code(await JsonAsync(blocked)).Should().Be("module_not_released", "CA 8");
            }
        }
        finally
        {
            await SetModuleReleasedAsync("inventory", true);
        }
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("' OR '1'='1")]
    [InlineData("\\")]
    public async Task Busca_usa_so_parametros_e_nao_trata_curinga(string term)
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var result = await OkAsync(await owner.GetAsync($"{Inventory}/traceability?q={Uri.EscapeDataString(term)}"));
        // Lista vazia é o esperado (nenhuma série ou lote começa com o termo); o que vier precisa começar com ele, como texto.
        result.GetProperty("serials").EnumerateArray().Select(s => s.GetProperty("serialNumber").GetString()).All(s => s!.StartsWith(term, StringComparison.Ordinal))
            .Should().BeTrue("CA 10: % e _ são texto, não curinga");
        result.GetProperty("lots").EnumerateArray().Select(s => s.GetProperty("lotNumber").GetString()).All(s => s!.StartsWith(term, StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public async Task Busca_por_serie_ou_lote_e_exata_ou_por_prefixo_ate_50()
    {
        var d = await TraceDatasetAsync();
        var serial = (await SerialsAsync(d.Order))[0];
        var bySerial = await OkAsync(await d.Owner.GetAsync($"{Inventory}/traceability?q={Uri.EscapeDataString(serial.SerialNumber)}"));
        bySerial.GetProperty("looksLikeSerial").GetBoolean().Should().BeTrue("a busca reconhece o formato da série");
        bySerial.GetProperty("serials")[0].GetProperty("id").GetGuid().Should().Be(serial.Id, "o exato vem primeiro");

        var lotNumber = await WithDbAsync(db => db.Lots.Where(l => l.Id == d.A.Lot).Select(l => l.LotNumber).SingleAsync());
        var byPrefix = await OkAsync(await d.Owner.GetAsync($"{Inventory}/traceability?q={Uri.EscapeDataString(lotNumber[..5])}"));
        byPrefix.GetProperty("lots").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).Should().Contain(d.A.Lot);
        byPrefix.GetProperty("lots").GetArrayLength().Should().BeLessThanOrEqualTo(50);
        (await OkAsync(await d.Owner.GetAsync($"{Inventory}/traceability?q=LA"))).GetProperty("lots").GetArrayLength().Should().BeLessThanOrEqualTo(50);
        var empty = await OkAsync(await d.Owner.GetAsync($"{Inventory}/traceability?q=nao-existe-{Guid.NewGuid():N}"));
        (empty.GetProperty("serials").GetArrayLength(), empty.GetProperty("lots").GetArrayLength()).Should().Be((0, 0));
        (await d.Owner.GetAsync($"{Inventory}/traceability?q={new string('x', 61)}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private sealed record TraceDataset(
        HttpClient Owner, Guid Order, Guid Project, Guid Product, Guid Legacy, Guid LegacyProject, (Guid Item, Guid Location, Guid Lot, string Receipt) A,
        (Guid Item, Guid Location, Guid Lot, string Receipt) B, (Guid Item, Guid Location, Guid Lot, string Receipt) C, string Shipment);
}
