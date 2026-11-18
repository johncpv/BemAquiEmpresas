using BemAqui.Catalog.Application;
using BemAqui.Catalog.Domain;
using ClosedXML.Excel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>CAT-02: validação por linha da importação (CA 2), números em pt-BR, o modelo (CA 1), a leitura e o limite de linhas (CA 7).</summary>
public class CatalogImportRulesTests
{
    private const string Gtin = "7891000315507";

    private static string?[] Row(string? code = "P-1", string? name = "Café 500 g", string? unit = null, string? ncm = null, string? gtin = null, string? price = null,
        string? cost = null, string? stock = null, string? category = null) => [code, name, unit, ncm, gtin, price, cost, stock, category];

    [Fact]
    public void Linha_completa_e_valida() =>
        ProductImportRules.Validate(2, Row(ncm: "0901.21.00", gtin: Gtin, price: "19,90", cost: "11,5", stock: "12", category: "Mercearia"), true, false).Should().BeNull();

    [Theory]
    [InlineData(null, null, "Linha 14: informe o código interno ou o código de barras, para a planilha poder ser importada de novo sem duplicar.")]
    [InlineData("P-1", "7891000315508", "Linha 14: código de barras inválido.")]
    public void Chave_e_codigo_de_barras(string? code, string? gtin, string expected) =>
        ProductImportRules.Validate(14, Row(code: code, gtin: gtin), true, false).Should().Be(expected);

    [Fact]
    public void Nome_so_e_obrigatorio_ao_criar()
    {
        ProductImportRules.Validate(3, Row(name: null), creating: true, false).Should().Be("Linha 3: informe o nome do produto.");
        ProductImportRules.Validate(3, Row(name: null), creating: false, false).Should().BeNull();
    }

    [Theory]
    [InlineData("0", "Linha 5: preço de venda inválido. Use um valor maior que zero, com até 2 casas.")]
    [InlineData("1,999", "Linha 5: preço de venda inválido. Use um valor maior que zero, com até 2 casas.")]
    [InlineData("abc", "Linha 5: preço de venda inválido. Use um valor maior que zero, com até 2 casas.")]
    public void Preco_invalido(string price, string expected) => ProductImportRules.Validate(5, Row(price: price), true, false).Should().Be(expected);

    [Fact]
    public void Sem_permissao_de_preco_a_coluna_nem_e_conferida() => ProductImportRules.Validate(5, Row(price: "abc"), true, pricesIgnored: true).Should().BeNull();

    [Fact]
    public void Saldo_inicial_pede_custo_maior_que_zero()
    {
        ProductImportRules.Validate(7, Row(stock: "10"), true, false).Should().Be("Linha 7: para lançar o saldo inicial, informe o custo (maior que zero).");
        ProductImportRules.Validate(7, Row(stock: "-1", cost: "2"), true, false).Should().StartWith("Linha 7: saldo inicial inválido");
        ProductImportRules.Validate(7, Row(stock: "0"), true, false).Should().BeNull("saldo zero não lança nada");
    }

    [Fact]
    public void NCM_com_8_digitos() => ProductImportRules.Validate(9, Row(ncm: "0901"), true, false).Should().Be("Linha 9: NCM inválido. Use os 8 dígitos.");

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("R$ 19,90", 19.90)]
    [InlineData("12.5", 12.5)]
    [InlineData("7", 7)]
    public void Numero_em_pt_BR_ou_com_ponto(string text, double expected) => ProductImportRules.ParseNumber(text).Should().Be((decimal)expected);

    [Fact]
    public void Modelo_tem_a_aba_de_ajuda_primeiro_e_as_colunas_do_CA_1()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ProductImportSheet.Template()));
        workbook.Worksheets.First().Name.Should().Be(ProductImportSheet.HelpSheet);
        var data = workbook.Worksheet(ProductImportSheet.DataSheet);
        Enumerable.Range(1, 9).Select(c => data.Cell(1, c).GetString()).Should().Equal(
            "Código interno", "Nome", "Unidade", "NCM", "Código de barras", "Preço de venda", "Custo", "Saldo inicial", "Categoria");
        ProductImportSheet.Read(ProductImportSheet.Template()).Value.Should().BeEmpty("o modelo só tem o cabeçalho");
    }

    [Fact]
    public void Leitura_pelo_cabecalho_em_qualquer_ordem_e_GTIN_numerico_sem_notacao_cientifica()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Planilha1");
        sheet.Cell(1, 1).Value = "codigo de barras";
        sheet.Cell(1, 2).Value = "NOME";
        sheet.Cell(1, 3).Value = "Preço de venda";
        sheet.Cell(2, 1).Value = 7891000315507d;
        sheet.Cell(2, 2).Value = "Café";
        sheet.Cell(2, 3).Value = 19.9d;
        sheet.Cell(4, 2).Value = "Açúcar";
        var rows = ProductImportSheet.Read(Save(workbook)).Value;
        rows.Should().HaveCount(2, "a linha 3 vazia não conta");
        rows[0].Line.Should().Be(2);
        rows[0].Cells[ProductImportColumns.Gtin].Should().Be(Gtin);
        rows[0].Cells[ProductImportColumns.Price].Should().Be("19.9");
        rows[1].Line.Should().Be(4);
    }

    [Fact]
    public void Mais_de_20_mil_linhas_da_a_mensagem_do_CA_7()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(ProductImportSheet.DataSheet);
        sheet.Cell(1, 1).Value = "Código interno";
        sheet.Cell(1, 2).Value = "Nome";
        for (var r = 2; r <= ProductImportRules.MaxRows + 2; r++)
        {
            sheet.Cell(r, 1).Value = "P" + r;
        }

        var read = ProductImportSheet.Read(Save(workbook));
        read.IsFailure.Should().BeTrue();
        read.Error!.Message.Should().Be("A planilha passa do limite de 20.000 linhas. Divida em arquivos menores.");
    }

    [Fact]
    public void Arquivo_que_nao_e_planilha_da_mensagem_do_que_fazer() =>
        ProductImportSheet.Read("nao e planilha"u8.ToArray()).Error!.Message.Should().Contain("Salve como");

    [Fact]
    public void Linhas_com_erro_voltam_com_o_motivo_e_texto_nunca_vira_formula()
    {
        var bytes = ProductImportSheet.Errors([new ProductImportError(14, "Linha 14: código de barras inválido.", Row(code: "=1+1", gtin: "123"))]);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(ProductImportSheet.ErrorsSheet);
        sheet.Cell(2, 1).GetValue<int>().Should().Be(14);
        sheet.Cell(2, 2).GetString().Should().Be("Linha 14: código de barras inválido.");
        sheet.Cell(2, 3).HasFormula.Should().BeFalse();
        sheet.Cell(2, 3).GetString().Should().Be("=1+1");
        ProductImportSheet.Read(bytes).Value.Single().Cells[ProductImportColumns.Code].Should().Be("=1+1", "a planilha corrigida volta pela importação");
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
