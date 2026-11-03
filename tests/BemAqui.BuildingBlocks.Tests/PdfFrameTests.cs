using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Pdf;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>EXP-01 (ADR 0045 §1): modelo único de PDF, com cabeçalho, título, rodapé "Emitido por … · Página x de y" e as marcas padrão.</summary>
public partial class PdfFrameTests
{
    internal static readonly PdfFrame Frame = new(
        new PdfCompany("Konebras", "Konebras Elevadores Ltda", "47.225.005/0001-86", "Rua A, 1 · Taubaté/SP", "(12) 3333-4444", null, true),
        "Fulano de Tal", new DateTimeOffset(2026, 11, 2, 15, 30, 0, TimeSpan.Zero));

    /// <summary>Texto do PDF sem espaços (a extração junta ou separa palavras conforme a posição) e se a página 1 está deitada.</summary>
    internal static string Text(byte[] pdf, out bool landscape)
    {
        using var document = PdfDocument.Open(pdf);
        var first = document.GetPage(1);
        landscape = first.Width > first.Height;
        return Spaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private sealed class Sample(PdfDocumentHeader header, int lines) : PdfDocumentFrame(PdfFrameTests.Frame, header)
    {
        protected override void ComposeContent(IContainer container) => container.Column(col =>
        {
            for (var i = 1; i <= lines; i++)
            {
                col.Item().Text($"Linha {i} do conteúdo");
            }
        });
    }

    private static async Task<string> RenderAsync(PdfDocumentHeader header, int lines = 3) => Text(await new QuestPdfRenderer().RenderAsync(new Sample(header, lines)), out _);

    [Fact]
    public async Task Cabecalho_titulo_e_rodape_padrao()
    {
        var text = await RenderAsync(new PdfDocumentHeader("Ordem de pagamento", "OP-2026-0001", new DateOnly(2026, 11, 2)));
        text.Should().Contain("KonebrasElevadoresLtda").And.Contain("CNPJ47.225.005/0001-86").And.Contain("Ordemdepagamento").And.Contain("OP-2026-0001")
            .And.Contain("Data:02/11/2026").And.Contain("EmitidoporFulanodeTalem02/11/202612:30").And.Contain("Página1de1");
        text.Should().NotContain("RASCUNHO").And.NotContain("Rascunho");
        PdfDocumentFrame.Issued(Frame).Should().Be("Emitido por Fulano de Tal em 02/11/2026 12:30", "horário de São Paulo");
    }

    [Theory]
    [InlineData(PdfMark.Draft, "Rascunho", "RASCUNHO")]
    [InlineData(PdfMark.Simulation, "Simulação", "SIMULAÇÃO")]
    [InlineData(PdfMark.Cancelled, "Cancelado", "CANCELADO")]
    [InlineData(PdfMark.Preview, "Prévia,semvalorfiscal", "PRÉVIA")]
    public async Task Marca_padrao_em_faixa_e_marca_dagua_em_todas_as_paginas(PdfMark mark, string band, string watermark)
    {
        var pdf = await new QuestPdfRenderer().RenderAsync(new Sample(new PdfDocumentHeader("Documento", "X-1", null, mark), 160));
        using var document = PdfDocument.Open(pdf);
        document.NumberOfPages.Should().BeGreaterThan(1);
        foreach (var page in document.GetPages())
        {
            var text = Spaces().Replace(page.Text, string.Empty);
            text.Should().Contain(band).And.Contain(watermark);
        }
    }

    [Fact]
    public void Rotulos_das_marcas()
    {
        PdfMarks.Label(PdfMark.Preview).Should().Be("Prévia, sem valor fiscal");
        PdfMarks.Label(PdfMark.None).Should().BeEmpty();
        PdfMarks.Watermark(PdfMark.Draft).Should().Be("RASCUNHO");
    }

    /// <summary>EXP-03: o aviso vai na faixa junto da marca, ou sozinho (sem marca d'água); o QR só aceita URL https absoluta.</summary>
    [Fact]
    public async Task Aviso_na_faixa_com_ou_sem_marca_e_QR_so_com_https()
    {
        var notice = await RenderAsync(new PdfDocumentHeader("Orçamento", "ORC-1", Notice: "Validade vencida em 01/10/2026: reabra com uma nova revisão."));
        notice.Should().Contain("Validadevencidaem01/10/2026:reabracomumanovarevisão.").And.NotContain("PRÉVIA").And.NotContain("RASCUNHO").And.NotContain("CANCELADO");
        (await RenderAsync(new PdfDocumentHeader("Pedido", "PC-1", Mark: PdfMark.Draft, Notice: "Pedido não aprovado: não envie ao fornecedor.")))
            .Should().Contain("Rascunho·Pedidonãoaprovado:nãoenvieaofornecedor.").And.Contain("RASCUNHO");
        PdfMarks.BandText(PdfMark.None, "  ").Should().BeNull();

        PdfQr.Svg("https://app.konebras.com.br/fab/etapa/1").Should().StartWith("<svg");
        var act = () => PdfQr.Svg("http://evil.example/x");
        act.Should().Throw<ArgumentException>();
        ((Action)(() => PdfQr.Svg("/fab/etapa/1"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Paisagem_quando_pedido()
    {
        var pdf = await new QuestPdfRenderer().RenderAsync(new Sample(new PdfDocumentHeader("Lista", Landscape: true), 1));
        Text(pdf, out var landscape).Should().Contain("Lista");
        landscape.Should().BeTrue();
    }
}
