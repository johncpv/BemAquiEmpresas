using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Purchasing.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03b com PostgreSQL real: baixar o PDF (CA 1, 9), enviar com o PDF guardado e o e-mail na fila (CA 2, 6, 7), a espera do
/// <c>Clean</c> e a falha com anexo infectado (CA 3), reenviar, inclusive o pedido alterado depois do envio (CA 4), os limites do PDF
/// (CA 8) e o histórico (CA 11). Banco desta classe; cada teste cria os próprios fornecedores e pedidos e filtra pelos ids dele.
/// </summary>
public class PurchaseOrderDispatchTests(TenancyFixture tenancy) : PurchaseOrderDispatchTestBase(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Baixar_PDF_so_do_aprovado_em_diante_e_sem_custo_nem_projeto()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierWithContactAsync(owner);
        var item = await ItemAsync();
        var draft = await CreateAsync(buyer, OrderBody(supplier, null, await TermAsync("30/60/90 dias"), (item, 10m, 5m, null)));
        var refused = await buyer.GetAsync($"{Base}/{Id(draft)}/pdf");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Aprove o pedido antes de enviar.");
        (await buyer.GetAsync($"{Base}/{Id(await SubmitAsync(buyer, draft))}/pdf")).StatusCode.Should().Be(HttpStatusCode.Conflict, "aguardando aprovação também não gera");

        var project = Project();
        var approved = await ApprovedAsync(owner, buyer, supplier, project);
        var response = await buyer.GetAsync($"{Base}/{Id(approved)}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = string.Join(" ", document.GetPages().Select(p => p.Text));
        text.Should().Contain(approved.GetProperty("number").GetString()).And.Contain("30/60/90 dias").And.Contain("1.200,00").And.Contain("Entregar no portão 2.");
        text.Should().NotContain(approved.GetProperty("projectNumber").GetString()!, "o PDF não mostra o projeto (CA 9)").And.NotContainAny("Custo", "Margem", "Projeto");
        (await WithDbAsync(db => db.Attachments.CountAsync(a => a.OwnerId == Id(approved)))).Should().Be(0, "baixar não guarda arquivo; o PDF guardado é o do envio");
    }

    [Fact]
    public async Task Enviar_guarda_o_PDF_enfileira_o_email_com_o_minimo_e_passa_a_Sent()
    {
        var (_, owner) = await ActorAsync("owner");
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierWithContactAsync(owner);
        var order = await ApprovedAsync(owner, buyer, supplier);
        order.GetProperty("canDispatch").GetBoolean().Should().BeTrue();
        var draft = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/dispatch-draft"));
        draft.GetProperty("to").GetString().Should().Be("compras@gama.com.br", "o contato que recebe cobrança (R-PRP-1)");
        draft.GetProperty("subject").GetString().Should().StartWith($"Pedido de compra {order.GetProperty("number").GetString()} — ");
        draft.GetProperty("isResend").GetBoolean().Should().BeFalse();

        // CA 6: endereço puro (422 no campo) e a confirmação fora dos contatos.
        foreach (var bad in new[] { "Compras <compras@gama.com.br>", "compras@gama.com.br\r\nBcc: x@y.com", "a@b.com,c@d.com" })
        {
            var invalid = await SendAsync(buyer, order, Dispatch(order, bad));
            invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(invalid)).GetProperty("errors").TryGetProperty("to", out _).Should().BeTrue(bad);
        }

        var stranger = await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br", ["dono@gmail.com"]));
        stranger.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await stranger.Content.ReadAsStringAsync()).Should().Contain("confirmRecipient").And.Contain("não está nos contatos de");
        var tooMany = await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br", ["a@gama.com.br", "b@gama.com.br", "c@gama.com.br", "d@gama.com.br"], confirm: true));
        tooMany.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(tooMany)).GetProperty("errors").TryGetProperty("cc", out _).Should().BeTrue();
        var longMessage = await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br", message: new string('x', 1001)));
        (await JsonAsync(longMessage)).GetProperty("errors").TryGetProperty("message", out _).Should().BeTrue();

        // CA 2: com a confirmação, o PDF é guardado, o e-mail vai para a fila e o pedido passa a Sent; a mesma chave não enfileira de novo.
        var key = Guid.NewGuid();
        var body = Dispatch(order, "compras@gama.com.br", ["dono@gmail.com"], confirm: true, message: "Valor {{ 1 | plus: 1 }} {% if true %}sim{% endif %} <b>x</b> https://mal.example");
        var sent = await SendAsync(buyer, order, body, key);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        (await SendAsync(buyer, order, body, key)).StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await JsonAsync(sent);
        detail.GetProperty("status").GetString().Should().Be("Sent");
        detail.GetProperty("sentAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        var logs = await WithDbAsync(db => db.EmailLogs.AsNoTracking().Where(l => l.Template == EmailDispatchLimiter.PurchaseOrderTemplate && l.RelatedId == Id(order)).ToListAsync());
        var log = logs.Should().ContainSingle().Subject;
        log.Template.Should().Be("purchase-order-sent");
        log.RelatedType.Should().Be("PurchaseOrder");
        log.Cc.Should().Be("dono@gmail.com");
        log.FromName.Should().EndWith(" via BemAqui").And.NotContainAny("\r", "\n");
        log.RequestedBy.Should().Be(buyerId);
        log.Model.Should().NotContain("http://localhost").And.NotContain("/app/compras", "o e-mail não leva link para o sistema (CA 7)");
        var pdf = await WithDbAsync(db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == log.AttachmentIds.Single()));
        (pdf.OwnerType, pdf.OwnerId, pdf.Origin, pdf.ContentType).Should().Be(("PurchaseOrder", Id(order), AttachmentOrigin.System, "application/pdf"));
        var dispatch = await WithDbAsync(db => db.PurchaseOrderDispatches.AsNoTracking().SingleAsync(d => d.OrderId == Id(order)));
        (dispatch.EmailLogId, dispatch.AttachmentId, dispatch.To, dispatch.RecipientConfirmed, dispatch.IsResend, dispatch.SentBy)
            .Should().Be((log.Id, pdf.Id, "compras@gama.com.br", true, false, (Guid?)buyerId));

        // CA 3 e 7: o job manda com o anexo, a cópia e o "Responder para"; a mensagem sai literal e sem link gerado.
        await RunJobAsync(log.Id);
        var mail = Transport.Sent.Should().ContainSingle(m => m.Content.Subject.Contains(order.GetProperty("number").GetString()!)).Subject;
        mail.To.Should().Be("compras@gama.com.br");
        mail.Cc.Should().Equal("dono@gmail.com");
        mail.ReplyTo.Should().EndWith("@teste.com");
        mail.Attachments.Should().ContainSingle().Which.FileName.Should().Be(order.GetProperty("number").GetString() + ".pdf");
        mail.Content.Html.Should().Contain("{{ 1 | plus: 1 }}").And.Contain("{% if true %}sim{% endif %}").And.Contain("&lt;b&gt;x&lt;/b&gt;").And.NotContain("<a");
        var documents = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/documents"));
        documents.GetProperty("dispatches")[0].GetProperty("status").GetString().Should().Be("Sent");
        documents.GetProperty("generated").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Job_espera_o_Clean_e_falha_com_o_anexo_infectado()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var order = await ApprovedAsync(owner, buyer, await SupplierWithContactAsync(owner));
        Scanner.Verdict = ScanVerdict.Unavailable;
        try
        {
            (await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br"))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Scanner.Verdict = ScanVerdict.Clean;
        }

        var log = await WithDbAsync(db => db.EmailLogs.AsNoTracking().SingleAsync(l => l.Template == EmailDispatchLimiter.PurchaseOrderTemplate && l.RelatedId == Id(order)));
        await RunJobAsync(log.Id);
        var waiting = await WithDbAsync(db => db.EmailLogs.AsNoTracking().SingleAsync(l => l.Id == log.Id));
        (waiting.Status, waiting.ScanWaits).Should().Be((EmailStatus.Queued, 1), "espera a varredura sem gastar tentativa");
        var documents = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/documents"));
        documents.GetProperty("dispatches")[0].GetProperty("waitingScan").GetBoolean().Should().BeTrue();

        await WithDbAsync(db => db.Attachments.Where(a => a.Id == log.AttachmentIds[0]).ExecuteUpdateAsync(s => s.SetProperty(a => a.ScanStatus, ScanStatus.Infected)));
        await RunJobAsync(log.Id);
        var failed = await WithDbAsync(db => db.EmailLogs.AsNoTracking().SingleAsync(l => l.Id == log.Id));
        failed.Status.Should().Be(EmailStatus.Failed);
        failed.Error.Should().Contain("antivírus");
        var view = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/documents"));
        view.GetProperty("dispatches")[0].GetProperty("status").GetString().Should().Be("Failed");
        Transport.Sent.Should().NotContain(m => m.Content.Subject.Contains(order.GetProperty("number").GetString()!));
    }

    [Fact]
    public async Task Reenviar_gera_PDF_novo_e_outro_envio_inclusive_do_pedido_alterado_depois_do_envio()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var order = await ApprovedAsync(owner, buyer, await SupplierWithContactAsync(owner));
        var sent = await JsonAsync(await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br")));
        var resent = await SendAsync(buyer, sent, Dispatch(sent, "compras@gama.com.br"));
        resent.StatusCode.Should().Be(HttpStatusCode.OK, await resent.Content.ReadAsStringAsync());
        var again = await JsonAsync(resent);
        again.GetProperty("status").GetString().Should().Be("Sent", "o Sent não muda de estado");
        var rows = await WithDbAsync(db => db.PurchaseOrderDispatches.AsNoTracking().Where(d => d.OrderId == Id(order)).OrderBy(d => d.CreatedAt).ToListAsync());
        rows.Select(r => r.IsResend).Should().Equal(false, true);
        rows.Select(r => r.AttachmentId).Distinct().Should().HaveCount(2, "cada envio gera um PDF novo");

        // Editado depois do envio: volta à aprovação com a faixa; reaprovado, a faixa continua até o reenvio, que volta a Sent.
        var line = again.GetProperty("lines")[0];
        var edited = await buyer.PutAsJsonAsync($"{Base}/{Id(order)}", new
        {
            partyId = again.GetProperty("partyId").GetGuid(), projectId = (Guid?)null, paymentTermId = again.GetProperty("paymentTermId").GetGuid(),
            expectedDate = again.GetProperty("expectedDate").GetString(), freight = 80m, ipiPct = 5m, notes = "Entregar no portão 2.",
            lines = new[] { new { itemId = line.GetProperty("itemId").GetGuid(), quantity = 100m, unitId = (Guid?)null, unitPrice = 13m, requestLineIds = Array.Empty<Guid>() } },
            version = Version(again),
        });
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var pending = await JsonAsync(edited);
        (pending.GetProperty("status").GetString(), pending.GetProperty("changedAfterSent").GetBoolean()).Should().Be(("PendingApproval", true));
        (await SendAsync(buyer, pending, Dispatch(pending, "compras@gama.com.br"))).StatusCode.Should().Be(HttpStatusCode.Conflict, "aguardando aprovação não envia");
        var reapproved = await JsonAsync(await DecideAsync(owner, pending, true));
        (reapproved.GetProperty("status").GetString(), reapproved.GetProperty("changedAfterSent").GetBoolean()).Should().Be(("Approved", true));
        var draft = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/dispatch-draft"));
        draft.GetProperty("isResend").GetBoolean().Should().BeTrue();
        var back = await JsonAsync(await SendAsync(buyer, reapproved, Dispatch(reapproved, "compras@gama.com.br")));
        (back.GetProperty("status").GetString(), back.GetProperty("changedAfterSent").GetBoolean()).Should().Be(("Sent", false), "a faixa some no reenvio (CA 4)");
        (await WithDbAsync(db => db.PurchaseOrderDispatches.CountAsync(d => d.OrderId == Id(order)))).Should().Be(3);
    }

    [Fact]
    public async Task Concorrencia_e_versao_antiga_dao_409()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var order = await ApprovedAsync(owner, buyer, await SupplierWithContactAsync(owner));
        (await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var stale = await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(stale)).Should().Be("concurrency_conflict");
    }

    [Fact]
    public async Task Limites_do_PDF_dao_422_antes_de_gerar()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var order = await ApprovedAsync(owner, buyer, await SupplierWithContactAsync(owner));
        var item = order.GetProperty("lines")[0].GetProperty("itemId").GetGuid();
        await WithDbAsync(async db =>
        {
            db.PurchaseOrderLines.AddRange(Enumerable.Range(2, PurchaseOrderRules.PdfMaxLines)
                .Select(i => new PurchaseOrderLine { OrderId = Id(order), Sequence = i, ItemId = item, Quantity = 1m, UnitPrice = 1m, LineTotal = 1m }));
            return await db.SaveChangesAsync();
        });
        var pdf = await buyer.GetAsync($"{Base}/{Id(order)}/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(pdf)).GetProperty("errors").TryGetProperty("lines", out _).Should().BeTrue();
        var current = await GetAsync(buyer, Id(order));
        (await SendAsync(buyer, current, Dispatch(current, "compras@gama.com.br"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await WithDbAsync(db => db.EmailLogs.CountAsync(l => l.Template == EmailDispatchLimiter.PurchaseOrderTemplate && l.RelatedId == Id(order)))).Should().Be(0, "nada é enfileirado");
    }

    [Fact]
    public async Task Historico_mostra_a_passagem_a_Sent_e_o_envio_com_os_enderecos_mascarados()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var order = await ApprovedAsync(owner, buyer, await SupplierWithContactAsync(owner));
        (await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br", ["dono@gmail.com"], confirm: true))).StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/history?pageSize=50"));
        var entries = history.GetProperty("items").EnumerateArray().ToList();
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "PurchaseOrder" && e.GetProperty("changes").GetString()!.Contains("\"Sent\"", StringComparison.Ordinal) && e.GetProperty("userId").ValueKind != JsonValueKind.Null);
        var dispatch = entries.Single(e => e.GetProperty("entity").GetString() == "PurchaseOrderDispatch");
        var changes = dispatch.GetProperty("changes").GetRawText();
        changes.Should().Contain("recipientConfirmed").And.NotContain("compras@gama.com.br").And.NotContain("dono@gmail.com");

        // Sem parties.view, a aba Documentos também mascara.
        var documents = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/documents"));
        documents.GetProperty("dispatches")[0].GetProperty("to").GetString().Should().Be("c***@gama.com.br");
        var full = await JsonAsync(await owner.GetAsync($"{Base}/{Id(order)}/documents"));
        full.GetProperty("dispatches")[0].GetProperty("to").GetString().Should().Be("compras@gama.com.br");
    }
}
