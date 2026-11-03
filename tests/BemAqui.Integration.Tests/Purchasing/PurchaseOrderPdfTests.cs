using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Purchasing.Application.Documents;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03b CA 1, 8 e 9 sem banco: a montagem do modelo do PDF (conversão para a unidade do fornecedor, código do fornecedor, totais do
/// pedido, endereço de entrega = endereço da empresa) e o PDF gerado pelo QuestPDF com a Archivo, lido de volta pelo texto, sem custo,
/// margem nem projeto.
/// </summary>
public class PurchaseOrderPdfTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    private static readonly Guid Plate = Guid.CreateVersion7();
    private static readonly Guid Screw = Guid.CreateVersion7();

    private static PurchaseOrder Order(string? notes = "Entregar no portão 2.") => new()
    {
        Number = "PC-2026-0007", PartyId = Guid.CreateVersion7(), ProjectId = Guid.CreateVersion7(), Status = PurchaseOrderStatus.Approved, ExpectedDate = new DateOnly(2026, 11, 13),
        Subtotal = 1_250m, Freight = 80m, IpiPct = 5m, IpiAmount = 62.5m, Total = 1_392.5m, Notes = notes,
    };

    private static List<PurchaseOrderLine> Lines(Guid order) =>
    [
        new() { OrderId = order, Sequence = 2, ItemId = Screw, Quantity = 50m, UnitPrice = 1m, LineTotal = 50m },
        new() { OrderId = order, Sequence = 1, ItemId = Plate, Quantity = 100m, UnitPrice = 12m, LineTotal = 1_200m },
    ];

    private static readonly Dictionary<Guid, PurchaseOrderPdfItem> Items = new()
    {
        [Plate] = new("MP-010", "Chapa de aço 3 mm", "KG"),
        [Screw] = new("MP-020", "Parafuso M8", "UN"),
    };

    /// <summary>A chapa: o fornecedor vende em "PC" (peça) de 25 kg, com o código dele; o parafuso não tem código do fornecedor.</summary>
    private static readonly Dictionary<Guid, PurchaseOrderPdfSupplierCode> Codes = new() { [Plate] = new("GA-3MM", "PC", 25m) };

    private static readonly PurchaseOrderPdfCompany Company = new("Konebras", "Konebras Elevadores Ltda", "12.345.678/0001-90", "Rua das Indústrias, 100 — Distrito Industrial — Contagem/MG",
        "(31) 3333-0000 · compras@konebras.com.br", null);

    private static readonly PurchaseOrderPdfSupplier Supplier = new("Gama Aços", "Gama Aços Ltda", "11222333000181");

    private static PurchaseOrderPdfModel Model(PurchaseOrder order) => PurchaseOrderPdfBuilder.Build(order, Lines(order.Id), Items, Codes, Company, Supplier, "30/60/90 dias", Today);

    [Theory]
    [InlineData("100", "12", "PC", "25", "4", "300", "PC")]
    [InlineData("10", "3.333333", "CX", "3", "3.3333", "9.999999", "CX")]
    [InlineData("100", "12", null, "25", "100", "12", "KG")]
    [InlineData("100", "12", "PC", "0", "100", "12", "KG")]
    [InlineData("100", "12", "  ", "25", "100", "12", "KG")]
    public void Linha_na_unidade_do_fornecedor(string quantity, string price, string? supplierUnit, string factor, string expectedQuantity, string expectedPrice, string expectedUnit)
    {
        static decimal D(string v) => decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
        var (q, p, u) = PurchaseOrderRules.SupplierLine(D(quantity), D(price), "KG", supplierUnit, D(factor));
        q.Should().Be(D(expectedQuantity));
        p.Should().Be(D(expectedPrice));
        u.Should().Be(expectedUnit);
    }

    [Fact]
    public void Modelo_tem_linhas_em_ordem_com_codigo_e_unidade_do_fornecedor_totais_e_o_endereco_da_empresa()
    {
        var model = Model(Order());
        model.Lines.Select(l => l.Sequence).Should().Equal(1, 2);
        var plate = model.Lines[0];
        plate.Should().BeEquivalentTo(new PurchaseOrderPdfLine(1, "MP-010", "GA-3MM", "Chapa de aço 3 mm", 4m, "PC", 300m, 1_200m));
        model.Lines[1].Should().BeEquivalentTo(new PurchaseOrderPdfLine(2, "MP-020", null, "Parafuso M8", 50m, "UN", 1m, 50m));
        (model.Subtotal, model.Freight, model.IpiAmount, model.Total).Should().Be((1_250m, 80m, 62.5m, 1_392.5m));
        model.DeliveryAddress.Should().Be(Company.Address, "local de entrega = endereço da empresa (CAD-06, decisão do PO item 46)");
        model.Supplier.Document.Should().Be("11.222.333/0001-81");
        model.PaymentTerm.Should().Be("30/60/90 dias");
        model.IssueDate.Should().Be(Today);
    }

    [Fact]
    public void Modelo_nao_tem_campo_de_custo_margem_nem_projeto()
    {
        var names = typeof(PurchaseOrderPdfModel).GetProperties().Select(p => p.Name)
            .Concat(typeof(PurchaseOrderPdfLine).GetProperties().Select(p => p.Name))
            .Concat(typeof(PurchaseOrderPdfSupplier).GetProperties().Select(p => p.Name)).ToList();
        names.Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Margin", StringComparison.Ordinal) || n.Contains("Project", StringComparison.Ordinal));
    }

    private static readonly PdfFrame Frame = new(
        new PdfCompany("Konebras", "Konebras Elevadores Ltda", "12.345.678/0001-90", Company.Address, Company.Contact, null, true), "Ana Compras",
        new DateTimeOffset(2026, 10, 24, 13, 0, 0, TimeSpan.Zero));

    private static async Task<string> TextAsync(PurchaseOrderPdfModel model)
    {
        using var document = PdfDocument.Open(await new QuestPdfRenderer().RenderAsync(new PurchaseOrderPdfDocument(Frame, model)));
        document.GetPages().SelectMany(p => p.Letters).Select(l => l.FontName).Distinct().Should().OnlyContain(f => f.Contains("Archivo"), "ADR 0022: só a Archivo embutida");
        return string.Join(" ", document.GetPages().Select(p => p.Text));
    }

    /// <summary>
    /// EXP-03 CA 4: o pedido migrado para o frame mantém o conteúdo da CMP-03b, na mesma ordem (fornecedor, condição, previsão, local de
    /// entrega, linhas, totais, observação); só o cabeçalho e o rodapé passam a ser os do frame. Sem custo, margem nem projeto.
    /// </summary>
    [Fact]
    public async Task PDF_no_frame_com_o_mesmo_conteudo_do_pedido_e_sem_custo()
    {
        var text = await TextAsync(Model(Order()));
        string[] body =
        [
            "Fornecedor: Gama Aços", "Razão social: Gama Aços Ltda", "CNPJ/CPF: 11.222.333/0001-81", "Condição de pagamento: 30/60/90 dias", "Previsão de entrega: 13/11/2026",
            "Local de entrega: Rua das Indústrias, 100", "MP-010", "Forn.: GA-3MM", "Chapa de aço 3 mm", "R$ 300,00", "1.200,00", "MP-020", "Parafuso M8", "Subtotal", "1.250,00",
            "Frete", "80,00", "IPI (5%)", "62,50", "Total", "1.392,50", "Observações", "Entregar no portão 2.",
        ];
        var at = 0;
        foreach (var piece in body)
        {
            var found = text.IndexOf(piece, at, StringComparison.Ordinal);
            found.Should().BeGreaterThanOrEqualTo(0, $"\"{piece}\" vem depois do anterior, como no layout da CMP-03b");
            at = found + piece.Length;
        }

        text.Should().Contain("Pedido de compra").And.Contain("PC-2026-0007").And.Contain("Data: 24/10/2026").And.Contain("Emitido por Ana Compras");
        text.Should().NotContainAny("Custo", "custo médio", "Margem", "Projeto", "PRJ-", "Rascunho", "RASCUNHO");
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, "Rascunho · Pedido não aprovado: não envie ao fornecedor.")]
    [InlineData(PurchaseOrderStatus.PendingApproval, "Rascunho · Pedido não aprovado: não envie ao fornecedor.")]
    [InlineData(PurchaseOrderStatus.Cancelled, "Cancelado")]
    public async Task Marca_por_situacao(PurchaseOrderStatus status, string band)
    {
        var order = Order();
        order.Status = status;
        (await TextAsync(Model(order))).Should().Contain(band);
        PurchaseOrderPdf.Mark(PurchaseOrderStatus.Sent).Should().Be(PdfMark.None);
        PurchaseOrderPdf.Mark(PurchaseOrderStatus.Received).Should().Be(PdfMark.None);
    }

    [Fact]
    public async Task PDF_de_300_linhas_quebra_pagina_com_o_cabecalho_da_tabela()
    {
        var order = Order(notes: null);
        var many = Enumerable.Range(1, PurchaseOrderRules.PdfMaxLines)
            .Select(i => new PurchaseOrderLine { OrderId = order.Id, Sequence = i, ItemId = Plate, Quantity = 1m, UnitPrice = 1m, LineTotal = 1m }).ToList();
        var model = PurchaseOrderPdfBuilder.Build(order, many, Items, Codes, Company, Supplier, null, Today);
        var bytes = await new QuestPdfRenderer().RenderAsync(new PurchaseOrderPdfDocument(Frame, model));
        using var document = PdfDocument.Open(bytes);
        document.NumberOfPages.Should().BeGreaterThan(1);
        document.GetPages().Should().OnlyContain(p => p.Text.Contains("Preço unit."));
        string.Join(" ", document.GetPages().Select(p => p.Text)).Should().Contain("Condição de pagamento: a combinar");
    }
}
