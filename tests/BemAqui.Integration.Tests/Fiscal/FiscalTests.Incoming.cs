using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Fiscal.Application.Incoming;
using BemAqui.Fiscal.Domain;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-04a (Testcontainers): importação com recusa por XML inseguro, assinatura, protocolo, destinatário e rate limit (CA 1 e 2); aviso da
/// SEFAZ, anexo de sistema com prefixo do tenant e log mascarado sem dado pessoal (CA 3, 5 e 14); chave duplicada e reimportação depois
/// de recusada (CA 4 e 12); fornecedor novo e papel (CA 6); de-para lembrado, gravado e item sem estoque (CA 7 e 8); pedido sugerido,
/// divergências e linha de outro pedido (CA 9 e 10); sem pedido com e sem <c>purchasing.approve</c> (CA 11); <c>fiscal.create</c> sem
/// <c>fiscal.import_incoming</c>, <c>purchasing.view</c>, IDOR e XML por registro (CA 13); auditoria (CA 15).
/// </summary>
public partial class FiscalTests
{
    private const string Incoming = "/api/v1/fiscal/incoming-invoices";
    private static readonly string[] Warehouse = ["fiscal.view", "fiscal.import_incoming"];

    private Task<(Guid Id, HttpClient Client)> IncomingActorAsync(params string[] grants) => fixture.ActorAsync(fixture.Tenancy.TenantA, FiscalFixture.HostA, false, grants);

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[] content, string fileName = "nota.xml")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        form.Add(file, "file", fileName);
        return await client.PostAsync(Incoming + "/import", form);
    }

    private static async Task<IncomingInvoiceView> ImportedAsync(HttpClient client, NfeSample sample)
    {
        var imported = await OkAsync<IncomingImportResult>(await ImportAsync(client, NfeSamples.Authorized(sample)));
        return await OkAsync<IncomingInvoiceView>(await client.GetAsync($"{Incoming}/{imported.Id}"));
    }

    private static async Task<string> RefusedAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.UnprocessableEntity)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString()!;
    }

    private async Task<Guid> SupplierAsync(string cnpj, params PartyRoleKind[] roles)
    {
        await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
        var party = new Party { Kind = PartyKind.PJ, Document = cnpj, LegalName = "FORNECEDOR TESTE LTDA " + cnpj[..4], CreatedAt = DateTimeOffset.UtcNow };
        foreach (var role in roles.Length == 0 ? [PartyRoleKind.Supplier] : roles)
        {
            party.Roles.Add(new PartyRole { PartyId = party.Id, Role = role });
        }

        db.Parties.Add(party);
        await db.SaveChangesAsync();
        return party.Id;
    }

    private async Task<Guid> StockItemAsync(string name, string unit = "UN", string ncm = "72085100")
    {
        await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var unitId = await db.Units.Where(u => u.Code == unit).Select(u => u.Id).SingleAsync();
        var item = new Item { Type = BemAqui.Inventory.Contracts.ItemType.RawMaterial, Code = "MP-" + Guid.NewGuid().ToString("N")[..8], Name = name, UnitId = unitId, Ncm = ncm, CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    /// <summary>Pedido aprovado gravado direto (o fluxo de aprovação é da CMP-03a): linhas na unidade de estoque.</summary>
    private async Task<(Guid Id, string Number, Guid[] Lines)> OrderAsync(Guid partyId, decimal freight, decimal ipi, params (Guid Item, decimal Quantity, decimal Price)[] lines)
    {
        await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<IPurchasingDb>();
        var subtotal = lines.Sum(l => Math.Round(l.Quantity * l.Price, 2));
        var order = new PurchaseOrder
        {
            Number = "PC-2026-" + Random.Shared.Next(100_000, 999_999), PartyId = partyId, Status = PurchaseOrderStatus.Approved, PaymentTermId = Guid.NewGuid(),
            ExpectedDate = DateOnly.FromDateTime(DateTime.UtcNow), Subtotal = subtotal, Freight = freight, IpiAmount = ipi, Total = subtotal + freight + ipi,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow,
        };
        db.PurchaseOrders.Add(order);
        var ids = new List<Guid>();
        var seq = 0;
        foreach (var (item, quantity, price) in lines)
        {
            var line = new PurchaseOrderLine { OrderId = order.Id, Sequence = ++seq, ItemId = item, Quantity = quantity, UnitPrice = price, LineTotal = Math.Round(quantity * price, 2) };
            db.PurchaseOrderLines.Add(line);
            ids.Add(line.Id);
        }

        await db.SaveChangesAsync();
        return (order.Id, order.Number, [.. ids]);
    }

    [Fact]
    public async Task Importa_o_xml_com_o_aviso_da_SEFAZ_o_anexo_de_sistema_no_prefixo_do_tenant_e_log_sem_dado_pessoal()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var sample = new NfeSample { CarrierPerson = true, Freight = 20m, Items = [new("CH-2MM", "CHAPA DE ACO 2MM", "72085100", "UN", 10m, 12.5m, Lot: "L-1")] };
        var response = await ImportAsync(warehouse, NfeSamples.Authorized(sample), "../../nota do fornecedor.xml");
        var imported = await OkAsync<IncomingImportResult>(response);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        imported.Items.Should().Be(1);
        imported.SupplierFound.Should().BeFalse();

        var view = await OkAsync<IncomingInvoiceView>(await warehouse.GetAsync($"{Incoming}/{imported.Id}"));
        view.Status.Should().Be(IncomingInvoiceStatus.Imported);
        view.SefazStatusChecked.Should().BeFalse("o sistema não consulta a SEFAZ (CA 3)");
        (view.Number, view.Series, view.AccessKey, view.Protocol, view.SignerDocument).Should().Be((sample.Number, 1, NfeSamples.AccessKey(sample), "135260000000001", sample.IssuerCnpj));
        view.Totals.Total.Should().Be(145m);
        view.Totals.Freight.Should().Be(20m);
        view.Duplicates.Should().ContainSingle().Which.Amount.Should().Be(145m);
        view.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { SupplierCode = "CH-2MM", Ncm = "72085100", Cfop = "5101", Unit = "UN", Quantity = 10m, UnitPrice = 12.5m, LotNumber = "L-1" });
        view.Issuer.Address!.City.Should().Be("SAO PAULO");
        view.Supplier.State.Should().Be(IncomingSupplierState.NotFound);
        view.Can.Import.Should().BeTrue();
        view.CheckBlockedReason.Should().Contain("Cadastre o fornecedor");

        // XML guardado como anexo de sistema, do dono IncomingInvoice, debaixo do id do tenant, com o nome pela chave (CA 5).
        var attachment = await fixture.QueryAsync(tenant, db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == view.XmlAttachmentId));
        (attachment.OwnerType, attachment.Origin, attachment.ScanStatus, attachment.ContentType).Should().Be((IncomingInvoiceService.AttachmentOwner, AttachmentOrigin.System, ScanStatus.Clean, "application/xml"));
        attachment.StoragePath.Should().StartWith(tenant.Id.ToString("N"));
        attachment.FileName.Should().Be(view.AccessKey + "-nfe.xml");
        var xml = await warehouse.GetAsync($"{Incoming}/{view.Id}/xml");
        xml.StatusCode.Should().Be(HttpStatusCode.OK);
        (await xml.Content.ReadAsStringAsync()).Should().Contain(view.AccessKey);

        // Log com a chave mascarada (últimos 6) e sem o transportador pessoa física; auditoria também sem ele (CA 14).
        var logs = fixture.AllLogs();
        logs.Should().Contain(IncomingInvoiceRules.MaskKey(view.AccessKey)).And.NotContain(view.AccessKey).And.NotContain(NfeSamples.CarrierName).And.NotContain(NfeSamples.CarrierCpf);
        var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(IncomingInvoice) && a.EntityId == view.Id.ToString()).ToListAsync());
        audit.Should().ContainSingle(a => a.Action == "Created");
        string.Concat(audit.Select(a => a.Changes)).Should().NotContain(NfeSamples.CarrierName).And.NotContain(NfeSamples.CarrierCpf).And.Contain("\"accessKey\"");
        (await OkAsync<IncomingInvoiceList>(await warehouse.GetAsync(Incoming))).Items.Should().Contain(i => i.Id == view.Id && i.PendingItems == 1);
    }

    [Fact]
    public async Task Recusa_xml_inseguro_sem_protocolo_alterado_de_outro_cnpj_de_outro_destinatario_e_outro_tipo_de_arquivo()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var sample = new NfeSample();
        (await RefusedAsync(await ImportAsync(warehouse, NfeSamples.Xxe()))).Should().Be(IncomingInvoiceRules.InvalidXmlMessage);
        (await RefusedAsync(await ImportAsync(warehouse, NfeSamples.BillionLaughs()))).Should().Be(IncomingInvoiceRules.InvalidXmlMessage);
        (await RefusedAsync(await ImportAsync(warehouse, NfeSamples.Authorized(sample, withProtocol: false)))).Should().Be(IncomingInvoiceRules.NoProtocolMessage);
        var altered = NfeSamples.SignedText(sample).Replace("CHAPA DE ACO 2MM", "CHAPA DE ACO 9MM", StringComparison.Ordinal);
        (await RefusedAsync(await ImportAsync(warehouse, Encoding.UTF8.GetBytes(altered)))).Should().Be(IncomingInvoiceRules.SignatureMismatchMessage);
        (await RefusedAsync(await ImportAsync(warehouse, NfeSamples.Authorized(sample, NfeSamples.Signer(NfeSamples.NewCnpj()))))).Should().Contain("outro CNPJ");
        (await RefusedAsync(await ImportAsync(warehouse, NfeSamples.Authorized(sample with { RecipientCnpj = NfeSamples.NewCnpj() })))).Should().Be(IncomingInvoiceRules.NotRecipientMessage);
        (await RefusedAsync(await ImportAsync(warehouse, "%PDF-1.4 nota"u8.ToArray(), "nota.pdf"))).Should().Contain("Tipo de arquivo não permitido");
        (await RefusedAsync(await ImportAsync(warehouse, new byte[IncomingInvoiceRules.MaxBytes + 1]))).Should().Contain("2 MB");
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.IncomingInvoices.AnyAsync(i => i.AccessKey == NfeSamples.AccessKey(sample)))).Should().BeFalse("nada foi gravado");
    }

    [Fact]
    public async Task Rate_limit_de_60_arquivos_por_hora_e_por_usuario()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var (_, other) = await IncomingActorAsync(Warehouse);
        for (var i = 0; i < FiscalFixture.IncomingImportsPerHour; i++)
        {
            (await ImportAsync(warehouse, "<a/>"u8.ToArray())).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        var limited = await ImportAsync(warehouse, "<a/>"u8.ToArray());
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await ImportAsync(other, "<a/>"u8.ToArray())).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o limite é por usuário");
    }

    [Fact]
    public async Task Chave_ja_importada_e_recusada_com_409_e_volta_a_entrar_depois_de_recusada()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var sample = new NfeSample();
        var first = await ImportedAsync(warehouse, sample);

        var duplicate = await ImportAsync(warehouse, NfeSamples.Authorized(sample));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().Should().Be(IncomingInvoiceRules.DuplicateCode);
        problem.GetProperty("detail").GetString().Should().MatchRegex(@"^Esta nota já foi importada em \d{2}/\d{2} por .+\.$");
        problem.GetProperty("invoiceId").GetGuid().Should().Be(first.Id);

        (await RefusedAsync(await warehouse.PostAsJsonAsync($"{Incoming}/{first.Id}/reject", new { reason = "" }))).Should().Contain("motivo");
        var rejected = await OkAsync<IncomingInvoiceView>(await warehouse.PostAsJsonAsync($"{Incoming}/{first.Id}/reject", new { reason = "Nota com o preço errado, o fornecedor vai cancelar." }));
        rejected.Status.Should().Be(IncomingInvoiceStatus.Rejected);
        rejected.RejectedReason.Should().StartWith("Nota com o preço errado");
        (await warehouse.PutAsJsonAsync($"{Incoming}/{first.Id}/order", new { withoutOrder = true })).StatusCode.Should().Be(HttpStatusCode.Conflict, "a recusada não muda mais");

        var again = await ImportedAsync(warehouse, sample);
        again.Id.Should().NotBe(first.Id, "a chave recusada fica livre para nova importação (CA 12)");
    }

    [Fact]
    public async Task Fornecedor_novo_pela_nota_e_terceiro_sem_o_papel_de_fornecedor()
    {
        var (_, warehouse) = await IncomingActorAsync([.. Warehouse, "parties.create", "parties.edit", "parties.view"]);
        var sample = new NfeSample();
        var view = await ImportedAsync(warehouse, sample);
        view.Supplier.State.Should().Be(IncomingSupplierState.NotFound);
        view.Can.CreateParty.Should().BeTrue();
        view.Issuer.Should().BeEquivalentTo(new { Document = sample.IssuerCnpj, Name = sample.IssuerName, StateRegistration = "110042490114" });

        // Cadastrado como cliente: "Adicionar papel".
        var partyId = await SupplierAsync(sample.IssuerCnpj, PartyRoleKind.Customer);
        (await OkAsync<IncomingInvoiceView>(await warehouse.PostAsync($"{Incoming}/{view.Id}/supplier", null))).Supplier
            .Should().BeEquivalentTo(new { State = IncomingSupplierState.NotSupplier, PartyId = (Guid?)partyId });
        await using (var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA))
        {
            var parties = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
            parties.PartyRoles.Add(new PartyRole { PartyId = partyId, Role = PartyRoleKind.Supplier });
            await parties.SaveChangesAsync();
        }

        var found = await OkAsync<IncomingInvoiceView>(await warehouse.PostAsync($"{Incoming}/{view.Id}/supplier", null));
        found.Supplier.State.Should().Be(IncomingSupplierState.Found);
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.IncomingInvoices.Where(i => i.Id == view.Id).Select(i => i.PartyId).SingleAsync())).Should().Be(partyId);
    }

    [Fact]
    public async Task De_para_com_fator_obrigatorio_sem_item_de_estoque_lembrado_na_proxima_nota_e_auditado()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (_, warehouse) = await IncomingActorAsync([.. Warehouse, "purchasing.approve"]);
        var cnpj = NfeSamples.NewCnpj();
        var partyId = await SupplierAsync(cnpj);
        var item = await StockItemAsync("Parafuso sextavado M8 " + Guid.NewGuid().ToString("N")[..4]);
        var account = await fixture.WithAsync<Guid, BemAqui.Finance.Contracts.IChartOfAccounts>(tenant, async c => (await c.PostableAsync(BemAqui.Finance.Contracts.AccountType.Expense))[0].Id);
        var sample = new NfeSample { IssuerCnpj = cnpj, Items = [new("PAR-M8", "PARAFUSO SEXTAVADO M8", "73181500", "CX", 3m, 120m), new("FRETE-SERV", "SERVICO DE ENTREGA", "00000000", "UN", 1m, 50m)] };
        var view = await ImportedAsync(warehouse, sample);
        var (box, service) = (view.Items[0], view.Items[1]);
        box.ItemId.Should().BeNull();

        // Unidade do fornecedor (CX) sem conversão: o fator é obrigatório antes de conferir (CA 8).
        var mapped = await OkAsync<IncomingInvoiceView>(await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{box.Id}/mapping", new { itemId = item, noStockItem = false }));
        mapped.Items[0].Should().BeEquivalentTo(new { FactorRequired = true, ConversionFactor = (decimal?)null, Resolved = false, MappingSource = (ItemMappingSource?)ItemMappingSource.Manual });
        mapped = await OkAsync<IncomingInvoiceView>(await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{box.Id}/mapping", new { itemId = item, conversionFactor = 100m, noStockItem = false }));
        mapped.Items[0].Should().BeEquivalentTo(new { StockQuantity = (decimal?)300m, StockUnitPrice = (decimal?)1.2m, Resolved = true, StockUnit = "UN" });

        // Serviço na mesma nota: "Sem item de estoque" com a conta de despesa (ponto 59).
        (await RefusedAsync(await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{service.Id}/mapping", new { noStockItem = true }))).Should().Contain("conta de despesa");
        mapped = await OkAsync<IncomingInvoiceView>(await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{service.Id}/mapping", new { noStockItem = true, expenseAccountId = account }));
        mapped.Items[1].Should().BeEquivalentTo(new { NoStockItem = true, ExpenseAccountId = (Guid?)account, Resolved = true });
        mapped.PendingItems.Should().Be(0);

        // Conferir sem pedido (com purchasing.approve e o motivo) grava o de-para para a próxima vez.
        (await OkAsync<IncomingInvoiceView>(await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/order", new { withoutOrder = true }))).WithoutOrder.Should().BeTrue();
        var checkedView = await OkAsync<IncomingInvoiceView>(await warehouse.PostAsJsonAsync($"{Incoming}/{view.Id}/check", new { withoutOrderReason = "Compra emergencial de balcão." }));
        checkedView.Status.Should().Be(IncomingInvoiceStatus.Checked);
        checkedView.WithoutOrderReason.Should().Be("Compra emergencial de balcão.");
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var match = await scope.ServiceProvider.GetRequiredService<BemAqui.Inventory.Contracts.IItemSupplierCodes>().FindAsync(partyId, "PAR-M8");
            match.Should().BeEquivalentTo(new { ItemId = item, SupplierUnit = "CX", ConversionFactor = 100m });
        }

        var next = await ImportedAsync(warehouse, new NfeSample { IssuerCnpj = cnpj, Items = [new("PAR-M8", "PARAFUSO SEXTAVADO M8", "73181500", "CX", 2m, 118m)] });
        next.Items[0].Should().BeEquivalentTo(new { ItemId = (Guid?)item, MappingSource = (ItemMappingSource?)ItemMappingSource.Remembered, ConversionFactor = (decimal?)100m, StockQuantity = (decimal?)200m });
        (await OkAsync<IncomingImportResult>(await ImportAsync(warehouse, NfeSamples.Authorized(new NfeSample { IssuerCnpj = cnpj, Items = [new("PAR-M8", "PARAFUSO SEXTAVADO M8", "73181500", "CX", 1m, 118m)] })))).Remembered.Should().Be(1);

        // Histórico: importação, de-para, pedido e conferência (CA 15).
        var history = JsonDocument.Parse(await (await warehouse.GetAsync($"{Incoming}/{view.Id}/history?pageSize=50")).Content.ReadAsStringAsync()).RootElement.GetProperty("items");
        var entities = history.EnumerateArray().Select(e => e.GetProperty("entity").GetString() + ":" + e.GetProperty("action").GetString()).ToList();
        entities.Should().Contain(["IncomingInvoice:Created", "IncomingInvoiceItem:Created", "IncomingInvoiceItem:Updated", "IncomingInvoice:Updated"]);
        history.EnumerateArray().Select(e => e.GetProperty("changes").GetString()).Should().Contain(c => c!.Contains("\"withoutOrderReason\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pedido_sugerido_divergencias_de_quantidade_e_preco_e_linha_de_outro_pedido()
    {
        var (_, buyer) = await IncomingActorAsync([.. Warehouse, "purchasing.view"]);
        var (_, blind) = await IncomingActorAsync(Warehouse);
        var cnpj = NfeSamples.NewCnpj();
        var partyId = await SupplierAsync(cnpj);
        var plate = await StockItemAsync("Chapa de aço 2mm " + Guid.NewGuid().ToString("N")[..4]);
        var cable = await StockItemAsync("Cabo de aço 8mm " + Guid.NewGuid().ToString("N")[..4], "UN", "73121090");
        var order = await OrderAsync(partyId, 30m, 5m, (plate, 8m, 12m), (cable, 20m, 4m));
        var other = await OrderAsync(partyId, 0m, 0m, (plate, 1m, 12m));
        var sample = new NfeSample
        {
            IssuerCnpj = cnpj, Freight = 40m, Complement = "Pedido " + order.Number,
            Items = [new("CH-2MM", "CHAPA DE ACO 2MM", "72085100", "UN", 10m, 12.5m, IpiRate: 5m), new("CB-8", "CABO DE ACO 8MM", "73121090", "UN", 15m, 4m)],
        };
        var view = await ImportedAsync(buyer, sample);
        view.Order.SuggestedId.Should().Be(order.Id, "o número do pedido está na informação complementar (CA 9)");
        view.Order.SuggestionReason.Should().Contain(order.Number);

        foreach (var (line, item) in new[] { (view.Items[0], plate), (view.Items[1], cable) })
        {
            (await buyer.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{line.Id}/mapping", new { itemId = item, noStockItem = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var linked = await OkAsync<IncomingInvoiceView>(await buyer.PutAsJsonAsync($"{Incoming}/{view.Id}/order", new { purchaseOrderId = order.Id, withoutOrder = false }));
        linked.Order.PurchaseOrderId.Should().Be(order.Id);
        linked.Items.Select(i => i.PoLineId).Should().Equal(order.Lines[0], order.Lines[1]);

        // CA 10: faturado acima do saldo e preço acima do pedido em vermelho; abaixo em âmbar; frete e IPI da nota × do pedido.
        var plateCmp = linked.Items[0].Comparison!;
        (plateCmp.QuantityDifference, plateCmp.QuantityLevel, plateCmp.PriceDifference, plateCmp.PricePercent, plateCmp.ValueDifference, plateCmp.PriceLevel)
            .Should().Be((2m, DivergenceLevel.Critical, 0.5m, 4.17m, 5m, DivergenceLevel.Critical));
        var cableCmp = linked.Items[1].Comparison!;
        (cableCmp.QuantityDifference, cableCmp.QuantityLevel, cableCmp.PriceLevel).Should().Be((-5m, DivergenceLevel.Warning, DivergenceLevel.Ok));
        linked.Order.Freight.Should().BeEquivalentTo(new { Order = 30m, Invoice = 40m, Level = DivergenceLevel.Critical });
        linked.Order.Ipi.Should().BeEquivalentTo(new { Order = 5m, Invoice = 6.25m, Level = DivergenceLevel.Critical });

        // Sem purchasing.view: o vínculo aparece, os valores do pedido não (CA 13).
        var hidden = await OkAsync<IncomingInvoiceView>(await blind.GetAsync($"{Incoming}/{view.Id}"));
        hidden.Order.ValuesVisible.Should().BeFalse();
        hidden.Order.Number.Should().Be(order.Number);
        hidden.Order.Freight.Should().BeNull();
        hidden.Items.Should().OnlyContain(i => i.Comparison == null);
        hidden.Order.Lines.Should().OnlyContain(l => l.UnitPrice == null && l.OpenQuantity == null);

        // Linha de outro pedido: 422.
        (await RefusedAsync(await buyer.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{view.Items[0].Id}/mapping", new { itemId = plate, noStockItem = false, poLineId = other.Lines[0] })))
            .Should().Contain("outro pedido");

        var checkedView = await OkAsync<IncomingInvoiceView>(await buyer.PostAsJsonAsync($"{Incoming}/{view.Id}/check", new { }));
        checkedView.Status.Should().Be(IncomingInvoiceStatus.Checked);
        checkedView.CheckedByName.Should().NotBeNull();
        checkedView.Divergences.Should().HaveCount(2).And.Contain(d => d.Sequence == 1 && d.QuantityLevel == DivergenceLevel.Critical && d.PriceLevel == DivergenceLevel.Critical);
    }

    [Fact]
    public async Task Nota_sem_pedido_so_e_conferida_com_purchasing_approve_e_motivo()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var (_, approver) = await IncomingActorAsync([.. Warehouse, "purchasing.approve"]);
        var cnpj = NfeSamples.NewCnpj();
        await SupplierAsync(cnpj);
        var item = await StockItemAsync("Chapa de aço 3mm " + Guid.NewGuid().ToString("N")[..4]);
        var view = await ImportedAsync(warehouse, new NfeSample { IssuerCnpj = cnpj });
        (await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{view.Items[0].Id}/mapping", new { itemId = item, noStockItem = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RefusedAsync(await warehouse.PostAsJsonAsync($"{Incoming}/{view.Id}/check", new { }))).Should().Be("Escolha o pedido de compra da nota ou marque Sem pedido.");

        var withoutOrder = await OkAsync<IncomingInvoiceView>(await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/order", new { withoutOrder = true }));
        withoutOrder.CheckBlockedReason.Should().Be(IncomingInvoiceRules.WithoutOrderBlockedMessage);
        (await RefusedAsync(await warehouse.PostAsJsonAsync($"{Incoming}/{view.Id}/check", new { withoutOrderReason = "Compra urgente" }), HttpStatusCode.Forbidden))
            .Should().Be(IncomingInvoiceRules.WithoutOrderBlockedMessage);

        (await OkAsync<IncomingInvoiceView>(await approver.GetAsync($"{Incoming}/{view.Id}"))).CheckBlockedReason.Should().BeNull();
        (await RefusedAsync(await approver.PostAsJsonAsync($"{Incoming}/{view.Id}/check", new { withoutOrderReason = "" }))).Should().Contain("motivo");
        var done = await OkAsync<IncomingInvoiceView>(await approver.PostAsJsonAsync($"{Incoming}/{view.Id}/check", new { withoutOrderReason = "Compra urgente de balcão." }));
        done.Status.Should().Be(IncomingInvoiceStatus.Checked);
        done.WithoutOrderApprovedByName.Should().NotBeNull();
        (await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{view.Items[0].Id}/mapping", new { noStockItem = false })).StatusCode.Should().Be(HttpStatusCode.Conflict, "a conferida não muda mais");
    }

    [Fact]
    public async Task Emitir_nao_importa_ver_nao_altera_item_de_outra_nota_e_404_e_o_xml_so_pelo_registro()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var (_, issuer) = await IncomingActorAsync("fiscal.view", "fiscal.create");
        var (_, viewer) = await IncomingActorAsync("fiscal.view");
        var a = await ImportedAsync(warehouse, new NfeSample());
        var b = await ImportedAsync(warehouse, new NfeSample());

        (await ImportAsync(issuer, NfeSamples.Authorized(new NfeSample()))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "fiscal.create emite nota de saída, mas não importa (D12)");
        (await issuer.PostAsJsonAsync($"{Incoming}/{a.Id}/reject", new { reason = "Não é nossa." })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var seen = await OkAsync<IncomingInvoiceView>(await viewer.GetAsync($"{Incoming}/{a.Id}"));
        seen.Can.Import.Should().BeFalse();
        seen.CheckBlockedReason.Should().Contain("permissão");
        (await viewer.PutAsJsonAsync($"{Incoming}/{a.Id}/order", new { withoutOrder = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"{Incoming}/{a.Id}/xml")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await warehouse.PutAsJsonAsync($"{Incoming}/{a.Id}/items/{b.Items[0].Id}/mapping", new { noStockItem = false })).StatusCode.Should().Be(HttpStatusCode.NotFound, "item de outra nota (IDOR)");

        (await viewer.GetAsync($"{Incoming}/{Guid.NewGuid()}/xml")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
