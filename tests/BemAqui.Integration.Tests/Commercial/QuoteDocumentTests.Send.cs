using System.Net;
using BemAqui.BuildingBlocks.Email;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteDocumentTests
{
    private static object Send(string to, uint version, string[]? cc = null, bool confirm = false, string message = "Segue o orçamento.") =>
        new { to, cc = cc ?? [], message, confirmRecipient = confirm, version };

    [Fact]
    public async Task Envio_por_email_com_PDF_copia_confirmacao_idempotencia_e_limite()
    {
        var owner = await OwnerAsync();
        var (id, detail) = await QuoteAsync(owner);
        var draft = await JsonAsync(await owner.GetAsync($"{Base}/{id}/send-draft"));
        draft.GetProperty("to").GetString().Should().Be("marta@aurora.com.br", "o contato de cobrança do cliente (CA 7)");

        // CA 9: endereço puro e confirmação fora dos contatos.
        (await (await owner.SendAsync(Post($"{Base}/{id}/email", Send("Marta <marta@aurora.com.br>", Version(detail))))).Content.ReadAsStringAsync()).Should().Contain("\"to\"");
        (await (await owner.SendAsync(Post($"{Base}/{id}/email", Send("outro@gmail.com", Version(detail))))).Content.ReadAsStringAsync()).Should().Contain("confirmRecipient");

        // CA 8: na fila com o PDF e a cópia; o rascunho vira enviado; a mesma chave não enfileira de novo.
        var key = Guid.NewGuid();
        var sent = await owner.SendAsync(Post($"{Base}/{id}/email", Send("marta@aurora.com.br", Version(detail), ["financeiro@aurora.com.br"], confirm: true), key));
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        (await owner.SendAsync(Post($"{Base}/{id}/email", Send("marta@aurora.com.br", Version(detail), ["financeiro@aurora.com.br"], confirm: true), key))).StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = await WithDbAsync(db => db.EmailLogs.Where(l => l.RelatedId == id).ToListAsync());
        logs.Should().ContainSingle();
        logs[0].Template.Should().Be("quote-sent");
        logs[0].AttachmentIds.Should().ContainSingle();
        logs[0].Cc.Should().Be("financeiro@aurora.com.br");
        logs[0].FromName.Should().EndWith(" via BemAqui");
        (await JsonAsync(await owner.GetAsync($"{Base}/{id}"))).GetProperty("status").GetString().Should().Be("Sent");

        // CA 12: o job lê o PDF pelo id e manda com a cópia e o responder-para.
        await using (var scope = await ScopeAsync())
        {
            var job = ActivatorUtilities.CreateInstance<SendEmailJob>(Factory.Services);
            await job.ExecuteAsync(tenancy.TenantA.Id, new SendEmailArgs(logs[0].Id), CancellationToken.None);
        }

        var mail = _transport.Sent.Should().ContainSingle(m => m.To == "marta@aurora.com.br").Subject;
        mail.Cc.Should().Equal("financeiro@aurora.com.br");
        mail.Attachments.Should().ContainSingle().Which.FileName.Should().EndWith(".pdf");
        mail.ReplyTo.Should().EndWith("@teste.com");
        (await JsonAsync(await owner.GetAsync($"{Base}/{id}/documents"))).GetProperty("dispatches")[0].GetProperty("status").GetString().Should().Be("Sent");

        // CA 11: 50 envios no dia, o 51º é recusado.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"INSERT INTO commercial.quote_dispatches (id, quote_id, email_log_id, attachment_id, \"to\", created_at) SELECT gen_random_uuid(), {id}, gen_random_uuid(), gen_random_uuid(), 'x@y.com', now() FROM generate_series(1, 49)"));
        var current = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        var blocked = await owner.SendAsync(Post($"{Base}/{id}/email", Send("marta@aurora.com.br", Version(current))));
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("Limite diário de envios de orçamento atingido");
    }
}
