using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-03b, regras sem banco: o prazo e a contagem, inclusive no limite das 24 h (CA 1 e 4); os limites de texto e as 20 cartas (CA 8); as
/// faixas contínuas e os números proibidos (CA 10 e 11); a trava do envio (CA 15) e a expiração do pedido (CA 2).
/// </summary>
public class FiscalEventRulesTests
{
    private static readonly DateTimeOffset Authorized = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static OutgoingInvoice AuthorizedInvoice()
    {
        var invoice = OutgoingInvoice.NewDraft(Guid.CreateVersion7(), "ROM-1", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, Authorized);
        invoice.BeginSubmission(new string('a', 64), Authorized);
        invoice.Apply(new EmitterNfeState(Guid.CreateVersion7(), FiscalDocumentStatus.Authorized, "3".PadRight(44, '1'), 2, 42, "135", Authorized, null, null, null), Authorized);
        return invoice;
    }

    [Fact]
    public void Prazo_conta_da_autorizacao_e_no_instante_do_fim_ja_esta_fora()
    {
        var deadline = FiscalEventRules.Deadline(Authorized, 24);

        deadline.Should().Be(Authorized.AddHours(24));
        FiscalEventRules.WithinWindow(deadline, Authorized.AddHours(24).AddSeconds(-1)).Should().BeTrue();
        FiscalEventRules.WithinWindow(deadline, Authorized.AddHours(24)).Should().BeFalse("o limite é exclusivo");
        FiscalEventRules.Deadline(null, 24).Should().BeNull("sem autorização, sem prazo");
        OutgoingInvoiceService.Remaining(TimeSpan.FromMinutes(192)).Should().Be("3 h 12 min");
        OutgoingInvoiceService.Remaining(TimeSpan.FromMinutes(45)).Should().Be("45 min");
        OutgoingInvoiceService.Remaining(TimeSpan.FromSeconds(30)).Should().Be("menos de 1 min");
    }

    [Fact]
    public void Cancelar_so_a_autorizada_dentro_do_prazo_sem_carta_em_andamento_e_sem_reenvio_imediato()
    {
        var invoice = AuthorizedInvoice();
        var deadline = FiscalEventRules.Deadline(invoice.AuthorizedAt, 24);

        invoice.BeginCancellation("Cliente desistiu da compra", null, Authorized.AddHours(25), deadline)!.Code.Should().Be(FiscalErrorCodes.CancellationWindowClosed);
        invoice.BeginCorrection(null, Authorized.AddHours(1)).Should().BeNull();
        invoice.BeginCancellation("Cliente desistiu da compra", null, Authorized.AddHours(1), deadline)!.Code.Should().Be(FiscalErrorCodes.EventInProgress, "carta em andamento (CA 15)");
        invoice.CorrectionFinished();

        invoice.BeginCancellation("Cliente desistiu da compra", null, Authorized.AddHours(1), deadline).Should().BeNull();
        invoice.CancellationSentAt.Should().Be(Authorized.AddHours(1));
        invoice.BeginCorrection(null, Authorized.AddHours(1))!.Code.Should().Be(FiscalErrorCodes.EventInProgress);
        invoice.BeginCancellation("Cliente desistiu da compra", null, Authorized.AddHours(1).AddMinutes(1), deadline)!.Code.Should().Be(FiscalErrorCodes.EventInProgress, "duplo clique");
        invoice.BeginCancellation("Cliente desistiu da compra", null, Authorized.AddHours(1).AddMinutes(3), deadline).Should().BeNull("sem resposta, reenvia depois de 2 min");

        invoice.MarkCancelled("135260000000099", Authorized.AddHours(2)).Should().BeTrue();
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Cancelled);
        invoice.CancellationSentAt.Should().BeNull();
        invoice.MarkCancellationPublished(new DateOnly(2026, 11, 1), Authorized.AddHours(2)).Should().BeTrue();
        invoice.MarkCancellationPublished(new DateOnly(2026, 11, 1), Authorized.AddHours(3)).Should().BeFalse("o InvoiceCancelled vai uma vez só");
        invoice.BeginCancellation("Cliente desistiu da compra", null, Authorized.AddHours(3), deadline)!.Code.Should().Be(FiscalErrorCodes.NotAuthorized);
        invoice.IsActive.Should().BeFalse("o romaneio volta a poder receber outra nota");
    }

    [Theory]
    [InlineData("curto demais", false)]
    [InlineData("Cliente desistiu", true)]
    [InlineData("  Cliente \u0007 desistiu  ", true)]
    public void Justificativa_tem_15_a_255_caracteres_depois_do_saneamento(string text, bool ok)
    {
        var (clean, error) = FiscalEventRules.Justification(text);

        (error is null).Should().Be(ok);
        if (ok)
        {
            clean.Should().Be("Cliente desistiu");
        }

        FiscalEventRules.Justification(new string('a', 256)).Error.Should().Be("Escreva o motivo com 15 a 255 caracteres.");
        FiscalEventRules.Correction(new string('a', 1000)).Error.Should().BeNull();
        FiscalEventRules.Correction(new string('a', 1001)).Error.Should().Be("Escreva a correção com 15 a 1000 caracteres.");
        FiscalEventRules.Correction("Pequeno").Error.Should().NotBeNull();
    }

    [Fact]
    public void Numeros_viram_faixas_continuas_por_serie_e_nota_autorizada_ou_cancelada_bloqueia()
    {
        var ranges = FiscalEventRules.Ranges([(2, 12), (2, 10), (2, 11), (2, 20), (3, 5), (2, 11)]);

        ranges.Should().Equal(new NumberRange(2, 10, 12), new NumberRange(2, 20, 20), new NumberRange(3, 5, 5));
        ranges[0].Label.Should().Be("10 a 12");
        ranges[0].Count.Should().Be(3);
        ranges[1].Label.Should().Be("20");
        FiscalEventRules.BlockingSituation(OutgoingInvoiceStatus.Authorized).Should().Be("autorizada");
        FiscalEventRules.BlockingSituation(OutgoingInvoiceStatus.Cancelled).Should().Be("cancelada");
        FiscalEventRules.BlockingSituation(OutgoingInvoiceStatus.Rejected).Should().BeNull("a rejeitada é justamente o número a inutilizar");
        FiscalEventRules.Blocked(21, "autorizada").Should().Be("O número 21 tem nota autorizada e não pode ser inutilizado.");
    }

    [Fact]
    public void Numero_a_inutilizar_e_so_da_rejeitada_e_a_inutilizacao_trava_ate_o_desfecho()
    {
        var rejected = new NumberGap { Series = 2, Number = 10, Reason = NumberGapReasons.Rejected, CreatedAt = Authorized };
        var denied = new NumberGap { Series = 2, Number = 11, Reason = NumberGapReasons.Denied, CreatedAt = Authorized };
        var first = Guid.CreateVersion7();

        denied.BeginInvalidation(first, null, Authorized)!.Code.Should().Be(FiscalErrorCodes.NumberNotInvalidable, "a denegada fica só registrada");
        rejected.BeginInvalidation(first, null, Authorized).Should().BeNull();
        rejected.BeginInvalidation(Guid.CreateVersion7(), Authorized, Authorized.AddMinutes(1))!.Code.Should().Be(FiscalErrorCodes.EventInProgress);
        rejected.InvalidationRefused(first);
        rejected.InvalidationEventId.Should().BeNull();
        rejected.Invalidate(first, Authorized);
        rejected.Status.Should().Be(NumberGapStatus.Invalidated);
        rejected.ToInvalidate.Should().BeFalse();
    }

    [Fact]
    public void Pedido_expira_so_no_fim_do_prazo_e_decidido_nao_muda_mais()
    {
        var request = new CancellationRequest { InvoiceId = Guid.CreateVersion7(), Reason = "Cliente desistiu da compra", RequestedAt = Authorized, ExpiresAt = Authorized.AddHours(24) };

        request.Expire(Authorized.AddHours(23)).Should().BeFalse();
        request.Expire(Authorized.AddHours(24)).Should().BeTrue();
        request.Status.Should().Be(CancellationRequestStatus.Expired);
        request.Decline(null, "Não é o caso de cancelar", Authorized.AddHours(25)).Should().BeFalse();
        request.Complete(null, Authorized.AddHours(25)).Should().BeFalse();
    }
}
