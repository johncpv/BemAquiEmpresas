using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>EXP-05 CA 16, sem banco: a última linha do livro simples soma só as notas autorizadas e os impostos saem pelo código do tributo.</summary>
public class OutgoingBookUnitTests
{
    private static OutgoingBookRow Row(decimal products, decimal icms, decimal ipi, decimal total, OutgoingInvoiceStatus status) =>
        new("1", 1, null, new DateOnly(2026, 11, 3), "Cliente", "47.225.005/0001-86", "5101", products, icms, ipi, total, OutgoingBook.StatusLabel(status), false);

    [Fact]
    public void Total_das_autorizadas_ignora_canceladas_rejeitadas_e_rascunhos()
    {
        OutgoingInvoiceStatus[] statuses = [OutgoingInvoiceStatus.Authorized, OutgoingInvoiceStatus.Cancelled, OutgoingInvoiceStatus.Authorized, OutgoingInvoiceStatus.Rejected, OutgoingInvoiceStatus.Draft];
        var rows = statuses.Select((s, i) => Row(100m * (i + 1), 18m * (i + 1), 5m * (i + 1), 123m * (i + 1), s)).ToList();

        var total = OutgoingBook.AuthorizedTotal(rows, statuses);

        total.IsTotal.Should().BeTrue();
        total.Number.Should().Be(OutgoingBook.TotalLabel);
        total.Recipient.Should().Be("2 notas");
        total.Products.Should().Be(400m, "a 1ª e a 3ª");
        total.Icms.Should().Be(72m);
        total.Ipi.Should().Be(20m);
        total.Total.Should().Be(492m);
        rows[1].Status.Should().Be("Cancelada");
    }

    [Fact]
    public void Imposto_pelo_codigo_do_tributo()
    {
        var totals = new InvoiceTotals(1000m, 0m, 0m, 0m, [new TaxTotal(TaxCodes.Icms, 1000m, 180m), new TaxTotal(TaxCodes.Ipi, 1000m, 50m), new TaxTotal(TaxCodes.Pis, 1000m, 6.5m)], 1050m, 0m);
        OutgoingBook.Tax(totals, TaxCodes.Icms).Should().Be(180m);
        OutgoingBook.Tax(totals, TaxCodes.Ipi).Should().Be(50m);
        OutgoingBook.Tax(totals, TaxCodes.IcmsSt).Should().Be(0m);
    }
}
