using BemAqui.Fiscal.Application.Emitter;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>Pedido de NF-e de teste (venda de um elevador de R$ 1.000,00 em SP), com destinatário canário para os testes de log.</summary>
public static class FiscalSamples
{
    public const string RecipientName = "CLIENTE DE TESTE LTDA";
    public const string RecipientEmail = "fiscal@exemplo.com.br";

    public static NfeIssueRequest Sample(string nature = "VENDA DE MERCADORIA", decimal total = 1000m) => new(
        "47225005000186", 2, nature, NfeOperationType.Outbound, NfePurpose.Normal, false, NfePresence.InPerson, NfeFreightMode.NoTransport,
        new NfeRecipient("11222333000181", null, RecipientName, NfeStateRegistration.Taxpayer, "111111111111", RecipientEmail,
            new NfeAddress("RUA DO DESTINATARIO", "200", null, "CENTRO", "3550308", "SAO PAULO", "SP", "01001000")),
        [new NfeItem("ELEV-001", "ELEVADOR DE CARGA", "84281000", null, "5101", "UN", 1m, total, total,
            new NfeItemTaxes(0, new NfeIcms("00", total, 18m, total * 0.18m), new NfePisCofins("01", total, 0.65m, total * 0.0065m), new NfePisCofins("01", total, 3m, total * 0.03m),
                IbsCbs: new NfeIbsCbs("000", "000001", total, new NfeTaxRate(0.10m, total * 0.001m), new NfeTaxRate(0m, 0m), new NfeTaxRate(0.90m, total * 0.009m))))],
        [new NfePayment(NfePaymentMethod.BankDeposit, total)]);
}
