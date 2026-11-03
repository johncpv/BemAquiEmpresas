using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-01: PDF da ordem de pagamento no modelo único, com a marca pela situação e a auditoria.</summary>
public partial class PaymentOrderTests
{
    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return PayablesTests.Flat(string.Join(" ", document.GetPages().Select(p => p.Text)));
    }

    private static async Task<string> PdfAsync(HttpClient client, Guid orderId)
    {
        var response = await client.GetAsync($"{Base}/{orderId}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        return PdfText(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PDF_da_ordem_sai_como_rascunho_antes_de_aprovar_sem_marca_depois_e_cancelado_ao_cancelar()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (financeId, finance) = await ActorAsync("finance");
        var supplier = await SupplierAsync(owner);
        var payable = await PayableAsync(finance, supplier);
        var submitted = await finance.SendAsync(Post(Base, Items((payable.GetProperty("id").GetGuid(), 950m))));
        submitted.StatusCode.Should().Be(HttpStatusCode.Created, await submitted.Content.ReadAsStringAsync());
        var order = await JsonAsync(submitted);
        var orderId = order.GetProperty("summary").GetProperty("id").GetGuid();
        var number = order.GetProperty("summary").GetProperty("number").GetString()!;

        var response = await finance.GetAsync($"{Base}/{orderId}/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be(number + ".pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var draft = PdfText(await response.Content.ReadAsByteArrayAsync());
        draft.Should().Contain(PayablesTests.Flat("Ordem de pagamento")).And.Contain(number).And.Contain("RASCUNHO").And.Contain(PayablesTests.Flat("Aguardando aprovação"))
            .And.Contain(payable.GetProperty("number").GetString()).And.Contain("950,00").And.Contain(PayablesTests.Flat("Aguardando a primeira decisão"))
            .And.Contain(PayablesTests.Flat("Emitido por Usuário de teste")).And.Contain("CNPJ");
        draft.Should().NotContain("52998224725", "a chave Pix e o CPF do titular saem mascarados, como na tela");

        var approved = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = "Pode pagar", version = Version(order) }));
        approved.GetProperty("summary").GetProperty("status").GetString().Should().Be("Approved");
        var final = await PdfAsync(finance, orderId);
        final.Should().NotContain("RASCUNHO").And.Contain(PayablesTests.Flat("Aprovada por Usuário de teste")).And.Contain(PayablesTests.Flat("Pode pagar"));

        (await owner.PostAsJsonAsync($"{Base}/{orderId}/cancel", new { reason = "Fornecedor pediu para segurar", version = Version(approved) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PdfAsync(finance, orderId)).Should().Contain("CANCELADO").And.Contain(PayablesTests.Flat("Fornecedor pediu para segurar"));

        // Cada PDF gerado entra na auditoria da ordem (quem, quando, formato).
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "PaymentOrder" && a.EntityId == orderId.ToString() && a.Action == AuditActions.Exported && a.UserId == financeId)))
            .Should().Be(3);
    }

    [Fact]
    public async Task Sem_acesso_ao_financeiro_nao_baixa_o_PDF_e_ordem_inexistente_responde_404()
    {
        var (_, finance) = await ActorAsync("finance");
        (await finance.GetAsync($"{Base}/{Guid.NewGuid()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (_, stranger) = await ActorAsync(null);
        (await stranger.GetAsync($"{Base}/{Guid.NewGuid()}/pdf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
