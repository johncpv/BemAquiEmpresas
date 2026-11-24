using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// CX-03, unidade: agrupamento dos títulos da sessão de caixa por adquirente, bandeira, forma e vencimento (CA 2); um título por parcela no
/// crédito parcelado (CA 3); arredondamento do líquido com a sobra de centavos na última parcela; Pix "Não localizado" fora do título (CA 4).
/// </summary>
public class CashSessionReceivablesUnitTests
{
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly Guid Acquirer = Guid.NewGuid();
    private static readonly Guid Credit = Guid.NewGuid();
    private static readonly Guid Installments = Guid.NewGuid();
    private static readonly Guid Pix = Guid.NewGuid();
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 20);

    private static CashSessionPayment Pay(Guid method, PaymentMethodType type, decimal amount, CardBrand? brand = null, int installments = 1, decimal fee = 0, int days = 0, DateOnly? day = null) =>
        new(day ?? Day, Guid.NewGuid(), method, type, type.ToString(), type is PaymentMethodType.Pix or PaymentMethodType.Cash ? null : Acquirer, brand, installments, amount, fee, days, null);

    [Fact]
    public void Cartao_agrupa_por_adquirente_bandeira_forma_e_vencimento_com_bruto_taxa_e_liquido()
    {
        var result = CashSessionReceivables.Build(Session, [
            Pay(Credit, PaymentMethodType.Credit, 100m, CardBrand.Visa, fee: 3m, days: 30),
            Pay(Credit, PaymentMethodType.Credit, 50m, CardBrand.Visa, fee: 3m, days: 30),
            Pay(Credit, PaymentMethodType.Credit, 80m, CardBrand.Elo, fee: 3m, days: 30),
            Pay(Credit, PaymentMethodType.Credit, 10m, CardBrand.Visa, fee: 3m, days: 30, day: Day.AddDays(1)),
        ], new Dictionary<Guid, decimal>(), Day);

        result.Should().HaveCount(3);
        var visa = result.Single(r => r.Key.Brand == "Visa" && r.Key.BusinessDate == Day);
        (visa.Gross, visa.ExpectedFee, visa.Net, visa.Key.DueDate, visa.Origin, visa.PartyId).Should().Be((150m, 4.50m, 145.50m, Day.AddDays(30), DocumentOrigin.CardSettlement, (Guid?)Acquirer));
        visa.Key.CashSessionId.Should().Be(Session);
        result.Single(r => r.Key.Brand == "Elo").Net.Should().Be(77.60m);
        result.Single(r => r.Key.BusinessDate == Day.AddDays(1)).Key.DueDate.Should().Be(Day.AddDays(31), "o vencimento é a data da venda + D+N");
    }

    [Fact]
    public void Parcelado_tem_um_titulo_por_parcela_com_prazo_e_centavos_na_ultima()
    {
        var result = CashSessionReceivables.Build(Session, [Pay(Installments, PaymentMethodType.CreditInstallments, 100m, CardBrand.Mastercard, installments: 3, fee: 4.99m, days: 30)],
            new Dictionary<Guid, decimal>(), Day);

        result.Should().HaveCount(3);
        var parcels = result.OrderBy(r => r.Key.DueDate).ToList();
        parcels.Select(p => p.Key.DueDate).Should().Equal(Day.AddDays(30), Day.AddDays(60), Day.AddDays(90));
        parcels.Select(p => p.Gross).Should().Equal(33.33m, 33.33m, 33.34m);
        parcels.Select(p => p.ExpectedFee).Should().Equal(1.66m, 1.66m, 1.67m);
        parcels.Sum(p => p.ExpectedFee).Should().Be(4.99m, "a taxa do pagamento é arredondada uma vez e dividida");
        parcels.Should().OnlyContain(p => p.Net == p.Gross - p.ExpectedFee);
    }

    [Fact]
    public void Pix_nao_localizado_sai_do_titulo_e_vira_titulo_de_quebra()
    {
        var result = CashSessionReceivables.Build(Session, [Pay(Pix, PaymentMethodType.Pix, 70m), Pay(Pix, PaymentMethodType.Pix, 30m), Pay(Cash, PaymentMethodType.Cash, 40m)],
            new Dictionary<Guid, decimal> { [Pix] = 30m }, Day);

        var pix = result.Single(r => r.Key.PaymentMethodId == Pix && !r.Key.Break);
        (pix.Gross, pix.Net, pix.Origin, pix.Key.DueDate).Should().Be((70m, 70m, DocumentOrigin.Sale, Day));
        var lost = result.Single(r => r.Key.Break);
        (lost.Gross, lost.Net, lost.PartyId).Should().Be((30m, 30m, (Guid?)null));
        result.Single(r => r.Key.PaymentMethodId == Cash).Gross.Should().Be(40m);
        result.Select(r => r.Key.Text).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Chave_sem_bandeira_nem_quebra_e_a_mesma_da_VND_01()
    {
        var key = new SaleReceivableKey(Day, "Online", null, Pix, null, Day);
        key.Text.Should().NotContain("quebra").And.EndWith("|-");
        (key with { Brand = "Visa" }).Text.Should().EndWith("|Visa");
        CashSessionReceivables.Split(10m, 3).Should().Equal(3.33m, 3.33m, 3.34m);
    }
}
