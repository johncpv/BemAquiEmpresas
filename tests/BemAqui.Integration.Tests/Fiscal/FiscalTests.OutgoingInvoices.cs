using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.Platform.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-03a com o <see cref="FakeFiscalEmitter"/> e banco real: rascunho do romaneio idempotente e "Faturar" (CA 1 e 2); cálculo sem imposto
/// digitado e saneamento 422 sem chamar o Emissor (CA 3 e 4); emissão autorizada com XML, DANFE, evento e e-mail, sem estoque, e o estorno
/// da expedição recusado (CA 6, 7, 12 e 14); retorno pelo webhook (CA 10); rejeição, correção e número a inutilizar (CA 8); denegada (CA 9);
/// pré-condições (CA 6); concorrência (CA 19); razão do projeto com <c>InvoiceTax</c> uma vez só (CA 11); <c>IOutgoingInvoices</c> (CA 13);
/// fila "A emitir" e pauta (CA 16); IDOR e arquivos (CA 18); auditoria mascarada (CA 21); a lista com 50 mil notas (CA 17, carga). O fake e o
/// tenant são da classe: contagens relativas ao início de cada teste.
/// </summary>
public partial class FiscalTests
{
    private const string Invoices = "/api/v1/fiscal/outgoing-invoices";
    private const string RecipientCanary = "CLIENTE CANARIO NOTA LTDA";
    private const string RecipientMailCanary = "canario.nota@cliente.com.br";

    private sealed record Sale(Guid Shipment, Guid Project, Guid Party, Guid Item, Guid ShipmentLine);

    /// <summary>Configuração, regras do seed ativas, certificado válido e integração testada, no tenant A.</summary>
    private async Task ReadyToIssueAsync(bool certificate = true)
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetRulesAsync(tenant);
        await fixture.ConfigureAsync(tenant);
        await using var scope = await fixture.ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IFiscalDb>();
        foreach (var rule in await db.TaxRules.ToListAsync())
        {
            rule.Activate(Guid.CreateVersion7(), DateTimeOffset.UtcNow, false);
        }

        if (certificate)
        {
            var settings = await db.FiscalSettings.SingleAsync();
            settings.RecordCertificate("WSP EQUIPAMENTOS", "47225005000186", "AB12CD", DateTimeOffset.UtcNow.AddDays(200), null, DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Venda pronta para a nota, gravada direto nas tabelas dos donos (cada um tem os próprios testes): cliente PJ com IE, endereço de cobrança
    /// com IBGE e e-mail; orçamento com o equipamento (R$ 100.000) e a instalação (serviço); contrato, projeto e o romaneio expedido do equipamento.
    /// </summary>
    private async Task<Sale> SaleAsync(string state = "SP", string? ie = "110042490114", decimal price = 100_000m)
    {
        await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var party = new Party { Kind = PartyKind.PJ, Document = "4" + Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            LegalName = RecipientCanary, StateRegistration = ie, Email = RecipientMailCanary, CreatedAt = now };
        party.Roles.Add(new PartyRole { PartyId = party.Id, Role = PartyRoleKind.Customer });
        party.Addresses.Add(new PartyAddress { PartyId = party.Id, Kind = AddressKind.Billing, ZipCode = "01001000", Street = "Rua do Cliente", Number = "200", District = "Centro",
            City = state == "SP" ? "São Paulo" : "Belo Horizonte", IbgeCode = state == "SP" ? "3550308" : "3106200", State = state, IsDefault = true });
        db.Parties.Add(party);
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new BemAqui.Inventory.Domain.Item { Type = BemAqui.Inventory.Contracts.ItemType.Finished, Code = "ELEV-" + Guid.NewGuid().ToString("N")[..6], Name = "Elevador de carga 2 paradas",
            UnitId = unit, Ncm = "8428" + Random.Shared.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture), IsActive = true, CreatedAt = now };
        db.Items.Add(item);
        var quote = new Quote { Number = "ORC-T-" + Guid.NewGuid().ToString("N")[..8], PartyId = party.Id, Status = QuoteStatus.Approved, TotalPrice = price + 10_000m, CreatedAt = now };
        var equipment = new QuoteItem { QuoteId = quote.Id, Sequence = 1, Kind = QuoteItemKind.Equipment, ItemId = item.Id, Description = "Elevador de carga", Quantity = 1, UnitPrice = price };
        var service = new QuoteItem { QuoteId = quote.Id, Sequence = 2, Kind = QuoteItemKind.Service, Description = "Instalação", Quantity = 1, UnitPrice = 10_000m };
        var contract = new Contract { Number = "CTR-T-" + Guid.NewGuid().ToString("N")[..8], QuoteId = quote.Id, PartyId = party.Id, SignedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            TotalValue = price + 10_000m, RevenueAccountId = Guid.CreateVersion7(), CreatedAt = now };
        var project = new Project { Number = "PRJ-T-" + Guid.NewGuid().ToString("N")[..8], ContractId = contract.Id, PartyId = party.Id, Name = "Obra de teste", Status = ProjectStatus.Fabricated, CreatedAt = now };
        db.AddRange(quote, equipment, service, contract, project,
            new ProjectItem { ProjectId = project.Id, QuoteItemId = equipment.Id, Sequence = 1, Description = "Elevador de carga", Quantity = 1, ItemId = item.Id },
            new ProjectItem { ProjectId = project.Id, QuoteItemId = service.Id, Sequence = 2, Description = "Instalação", Quantity = 1 });
        var shipment = new Shipment { Number = "ROM-T-" + Guid.NewGuid().ToString("N")[..6], ProjectId = project.Id, PartyId = party.Id, DeliveryAddress = "Rua da Obra, 100 — Centro — Caçapava/SP",
            Volumes = 2, GrossWeight = 850.5m, Status = ShipmentState.Shipped, CreatedAt = now, ShippedAt = now };
        var line = new ShipmentLine { ShipmentId = shipment.Id, Sequence = 1, ItemId = item.Id, Quantity = 1, LocationId = Guid.CreateVersion7() };
        db.AddRange(shipment, line, new ShipmentLineSerial { ShipmentId = shipment.Id, LineId = line.Id, SerialId = Guid.CreateVersion7(), SerialNumber = "SN-0001" });
        await db.SaveChangesAsync();
        return new Sale(shipment.Id, project.Id, party.Id, item.Id, line.Id);
    }

    private Task<(Guid Id, HttpClient Client)> OwnerAsync() => fixture.ActorAsync(fixture.Tenancy.TenantA, FiscalFixture.HostA, owner: true);

    private static HttpRequestMessage CreateInvoice(Guid shipment, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Invoices) { Content = JsonContent.Create(new { shipmentId = shipment }) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonDocument.Parse(body).RootElement;
    }

    private static object Edit(JsonElement invoice, decimal? ipiRate = 5m, string? description = null, decimal freight = 0m, string? info = null) => new
    {
        items = invoice.GetProperty("items").EnumerateArray().Select(i => new
        {
            id = i.GetProperty("id").GetGuid(), quantity = i.GetProperty("quantity").GetDecimal(), unitPrice = i.GetProperty("unitPrice").GetDecimal(), discount = 0m,
            description = description ?? i.GetProperty("description").GetString(), additionalInfo = i.GetProperty("additionalInfo").GetString(), taxRuleId = (Guid?)null, ipiRate,
        }).ToArray(),
        freight, otherExpenses = 0m, additionalInformation = info ?? invoice.GetProperty("additionalInformation").GetString(), sendToCustomer = true, emailCc = (string?)null,
        version = invoice.GetProperty("version").GetUInt32(),
    };

    /// <summary>Rascunho criado e salvo com a alíquota do IPI da TIPI (a regra do seed é "pela TIPI").</summary>
    private async Task<(HttpClient Owner, Sale Sale, JsonElement Draft)> DraftAsync(string state = "SP")
    {
        var (_, owner) = await OwnerAsync();
        var sale = await SaleAsync(state);
        var created = await Ok(await owner.SendAsync(CreateInvoice(sale.Shipment)), HttpStatusCode.Created);
        var saved = await Ok(await owner.PutAsJsonAsync($"{Invoices}/{created.GetProperty("id").GetGuid()}", Edit(created)));
        return (owner, sale, saved);
    }

    private static Task<HttpResponseMessage> IssueInvoiceAsync(HttpClient client, JsonElement invoice) =>
        client.PostAsJsonAsync($"{Invoices}/{invoice.GetProperty("id").GetGuid()}/issue", new { version = invoice.GetProperty("version").GetUInt32() });

    // ---------- CA 1, 2 e 13 ----------

    [Fact]
    public async Task Rascunho_nasce_do_romaneio_preenchido_so_com_mercadoria_e_repete_pela_chave()
    {
        await ReadyToIssueAsync();
        var (_, owner) = await OwnerAsync();
        var sale = await SaleAsync();
        var key = Guid.NewGuid();

        var created = await Ok(await owner.SendAsync(CreateInvoice(sale.Shipment, key)), HttpStatusCode.Created);

        created.GetProperty("status").GetString().Should().Be("Draft");
        created.GetProperty("operation").GetString().Should().Be("OwnProductionSale", "equipamento é venda de produção própria, nunca remessa (D4)");
        created.GetProperty("shipmentId").GetGuid().Should().Be(sale.Shipment);
        var recipient = created.GetProperty("recipient");
        recipient.GetProperty("name").GetString().Should().Be(RecipientCanary);
        recipient.GetProperty("type").GetString().Should().Be("Contributor");
        recipient.GetProperty("cityCode").GetString().Should().Be("3550308");
        recipient.GetProperty("email").GetString().Should().Be(RecipientMailCanary);
        var item = created.GetProperty("items").EnumerateArray().Should().ContainSingle("a instalação (Service) fica para a NFS-e (CA 13)").Subject;
        item.GetProperty("unitPrice").GetDecimal().Should().Be(100_000m, "o valor vem do contrato");
        item.GetProperty("ncm").GetString().Should().StartWith("8428").And.HaveLength(8);
        item.GetProperty("additionalInfo").GetString().Should().Be("Séries: SN-0001");
        item.GetProperty("needsIpiRate").GetBoolean().Should().BeTrue();
        created.GetProperty("additionalInformation").GetString().Should().StartWith("Endereço da obra: Rua da Obra");
        created.GetProperty("volumes").GetInt32().Should().Be(2);
        created.GetProperty("calculationError").GetString().Should().Contain("IPI", "sem a alíquota da TIPI o cálculo espera");

        var replay = await Ok(await owner.SendAsync(CreateInvoice(sale.Shipment, key)), HttpStatusCode.Created);
        replay.GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid(), "o mesmo Idempotency-Key devolve a nota original (CA 19)");
        var other = await owner.SendAsync(CreateInvoice(sale.Shipment));
        other.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(other)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.InvoiceExists);
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.OutgoingInvoices.CountAsync(i => i.ShipmentId == sale.Shipment))).Should().Be(1);
    }

    [Fact]
    public async Task Faturar_mostra_o_romaneio_expedido_sem_nota_e_dois_rascunhos_juntos_dao_uma_nota()
    {
        await ReadyToIssueAsync();
        var (_, owner) = await OwnerAsync();
        var sale = await SaleAsync();

        var billing = await Ok(await owner.GetAsync($"{Invoices}/billing?projectId={sale.Project}"));
        billing.GetProperty("shipments").EnumerateArray().Should().ContainSingle(s => s.GetProperty("shipmentId").GetGuid() == sale.Shipment && s.GetProperty("canIssue").GetBoolean());
        billing.GetProperty("pendingItems").GetArrayLength().Should().Be(0, "o equipamento já saiu no romaneio");
        billing.GetProperty("serviceItems").GetInt32().Should().Be(1);

        var results = await Task.WhenAll(owner.SendAsync(CreateInvoice(sale.Shipment)), owner.SendAsync(CreateInvoice(sale.Shipment)));
        results.Select(r => r.StatusCode).Should().Contain(HttpStatusCode.Created).And.Contain(HttpStatusCode.Conflict, "índice único parcial de nota ativa por romaneio (CA 19)");
        billing = await Ok(await owner.GetAsync($"{Invoices}/billing?projectId={sale.Project}"));
        billing.GetProperty("shipments")[0].GetProperty("canIssue").GetBoolean().Should().BeFalse();
        billing.GetProperty("shipments")[0].GetProperty("invoiceLabel").GetString().Should().Be("Rascunho de nota");
    }

    // ---------- CA 3 e 4 ----------

    [Fact]
    public async Task Salvar_refaz_o_calculo_sem_imposto_digitado_e_saneia_pelos_limites_sem_chamar_o_Emissor()
    {
        await ReadyToIssueAsync();
        var fake = fixture.Fake(fixture.Tenancy.TenantA);
        var (owner, _, draft) = await DraftAsync();
        draft.GetProperty("calculationError").ValueKind.Should().Be(JsonValueKind.Null);
        var item = draft.GetProperty("items")[0];
        item.GetProperty("cfop").GetString().Should().Be("5101");
        var taxes = item.GetProperty("taxes").EnumerateArray().ToDictionary(t => t.GetProperty("code").GetString()!, t => t.GetProperty("amount").GetDecimal());
        taxes["IPI"].Should().Be(5_000m);
        taxes["ICMS"].Should().Be(18_000m, "contribuinte: o IPI fica fora da base do ICMS");
        taxes["PIS"].Should().Be(650m);
        taxes["COFINS"].Should().Be(3_000m);
        draft.GetProperty("totalAmount").GetDecimal().Should().Be(105_000m, "o IPI soma ao total da nota");
        draft.GetProperty("costTaxAmount").GetDecimal().Should().Be(18_000m + 650m + 3_000m + 900m + 100m, "ICMS, PIS, COFINS e IBS/CBS no custo; IPI fora (ponto 41)");
        draft.GetProperty("installments").ValueKind.Should().Be(JsonValueKind.Array);

        var withFreight = await Ok(await owner.PutAsJsonAsync($"{Invoices}/{draft.GetProperty("id").GetGuid()}", Edit(draft, freight: 1_000m, description: "Elevador\tde \u0007carga   2 paradas")));
        withFreight.GetProperty("items")[0].GetProperty("freight").GetDecimal().Should().Be(1_000m, "frete rateado pelo valor");
        withFreight.GetProperty("items")[0].GetProperty("description").GetString().Should().Be("Elevador de carga 2 paradas", "sem caracteres de controle e com espaços normalizados");
        withFreight.GetProperty("totalAmount").GetDecimal().Should().Be(106_050m, "o frete entra na base do IPI e no total");

        var previews = fake.Count("previa");
        var issues = fake.Count("emitir");
        var tooLong = await owner.PutAsJsonAsync($"{Invoices}/{draft.GetProperty("id").GetGuid()}", Edit(withFreight, description: new string('A', 121)));
        tooLong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(tooLong);
        problem.GetProperty("errors").GetProperty("items[0].description")[0].GetString().Should().Be("A descrição do item 1 passa de 120 caracteres.");
        var info = await owner.PutAsJsonAsync($"{Invoices}/{draft.GetProperty("id").GetGuid()}", Edit(withFreight, info: new string('B', 5001)));
        info.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (fake.Count("previa"), fake.Count("emitir")).Should().Be((previews, issues), "o saneamento nunca chama o Emissor (CA 4)");

        var pdf = await owner.GetAsync($"{Invoices}/{draft.GetProperty("id").GetGuid()}/preview");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        fake.Count("previa").Should().Be(previews + 1, "a prévia não consome número (CA 5)");
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.OutgoingInvoices.AsNoTracking().SingleAsync(i => i.Id == draft.GetProperty("id").GetGuid()))).Number.Should().BeNull();
    }

    // ---------- CA 6, 7, 12 e 14 ----------

    [Fact]
    public async Task Emitir_autoriza_guarda_XML_e_DANFE_publica_o_evento_envia_o_email_e_nao_mexe_no_estoque()
    {
        await ReadyToIssueAsync();
        var tenant = fixture.Tenancy.TenantA;
        var (owner, sale, draft) = await DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        var movements = await fixture.QueryAsync(tenant, db => db.Set<BemAqui.Inventory.Domain.StockMovement>().CountAsync());

        var check = await Ok(await owner.GetAsync($"{Invoices}/{id}/issue-check"));
        check.GetProperty("preconditions").EnumerateArray().Should().OnlyContain(p => p.GetProperty("ok").GetBoolean());
        check.GetProperty("environment").GetString().Should().Be("Homologation");
        var issued = await Ok(await IssueInvoiceAsync(owner, draft));

        issued.GetProperty("status").GetString().Should().Be("Authorized");
        issued.GetProperty("idempotencyKey").GetString().Should().Be($"nfe-{id:D}-1");
        var invoice = await fixture.InvoiceOfAsync(tenant, id);
        invoice.PayloadHash.Should().HaveLength(64);
        invoice.Number.Should().BePositive();
        invoice.IssuedOn.Should().NotBeNull();
        invoice.XmlAttachmentId.Should().NotBeNull();
        invoice.DanfeAttachmentId.Should().NotBeNull("DANFE em PDF do Emissor guardada (lacuna L-c)");
        invoice.EmailedAt.Should().NotBeNull();
        var request = fixture.Fake(tenant).Requests.Last();
        request.Items.Should().ContainSingle().Which.Cfop.Should().Be("5101");
        request.Payments.Should().ContainSingle().Which.Method.Should().Be(BemAqui.Fiscal.Application.Emitter.NfePaymentMethod.NoPayment, "sem duplicatas (D4)");
        request.AdditionalInformation.Should().Contain("Endereço da obra").And.Contain("Séries: SN-0001");

        var outbox = await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains(nameof(InvoiceAuthorized))).Select(m => m.Payload).ToListAsync());
        var published = outbox.Should().ContainSingle(p => p.Contains(id.ToString())).Subject;
        published.Should().NotContain(RecipientCanary).And.NotContain(RecipientMailCanary);
        JsonDocument.Parse(published).RootElement.EnumerateObject().Single(p => p.Name.Equals("ProjectTaxApplies", StringComparison.OrdinalIgnoreCase)).Value.GetBoolean().Should().BeFalse("nota de homologação nunca entra no custo (ADR 0025 adendo)");
        var mail = await fixture.QueryAsync(tenant, db => db.EmailLogs.AsNoTracking().SingleAsync(e => e.RelatedId == id));
        mail.Template.Should().Be(OutgoingInvoiceEffects.EmailTemplate);
        mail.To.Should().Be(RecipientMailCanary);
        mail.AttachmentIds.Should().BeEquivalentTo([invoice.XmlAttachmentId!.Value, invoice.DanfeAttachmentId!.Value]);
        (await fixture.QueryAsync(tenant, db => db.Set<BemAqui.Inventory.Domain.StockMovement>().CountAsync())).Should().Be(movements, "a nota só referencia o romaneio (D4)");

        var reverse = await owner.PostAsJsonAsync($"/api/v1/production/shipments/{sale.Shipment}/reverse",
            new { reason = "Teste", version = await fixture.QueryAsync(tenant, db => db.Set<Shipment>().Where(s => s.Id == sale.Shipment).Select(s => s.Version).SingleAsync()) });
        reverse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(reverse)).GetProperty("detail").GetString().Should().Be($"Cancele a NF-e {invoice.Number} antes de estornar a expedição.");

        var resent = await owner.PostAsync($"{Invoices}/{id}/resend", null);
        resent.StatusCode.Should().Be(HttpStatusCode.OK, await resent.Content.ReadAsStringAsync());
        (await fixture.QueryAsync(tenant, db => db.EmailLogs.CountAsync(e => e.RelatedId == id))).Should().Be(2, "\"Reenviar XML e DANFE\"");
        var shipment = await Ok(await owner.GetAsync($"/api/v1/production/shipments/{sale.Shipment}"));
        shipment.GetProperty("next").GetProperty("invoiceId").GetGuid().Should().Be(id, "o romaneio mostra a nota com link (CA 15)");
    }

    [Fact]
    public async Task Sem_resposta_na_hora_fica_processando_e_o_webhook_autoriza()
    {
        await ReadyToIssueAsync();
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        var (owner, _, draft) = await DraftAsync();
        fake.Next = FakeIssue.Delay;

        var issued = await Ok(await IssueInvoiceAsync(owner, draft), HttpStatusCode.Accepted);
        issued.GetProperty("status").GetString().Should().Be("Processing");
        issued.GetProperty("message").GetString().Should().Be(OutgoingInvoiceService.SentMessage);
        var invoice = await fixture.InvoiceOfAsync(tenant, draft.GetProperty("id").GetGuid());
        var doc = fake.Document(invoice.AccessKey!)!;
        (await IssueInvoiceAsync(owner, await Ok(await owner.GetAsync($"{Invoices}/{invoice.Id}")))).StatusCode.Should().Be(HttpStatusCode.Conflict, "processando nunca reemite (CA 10)");

        fake.Settle(invoice.AccessKey!, BemAqui.Fiscal.Domain.FiscalDocumentStatus.Authorized);
        var (_, delivery) = await fixture.PostWebhookAsync(tenant, fake.WebhookBody(doc, "nfe.autorizada"), "nfe.autorizada");
        await fixture.ProcessWebhookAsync(tenant, delivery);

        var after = await fixture.InvoiceOfAsync(tenant, invoice.Id);
        after.Status.Should().Be(OutgoingInvoiceStatus.Authorized);
        after.DanfeAttachmentId.Should().NotBeNull();
        after.AuthorizationPublishedAt.Should().NotBeNull();
        fake.Next = FakeIssue.Authorize;
    }

    // ---------- CA 8 e 9 ----------

    [Fact]
    public async Task Rejeitada_avisa_registra_o_numero_a_inutilizar_e_corrigida_emite_com_a_proxima_tentativa()
    {
        await ReadyToIssueAsync();
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        var (owner, _, draft) = await DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        fake.Next = FakeIssue.Reject;

        var rejected = await Ok(await IssueInvoiceAsync(owner, draft), HttpStatusCode.Accepted);
        rejected.GetProperty("status").GetString().Should().Be("Rejected");
        var view = rejected.GetProperty("invoice");
        view.GetProperty("rejectionCode").GetInt32().Should().Be(225);
        view.GetProperty("suggestedAction").GetString().Should().NotBeNullOrEmpty();
        var first = await fixture.InvoiceOfAsync(tenant, id);
        (await fixture.FiscalAsync(tenant, db => db.NumberGaps.AsNoTracking().SingleAsync(g => g.InvoiceId == id))).Should()
            .Match<NumberGap>(g => g.Number == first.Number && g.Reason == NumberGapReasons.Rejected && g.Attempt == 1);
        (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.Type == OutgoingInvoiceEffects.RejectedType && n.Link == "/app/fiscal/saida/" + id))).Should().BeTrue();

        var reopened = await Ok(await owner.PostAsJsonAsync($"{Invoices}/{id}/reopen", new { version = view.GetProperty("version").GetUInt32() }));
        reopened.GetProperty("status").GetString().Should().Be("Draft");
        reopened.GetProperty("attempt").GetInt32().Should().Be(2);
        reopened.GetProperty("items").GetArrayLength().Should().Be(1, "volta com os dados");
        fake.Next = FakeIssue.Authorize;
        var issued = await Ok(await IssueInvoiceAsync(owner, reopened));
        issued.GetProperty("idempotencyKey").GetString().Should().Be($"nfe-{id:D}-2");
        issued.GetProperty("status").GetString().Should().Be("Authorized");
    }

    [Fact]
    public async Task Denegada_fica_com_a_orientacao_e_nao_volta_para_o_rascunho()
    {
        await ReadyToIssueAsync();
        var fake = fixture.Fake(fixture.Tenancy.TenantA);
        var (owner, _, draft) = await DraftAsync();
        fake.Next = FakeIssue.Deny;

        var denied = await Ok(await IssueInvoiceAsync(owner, draft), HttpStatusCode.Accepted);
        fake.Next = FakeIssue.Authorize;
        denied.GetProperty("status").GetString().Should().Be("Denied");
        denied.GetProperty("invoice").GetProperty("guidance").GetString().Should().Be(OutgoingInvoice.DeniedGuidance);
        var reopen = await owner.PostAsJsonAsync($"{Invoices}/{draft.GetProperty("id").GetGuid()}/reopen", new { version = denied.GetProperty("invoice").GetProperty("version").GetUInt32() });
        reopen.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(reopen)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.InvoiceDenied);
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.NumberGaps.AnyAsync(g => g.InvoiceId == draft.GetProperty("id").GetGuid() && g.Reason == NumberGapReasons.Denied))).Should().BeTrue();
    }

    // ---------- CA 6 e 19 ----------

    [Fact]
    public async Task Pre_condicao_faltando_bloqueia_a_emissao_com_o_link_e_sem_chamar_o_Emissor()
    {
        await ReadyToIssueAsync(certificate: false);
        var fake = fixture.Fake(fixture.Tenancy.TenantA);
        var (owner, _, draft) = await DraftAsync();
        var before = fake.Count("emitir");

        var check = await Ok(await owner.GetAsync($"{Invoices}/{draft.GetProperty("id").GetGuid()}/issue-check"));
        var certificate = check.GetProperty("preconditions").EnumerateArray().Single(p => p.GetProperty("key").GetString() == "certificate");
        certificate.GetProperty("ok").GetBoolean().Should().BeFalse();
        certificate.GetProperty("link").GetString().Should().Be("/app/configuracoes/fiscal/certificado");
        var issue = await IssueInvoiceAsync(owner, draft);
        issue.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(issue)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.IssueBlocked);
        fake.Count("emitir").Should().Be(before);
    }

    [Fact]
    public async Task Duas_emissoes_simultaneas_do_mesmo_rascunho_so_uma_vai_a_SEFAZ()
    {
        await ReadyToIssueAsync();
        var fake = fixture.Fake(fixture.Tenancy.TenantA);
        var (owner, _, draft) = await DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        var before = fake.Calls.Count(c => c.Operation == "emitir" && c.IdempotencyKey == $"nfe-{id:D}-1");

        var results = await Task.WhenAll(IssueInvoiceAsync(owner, draft), IssueInvoiceAsync(owner, draft));

        results.Count(r => r.IsSuccessStatusCode).Should().Be(1, string.Join(" | ", await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()))));
        results.Should().Contain(r => r.StatusCode == HttpStatusCode.Conflict);
        fake.Calls.Count(c => c.Operation == "emitir" && c.IdempotencyKey == $"nfe-{id:D}-1").Should().Be(before + 1);
    }

    // ---------- CA 11 ----------

    [Fact]
    public async Task Imposto_da_nota_de_producao_entra_na_razao_do_projeto_uma_vez_so()
    {
        await ReadyToIssueAsync();
        var sale = await SaleAsync();
        var invoiceId = Guid.CreateVersion7();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var evt = new InvoiceAuthorized(invoiceId, InvoiceType.NFe, sale.Project, sale.Shipment, sale.Party, today, 105_000m, [new InvoiceTaxAmount("ICMS", 18_000m, true)], 22_650m, true);
        await using (var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA))
        {
            var consumer = ActivatorUtilities.CreateInstance<InvoiceTaxConsumer>(scope.ServiceProvider);
            await consumer.HandleAsync(evt, CancellationToken.None);
            await consumer.HandleAsync(evt, CancellationToken.None);
            await consumer.HandleAsync(evt with { InvoiceId = Guid.CreateVersion7(), ProjectTaxApplies = false }, CancellationToken.None);
        }

        var lines = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Set<ProjectCost>().AsNoTracking().Where(c => c.ProjectId == sale.Project).ToListAsync());
        var line = lines.Should().ContainSingle("idempotente por (InvoiceTax, nota, seq) e nota de homologação fora").Subject;
        (line.Source, line.CostType, line.SourceId, line.Seq, line.Amount, line.InvoiceAmount, line.OccurredOn)
            .Should().Be((ProjectCostSource.InvoiceTax, CostType.Tax, invoiceId, 1, 22_650m, (decimal?)105_000m, today));
    }

    // ---------- CA 13 ----------

    [Fact]
    public async Task IOutgoingInvoices_conta_so_a_autorizada_de_producao_pela_data_da_nota()
    {
        var tenant = fixture.Tenancy.TenantA;
        var project = Guid.CreateVersion7();
        var day = new DateOnly(2014, 3, 10);
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.OutgoingInvoices.AddRange(
                ProductionInvoice(project, day, 1_000m),
                ProductionInvoice(project, day.AddDays(40), 500m),
                ProductionInvoice(project, day, 777m, FiscalEnvironment.Homologation));
            await db.SaveChangesAsync();
        }

        await using var read = await fixture.ScopeAsync(tenant);
        var contract = read.ServiceProvider.GetRequiredService<IOutgoingInvoices>();
        (await contract.RevenueAsync(day, day.AddDays(30), [project])).Sum(r => r.Amount).Should().Be(1_000m, "pela data da nota e só de produção");
        var invoiced = (await contract.InvoicedByProjectAsync([project]))[project];
        (invoiced.EquipmentAmount, invoiced.ServiceAmount, invoiced.Count).Should().Be((1_500m, 0m, 2));
    }

    /// <summary>Nota autorizada direto no banco (para o contrato e a carga), com a data da nota.</summary>
    private static OutgoingInvoice ProductionInvoice(Guid project, DateOnly issuedOn, decimal total, FiscalEnvironment environment = FiscalEnvironment.Production)
    {
        var invoice = OutgoingInvoice.NewDraft(Guid.CreateVersion7(), "ROM-X", project, Guid.CreateVersion7(), Guid.CreateVersion7(), null, DateTimeOffset.UtcNow);
        invoice.Environment = environment;
        invoice.ApplyTotals(new InvoiceTotals(total, 0, 0, 0, [new TaxTotal("ICMS", total, total * 0.18m)], total, total * 0.18m), null);
        invoice.BeginSubmission(new string('a', 64), DateTimeOffset.UtcNow);
        var number = Random.Shared.NextInt64(1, long.MaxValue / 2);
        invoice.Apply(new EmitterNfeState(Guid.CreateVersion7(), FiscalDocumentStatus.Authorized, number.ToString("D44", System.Globalization.CultureInfo.InvariantCulture)[..44], 2, number,
            "1", issuedOn.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc), null, null, null), DateTimeOffset.UtcNow);
        invoice.MarkAuthorizationPublished(issuedOn, DateTimeOffset.UtcNow);
        // XML e DANFE "guardados": a conferência de pendentes dos outros testes da classe não pega estas notas.
        invoice.AttachAuthorizedXml(Guid.CreateVersion7(), new string('b', 64));
        invoice.AttachDanfe(Guid.CreateVersion7());
        return invoice;
    }

    // ---------- CA 16 ----------

    [Fact]
    public async Task Fila_A_emitir_e_pauta_trazem_o_romaneio_expedido_sem_nota()
    {
        await ReadyToIssueAsync();
        var (_, owner) = await OwnerAsync();
        var sale = await SaleAsync();

        var queue = await Ok(await owner.GetAsync($"{Invoices}/to-issue"));
        var row = queue.EnumerateArray().Single(r => r.GetProperty("shipmentId").GetGuid() == sale.Shipment);
        row.GetProperty("estimatedAmount").GetDecimal().Should().Be(100_000m);
        await using (var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA))
        {
            var agenda = scope.ServiceProvider.GetServices<IAgendaSource>().OfType<OutgoingInvoiceAgendaSource>().Single();
            var items = await agenda.ItemsAsync(new AgendaRequest(Guid.Empty, null!, new HashSet<string> { OutgoingInvoiceAgendaSource.ToIssue }, DateOnly.FromDateTime(DateTime.UtcNow)));
            items.Should().ContainSingle().Which.Action.Should().Be("Emitir NF-e");
        }

        await Ok(await owner.SendAsync(CreateInvoice(sale.Shipment)), HttpStatusCode.Created);
        queue = await Ok(await owner.GetAsync($"{Invoices}/to-issue"));
        queue.EnumerateArray().Should().NotContain(r => r.GetProperty("shipmentId").GetGuid() == sale.Shipment, "com nota ativa sai da fila");
    }

    // ---------- CA 18, 20 e 21 ----------

    [Fact]
    public async Task Item_e_arquivo_so_pela_propria_nota_descarte_e_logico_e_a_auditoria_mascara_o_destinatario()
    {
        await ReadyToIssueAsync();
        var tenant = fixture.Tenancy.TenantA;
        var (owner, _, first) = await DraftAsync();
        var (_, _, second) = await DraftAsync();
        var otherItem = second.GetProperty("items")[0].GetProperty("id").GetGuid();

        (await owner.GetAsync($"{Invoices}/{first.GetProperty("id").GetGuid()}/items/{otherItem}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR");
        (await owner.GetAsync($"{Invoices}/{second.GetProperty("id").GetGuid()}/items/{otherItem}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"{Invoices}/{first.GetProperty("id").GetGuid()}/xml")).StatusCode.Should().Be(HttpStatusCode.NotFound, "rascunho sem XML");
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view");
        (await viewer.PostAsJsonAsync($"{Invoices}/{first.GetProperty("id").GetGuid()}/discard", new { version = first.GetProperty("version").GetUInt32() })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "descartar exige fiscal.edit");

        var discarded = await Ok(await owner.PostAsJsonAsync($"{Invoices}/{first.GetProperty("id").GetGuid()}/discard", new { version = first.GetProperty("version").GetUInt32() }));
        discarded.GetProperty("discardedAt").ValueKind.Should().Be(JsonValueKind.String);
        (await fixture.FiscalAsync(tenant, db => db.OutgoingInvoices.AnyAsync(i => i.Id == first.GetProperty("id").GetGuid()))).Should().BeTrue("nunca é apagada (CA 20)");

        var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(OutgoingInvoice) && a.EntityId == second.GetProperty("id").GetGuid().ToString())
            .Select(a => a.Changes).ToListAsync());
        audit.Should().NotBeEmpty();
        string.Join("\n", audit).Should().NotContain(RecipientCanary).And.NotContain(RecipientMailCanary).And.Contain("recipientName");
        var history = await Ok(await owner.GetAsync($"{Invoices}/{second.GetProperty("id").GetGuid()}/history"));
        history.GetProperty("items").GetArrayLength().Should().BePositive();
    }

    // ---------- CA 17 (carga) ----------

    [Fact]
    [Trait(LoadTest.Trait, LoadTest.Value)]
    public async Task Lista_com_50_mil_notas_responde_em_menos_de_1_segundo()
    {
        var tenant = fixture.Tenancy.TenantA;
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            for (var batch = 0; batch < 10; batch++)
            {
                for (var i = 0; i < 5_000; i++)
                {
                    db.OutgoingInvoices.Add(ProductionInvoice(Guid.CreateVersion7(), new DateOnly(2013, 1, 1).AddDays(i % 365), 1_000m + i));
                }

                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
        }

        var (_, owner) = await OwnerAsync();
        await owner.GetAsync($"{Invoices}?pageSize=50");
        var watch = Stopwatch.StartNew();
        var page = await Ok(await owner.GetAsync($"{Invoices}?from=2013-01-01&to=2013-12-31&status=Authorized&pageSize=50"));
        watch.Stop();
        page.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(50_000);
        watch.ElapsedMilliseconds.Should().BeLessThan(1_000);
    }
}
