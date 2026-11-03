using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>EXP-04 CA 3, 7, 8 e 14, sem banco: modelos sem valor do instalador nem custo, marcas por situação e o custo da NC só com a ação.</summary>
public class ProductionPdfRulesTests
{
    /// <summary>CA 3 e 7: a OS de instalação e o termo de aceite não têm de onde tirar valor, custo ou preço.</summary>
    [Fact]
    public void Modelos_da_OS_de_instalacao_e_do_termo_nao_tem_valor_nem_custo()
    {
        var names = new[]
            {
                typeof(InstallationPdfModel), typeof(InstallationPdfStep), typeof(InstallationPdfCheck), typeof(AcceptanceTermPdfModel), typeof(AcceptanceTermEquipment),
                typeof(AcceptanceTermCheck),
            }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();
        names.Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Price", StringComparison.Ordinal) || n.Contains("Value", StringComparison.Ordinal)
            || n.Contains("Amount", StringComparison.Ordinal) || n.Contains("Margin", StringComparison.Ordinal));
    }

    [Fact]
    public void Marcas_da_OS_de_instalacao_do_termo_e_da_NC()
    {
        InstallationPdf.Mark(ProductionOrderStatus.Cancelled, " Obra embargada ").Should().Be((PdfMark.Cancelled, "Motivo: Obra embargada"));
        InstallationPdf.Mark(ProductionOrderStatus.InProgress, null).Should().Be((PdfMark.None, (string?)null));
        InstallationPdf.TermNotice(ProductionOrderStatus.Completed, new DateOnly(2026, 11, 3)).Should().Be("Aceite registrado em 03/11/2026");
        InstallationPdf.TermNotice(ProductionOrderStatus.InProgress, null).Should().BeNull("termo em branco até o aceite");
        InstallationPdf.Letters(["Preparação da obra", "Montagem na obra", "Entrega técnica"]).Should().Equal("P", "M", "E");
        InstallationPdf.CurrentIndex([true, false, false]).Should().Be(1);
        InstallationPdf.CurrentIndex([true, true]).Should().Be(2, "todas concluídas: tudo cheio");

        NonConformityPdf.Notice(NonConformityStatus.Open).Should().Be("Em aberto");
        NonConformityPdf.Notice(NonConformityStatus.InAction).Should().Be("Em aberto");
        NonConformityPdf.Notice(NonConformityStatus.Closed).Should().BeNull();
        NonConformityPdf.Cost(350m, visible: false).Should().BeNull("CA 14: custo estimado só com projects.view_cost");
        NonConformityPdf.Cost(350m, visible: true).Should().Be(350m);
    }
}
