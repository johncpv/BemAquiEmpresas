using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected const string Shipments = "/api/v1/production/shipments";

    /// <summary>OS concluída com o produto no estoque (PRD-06a2), pronta para o romaneio: o dono, a OS, o projeto e o produto.</summary>
    protected async Task<(HttpClient Owner, Guid Order, Guid Project, Guid Product)> ShippableAsync(bool serial = true, decimal quantity = 2m)
    {
        var (owner, order, project, product, _) = await FinishedDatasetAsync(serial, quantity);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        return (owner, order, project, product);
    }

    protected static async Task<JsonElement> ShipmentFormAsync(HttpClient client, Guid project, Guid? order = null) =>
        await OkAsync(await client.GetAsync($"{Shipments}/form?projectId={project}" + (order is { } o ? $"&productionOrderId={o}" : string.Empty)));

    protected static Guid? GuidOrNull(JsonElement e, string name) => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetGuid();

    /// <summary>Linha a partir do que está pronto para expedir: com série, todas as séries; sem série, o que dá para expedir.</summary>
    protected static object LineOf(JsonElement available, bool general = false, decimal? quantity = null)
    {
        var serial = available.GetProperty("tracksSerial").GetBoolean();
        var ids = serial ? available.GetProperty("serials").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToArray() : null;
        return new
        {
            itemId = available.GetProperty("itemId").GetGuid(), locationId = available.GetProperty("locationId").GetGuid(), lotId = GuidOrNull(available, "lotId"),
            quantity = quantity ?? (serial ? ids!.Length : available.GetProperty("shippable").GetDecimal()), serialIds = ids, fromGeneralStock = general,
        };
    }

    /// <summary>Todas as linhas do formulário (padrão do CA 1: todas as séries e quantidades).</summary>
    protected static object[] AllLines(JsonElement form) => form.GetProperty("available").EnumerateArray().Select(l => LineOf(l)).ToArray();

    protected static object ShipmentBody(Guid project, object[] lines, Guid? carrier = null, string? driver = "José da Silva") => new
    {
        projectId = project, deliveryAddress = "Rua da Obra, 100 — Centro — Belo Horizonte/MG", carrierPartyId = carrier, vehiclePlate = "abc 1d23", driverName = driver, volumes = 2,
        grossWeight = 850.5m, plannedDate = Iso(Today.AddDays(2)), lines,
    };

    protected static Task<HttpResponseMessage> CreateShipmentAsync(HttpClient client, Guid project, object[] lines, Guid? key = null, Guid? carrier = null) =>
        client.SendAsync(Post(Shipments, ShipmentBody(project, lines, carrier), key));

    protected static async Task<JsonElement> DraftShipmentAsync(HttpClient client, Guid project)
    {
        var form = await ShipmentFormAsync(client, project);
        return await CreatedAsync(await CreateShipmentAsync(client, project, AllLines(form)));
    }

    protected static Task<HttpResponseMessage> ShipmentActionAsync(HttpClient client, Guid id, string action, uint version, string? reason = null) =>
        client.SendAsync(Post($"{Shipments}/{id}/{action}", reason is null ? new { version } : new { reason, version }));

    protected static async Task<JsonElement> ShipmentAsync(HttpClient client, Guid id) => await OkAsync(await client.GetAsync($"{Shipments}/{id}"));

    protected async Task<List<StockMovement>> ShipmentMovementsAsync(Guid shipment) =>
        await WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSources.Shipment && m.SourceId == shipment).OrderBy(m => m.PostedSeq).ToListAsync());

    /// <summary>Produto acabado sem projeto (estoque geral): item novo do tipo produto acabado recebido pelo motor num local novo.</summary>
    protected async Task<(Guid Item, Guid Location)> GeneralStockAsync(decimal quantity = 3m)
    {
        var item = await MaterialAsync("Cabina avulsa " + Guid.NewGuid().ToString("N")[..4], ItemType.Finished, "UN");
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        var location = new Location { Code = Unique("L"), Name = "Expedição " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var posted = await scope.ServiceProvider.GetRequiredService<IStockMovements>()
            .PostAsync(new StockPosting(StockMovementType.Receipt, item, location.Id, null, quantity, 500m, StockSources.GoodsReceipt, Guid.NewGuid()));
        posted.IsSuccess.Should().BeTrue(posted.Error?.Message);
        await transaction.CommitAsync();
        return (item, location.Id);
    }

    protected static object GeneralLine(Guid item, Guid location, decimal quantity, bool general = true) =>
        new { itemId = item, locationId = location, lotId = (Guid?)null, quantity, serialIds = (Guid[]?)null, fromGeneralStock = general };

    protected static async Task<Guid> CarrierAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Transportes " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Carrier" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    protected static async Task<HttpResponseMessage> UploadShipmentFileAsync(HttpClient client, Guid shipmentId, byte[] content, string name)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        return await client.PostAsync($"/api/v1/attachments/Shipment/{shipmentId}", form);
    }
}

/// <summary>
/// PRD-06b, romaneio de expedição (movimenta estoque, Testcontainers obrigatório): montar com ROM e idempotência (CA 1); série repetida, de
/// outro projeto e estoque geral (CA 2); PDF sem valores (CA 3); confirmar com a saída <c>Shipment</c> síncrona, as séries e o evento na
/// mesma transação, fora do custo do projeto (CA 4 e 7); 409 <c>insufficient_stock</c> sem gravar (CA 5); estoque geral por outra pessoa
/// (CA 6); próximos passos (CA 8 e 9); cancelar e estornar (CA 10 e 11); bloqueio do estorno da conclusão com produto expedido ou em
/// romaneio aberto; anexos (CA 14); três camadas e IDOR (CA 15); concorrência (CA 16); auditoria mascarada (CA 17). Cada teste cria a
/// própria OS, o projeto, os itens e os locais e filtra pelos ids dele.
/// </summary>
public partial class FabricationDeliveryTests
{
    [Fact]
    public async Task Novo_romaneio_vem_preenchido_nasce_ROM_em_rascunho_e_repete_pela_chave()
    {
        var (owner, order, project, product) = await ShippableAsync(serial: true, quantity: 2m);
        var serials = await SerialsAsync(order);
        var form = await ShipmentFormAsync(owner, project, order);
        form.GetProperty("productionOrderId").GetGuid().Should().Be(order, "CA 9: vindo da OS");
        var line = form.GetProperty("available").EnumerateArray().Should().ContainSingle().Subject;
        line.GetProperty("itemId").GetGuid().Should().Be(product);
        line.GetProperty("serials").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).Should().BeEquivalentTo(serials.Select(s => s.Id), "CA 1: todas as séries do projeto");
        line.GetProperty("serials").EnumerateArray().Should().OnlyContain(s => s.GetProperty("productionOrderId").GetGuid() == order);
        (line.GetProperty("onHand").GetDecimal(), line.GetProperty("available").GetDecimal()).Should().Be((2m, 2m));

        var carrier = await CarrierAsync(owner);
        var key = Guid.NewGuid();
        var (_, creator) = await ActorAsync("production:view,create");
        var created = await CreatedAsync(await CreateShipmentAsync(creator, project, AllLines(form), key, carrier));
        created.GetProperty("number").GetString().Should().StartWith($"ROM-{Year}-", "CA 1 e D15");
        created.GetProperty("status").GetString().Should().Be("Draft");
        created.GetProperty("vehiclePlate").GetString().Should().Be("ABC1D23");
        created.GetProperty("carrierPartyId").GetGuid().Should().Be(carrier);
        created.GetProperty("lines").EnumerateArray().Single().GetProperty("serials").GetArrayLength().Should().Be(2);
        var replay = await CreatedAsync(await CreateShipmentAsync(creator, project, AllLines(form), key, carrier));
        replay.GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid(), "Idempotency-Key: a mesma montagem não duplica");
        (await WithDbAsync(db => db.Shipments.CountAsync(s => s.ProjectId == project))).Should().Be(1);
        (await creator.PostAsJsonAsync(Shipments, ShipmentBody(project, AllLines(form)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "sem Idempotency-Key");

        // Padrão 6: a última transportadora usada com o cliente vem com a tag "Sugerido"; a série já montada mostra o romaneio.
        var again = await ShipmentFormAsync(owner, project);
        again.GetProperty("suggestedCarrierId").GetGuid().Should().Be(carrier);
        again.GetProperty("carriers").EnumerateArray().Should().Contain(c => c.GetProperty("id").GetGuid() == carrier && c.GetProperty("suggested").GetBoolean());
        again.GetProperty("available").EnumerateArray().Single().GetProperty("serials").EnumerateArray()
            .Should().OnlyContain(s => s.GetProperty("openShipmentNumber").GetString() == created.GetProperty("number").GetString());
        var list = await OkAsync(await owner.GetAsync($"{Shipments}?projectId={project}&filter=todas"));
        list.GetProperty("items").EnumerateArray().Should().ContainSingle().Which.GetProperty("lines").GetInt32().Should().Be(1);
        list.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).Should().NotContain(["driverName", "vehiclePlate"], "dado pessoal só no detalhe");
    }

    [Fact]
    public async Task Serie_em_outro_romaneio_ou_de_outro_projeto_e_recusada_e_o_estoque_geral_entra_marcado()
    {
        var (owner, _, project, _) = await ShippableAsync(serial: true, quantity: 2m);
        var form = await ShipmentFormAsync(owner, project);
        var first = await CreatedAsync(await CreateShipmentAsync(owner, project, AllLines(form)));
        var serialNumber = form.GetProperty("available")[0].GetProperty("serials")[0].GetProperty("serialNumber").GetString();
        var repeated = await CreateShipmentAsync(owner, project, AllLines(form));
        repeated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 2");
        (await JsonAsync(repeated)).GetProperty("detail").GetString().Should().Contain($"já está no romaneio {first.GetProperty("number").GetString()}");

        var (otherOwner, _, otherProject, _) = await ShippableAsync(serial: true, quantity: 1m);
        var foreign = await CreateShipmentAsync(owner, project, AllLines(await ShipmentFormAsync(otherOwner, otherProject)));
        foreign.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 2: série de outro projeto");
        (await JsonAsync(foreign)).GetProperty("detail").GetString().Should().EndWith("é de outro projeto.");

        var (item, location) = await GeneralStockAsync(3m);
        var unmarked = await CreateShipmentAsync(owner, project, [GeneralLine(item, location, 1m, general: false)]);
        unmarked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "produto sem projeto só pela opção 'Incluir produto do estoque geral'");
        var options = await OkAsync(await owner.GetAsync($"{Shipments}/general-stock?q={Uri.EscapeDataString((await WithDbAsync(db => db.Items.Where(i => i.Id == item).Select(i => i.Code).SingleAsync())))}"));
        options.EnumerateArray().Should().ContainSingle(o => o.GetProperty("itemId").GetGuid() == item).Which.GetProperty("shippable").GetDecimal().Should().Be(3m);
        (await CreateShipmentAsync(owner, project, [GeneralLine(item, location, 4m)])).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "acima do estoque geral");
        await OkAsync(await ShipmentActionAsync(owner, first.GetProperty("id").GetGuid(), "cancel", Version(first), "Montado errado"));
        var marked = await CreatedAsync(await CreateShipmentAsync(owner, project, [.. AllLines(form), GeneralLine(item, location, 2m)]));
        marked.GetProperty("hasGeneralStock").GetBoolean().Should().BeTrue();
        marked.GetProperty("lines").EnumerateArray().Should().Contain(l => l.GetProperty("fromGeneralStock").GetBoolean() && l.GetProperty("itemId").GetGuid() == item);
    }

    [Fact]
    public async Task PDF_do_romaneio_tem_entrega_itens_series_e_assinaturas_sem_valor_nem_custo()
    {
        var (owner, order, project, _) = await ShippableAsync(serial: true, quantity: 2m);
        var shipment = await DraftShipmentAsync(owner, project);
        var response = await owner.GetAsync($"{Shipments}/{shipment.GetProperty("id").GetGuid()}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = string.Join(" ", document.GetPages().Select(p => p.Text));
        text.Should().Contain(shipment.GetProperty("number").GetString()).And.Contain("Rua da Obra, 100").And.Contain("Recebido por").And.Contain("ABC1D23");
        foreach (var serial in await SerialsAsync(order))
        {
            text.Should().Contain(serial.SerialNumber, "CA 3: itens com as séries");
        }

        text.Should().NotContainAny(["R$", "Custo", "Valor", "Preço"], "CA 3: o romaneio não é documento fiscal e não tem custo");
        document.GetPages().SelectMany(p => p.Letters).Select(l => l.FontName).Distinct().Should().OnlyContain(f => f.Contains("Archivo"), "imagem Alpine sem fonte do sistema");
    }

    [Fact]
    public async Task Confirmar_lanca_a_saida_Shipment_expede_as_series_guarda_o_PDF_e_fica_fora_do_custo_do_projeto()
    {
        var (owner, order, project, product) = await ShippableAsync(serial: true, quantity: 2m);
        var realizedBefore = await RealizedMaterialAsync(owner, project);
        var shipment = await DraftShipmentAsync(owner, project);
        var id = shipment.GetProperty("id").GetGuid();
        var impact = await OkAsync(await owner.GetAsync($"{Shipments}/{id}/impact"));
        var impactLine = impact.GetProperty("lines").EnumerateArray().Single();
        (impactLine.GetProperty("onHandBefore").GetDecimal(), impactLine.GetProperty("onHandAfter").GetDecimal()).Should().Be((2m, 0m), "CA 4: físico antes → depois");
        impactLine.GetProperty("serialNumbers").GetArrayLength().Should().Be(2);
        impact.GetProperty("requiresStockApproval").GetBoolean().Should().BeFalse();

        var (_, viewer) = await ActorAsync("production:view,create");
        (await ShipmentActionAsync(viewer, id, "confirm", Version(shipment))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "confirmar pede production.edit");
        var (_, editor) = await ActorAsync("production:view,edit");
        var shipped = await OkAsync(await ShipmentActionAsync(editor, id, "confirm", Version(shipment)));
        shipped.GetProperty("status").GetString().Should().Be("Shipped");
        shipped.GetProperty("shippedAt").ValueKind.Should().Be(JsonValueKind.String);
        var movement = (await ShipmentMovementsAsync(id)).Should().ContainSingle().Subject;
        (movement.Type, movement.ItemId, movement.Quantity, movement.Leg, movement.ProjectId).Should().Be((StockMovementType.Shipment, product, -2m, 1, (Guid?)project), "CA 4: chave (Shipment, romaneio, linha, Shipment)");
        movement.UnitCost.Should().Be(await WithDbAsync(db => db.StockMovements.Where(m => m.SourceId == order && m.Type == StockMovementType.Production).Select(m => m.UnitCost).SingleAsync()), "custo médio do momento");
        movement.SourceNumber.Should().Be(shipment.GetProperty("number").GetString());
        shipped.GetProperty("lines")[0].GetProperty("movementId").GetGuid().Should().Be(movement.Id);
        (await SerialsAsync(order)).Should().OnlyContain(s => s.Status == SerialState.Shipped && s.ShipmentId == id && s.ShippedAt != null, "CA 4: séries Expedido");
        (await OutboxAsync<ShipmentDispatched>(id)).Should().Be(1, "o evento vai para o outbox só para avisos");
        var files = await OkAsync(await owner.GetAsync($"/api/v1/attachments/Shipment/{id}"));
        files.EnumerateArray().Should().ContainSingle(f => f.GetProperty("origin").GetString() == "System" && f.GetProperty("fileName").GetString() == shipment.GetProperty("number").GetString() + ".pdf");
        shipped.GetProperty("pdfAttachmentId").ValueKind.Should().Be(JsonValueKind.String);

        await DrainAsync();
        (await RealizedMaterialAsync(owner, project)).Should().Be(realizedBefore, "CA 7 (ADR 0025 §1): a saída Shipment nunca entra no custo do projeto");
        (await MaterialLedgerAsync(project)).Should().NotContain(c => c.Amount < 0 && c.ProductionOrderId == null);
        var timeline = await TextsAsync(owner, $"/api/v1/projects/{project}/timeline");
        timeline.Should().Contain($"Romaneio {shipment.GetProperty("number").GetString()} expedido: 2 unidades", "CA 13: a linha do tempo do projeto");

        // CA 8: sem OS de instalação, o próximo passo é registrar a entrega; o romaneio expedido fica em modo leitura.
        shipped.GetProperty("next").GetProperty("installationOrderId").ValueKind.Should().Be(JsonValueKind.Null);
        shipped.GetProperty("next").GetProperty("canConfirm").GetBoolean().Should().BeFalse();
        (await ShipmentActionAsync(editor, id, "confirm", Version(shipped))).StatusCode.Should().Be(HttpStatusCode.Conflict, "confirmado não confirma de novo");
        (await ShipmentActionAsync(editor, id, "cancel", Version(shipped), "Desisti")).StatusCode.Should().Be(HttpStatusCode.Conflict, "o expedido se estorna");
        (await owner.PutAsJsonAsync($"{Shipments}/{id}", ShipmentBody(project, [], null) as object)).StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Sem_saldo_na_confirmacao_responde_409_e_nada_grava()
    {
        var (owner, _, project, product) = await ShippableAsync(serial: false, quantity: 2m);
        var shipment = await DraftShipmentAsync(owner, project);
        var id = shipment.GetProperty("id").GetGuid();
        var location = shipment.GetProperty("lines")[0].GetProperty("locationId").GetGuid();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostAsync(new StockPosting(StockMovementType.Issue, product, location, null, -1m, null, StockSources.ManualPosting, Guid.NewGuid())))
                .IsSuccess.Should().BeTrue("alguém tirou o produto antes da confirmação");
            await transaction.CommitAsync();
        }

        var impact = await OkAsync(await owner.GetAsync($"{Shipments}/{id}/impact"));
        impact.GetProperty("lines")[0].GetProperty("insufficient").GetBoolean().Should().BeTrue();
        var refused = await ShipmentActionAsync(owner, id, "confirm", Version(shipment));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 5");
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("insufficient_stock");
        problem.GetProperty("detail").GetString().Should().Contain("1,0000", "o saldo atual na mensagem");
        (await ShipmentAsync(owner, id)).GetProperty("status").GetString().Should().Be("Draft", "nada grava");
        (await ShipmentMovementsAsync(id)).Should().BeEmpty();
        (await OutboxAsync<ShipmentDispatched>(id)).Should().Be(0);
        (await WithDbAsync(db => db.Attachments.CountAsync(a => a.OwnerType == "Shipment" && a.OwnerId == id))).Should().Be(0, "o PDF só é guardado na expedição");
    }

    [Fact]
    public async Task Estoque_geral_fica_aguardando_e_so_outra_pessoa_com_inventory_approve_confirma()
    {
        var (owner, _, project, _) = await ShippableAsync(serial: true, quantity: 1m);
        var (item, location) = await GeneralStockAsync(2m);
        var (authorId, author) = await ActorAsync("production:view,create,edit;inventory:view,approve");
        var (approverId, approver) = await ActorAsync("production:view;inventory:view,approve");
        var form = await ShipmentFormAsync(author, project);
        var shipment = await CreatedAsync(await CreateShipmentAsync(author, project, [.. AllLines(form), GeneralLine(item, location, 2m)]));
        var id = shipment.GetProperty("id").GetGuid();

        var awaiting = await OkAsync(await ShipmentActionAsync(author, id, "confirm", Version(shipment)));
        awaiting.GetProperty("status").GetString().Should().Be("AwaitingStockApproval", "CA 6: aguardando a confirmação do estoque");
        (await ShipmentMovementsAsync(id)).Should().BeEmpty("nada sai do estoque antes da confirmação");
        var notice = await WithDbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == approverId && n.Link == ShipmentService.Link + id));
        notice.Title.Should().Be($"Romaneio {shipment.GetProperty("number").GetString()} aguardando confirmação: produto do estoque geral.");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == authorId && n.Link == ShipmentService.Link + id))).Should().Be(0, "quem pediu não é avisado");
        var mine = await OkAsync(await approver.GetAsync($"{Shipments}?filter=aguardando"));
        mine.GetProperty("items").EnumerateArray().Should().Contain(r => r.GetProperty("id").GetGuid() == id, "CA 13: filtro 'Aguardando você'");

        var own = await ShipmentActionAsync(author, id, "confirm-stock", Version(awaiting));
        own.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 6: o autor não confirma");
        (await JsonAsync(own)).GetProperty("detail").GetString().Should().Be("Quem montou o romaneio não confirma produto do estoque geral.");
        var (_, editor) = await ActorAsync("production:view,edit");
        (await ShipmentActionAsync(editor, id, "confirm-stock", Version(awaiting))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem inventory.approve");
        var (_, noProduction) = await ActorAsync("inventory:view,approve");
        (await ShipmentActionAsync(noProduction, id, "confirm-stock", Version(awaiting))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem o módulo Produção no grupo");

        var done = await OkAsync(await ShipmentActionAsync(approver, id, "confirm-stock", Version(awaiting)));
        done.GetProperty("status").GetString().Should().Be("Shipped");
        done.GetProperty("selfApproved").GetBoolean().Should().BeFalse();
        done.GetProperty("stockApprovedByName").ValueKind.Should().Be(JsonValueKind.String, "CA 17: quem confirmou");
        var movements = await ShipmentMovementsAsync(id);
        movements.Should().HaveCount(2);
        movements.Single(m => m.ItemId == item).ProjectId.Should().BeNull("o estoque geral sai sem projeto: o que é do projeto não muda");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == authorId && n.Type == ShipmentService.DecisionType && n.Link == ShipmentService.Link + id))).Should().Be(1);
    }

    [Fact]
    public async Task Cancelar_libera_as_series_e_estornar_devolve_o_produto_ao_estoque()
    {
        var (owner, order, project, product) = await ShippableAsync(serial: true, quantity: 2m);
        var first = await DraftShipmentAsync(owner, project);
        var firstId = first.GetProperty("id").GetGuid();
        (await ShipmentActionAsync(owner, firstId, "cancel", Version(first), " ")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");
        var cancelled = await OkAsync(await ShipmentActionAsync(owner, firstId, "cancel", Version(first), "Cliente pediu outra data"));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled", "CA 10");
        cancelled.GetProperty("cancelReason").GetString().Should().Be("Cliente pediu outra data");

        var second = await DraftShipmentAsync(owner, project);
        var id = second.GetProperty("id").GetGuid();
        var shipped = await OkAsync(await ShipmentActionAsync(owner, id, "confirm", Version(second)));
        var (_, editor) = await ActorAsync("production:view,edit");
        (await ShipmentActionAsync(editor, id, "reverse", Version(shipped), "Lançado errado")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "estornar pede production.approve");
        (await ShipmentActionAsync(owner, id, "reverse", Version(shipped))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");
        var reversed = await OkAsync(await ShipmentActionAsync(owner, id, "reverse", Version(shipped), "O caminhão não saiu"));
        reversed.GetProperty("status").GetString().Should().Be("Reversed", "CA 11");
        var movements = await ShipmentMovementsAsync(id);
        movements.Should().HaveCount(2);
        var inverse = movements.Single(m => m.ReversesMovementId != null);
        inverse.ReversesMovementId.Should().Be(movements.Single(m => m.ReversesMovementId == null).Id, "ADR 0023 §4");
        inverse.Quantity.Should().Be(2m);
        (await WithDbAsync(db => db.StockPositions.AsNoTracking().Where(p => p.ItemId == product).SumAsync(p => p.QuantityOnHand))).Should().Be(2m);
        (await SerialsAsync(order)).Should().OnlyContain(s => s.Status == SerialState.InStock && s.ShipmentId == null, "CA 11: séries de volta ao estoque");
        (await OutboxAsync<ShipmentReversed>(id)).Should().Be(1);
        (await ShipmentActionAsync(owner, id, "reverse", Version(reversed), "De novo")).StatusCode.Should().Be(HttpStatusCode.Conflict, "estornado não estorna de novo");
        await DraftShipmentAsync(owner, project);
        var timeline = await TextsAsync(owner, $"/api/v1/projects/{project}/timeline");
        timeline.Should().Contain($"Expedição do {second.GetProperty("number").GetString()} estornada: O caminhão não saiu");
    }

    [Fact]
    public async Task Estorno_da_conclusao_e_recusado_com_o_produto_em_romaneio_ou_expedido()
    {
        var (owner, order, project, _) = await ShippableAsync(serial: true, quantity: 2m);
        var shipment = await DraftShipmentAsync(owner, project);
        var open = await ReverseOrderAsync(owner, order, await OrderVersionAsync(order));
        open.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(open)).Should().Be("product_in_shipment");
        await OkAsync(await ShipmentActionAsync(owner, shipment.GetProperty("id").GetGuid(), "confirm", Version(shipment)));
        var preview = await OkAsync(await owner.GetAsync($"{Base}/{order}/completion"));
        preview.GetProperty("reverseBlocker").GetString().Should().Be("O produto já foi expedido.");
        var refused = await ReverseOrderAsync(owner, order, Version(preview));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "PRD-06a2 CA 10 com expedição de verdade");
        Code(await JsonAsync(refused)).Should().Be("serial_shipped");
        (await OrderAsync(order)).Status.Should().Be(BemAqui.Production.Domain.ProductionOrderStatus.Completed);

        // Sem série: o projeto não tem mais o produto no estoque depois da expedição.
        var (owner2, plain, plainProject, _) = await ShippableAsync(serial: false, quantity: 2m);
        var draft = await DraftShipmentAsync(owner2, plainProject);
        await OkAsync(await ShipmentActionAsync(owner2, draft.GetProperty("id").GetGuid(), "confirm", Version(draft)));
        var plainRefused = await ReverseOrderAsync(owner2, plain, await OrderVersionAsync(plain));
        plainRefused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(plainRefused)).Should().Be("serial_shipped");
    }

    [Fact]
    public async Task Duas_confirmacoes_juntas_lancam_uma_saida_e_a_mesma_serie_nao_entra_em_dois_romaneios()
    {
        var (owner, _, project, _) = await ShippableAsync(serial: true, quantity: 2m);
        var shipment = await DraftShipmentAsync(owner, project);
        var id = shipment.GetProperty("id").GetGuid();
        var (_, other) = await ActorAsync("production:view,edit");
        var results = await Task.WhenAll(ShipmentActionAsync(owner, id, "confirm", Version(shipment)), ShipmentActionAsync(other, id, "confirm", Version(shipment)));
        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict], "CA 16: xmin; 409, nunca 500");
        (await ShipmentMovementsAsync(id)).Should().ContainSingle("a saída não duplica");
        (await OutboxAsync<ShipmentDispatched>(id)).Should().Be(1);

        var (owner2, _, project2, _) = await ShippableAsync(serial: true, quantity: 1m);
        var lines = AllLines(await ShipmentFormAsync(owner2, project2));
        var (_, creator) = await ActorAsync("production:view,create");
        var both = await Task.WhenAll(CreateShipmentAsync(owner2, project2, lines), CreateShipmentAsync(creator, project2, lines));
        both.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "CA 16: índice único parcial da série");
        both.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        (await WithDbAsync(db => db.Shipments.CountAsync(s => s.ProjectId == project2))).Should().Be(1);
    }

    [Fact]
    public async Task Tres_camadas_IDOR_e_donos_conferidos_pelos_contratos()
    {
        var (owner, _, project, _) = await ShippableAsync(serial: true, quantity: 1m);
        var shipment = await DraftShipmentAsync(owner, project);
        var id = shipment.GetProperty("id").GetGuid();
        var lineId = shipment.GetProperty("lines")[0].GetProperty("id").GetGuid();
        (await OkAsync(await owner.GetAsync($"{Shipments}/{id}/lines/{lineId}"))).GetProperty("id").GetGuid().Should().Be(lineId);

        var (_, nobody) = await ActorAsync(null);
        var forbidden = await nobody.GetAsync(Shipments);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden", "CA 15: sem grupo");
        var (_, viewer) = await ActorAsync("production:view");
        (await viewer.GetAsync($"{Shipments}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateShipmentAsync(viewer, project, [])).StatusCode.Should().Be(HttpStatusCode.Forbidden, "montar pede production.create");
        (await ShipmentActionAsync(viewer, id, "cancel", Version(shipment), "x")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "cancelar pede production.edit");
        (await viewer.GetAsync($"{Shipments}/form?projectId={project}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // IDOR: a linha de um romaneio pelo id de outro dá 404.
        var (otherOwner, _, otherProject, _) = await ShippableAsync(serial: true, quantity: 1m);
        var otherShipment = await DraftShipmentAsync(otherOwner, otherProject);
        (await owner.GetAsync($"{Shipments}/{otherShipment.GetProperty("id").GetGuid()}/lines/{lineId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "CA 15: IDOR");
        (await owner.GetAsync($"{Shipments}/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Projeto e transportadora pelos Contracts dos donos.
        (await CreateShipmentAsync(owner, Guid.CreateVersion7(), AllLines(await ShipmentFormAsync(owner, project)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var notCarrier = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Cliente " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Customer" } });
        var customer = (await JsonAsync(notCarrier)).GetProperty("id").GetGuid();
        await OkAsync(await ShipmentActionAsync(owner, id, "cancel", Version(shipment), "Refazer"));
        var wrongCarrier = await CreateShipmentAsync(owner, project, AllLines(await ShipmentFormAsync(owner, project)), carrier: customer);
        wrongCarrier.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a transportadora tem o papel Transportadora");

        await SetModuleReleasedAsync("production", false);
        try
        {
            var blocked = await owner.GetAsync(Shipments);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released", "CA 15: módulo não liberado");
        }
        finally
        {
            await SetModuleReleasedAsync("production", true);
        }
    }

    [Fact]
    public async Task Comprovantes_entram_na_aba_Documentos_com_production_create()
    {
        var (owner, _, project, _) = await ShippableAsync(serial: true, quantity: 1m);
        var shipment = await DraftShipmentAsync(owner, project);
        var id = shipment.GetProperty("id").GetGuid();
        var png = Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 2048);
        var (_, viewer) = await ActorAsync("production:view");
        (await UploadShipmentFileAsync(viewer, id, png, "canhoto.png")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 14: gravar pede production.create");
        var (_, creator) = await ActorAsync("production:view,create");
        (await UploadShipmentFileAsync(creator, id, png, "canhoto.png")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await UploadShipmentFileAsync(creator, id, "<?xml version=\"1.0\"?><a/>"u8.ToArray(), "nota.xml")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "só pdf, png e jpg");
        (await UploadShipmentFileAsync(creator, id, Pad("%PDF-1.7"u8.ToArray(), 11 * 1024 * 1024), "grande.pdf")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "até 10 MB");
        (await UploadShipmentFileAsync(creator, Guid.CreateVersion7(), png, "canhoto.png")).StatusCode.Should().Be(HttpStatusCode.NotFound, "dono conferido");
        var files = await OkAsync(await viewer.GetAsync($"/api/v1/attachments/Shipment/{id}"));
        files.EnumerateArray().Should().ContainSingle(f => f.GetProperty("fileName").GetString() == "canhoto.png", "ler com production.view");
    }

    [Fact]
    public async Task Historico_mostra_montagem_e_confirmacao_com_placa_e_motorista_mascarados()
    {
        var (owner, _, project, _) = await ShippableAsync(serial: true, quantity: 1m);
        var shipment = await DraftShipmentAsync(owner, project);
        var id = shipment.GetProperty("id").GetGuid();
        await OkAsync(await ShipmentActionAsync(owner, id, "confirm", Version(shipment)));
        var (_, viewer) = await ActorAsync("production:view");
        var history = await OkAsync(await viewer.GetAsync($"{Shipments}/{id}/history?pageSize=100"));
        var entities = history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entity").GetString()).ToList();
        entities.Should().Contain(["Shipment", "ShipmentLine", "ShipmentLineSerial"], "CA 17: antes/depois do romaneio e das linhas");
        var raw = history.GetRawText();
        raw.Should().NotContain("José da Silva", "CA 17: motorista mascarado").And.NotContain("ABC1D23", "placa mascarada");
        raw.Should().Contain("***");
        raw.Should().Contain("Shipped", "a confirmação no histórico");
    }
}