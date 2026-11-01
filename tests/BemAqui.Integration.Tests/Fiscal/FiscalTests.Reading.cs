using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Fiscal.Application.Incoming;
using BemAqui.Fiscal.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-05 (Testcontainers, com o worker de verdade num socket Unix): PDF lido no worker e conferido campo a campo, seguindo a FIS-04a, com
/// o PDF por registro, a marca e o histórico (CA 1, 2, 4, 5, 7, 8, 11 e 13); PDF malicioso, grande, sem texto e com páginas demais (CA 1 a 3);
/// leitura presa morta no tempo limite (CA 2); chave já importada pelo XML (CA 5); "Digitar a nota" (CA 9); pedido do XML (CA 6);
/// "Substituir pelo XML" com diferença (CA 10); permissão, IDOR e rate limit (CA 1 e 11); log só com a chave mascarada (CA 12).
/// </summary>
public partial class FiscalTests
{
    private static async Task<HttpResponseMessage> UploadPdfAsync(HttpClient client, byte[] content, string fileName = "danfe.pdf")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        return await client.PostAsync(Incoming + "/pdf", form);
    }

    /// <summary>PDF enviado e lido pelo worker (o job como o Hangfire faria): a leitura pronta.</summary>
    private async Task<IncomingReadingView> ReadPdfAsync(HttpClient client, byte[] pdf)
    {
        var sent = await OkAsync<IncomingReadingView>(await UploadPdfAsync(client, pdf));
        sent.Status.Should().Be(IncomingReadingStatus.Reading);
        await fixture.RunPdfReadingAsync(fixture.Tenancy.TenantA, sent.Id);
        return await OkAsync<IncomingReadingView>(await client.GetAsync($"{Incoming}/{sent.Id}/pdf-reading"));
    }

    /// <summary>A conferência com os valores lidos (e as correções dadas) e todos os campos marcados como conferidos.</summary>
    private static CheckReadingRequest Confirmed(IncomingReadingView reading, Func<string, string?, string?>? fix = null, IReadOnlyList<string>? skip = null)
    {
        var values = reading.Fields.ToDictionary(f => f.Key, f => fix is null ? f.Value : fix(f.Key, f.Value));
        string? V(string key) => values.GetValueOrDefault(key);
        decimal? D(string key) => V(key) is { } v ? decimal.Parse(v, CultureInfo.InvariantCulture) : 0m;
        var count = reading.Fields.Count(f => f.Key.EndsWith(".code", StringComparison.Ordinal));
        var items = Enumerable.Range(0, count).Select(i => new ReadingItemInput(V($"items.{i}.code"), V($"items.{i}.description"), V($"items.{i}.ncm"), V($"items.{i}.unit"),
            D($"items.{i}.quantity"), D($"items.{i}.unitPrice"), D($"items.{i}.amount"))).ToList();
        return new CheckReadingRequest(V("accessKey"), V("issuedAt") is { } d ? DateOnly.Parse(d, CultureInfo.InvariantCulture) : null, V("issuerName"), V("recipientDocument"),
            new ReadingTotalsInput(D("totals.products"), D("totals.freight"), D("totals.insurance"), D("totals.discount"), D("totals.otherExpenses"), D("totals.ipi"), D("totals.icmsBase"),
                D("totals.icms"), D("totals.icmsStBase"), D("totals.icmsSt"), D("totals.total")),
            items, IncomingReadingRules.RequiredKeys(count).Where(k => skip?.Contains(k) != true).ToList());
    }

    private async Task<Guid> SupplierWithContactAsync(string cnpj, string? email)
    {
        var id = await SupplierAsync(cnpj);
        if (email is not null)
        {
            await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
            var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
            db.PartyContacts.Add(new PartyContact { PartyId = id, Name = "Faturamento", Email = email, ReceivesBilling = true });
            await db.SaveChangesAsync();
        }

        return id;
    }

    [Fact]
    public async Task Pdf_lido_no_worker_vira_nota_conferida_campo_a_campo_com_o_pdf_a_marca_e_o_historico_e_segue_a_FIS04a()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var cnpj = NfeSamples.NewCnpj();
        await SupplierAsync(cnpj);
        var item = await StockItemAsync("Chapa de aço 2mm " + Guid.NewGuid().ToString("N")[..4]);
        var sample = new NfeSample { IssuerCnpj = cnpj, Freight = 20m };
        var jobsBefore = fixture.Jobs.All.Count;

        var reading = await ReadPdfAsync(warehouse, await DanfeSamples.PdfAsync(sample));

        fixture.Jobs.All.Skip(jobsBefore).Should().Contain(j => j.Type == typeof(PdfInvoiceReadingJob) && (Guid)j.Args[0]! == tenant.Id, "o job sempre recebe o tenantId");
        reading.Should().BeEquivalentTo(new { Status = IncomingReadingStatus.Ready, Result = (PdfReadingOutcome?)PdfReadingOutcome.Read, Source = IncomingInvoiceSource.Pdf, HasPdf = true, XmlByKeyAvailable = false });
        reading.XmlByKeyMessage.Should().Be(IncomingReadingRules.NoDfeMessage, "sem Distribuição DF-e no MVP (CA 6)");
        reading.DurationMs.Should().NotBeNull();
        reading.Key!.Should().BeEquivalentTo(new { IssuerDocument = cnpj, Model = "55", Series = 1, Number = sample.Number });
        reading.Supplier!.State.Should().Be(IncomingSupplierState.Found);
        reading.TenantDocument.Should().Be(NfeSamples.TenantCnpj);
        reading.Fields.Single(f => f.Key == "issuerName").State.Should().Be(ReadingFieldState.Uncertain);
        reading.Fields.Should().Contain(new ReadingField("totals.total", "145", ReadingFieldState.Read));

        // PDF guardado como anexo de sistema do rascunho, debaixo do prefixo do tenant, e lido só pelo endpoint.
        var attachment = await fixture.QueryAsync(tenant, db => db.Attachments.AsNoTracking().SingleAsync(a => a.OwnerType == IncomingInvoiceService.PdfAttachmentOwner && a.OwnerId == reading.Id));
        (attachment.Origin, attachment.ContentType).Should().Be((AttachmentOrigin.System, "application/pdf"));
        attachment.StoragePath.Should().StartWith(tenant.Id.ToString("N"));
        (await warehouse.GetAsync($"{Incoming}/{reading.Id}/pdf-reading/pdf")).Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        // Todo campo conferido antes de "Conferir nota" (CA 7).
        var missing = await warehouse.PostAsJsonAsync($"{Incoming}/{reading.Id}/pdf-reading/check", Confirmed(reading, skip: ["items.0.ncm"]));
        (await RefusedAsync(missing)).Should().StartWith("Falta conferir 1 campo");
        var request = Confirmed(reading, (k, v) => k == "issuerName" ? sample.IssuerName + " (corrigido)" : v);
        var created = await OkAsync<CheckReadingResult>(await warehouse.PostAsJsonAsync($"{Incoming}/{reading.Id}/pdf-reading/check", request));
        created.Source.Should().Be(IncomingInvoiceSource.Pdf);
        (await warehouse.PostAsJsonAsync($"{Incoming}/{reading.Id}/pdf-reading/check", request)).StatusCode.Should().Be(HttpStatusCode.Conflict, "a leitura conferida virou nota");

        // A nota nasce Pdf, sem assinatura nem protocolo, com a marca, o PDF e o fornecedor pela chave, e segue pela FIS-04a (CA 8).
        var view = await OkAsync<IncomingInvoiceView>(await warehouse.GetAsync($"{Incoming}/{created.InvoiceId}"));
        view.Should().BeEquivalentTo(new { Status = IncomingInvoiceStatus.Imported, Source = IncomingInvoiceSource.Pdf, SourceMark = IncomingReadingRules.PdfMark, Protocol = string.Empty, AccessKey = NfeSamples.AccessKey(sample) });
        view.Issuer.Name.Should().EndWith("(corrigido)");
        view.Supplier.State.Should().Be(IncomingSupplierState.Found);
        view.Totals.Freight.Should().Be(20m);
        view.ReadingId.Should().Be(reading.Id);
        (await warehouse.GetAsync($"{Incoming}/{created.InvoiceId}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await warehouse.GetAsync($"{Incoming}/{created.InvoiceId}/xml")).StatusCode.Should().Be(HttpStatusCode.NotFound, "não há XML");
        (await OkAsync<IncomingInvoiceList>(await warehouse.GetAsync(Incoming))).Items.Should().Contain(i => i.Id == created.InvoiceId && i.Source == IncomingInvoiceSource.Pdf);

        (await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/items/{view.Items[0].Id}/mapping", new { itemId = item, noStockItem = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await OrderAsync(view.Supplier.PartyId!.Value, 20m, 0m, (item, 10m, 12.5m));
        (await warehouse.PutAsJsonAsync($"{Incoming}/{view.Id}/order", new { purchaseOrderId = order.Id })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OkAsync<IncomingInvoiceView>(await warehouse.PostAsJsonAsync($"{Incoming}/{view.Id}/check", new { }))).Status.Should().Be(IncomingInvoiceStatus.Checked);
        var forReceipt = await fixture.WithAsync<BemAqui.Fiscal.Contracts.IncomingInvoiceForReceipt?, BemAqui.Fiscal.Contracts.IIncomingInvoiceReceipts>(tenant, r => r.FindForReceiptAsync(view.Id));
        forReceipt!.SourceMark.Should().Be(IncomingReadingRules.PdfMark, "a marca vai para os títulos a pagar (FIS-04b)");

        // Histórico: o que foi lido, o que foi corrigido e quem conferiu (CA 13); log só com a chave mascarada (CA 12).
        var history = await warehouse.GetAsync($"{Incoming}/{view.Id}/history?pageSize=50");
        var entries = JsonDocument.Parse(await history.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray().ToList();
        var readingEntries = entries.Where(e => e.GetProperty("entity").GetString() == nameof(IncomingInvoiceReading)).Select(e => e.GetProperty("changes").GetString()!).ToList();
        readingEntries.Should().Contain(c => c.Contains("\"fields\"", StringComparison.Ordinal), "o que foi lido");
        readingEntries.Should().Contain(c => c.Contains("\"correctedFields\"", StringComparison.Ordinal) && c.Contains("issuerName", StringComparison.Ordinal) && c.Contains("\"checkedBy\"", StringComparison.Ordinal));
        var logs = fixture.AllLogs();
        logs.Should().Contain(IncomingInvoiceRules.MaskKey(view.AccessKey)).And.NotContain(view.AccessKey).And.NotContain("CHAPA DE ACO 2MM");
    }

    [Fact]
    public async Task Recusa_pdf_inseguro_grande_ou_de_outro_tipo_e_o_worker_recusa_o_javascript_comprimido_o_pdf_sem_texto_e_as_paginas_demais()
    {
        var (id, warehouse) = await IncomingActorAsync(Warehouse);
        (await RefusedAsync(await UploadPdfAsync(warehouse, DanfeSamples.JavaScript()))).Should().Be(IncomingReadingRules.UnsafeMessage);
        (await RefusedAsync(await UploadPdfAsync(warehouse, DanfeSamples.JavaScript(obfuscated: true)))).Should().Be(IncomingReadingRules.UnsafeMessage);
        (await RefusedAsync(await UploadPdfAsync(warehouse, DanfeSamples.EmbeddedFile()))).Should().Be(IncomingReadingRules.UnsafeMessage);
        (await RefusedAsync(await UploadPdfAsync(warehouse, DanfeSamples.Encrypted()))).Should().Be(IncomingReadingRules.UnsafeMessage);
        (await RefusedAsync(await UploadPdfAsync(warehouse, new byte[IncomingReadingRules.MaxPdfBytes + 1]))).Should().Contain("5 MB");
        (await RefusedAsync(await UploadPdfAsync(warehouse, NfeSamples.Authorized(new NfeSample()), "nota.pdf"))).Should().Contain("Tipo de arquivo não permitido");
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.IncomingInvoiceReadings.CountAsync(r => r.CreatedBy == id))).Should().Be(0, "nada foi gravado");

        var (_, other) = await IncomingActorAsync(Warehouse);
        var hidden = await ReadPdfAsync(other, DanfeSamples.CompressedJavaScript());
        hidden.Should().BeEquivalentTo(new { Status = IncomingReadingStatus.Failed, Result = (PdfReadingOutcome?)PdfReadingOutcome.Unsafe, Message = IncomingReadingRules.UnsafeMessage });
        hidden.Fields.Should().BeEmpty();
        var scanned = await ReadPdfAsync(other, await DanfeSamples.ImageOnlyAsync());
        scanned.Should().BeEquivalentTo(new { Status = IncomingReadingStatus.Failed, Result = (PdfReadingOutcome?)PdfReadingOutcome.NoText, Message = IncomingReadingRules.NoTextMessage });
        var pages = await ReadPdfAsync(other, await DanfeSamples.PagesAsync(11));
        pages.Result.Should().Be(PdfReadingOutcome.TooManyPages);
        (await RefusedAsync(await other.PostAsJsonAsync($"{Incoming}/{scanned.Id}/pdf-reading/check", Confirmed(scanned)), HttpStatusCode.Conflict)).Should().Contain("digite a nota");

        // "Digitar a nota" a partir da leitura que não serviu: o formulário vazio, o PDF ainda à mão (CA 9).
        var manual = await OkAsync<IncomingReadingView>(await other.PostAsync($"{Incoming}/{scanned.Id}/pdf-reading/manual", null));
        manual.Should().BeEquivalentTo(new { Source = IncomingInvoiceSource.Manual, Status = IncomingReadingStatus.Ready, HasPdf = true });
        manual.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task Leitura_presa_e_morta_no_tempo_limite_e_fica_falhou_por_tempo()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var kills = fixture.PdfReaderKills;
        fixture.PdfBehavior = (_, _) =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(FiscalFixture.PdfTimeoutSeconds * 3));
            return BemAqui.PdfReader.ReadResponse.Of("Read", 1, 0);
        };
        try
        {
            var reading = await ReadPdfAsync(warehouse, await DanfeSamples.PdfAsync(new NfeSample()));
            reading.Should().BeEquivalentTo(new { Status = IncomingReadingStatus.Failed, Result = (PdfReadingOutcome?)PdfReadingOutcome.TimedOut, Message = IncomingReadingRules.FailedMessage });
            fixture.PdfReaderKills.Should().Be(kills + 1, "estourado o tempo, o processo do worker é morto (e o container reinicia)");
            reading.HasPdf.Should().BeFalse("o PDF que não terminou a conferência de segurança não é mostrado");
            (await RefusedAsync(await warehouse.GetAsync($"{Incoming}/{reading.Id}/pdf-reading/pdf"), HttpStatusCode.Conflict)).Should().Be(IncomingReadingRules.PdfNotShownMessage);
        }
        finally
        {
            fixture.PdfBehavior = null;
        }

        // O worker reiniciado volta a ler.
        await Task.Delay(500);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(fixture.PdfSocket) && DateTime.UtcNow < until)
        {
            await Task.Delay(100);
        }

        (await ReadPdfAsync(warehouse, await DanfeSamples.PdfAsync(new NfeSample()))).Result.Should().Be(PdfReadingOutcome.Read);
    }

    [Fact]
    public async Task Chave_ja_importada_pelo_xml_e_recusada_com_409_e_o_link()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var sample = new NfeSample();
        var imported = await ImportedAsync(warehouse, sample);

        var reading = await ReadPdfAsync(warehouse, await DanfeSamples.PdfAsync(sample));
        reading.DuplicateInvoiceId.Should().Be(imported.Id);
        reading.DuplicateMessage.Should().StartWith("Esta nota já foi importada em");

        var duplicate = await warehouse.PostAsJsonAsync($"{Incoming}/{reading.Id}/pdf-reading/check", Confirmed(reading));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().Should().Be(IncomingInvoiceRules.DuplicateCode);
        problem.GetProperty("invoiceId").GetGuid().Should().Be(imported.Id);
    }

    [Fact]
    public async Task Digitar_a_nota_com_a_chave_obrigatoria_a_mesma_conferencia_e_a_soma_dos_itens()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var sample = new NfeSample();
        var draft = await OkAsync<IncomingReadingView>(await warehouse.PostAsync($"{Incoming}/manual", null));
        draft.Should().BeEquivalentTo(new { Source = IncomingInvoiceSource.Manual, Status = IncomingReadingStatus.Ready, HasPdf = false });

        var key = NfeSamples.AccessKey(sample);
        var issued = DateOnly.FromDateTime(sample.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        var typed = new CheckReadingRequest(key, issued, sample.IssuerName, NfeSamples.TenantCnpj, new ReadingTotalsInput(125m, 0m, 0m, 0m, 0m, 0m, 125m, 22.5m, 0m, 0m, 125m),
            [new ReadingItemInput("CH-2MM", "CHAPA DE ACO 2MM", "72085100", "UN", 10m, 12.5m, 125m)], IncomingReadingRules.RequiredKeys(1));
        string Url() => $"{Incoming}/{draft.Id}/pdf-reading/check";
        (await RefusedAsync(await warehouse.PostAsJsonAsync(Url(), typed with { AccessKey = key[..43] + ((key[43] - '0' + 1) % 10) }))).Should().Contain("dígito verificador");
        (await RefusedAsync(await warehouse.PostAsJsonAsync(Url(), typed with { AccessKey = null }))).Should().Contain("44 dígitos");
        (await RefusedAsync(await warehouse.PostAsJsonAsync(Url(), typed with { RecipientDocument = NfeSamples.NewCnpj() }))).Should().Be(IncomingInvoiceRules.NotRecipientMessage);
        (await RefusedAsync(await warehouse.PostAsJsonAsync(Url(), typed with { Totals = typed.Totals! with { Products = 120m } }))).Should().Be("A soma dos itens (R$ 125,00) não bate com o total da nota (R$ 120,00).");

        var created = await OkAsync<CheckReadingResult>(await warehouse.PostAsJsonAsync(Url(), typed));
        var view = await OkAsync<IncomingInvoiceView>(await warehouse.GetAsync($"{Incoming}/{created.InvoiceId}"));
        view.Should().BeEquivalentTo(new { Source = IncomingInvoiceSource.Manual, SourceMark = IncomingReadingRules.ManualMark, PdfAttachmentId = (Guid?)null, Number = sample.Number });
        view.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { SupplierCode = "CH-2MM", Quantity = 10m, UnitPrice = 12.5m, Amount = 125m });
        var saved = await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.IncomingInvoiceReadings.AsNoTracking().SingleAsync(r => r.Id == draft.Id));
        saved.CorrectedFields.Should().HaveCount(IncomingReadingRules.RequiredKeys(1).Count, "na nota digitada, todo campo foi digitado");
        saved.CheckedBy.Should().NotBeNull();
    }

    [Fact]
    public async Task Pedir_o_xml_ao_fornecedor_pelo_email_da_fila_com_o_numero_e_a_chave()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var cnpj = NfeSamples.NewCnpj();
        var sample = new NfeSample { IssuerCnpj = cnpj };
        var reading = await ReadPdfAsync(warehouse, await DanfeSamples.PdfAsync(sample));
        string Url() => $"{Incoming}/{reading.Id}/pdf-reading/xml-request";

        (await RefusedAsync(await warehouse.PostAsync(Url(), null))).Should().Contain("Cadastre o fornecedor");
        var party = await SupplierWithContactAsync(cnpj, null);
        (await RefusedAsync(await warehouse.PostAsync(Url(), null))).Should().Contain("não tem contato com e-mail");
        await using (var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
            db.PartyContacts.Add(new PartyContact { PartyId = party, Name = "Faturamento", Email = "nfe@fornecedor.com.br", ReceivesBilling = true });
            await db.SaveChangesAsync();
        }

        var sent = await OkAsync<XmlRequestResult>(await warehouse.PostAsync(Url(), null));
        sent.To.Should().Be("n***@fornecedor.com.br", "sem parties.view, o endereço vai mascarado");
        var email = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.EmailLogs.AsNoTracking().SingleAsync(l => l.RelatedId == reading.Id));
        (email.Template, email.To).Should().Be((IncomingInvoiceService.XmlRequestTemplate, "nfe@fornecedor.com.br"));
        email.Model.Should().Contain(sample.Number.ToString(CultureInfo.InvariantCulture)).And.Contain(NfeSamples.AccessKey(sample)[..4]);
        (await RefusedAsync(await warehouse.PostAsync(Url(), null), HttpStatusCode.Conflict)).Should().Contain("24 horas");
        (await OkAsync<IncomingReadingView>(await warehouse.GetAsync($"{Incoming}/{reading.Id}/pdf-reading"))).XmlRequestedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Substituir_pelo_xml_confere_a_chave_troca_a_origem_e_avisa_as_diferencas()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var (financeImporter, _) = await IncomingActorAsync([.. Warehouse, "finance.edit"]);
        var (financeOnly, _) = await IncomingActorAsync("finance.view", "finance.edit");
        var sample = new NfeSample();
        var reading = await ReadPdfAsync(warehouse, await DanfeSamples.PdfAsync(sample));
        var created = await OkAsync<CheckReadingResult>(await warehouse.PostAsJsonAsync($"{Incoming}/{reading.Id}/pdf-reading/check", Confirmed(reading, (k, v) => k == "totals.icms" ? "0" : v)));

        async Task<HttpResponseMessage> ReplaceAsync(byte[] xml)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(xml);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            form.Add(file, "file", "nota.xml");
            return await warehouse.PostAsync($"{Incoming}/{created.InvoiceId}/replace-xml", form);
        }

        (await RefusedAsync(await ReplaceAsync(NfeSamples.Authorized(new NfeSample())))).Should().Contain("outra nota");
        (await RefusedAsync(await ReplaceAsync(NfeSamples.Authorized(sample, withProtocol: false)))).Should().Be(IncomingInvoiceRules.NoProtocolMessage, "toda a validação da FIS-04a");

        var replaced = await OkAsync<IncomingInvoiceView>(await ReplaceAsync(NfeSamples.Authorized(sample)));
        replaced.Should().BeEquivalentTo(new { Source = IncomingInvoiceSource.Xml, SourceMark = (string?)null, Protocol = "135260000000001" });
        replaced.XmlAttachmentId.Should().NotBeNull();
        replaced.ReplacedByXmlAt.Should().NotBeNull();
        replaced.XmlDifferences.Should().ContainSingle().Which.Should().Be(new IncomingXmlDifference("Valor do ICMS", "R$ 0,00", "R$ 22,50"));
        replaced.Totals.Icms.Should().Be(0m, "os valores conferidos ficam; a diferença fica registrada");
        var notified = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Notifications.AsNoTracking().Where(n => n.Type == IncomingInvoiceService.XmlDifferenceNotification && n.Link.EndsWith(created.InvoiceId.ToString())).Select(n => n.UserId).ToListAsync());
        notified.Should().Contain(financeImporter).And.NotContain(financeOnly, "só quem tem fiscal.import_incoming e finance.edit");
        (await RefusedAsync(await ReplaceAsync(NfeSamples.Authorized(sample)), HttpStatusCode.Conflict)).Should().Be("Esta nota já tem o XML.");
    }

    [Fact]
    public async Task So_quem_importa_le_digita_confere_e_substitui_o_rascunho_inexistente_e_404_e_o_limite_e_por_usuario()
    {
        var (_, warehouse) = await IncomingActorAsync(Warehouse);
        var (_, viewer) = await IncomingActorAsync("fiscal.view");
        var reading = await ReadPdfAsync(warehouse, await DanfeSamples.PdfAsync(new NfeSample()));

        (await UploadPdfAsync(viewer, await DanfeSamples.PdfAsync(new NfeSample()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"{Incoming}/manual", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"{Incoming}/{reading.Id}/pdf-reading")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"{Incoming}/{reading.Id}/pdf-reading/pdf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"{Incoming}/{reading.Id}/pdf-reading/check", Confirmed(reading))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"{Incoming}/{reading.Id}/pdf-reading/xml-request", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await warehouse.GetAsync($"{Incoming}/{Guid.NewGuid()}/pdf-reading")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await warehouse.GetAsync($"{Incoming}/{Guid.NewGuid()}/pdf-reading/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"{Incoming}/{Guid.NewGuid()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OkAsync<IncomingInvoiceList>(await warehouse.GetAsync(Incoming))).Readings.Should().Contain(r => r.Id == reading.Id);
        (await OkAsync<IncomingInvoiceList>(await viewer.GetAsync(Incoming))).Readings.Should().BeEmpty("rascunho só para quem importa");

        var (_, limited) = await IncomingActorAsync(Warehouse);
        for (var i = 0; i < FiscalFixture.PdfReadingsPerHour; i++)
        {
            (await UploadPdfAsync(limited, "texto"u8.ToArray(), "nota.txt")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        var blocked = await UploadPdfAsync(limited, "texto"u8.ToArray(), "nota.txt");
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.Should().NotBeNull();
        (await UploadPdfAsync(warehouse, "texto"u8.ToArray(), "nota.txt")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o limite é por usuário");
    }
}
