using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Application.Invoices.Nfse;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-06 com o <see cref="FakeFiscalEmitter"/> e banco real: a instalação aceita na fila e na pauta e o rascunho preenchido pela regra de
/// prestação de serviço (CA 1 e 2); a emissão pelo Emissor com a NFS-e do município habilitada, a autorização pela conferência, XML, PDF e
/// e-mail (CA 3 e 4); o processo provisório pelo portal, o registro com anexo, o aviso do ISS e o número repetido (CA 5 e 6); a razão e o
/// <c>IOutgoingInvoices</c> com a nota de produção (CA 8); o cancelamento registrado com 2FA e o reverso (CA 9); IDOR e ação própria (CA 10);
/// nenhuma credencial da prefeitura na API (CA 11); nunca apagar e a concorrência (CA 12). O fake e o tenant são da classe: contagens
/// relativas ao início de cada teste.
/// </summary>
public partial class FiscalTests
{
    private const string Nfse = "/api/v1/fiscal/outgoing-invoices";

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record Installation(Guid Order, Guid Project, Guid Party, Guid ServiceItem);

    /// <summary>
    /// Venda mista com a instalação aceita, gravada direto nas tabelas dos donos: cliente PJ com endereço de cobrança e o da obra (Caçapava,
    /// IBGE 3508504), orçamento com o equipamento e a instalação (serviço de R$ 10.000), contrato, projeto com a obra e a OS de instalação
    /// concluída; e o <c>InstallationAccepted</c> processado pelo consumidor do Fiscal.
    /// </summary>
    private async Task<Installation> AcceptedInstallationAsync(decimal service = 10_000m)
    {
        var tenant = fixture.Tenancy.TenantA;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Installation result;
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var now = DateTimeOffset.UtcNow;
            var party = new Party { Kind = PartyKind.PJ, Document = "4" + Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                LegalName = RecipientCanary, StateRegistration = "110042490114", Email = RecipientMailCanary, CreatedAt = now };
            party.Roles.Add(new PartyRole { PartyId = party.Id, Role = PartyRoleKind.Customer });
            party.Addresses.Add(new PartyAddress { PartyId = party.Id, Kind = AddressKind.Billing, ZipCode = "01001000", Street = "Rua do Cliente", Number = "200", District = "Centro",
                City = "São Paulo", IbgeCode = "3550308", State = "SP", IsDefault = true });
            var site = new PartyAddress { PartyId = party.Id, Kind = AddressKind.Site, ZipCode = "12280000", Street = "Rua da Obra", Number = "100", District = "Centro", City = "Caçapava",
                IbgeCode = "3508504", State = "SP" };
            party.Addresses.Add(site);
            db.Parties.Add(party);
            var quote = new Quote { Number = "ORC-N-" + Guid.NewGuid().ToString("N")[..8], PartyId = party.Id, Status = QuoteStatus.Approved, TotalPrice = 100_000m + service, CreatedAt = now };
            var equipment = new QuoteItem { QuoteId = quote.Id, Sequence = 1, Kind = QuoteItemKind.Equipment, Description = "Elevador de carga", Quantity = 1, UnitPrice = 100_000m };
            var install = new QuoteItem { QuoteId = quote.Id, Sequence = 2, Kind = QuoteItemKind.Service, Description = "Instalação\tdo elevador", Quantity = 1, UnitPrice = service };
            var contract = new Contract { Number = "CTR-N-" + Guid.NewGuid().ToString("N")[..8], QuoteId = quote.Id, PartyId = party.Id, SignedAt = today, TotalValue = 100_000m + service,
                RevenueAccountId = Guid.CreateVersion7(), CreatedAt = now };
            var project = new Project { Number = "PRJ-N-" + Guid.NewGuid().ToString("N")[..8], ContractId = contract.Id, PartyId = party.Id, Name = "Obra NFS-e", Status = ProjectStatus.Delivered,
                SiteAddressId = site.Id, CreatedAt = now };
            var equipmentItem = new ProjectItem { ProjectId = project.Id, QuoteItemId = equipment.Id, Sequence = 1, Description = "Elevador de carga", Quantity = 1 };
            var serviceItem = new ProjectItem { ProjectId = project.Id, QuoteItemId = install.Id, Sequence = 2, Description = "Instalação\tdo elevador", Quantity = 1 };
            var order = new ProductionOrder
            {
                Number = "OSI-" + Guid.NewGuid().ToString("N")[..6], Type = ProductionOrderType.Installation, ProjectId = project.Id, ContractId = contract.Id, InstallerId = Guid.CreateVersion7(),
                ContractValue = 1_500m, PlannedStart = today.AddDays(-10), PlannedEnd = today.AddDays(-1), Status = ProductionOrderStatus.Completed, StartedOn = today.AddDays(-5),
                AcceptedOn = today, AcceptedByName = "Síndico", CreatedAt = now,
            };
            db.AddRange(quote, equipment, install, contract, project, equipmentItem, serviceItem, order, new ProductionOrderProjectItem { OrderId = order.Id, ProjectItemId = equipmentItem.Id });
            await db.SaveChangesAsync();
            result = new Installation(order.Id, project.Id, party.Id, serviceItem.Id);
        }

        await AcceptAsync(result, today);
        return result;
    }

    /// <summary>O consumidor do <c>InstallationAccepted</c> (como a inbox entregaria).</summary>
    private async Task AcceptAsync(Installation installation, DateOnly on)
    {
        await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
        var consumer = ActivatorUtilities.CreateInstance<InstallationBillingConsumer>(scope.ServiceProvider);
        await consumer.HandleAsync(new InstallationAccepted(installation.Order, installation.Project, on), CancellationToken.None);
    }

    /// <summary>Os códigos da NFS-e de Configurações → Fiscal (FIS-02a), como a contadora passaria; opcionalmente em produção.</summary>
    private async Task NfseSettingsAsync(bool production = false)
    {
        await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<IFiscalDb>();
        var settings = await db.FiscalSettings.SingleAsync();
        settings.NfseMunicipalCode = "7.02.01";
        settings.NfseCtn = "070201000";
        settings.NfseNbs = "120011000";
        if (production)
        {
            settings.Environment = FiscalEnvironment.Production; // direto no banco: a troca pela tela (2FA e catálogo) é da FIS-02a
            foreach (var rule in await db.TaxRules.Where(r => r.Status == TaxRuleStatus.Active).ToListAsync())
            {
                rule.Confirm(Guid.CreateVersion7(), DateTimeOffset.UtcNow); // em produção, só regra conferida (FIS-02b CA 4)
            }
        }

        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage CreateNfse(Guid order, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Nfse + "/nfse") { Content = JsonContent.Create(new { installationOrderId = order }) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private static MultipartFormDataContent RegisterForm(JsonElement nfse, long number, decimal amount, decimal iss, bool file = true, string? date = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(number.ToString(System.Globalization.CultureInfo.InvariantCulture)), "number" },
            { new StringContent("AB-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)), "verificationCode" },
            { new StringContent(date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "issuedOn" },
            { new StringContent(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "servicesAmount" },
            { new StringContent(iss.ToString(System.Globalization.CultureInfo.InvariantCulture)), "issAmount" },
            { new StringContent(nfse.GetProperty("version").GetUInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)), "version" },
        };
        if (file)
        {
            var pdf = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n% NFS-e da prefeitura\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"));
            pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(pdf, "file", "nfse-prefeitura.pdf");
        }

        return form;
    }

    /// <summary>Os eventos do outbox de um tipo (o filtro pelo id da nota é em memória: o payload é jsonb).</summary>
    private Task<List<string>> OutboxAsync(string type) =>
        fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains(type)).Select(m => m.Payload).ToListAsync());

    private static long NewNumber() => Random.Shared.NextInt64(100_000, 999_999_999);

    private async Task<(HttpClient Owner, Installation Installation, JsonElement Draft)> NfseDraftAsync()
    {
        var (_, owner) = await OwnerAsync();
        var installation = await AcceptedInstallationAsync();
        var draft = await Ok(await owner.SendAsync(CreateNfse(installation.Order)), HttpStatusCode.Created);
        return (owner, installation, draft);
    }

    // ---------- CA 1 e 2 ----------

    [Fact]
    public async Task Aceite_tecnico_poe_a_instalacao_na_fila_e_na_pauta_e_o_rascunho_nasce_pela_regra_de_servico()
    {
        await ReadyToIssueAsync();
        await NfseSettingsAsync();
        var (_, owner) = await OwnerAsync();
        var installation = await AcceptedInstallationAsync();
        await AcceptAsync(installation, DateOnly.FromDateTime(DateTime.UtcNow)); // a inbox entregou de novo: nada muda
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.InstallationBillings.CountAsync(b => b.InstallationOrderId == installation.Order))).Should().Be(1);

        var queue = await Ok(await owner.GetAsync($"{Nfse}/installations-to-invoice"));
        queue.EnumerateArray().Single(r => r.GetProperty("installationOrderId").GetGuid() == installation.Order).GetProperty("estimatedAmount").GetDecimal().Should().Be(10_000m);
        await using (var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA))
        {
            var agenda = scope.ServiceProvider.GetServices<IAgendaSource>().OfType<NfseAgendaSource>().Single();
            var items = await agenda.ItemsAsync(new AgendaRequest(Guid.Empty, null!, new HashSet<string> { NfseAgendaSource.ToIssue }, DateOnly.FromDateTime(DateTime.UtcNow)));
            items.Should().ContainSingle().Which.Action.Should().Be("Emitir NFS-e");
        }

        var key = Guid.NewGuid();
        var draft = await Ok(await owner.SendAsync(CreateNfse(installation.Order, key)), HttpStatusCode.Created);

        draft.GetProperty("status").GetString().Should().Be("Draft");
        draft.GetProperty("mode").GetString().Should().Be("Portal", "o Emissor ainda não emite a NFS-e de Caçapava (lacuna L-m)");
        draft.GetProperty("recipient").GetProperty("name").GetString().Should().Be(RecipientCanary);
        var service = draft.GetProperty("service");
        service.GetProperty("serviceItem").GetString().Should().Be("7.02");
        service.GetProperty("municipalCode").GetString().Should().Be("7.02.01");
        service.GetProperty("serviceCityCode").GetString().Should().Be("3508504", "ISS no município da obra (item 7.02)");
        service.GetProperty("issAmount").GetDecimal().Should().Be(500m, "5% de R$ 10.000 pela regra de prestação de serviço");
        service.GetProperty("description").GetString().Should().StartWith("Instalação de Elevador de carga, projeto PRJ-N-").And.Contain(", OS OSI-");
        var item = draft.GetProperty("items").EnumerateArray().Should().ContainSingle("só o item Service do contrato").Subject;
        item.GetProperty("projectItemId").GetGuid().Should().Be(installation.ServiceItem);
        item.GetProperty("description").GetString().Should().Be("Instalação do elevador", "saneada como a FIS-03a CA 4");
        draft.GetProperty("totalAmount").GetDecimal().Should().Be(10_000m);
        draft.GetProperty("costTaxAmount").GetDecimal().Should().Be(500m + 65m + 300m + 90m + 10m, "ISS, PIS, COFINS e IBS/CBS no custo do projeto (ADR 0025 adendo)");
        draft.GetProperty("portalCopy").EnumerateArray().Select(f => f.GetProperty("key").GetString()).Should().Contain(["taker", "description", "municipalCode", "amount", "issAmount"]);

        var replay = await Ok(await owner.SendAsync(CreateNfse(installation.Order, key)), HttpStatusCode.Created);
        replay.GetProperty("id").GetGuid().Should().Be(draft.GetProperty("id").GetGuid(), "o mesmo Idempotency-Key devolve o rascunho original");
        var again = await owner.SendAsync(CreateNfse(installation.Order));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(again)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.InvoiceExists);
        queue = await Ok(await owner.GetAsync($"{Nfse}/installations-to-invoice"));
        queue.EnumerateArray().Should().NotContain(r => r.GetProperty("installationOrderId").GetGuid() == installation.Order, "com NFS-e ativa sai da fila");
    }

    // ---------- CA 3 e 4 ----------

    [Fact]
    public async Task Com_a_NFSe_habilitada_emite_pelo_Emissor_com_a_chave_nfse_e_a_conferencia_autoriza_guarda_os_arquivos_e_envia()
    {
        await ReadyToIssueAsync();
        await NfseSettingsAsync();
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        fake.Scopes = ["consultar", "nfe:emitir", "nfe:eventos", "nfse:emitir"];
        fake.NfseCities = ["3508504"];
        var (owner, _, draft) = await NfseDraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        draft.GetProperty("mode").GetString().Should().Be("Emitter");
        draft.GetProperty("actions").GetProperty("canIssue").GetBoolean().Should().BeTrue();
        draft.GetProperty("actions").GetProperty("canRegister").GetBoolean().Should().BeFalse("com o Emissor emitindo, nada de registro manual");

        var check = await Ok(await owner.GetAsync($"{Nfse}/{id}/nfse/issue-check"));
        check.GetProperty("preconditions").EnumerateArray().Should().OnlyContain(p => p.GetProperty("ok").GetBoolean(), check.ToString());
        fake.Next = FakeIssue.Delay;
        var issued = await Ok(await owner.PostAsJsonAsync($"{Nfse}/{id}/nfse/issue", new { version = draft.GetProperty("version").GetUInt32() }), HttpStatusCode.Accepted);
        fake.Next = FakeIssue.Authorize;

        issued.GetProperty("idempotencyKey").GetString().Should().Be($"nfse-{id:D}-1");
        issued.GetProperty("status").GetString().Should().Be("Processing");
        issued.GetProperty("message").GetString().Should().Be(NfseInvoiceService.SentMessage);
        var request = fake.NfseRequests.Last();
        (request.Service.ServiceCityCode, request.Service.IssAmount, request.Service.ServicesAmount, request.Service.MunicipalTaxCode).Should().Be(("3508504", 500m, 10_000m, "7.02.01"));
        request.Service.IbsCbs.Should().NotBeNull("Reg60_RTC obrigatório no layout 4.00 de Caçapava");
        (await fixture.InvoiceOfAsync(tenant, id)).PayloadHash.Should().HaveLength(64);

        await fixture.AgeAsync(tenant, id, TimeSpan.FromMinutes(10));
        await fixture.CheckPendingAsync(tenant);

        var invoice = await fixture.InvoiceOfAsync(tenant, id);
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Authorized, "sem webhook de NFS-e, a consulta forçada da conferência resolve (CA 4)");
        invoice.Number.Should().BePositive();
        invoice.VerificationCode.Should().StartWith("VER");
        invoice.Series.Should().BeNull();
        (invoice.XmlAttachmentId, invoice.DanfeAttachmentId).Should().Match<(Guid?, Guid?)>(f => f.Item1 != null && f.Item2 != null, "XML e PDF da prefeitura guardados");
        invoice.EmailedAt.Should().NotBeNull();
        var mail = await fixture.QueryAsync(tenant, db => db.EmailLogs.AsNoTracking().SingleAsync(e => e.RelatedId == id));
        mail.Template.Should().Be(OutgoingInvoiceEffects.NfseEmailTemplate);
        var outbox = await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains(nameof(InvoiceAuthorized))).Select(m => m.Payload).ToListAsync());
        var published = outbox.Should().ContainSingle(p => p.Contains(id.ToString())).Subject;
        published.Should().NotContain(RecipientCanary);
        JsonDocument.Parse(published).RootElement.EnumerateObject().Single(p => p.Name.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value.ToString().Should().BeOneOf("NFSe", "1");

        await fixture.CheckPendingAsync(tenant);
        (await OutboxAsync(nameof(InvoiceAuthorized))).Count(p => p.Contains(id.ToString(), StringComparison.Ordinal))
            .Should().Be(1, "o InvoiceAuthorized vai uma vez só");
    }

    // ---------- CA 5, 6, 10 e 12 ----------

    [Fact]
    public async Task Sem_a_NFSe_habilitada_registra_a_nota_da_prefeitura_com_anexo_aviso_do_ISS_e_numero_unico()
    {
        await ReadyToIssueAsync();
        await NfseSettingsAsync();
        var tenant = fixture.Tenancy.TenantA;
        var (owner, installation, draft) = await NfseDraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        draft.GetProperty("portalNotice").GetString().Should().Be(NfseRules.PortalNotice);
        draft.GetProperty("portalUrl").GetString().Should().Be(NfseRules.PortalUrl);

        var issue = await owner.PostAsJsonAsync($"{Nfse}/{id}/nfse/issue", new { version = draft.GetProperty("version").GetUInt32() });
        issue.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(issue)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.NfseEmitterUnavailable);
        (await owner.PostAsync($"{Nfse}/{id}/nfse/register", RegisterForm(draft, NewNumber(), 10_000m, 500m, file: false))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "o PDF ou o XML da nota é obrigatório");
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view");
        (await viewer.PostAsync($"{Nfse}/{id}/nfse/register", RegisterForm(draft, NewNumber(), 10_000m, 500m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetAsync($"{Invoices}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a NFS-e não abre pela rota da NF-e (IDOR)");

        var number = NewNumber();
        var registered = await Ok(await owner.PostAsync($"{Nfse}/{id}/nfse/register", RegisterForm(draft, number, 10_000m, 480m)));

        registered.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().ContainSingle()
            .Which.Should().Be("O ISS da nota é diferente do calculado (R$ 500,00). Confira com a contadora.");
        var view = registered.GetProperty("invoice");
        (view.GetProperty("status").GetString(), view.GetProperty("origin").GetString(), view.GetProperty("number").GetInt64()).Should().Be(("Authorized", "PortalRegistered", number));
        view.GetProperty("issTyped").GetDecimal().Should().Be(480m);
        view.GetProperty("issCalculated").GetDecimal().Should().Be(500m);
        view.GetProperty("costTaxAmount").GetDecimal().Should().Be(480m + 65m + 300m + 90m + 10m, "o ISS da nota da prefeitura vale para o custo do projeto");
        var invoice = await fixture.InvoiceOfAsync(tenant, id);
        invoice.DanfeAttachmentId.Should().NotBeNull("o PDF anexado fica como o arquivo da nota");
        invoice.IssuedOn.Should().NotBeNull();
        var outbox = await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains(nameof(InvoiceAuthorized))).Select(m => m.Payload).ToListAsync());
        var published = JsonDocument.Parse(outbox.Single(p => p.Contains(id.ToString()))).RootElement;
        published.EnumerateObject().Single(p => p.Name.Equals("Taxes", StringComparison.OrdinalIgnoreCase)).Value.EnumerateArray()
            .Single(t => t.EnumerateObject().Single(p => p.Name.Equals("Tax", StringComparison.OrdinalIgnoreCase)).Value.GetString() == "ISS")
            .EnumerateObject().Single(p => p.Name.Equals("Amount", StringComparison.OrdinalIgnoreCase)).Value.GetDecimal().Should().Be(480m);
        (await owner.GetAsync($"{Invoices}/{id}/danfe")).StatusCode.Should().Be(HttpStatusCode.OK, "o arquivo só pelo endpoint da nota");

        // LGPD: a NFS-e sai no arquivo do titular e, emitida, fica retida como documento fiscal.
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var provider = scope.ServiceProvider.GetServices<BemAqui.BuildingBlocks.Privacy.IPersonalDataProvider>().OfType<FiscalPersonalDataProvider>().Single();
            var subject = new BemAqui.BuildingBlocks.Privacy.PersonalDataSubject(BemAqui.SharedKernel.PersonalDataSubjectKind.Party, installation.Party, RecipientCanary, [],
                PartyId: installation.Party);
            var section = await provider.ExportAsync(new BemAqui.BuildingBlocks.Privacy.PersonalDataExportContext(subject, "LGPD-T", new HashSet<string>()));
            JsonSerializer.Serialize(section, Relaxed).Should().Contain("AB-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)).And.Contain("Registrada (portal da prefeitura)");
            var plan = await provider.PlanAnonymizationAsync(new BemAqui.BuildingBlocks.Privacy.PersonalDataAnonymizationContext(subject, "LGPD-T", DateOnly.FromDateTime(DateTime.UtcNow),
                BemAqui.BuildingBlocks.Privacy.PersonalDataRetention.Defaults, new Dictionary<string, DateOnly>()));
            JsonSerializer.Serialize(plan, Relaxed).Should().Contain("Notas fiscais de saída emitidas");
        }

        // Número repetido no tenant: 409 (CA 6); duas gravações da mesma instalação: só uma (CA 12).
        var (_, other, otherDraft) = await NfseDraftAsync();
        var repeated = await owner.PostAsync($"{Nfse}/{otherDraft.GetProperty("id").GetGuid()}/nfse/register", RegisterForm(otherDraft, number, 10_000m, 500m));
        repeated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(repeated)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.NfseNumberExists);
        var both = await Task.WhenAll(
            owner.PostAsync($"{Nfse}/{otherDraft.GetProperty("id").GetGuid()}/nfse/register", RegisterForm(otherDraft, NewNumber(), 10_000m, 500m)),
            owner.PostAsync($"{Nfse}/{otherDraft.GetProperty("id").GetGuid()}/nfse/register", RegisterForm(otherDraft, NewNumber(), 10_000m, 500m)));
        both.Count(r => r.IsSuccessStatusCode).Should().Be(1, string.Join(" | ", await Task.WhenAll(both.Select(r => r.Content.ReadAsStringAsync()))));
        (await fixture.FiscalAsync(tenant, db => db.OutgoingInvoices.CountAsync(i => i.InstallationOrderId == other.Order && i.Status == OutgoingInvoiceStatus.Authorized))).Should().Be(1);
        (await owner.DeleteAsync($"{Nfse}/{id}")).StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await fixture.FiscalAsync(tenant, db => db.OutgoingInvoices.AnyAsync(i => i.Id == id))).Should().BeTrue("nota com número nunca é apagada");
        _ = installation;
    }

    // ---------- CA 7, 8 e 9 ----------

    [Fact]
    public async Task Nota_de_producao_entra_na_razao_e_no_faturamento_e_o_cancelamento_registrado_com_2FA_lanca_o_reverso()
    {
        await ReadyToIssueAsync();
        await NfseSettingsAsync(production: true);
        var tenant = fixture.Tenancy.TenantA;
        var (owner, installation, draft) = await NfseDraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        var registered = await Ok(await owner.PostAsync($"{Nfse}/{id}/nfse/register", RegisterForm(draft, NewNumber(), 10_000m, 500m)));
        registered.GetProperty("warnings").GetArrayLength().Should().Be(0);

        var authorized = JsonSerializer.Deserialize<InvoiceAuthorized>((await OutboxAsync(nameof(InvoiceAuthorized))).Single(p => p.Contains(id.ToString(), StringComparison.Ordinal)), Web)!;
        authorized.Type.Should().Be(InvoiceType.NFSe);
        authorized.ProjectTaxApplies.Should().BeTrue("nota de produção a partir da data de corte (ADR 0025 adendo)");
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var consumer = ActivatorUtilities.CreateInstance<InvoiceTaxConsumer>(scope.ServiceProvider);
            await consumer.HandleAsync(authorized, CancellationToken.None);
            await consumer.HandleAsync(authorized, CancellationToken.None);
        }

        var ledger = await fixture.QueryAsync(tenant, db => db.Set<ProjectCost>().AsNoTracking().Where(c => c.ProjectId == installation.Project).ToListAsync());
        ledger.Should().ContainSingle("idempotente (CA 8)").Which.Should().Match<ProjectCost>(c => c.Source == ProjectCostSource.InvoiceTax && c.Amount == 965m && c.InvoiceAmount == 10_000m);
        await using (var read = await fixture.ScopeAsync(tenant))
        {
            var invoices = read.ServiceProvider.GetRequiredService<IOutgoingInvoices>();
            (await invoices.InvoicedByProjectAsync([installation.Project]))[installation.Project].ServiceAmount.Should().Be(10_000m, "o \"a faturar\" conta a NFS-e");
            (await invoices.RevenueAsync(authorized.IssuedOn, authorized.IssuedOn, [installation.Project])).Should().ContainSingle(r => r.Type == InvoiceType.NFSe && r.Amount == 10_000m);
        }

        // CA 9: só fiscal.approve, com 2FA de novo; a nota fica cancelada e o reverso entra na razão.
        var (_, creator) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view", "fiscal.create");
        var version = (await Ok(await owner.GetAsync($"{Nfse}/{id}/nfse"))).GetProperty("version").GetUInt32();
        var body = new { reason = "Valor do serviço digitado errado na prefeitura", protocol = "CANC-123", code = (string?)null, version };
        (await creator.PostAsJsonAsync($"{Nfse}/{id}/nfse/register-cancellation", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (_, approver, totp) = await fixture.OwnerWithTotpAsync(tenant, FiscalFixture.HostA);
        var noCode = await approver.PostAsJsonAsync($"{Nfse}/{id}/nfse/register-cancellation", body);
        noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(noCode)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.TwoFactorRequired);

        var cancelled = await Ok(await approver.PostAsJsonAsync($"{Nfse}/{id}/nfse/register-cancellation", body with { code = await totp() }));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        cancelled.GetProperty("cancellation").GetProperty("protocol").GetString().Should().Be("CANC-123");
        var cancelledEvent = JsonSerializer.Deserialize<InvoiceCancelled>((await OutboxAsync(nameof(InvoiceCancelled))).Single(p => p.Contains(id.ToString(), StringComparison.Ordinal)), Web)!;
        cancelledEvent.ProjectTaxApplies.Should().BeTrue();
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            await ActivatorUtilities.CreateInstance<InvoiceTaxReversalConsumer>(scope.ServiceProvider).HandleAsync(cancelledEvent, CancellationToken.None);
        }

        ledger = await fixture.QueryAsync(tenant, db => db.Set<ProjectCost>().AsNoTracking().Where(c => c.ProjectId == installation.Project).ToListAsync());
        ledger.Sum(c => c.Amount).Should().Be(0m, "o reverso fecha o imposto da nota cancelada");
        ledger.Should().Contain(c => c.Source == ProjectCostSource.InvoiceTax, "a linha original nunca é apagada");
        await using (var read = await fixture.ScopeAsync(tenant))
        {
            (await read.ServiceProvider.GetRequiredService<IOutgoingInvoices>().InvoicedByProjectAsync([installation.Project])).Should().NotContainKey(installation.Project,
                "a cancelada deixa de contar no faturamento");
        }

        var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(OutgoingInvoice) && a.EntityId == id.ToString()).Select(a => a.Changes)
            .ToListAsync());
        string.Join("\n", audit).Should().Contain("portalCancellation").And.Contain("origin").And.NotContain(RecipientCanary);
    }

    // ---------- CA 11 ----------

    [Fact]
    public async Task Nenhuma_credencial_da_prefeitura_passa_pela_API_do_BemAqui()
    {
        // O OpenAPI versionado (o OpenApiDocumentTests garante que é o do Host): nenhum campo de credencial da prefeitura.
        var document = (await File.ReadAllTextAsync(Path.Combine(OpenApiDocumentTests.RepositoryRoot(), "src", "Web", "openapi.json"))).ToUpperInvariant();
        document.Should().Contain("NFSEVIEW", "o contrato da NFS-e está no OpenAPI");
        foreach (var field in new[] { "CODIGOUSUARIO", "CODIGOCONTRIBUINTE", "SENHAWEB", "CITYHALLPASSWORD", "MUNICIPALUSER", "TAXPAYERCODE", "CREDENCIAIS-NFSE" })
        {
            document.Should().NotContain(field, "as credenciais da prefeitura ficam só no Emissor Fiscal (ADR 0027)");
        }
    }
}
