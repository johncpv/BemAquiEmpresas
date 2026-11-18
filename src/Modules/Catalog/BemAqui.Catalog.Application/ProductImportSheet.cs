using System.Globalization;
using System.IO.Compression;
using BemAqui.BuildingBlocks.Export;
using BemAqui.Catalog.Domain;
using BemAqui.SharedKernel;
using ClosedXML.Excel;

namespace BemAqui.Catalog.Application;

/// <summary>
/// A planilha da importação de produtos (CAT-02): o modelo para baixar (CA 1), a leitura do arquivo enviado (CA 2 e 7) e a planilha com as linhas
/// com erro (CA 3). Texto é sempre texto: o que começa com <c>= + - @</c> ganha o prefixo de texto da planilha, como na exportação (ADR 0045).
/// </summary>
public static class ProductImportSheet
{
    public const string DataSheet = "Produtos";
    public const string HelpSheet = "Como preencher";
    public const string ErrorsSheet = "Produtos";
    public const string InvalidFileCode = "catalog_import_invalid_file";
    public const string TooManyRowsCode = "catalog_import_too_many_rows";

    /// <summary>Teto do conteúdo descompactado (o xlsx é um zip): protege o servidor de arquivo pequeno que explode ao abrir.</summary>
    public const long MaxUncompressedBytes = 150L * 1024 * 1024;

    private const int MaxCellLength = 500;

    private static readonly (string Header, string Required, string What, string Example)[] Help =
    [
        ("Código interno", "Este ou o código de barras", "O código do produto no seu controle (até 30 caracteres). É a chave para importar de novo sem duplicar: o mesmo código atualiza o produto. Vazio: o sistema gera MER-000001, MER-000002…", "CAM-001"),
        ("Nome", "Sim, no produto novo", "O nome que aparece no caixa e no cupom (até 150 caracteres). No produto que já existe, vazio mantém o nome atual.", "Camiseta básica branca M"),
        ("Unidade", "Não", "A unidade de venda: UN, KG, CX, L… (a sigla cadastrada no Estoque). Vazio: UN. Só vale para o produto novo.", "UN"),
        ("NCM", "Não", "A classificação fiscal, com 8 dígitos (pontos são ignorados). Só vale para o produto novo; precisa dela para emitir nota.", "6109.10.00"),
        ("Código de barras", "Este ou o código interno", "O GTIN/EAN de 8, 12, 13 ou 14 dígitos, com o dígito verificador certo. Sem código interno, ele vira a chave da reimportação.", "7891234567895"),
        ("Preço de venda", "Não", "O preço na tabela Balcão, maior que zero, com até 2 casas (vírgula para os centavos). Só entra para quem pode alterar preço.", "39,90"),
        ("Custo", "Só com saldo inicial", "O custo de compra de uma unidade, usado no saldo inicial. Sem saldo inicial, é ignorado: o custo vem das compras.", "18,50"),
        ("Saldo inicial", "Não", "A quantidade em estoque hoje. Entra como saldo inicial, uma vez só por produto, depois da confirmação \"Lançar saldo inicial\".", "12"),
        ("Categoria", "Não", "O nome da categoria do produto. Se não existir, é criada. Vazio: \"Geral\" no produto novo; o atual no que já existe.", "Roupas"),
    ];

    /// <summary>CA 1: a primeira aba explica cada coluna; a segunda ("Produtos") tem só o cabeçalho, com as colunas de código em formato texto.</summary>
    public static byte[] Template()
    {
        using var workbook = XlsxTabularExporter.NewWorkbook();
        var help = workbook.Worksheets.Add(HelpSheet);
        help.Cell(1, 1).Value = "Importar produtos: como preencher";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(1, 1).Style.Font.FontSize = 14;
        help.Cell(2, 1).Value = "Preencha a aba \"Produtos\", uma linha por produto, até 20.000 linhas. Não mude os nomes das colunas. Antes de gravar, o sistema mostra a prévia com os erros por linha.";
        string[] headers = ["Coluna", "Obrigatória", "O que vai", "Exemplo"];
        for (var c = 0; c < headers.Length; c++)
        {
            help.Cell(4, c + 1).Value = headers[c];
            help.Cell(4, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < Help.Length; r++)
        {
            var (header, required, what, example) = Help[r];
            Text(help.Cell(5 + r, 1), header);
            Text(help.Cell(5 + r, 2), required);
            Text(help.Cell(5 + r, 3), what);
            Text(help.Cell(5 + r, 4), example);
        }

        var notes = 6 + Help.Length;
        Text(help.Cell(notes, 1), "Importar de novo a mesma planilha atualiza os produtos (pelo código interno ou, na falta dele, pelo código de barras) e não lança o saldo inicial outra vez.");
        Text(help.Cell(notes + 1, 1), "Unidade e NCM de um produto que já existe são do Estoque: mude-os na tela do item. Produtos com variações (grade) são criados na tela do produto.");
        help.Column(1).Width = 20;
        help.Column(2).Width = 24;
        help.Column(3).Width = 90;
        help.Column(3).Style.Alignment.WrapText = true;
        help.Column(4).Width = 26;

        var data = workbook.Worksheets.Add(DataSheet);
        Header(data, ProductImportColumns.Headers);
        foreach (var text in new[] { ProductImportColumns.Code, ProductImportColumns.Name, ProductImportColumns.Unit, ProductImportColumns.Ncm, ProductImportColumns.Gtin, ProductImportColumns.Category })
        {
            data.Column(text + 1).Style.NumberFormat.Format = "@";
        }

        data.Column(ProductImportColumns.Price + 1).Style.NumberFormat.Format = "#,##0.00";
        data.Column(ProductImportColumns.Cost + 1).Style.NumberFormat.Format = "#,##0.00####";
        data.Column(ProductImportColumns.Stock + 1).Style.NumberFormat.Format = "#,##0.####";
        return Save(workbook);
    }

    /// <summary>
    /// CA 3: as linhas com erro, com o número da linha original e o motivo nas duas primeiras colunas e as colunas do modelo depois. A aba se chama
    /// "Produtos": corrigida, a planilha volta direto pela importação (as duas colunas a mais são ignoradas).
    /// </summary>
    public static byte[] Errors(IReadOnlyList<ProductImportError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        using var workbook = XlsxTabularExporter.NewWorkbook();
        var sheet = workbook.Worksheets.Add(ErrorsSheet);
        Header(sheet, ["Linha", "Motivo", .. ProductImportColumns.Headers]);
        for (var r = 0; r < errors.Count; r++)
        {
            var error = errors[r];
            sheet.Cell(r + 2, 1).Value = error.Line;
            Text(sheet.Cell(r + 2, 2), error.Message);
            for (var c = 0; c < ProductImportColumns.Headers.Count; c++)
            {
                Text(sheet.Cell(r + 2, c + 3), c < error.Cells.Count ? error.Cells[c] : null);
            }
        }

        sheet.Column(2).Width = 60;
        return Save(workbook);
    }

    /// <summary>
    /// Lê o arquivo: confere o tamanho descompactado, abre com o ClosedXML, acha a aba "Produtos" (ou a primeira com o cabeçalho do modelo), mapeia
    /// as colunas pelo cabeçalho e devolve as linhas não vazias, com o número da linha da planilha. Mais de 20 mil linhas = erro do CA 7.
    /// </summary>
    public static Result<IReadOnlyList<(int Line, string?[] Cells)>> Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!FitsUncompressed(content))
        {
            return Error.Validation(InvalidFileCode, "A planilha é grande demais para ler. Divida em arquivos menores.");
        }

        XLWorkbook workbook;
        try
        {
            workbook = XlsxTabularExporter.OpenWorkbook(new MemoryStream(content, writable: false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unreadable();
        }

        using (workbook)
        {
            var found = FindSheet(workbook);
            if (found is not { } match)
            {
                return Error.Validation(InvalidFileCode, "A planilha não está no modelo: não achamos a coluna \"Código interno\" nem a \"Código de barras\". Baixe o modelo e copie seus dados nele.");
            }

            var (sheet, map) = match;
            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<(int, string?[])>();
            for (var r = 2; r <= last; r++)
            {
                var cells = new string?[ProductImportColumns.Headers.Count];
                foreach (var (column, index) in map)
                {
                    cells[index] = CellText(sheet.Cell(r, column));
                }

                if (ProductImportRules.IsBlank(cells))
                {
                    continue;
                }

                if (rows.Count == ProductImportRules.MaxRows)
                {
                    return Error.Validation(TooManyRowsCode, ProductImportRules.TooManyRowsMessage);
                }

                rows.Add((r, cells));
            }

            return rows;
        }
    }

    private static Error Unreadable() =>
        Error.Validation(InvalidFileCode, "Não conseguimos ler a planilha. Salve como \"Pasta de Trabalho do Excel (.xlsx)\" e envie de novo.");

    private static bool FitsUncompressed(byte[] content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > MaxUncompressedBytes)
                {
                    return false;
                }
            }

            return true;
        }
        catch (InvalidDataException)
        {
            // Não é zip: a abertura logo abaixo dá a mensagem de arquivo ilegível.
            return true;
        }
    }

    private static (IXLWorksheet Sheet, Dictionary<int, int> Map)? FindSheet(XLWorkbook workbook)
    {
        var ordered = workbook.Worksheets.OrderBy(w => string.Equals(w.Name.Trim(), DataSheet, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(w => w.Position);
        foreach (var sheet in ordered)
        {
            var map = new Dictionary<int, int>();
            var lastColumn = Math.Min(sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0, 50);
            for (var c = 1; c <= lastColumn; c++)
            {
                var index = ProductImportColumns.IndexOf(CellText(sheet.Cell(1, c)));
                if (index >= 0 && !map.ContainsValue(index))
                {
                    map[c] = index;
                }
            }

            if (map.ContainsValue(ProductImportColumns.Code) || map.ContainsValue(ProductImportColumns.Gtin))
            {
                return (sheet, map);
            }
        }

        return null;
    }

    /// <summary>
    /// Texto da célula: número inteiro sem casas e sem notação científica (código de barras digitado como número), decimal com ponto, data em
    /// ISO; fórmula pelo valor calculado que veio no arquivo. Corta em 500 caracteres.
    /// </summary>
    private static string? CellText(IXLCell cell)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        string? text;
        if (value.IsBlank)
        {
            text = null;
        }
        else if (value.IsNumber)
        {
            var number = value.GetNumber();
            text = Math.Abs(number) < 1e15 && number == Math.Floor(number)
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : ((decimal)number).ToString(CultureInfo.InvariantCulture);
        }
        else if (value.IsText)
        {
            text = value.GetText();
        }
        else if (value.IsDateTime)
        {
            text = value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else
        {
            text = value.ToString(CultureInfo.InvariantCulture);
        }

        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > MaxCellLength ? text[..MaxCellLength] : text;
    }

    private static void Header(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            sheet.Column(c + 1).Width = Math.Max(14, headers[c].Length + 4);
        }

        sheet.SheetView.FreezeRows(1);
    }

    /// <summary>Grava texto como texto: o que começa com = + - @ ganha o prefixo de texto da planilha (nunca vira fórmula).</summary>
    private static void Text(IXLCell cell, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        cell.SetValue(text);
        if (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            cell.Style.IncludeQuotePrefix = true;
        }
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
