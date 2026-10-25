using System.Reflection;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PRD-06b (D4, ADR 0023 §1 e ADR 0006): a confirmação do romaneio chama o estoque dentro da transação da Produção e desfaz tudo na falha; só
/// o motor e o serviço de séries gravam no estoque; os DTOs, o contrato e o PDF do romaneio não têm valor nem custo (não é documento
/// fiscal); placa e motorista são dado pessoal e não aparecem na lista.
/// </summary>
public class ShipmentRulesTests
{
    private static readonly string[] CostWords = ["Cost", "Amount", "Price", "Value", "Average"];

    private static string Production => Path.Combine(Solution.RootDirectory, "src", "Modules", "Production", "BemAqui.Production.Application");

    [Fact]
    public void Confirmacao_baixa_o_estoque_na_transacao_do_romaneio()
    {
        var confirm = File.ReadAllText(Path.Combine(Production, "ShipmentService.Confirm.cs"));
        confirm.Should().Contain("BeginTransactionAsync", "a Produção é dona da transação da expedição (ADR 0023 §1)");
        confirm.Should().Contain("PostManyAsync", "a saída Shipment é pelo motor (IStockMovements)");
        confirm.Should().Contain("serials.ShipAsync", "as séries vão a Shipped pelo IStockSerials");
        confirm.Should().Contain("RollbackAsync", "falta de saldo desfaz tudo (CA 5)");
        confirm.Should().Contain("StockSources.Shipment", "a origem do movimento é o romaneio");
    }

    [Fact]
    public void Evento_do_romaneio_nao_tem_consumidor_que_mexe_no_estoque()
    {
        var host = File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Host", "BemAqui.Host", "HostEvents.cs"));
        host.Should().Contain("AddEvent<BemAqui.Production.Contracts.ShipmentDispatched>()", "D4: a baixa é síncrona; o evento é só aviso e projeção");
        host.Should().NotContain("AddHandler<BemAqui.Production.Contracts.ShipmentDispatched");
        host.Should().NotContain("AddHandler<BemAqui.Production.Contracts.ShipmentReversed");
    }

    [Theory]
    [InlineData(typeof(ShipmentDetail))]
    [InlineData(typeof(ShipmentRow))]
    [InlineData(typeof(ShipmentLineView))]
    [InlineData(typeof(ShipmentSerialView))]
    [InlineData(typeof(ReadyToShipLine))]
    [InlineData(typeof(ReadyToShipProject))]
    [InlineData(typeof(ShipmentForm))]
    [InlineData(typeof(ShipmentImpactLine))]
    [InlineData(typeof(ShipmentPdfModel))]
    [InlineData(typeof(ShipmentPdfItem))]
    [InlineData(typeof(ShipmentInfo))]
    [InlineData(typeof(ShipmentLineInfo))]
    [InlineData(typeof(ShipmentDispatched))]
    [InlineData(typeof(FinishedGoodsStockLine))]
    [InlineData(typeof(Shipment))]
    [InlineData(typeof(ShipmentLine))]
    public void Romaneio_nao_tem_valor_nem_custo(Type type) =>
        type.GetProperties().Select(p => p.Name).Where(n => CostWords.Any(w => n.Contains(w, StringComparison.Ordinal)))
            .Should().BeEmpty($"{type.Name}: o romaneio não é documento fiscal e é lido com production.view (PRD-06b CA 3)");

    [Fact]
    public void Placa_e_motorista_sao_dado_pessoal_e_ficam_fora_da_lista()
    {
        typeof(Shipment).GetProperty(nameof(Shipment.DriverName))!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("CA 17: motorista mascarado no diff");
        typeof(Shipment).GetProperty(nameof(Shipment.VehiclePlate))!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("CA 17: placa mascarada no diff");
        typeof(ShipmentRow).GetProperties().Select(p => p.Name).Should().NotContain(["DriverName", "VehiclePlate"]);
        typeof(ShipmentInfo).GetProperties().Select(p => p.Name).Should().NotContain(["DriverName", "VehiclePlate"], "o contrato vai para outros módulos sem dado pessoal");
        typeof(ShipmentDispatched).GetProperties().Select(p => p.Name).Should().NotContain(["DriverName", "VehiclePlate", "DeliveryAddress"]);
    }
}
