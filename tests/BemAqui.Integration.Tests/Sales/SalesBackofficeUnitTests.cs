using BemAqui.Dashboard.Application;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>PDV-07: regras puras da retaguarda de vendas e dos cards da loja (alerta de cancelamentos da PP-22, ticket médio, máscara e chips).</summary>
public class SalesBackofficeUnitTests
{
    [Theory]
    [InlineData(3, 97, 3, false)] // exatamente 3% não passa
    [InlineData(4, 96, 4, true)]
    [InlineData(2, 98, 2, false)]
    [InlineData(5, 95, 5, true)]
    [InlineData(0, 0, 0, false)] // turno sem vendas
    public void Alerta_de_cancelamentos_passa_de_3_porcento_das_vendas_do_turno(decimal cancelled, decimal done, decimal expectedPct, bool alertAt3)
    {
        var pct = StoreSalesRules.CancelledPercent(cancelled, done);
        pct.Should().Be(expectedPct);
        StoreSalesRules.CancelAlert(pct, 3m).Should().Be(alertAt3);
    }

    [Fact]
    public void Alerta_zero_desliga_e_ticket_medio_sem_venda_e_nulo()
    {
        StoreSalesRules.CancelAlert(50m, 0m).Should().BeFalse();
        StoreSalesRules.Ticket(100m, 0).Should().BeNull();
        StoreSalesRules.Ticket(100m, 3).Should().Be(33.33m);
    }

    [Theory]
    [InlineData("529.982.247-25", "***.982.247-**")]
    [InlineData("11222333000181", "**.***.333/0001-**")]
    [InlineData("", "***")]
    public void Documento_do_consumidor_sai_mascarado_na_lista(string document, string expected) =>
        SaleCustomerDocumentService.Mask(document).Should().Be(expected);

    [Fact]
    public void Chips_da_venda_na_ordem_da_tela()
    {
        var row = new SaleListItem(Guid.NewGuid(), "LJ-01.1-000001", DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.Today), SaleChannel.Pos, null, null, null, null, null, null,
            10m, [], SaleStatus.Completed, SaleStockStatus.Shortfall, HasDivergence: true, Offline: true, CustomerDocument: null);
        SaleBackofficeService.Chips(row).Should().Equal("Concluída", "Estoque: falta de saldo", "Com divergência", "Feita sem internet");
        SaleBackofficeService.Chips(row with { Status = SaleStatus.Cancelled, StockStatus = SaleStockStatus.Reversed, HasDivergence = false, Offline = false })
            .Should().Equal("Cancelada");
    }
}
