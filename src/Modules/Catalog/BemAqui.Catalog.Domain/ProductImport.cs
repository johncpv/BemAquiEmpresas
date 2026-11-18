using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Catalog.Domain;

/// <summary>Situação de uma importação (CAT-02): prévia gravada, rodando (no pedido ou no job), concluída ou com falha.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductImportStatus>))]
public enum ProductImportStatus
{
    Previewed,
    Running,
    Completed,
    Failed,
}

/// <summary>O que a linha faz: cria um produto (com item novo ou ligado a um item que já existe) ou atualiza o produto achado pela chave.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductImportAction>))]
public enum ProductImportAction
{
    Create,
    Update,
}

/// <summary>Uma linha da planilha como veio, em texto (as 9 colunas do modelo, na ordem de <see cref="ProductImportColumns"/>).</summary>
public sealed record ProductImportRow(int Line, IReadOnlyList<string?> Cells, ProductImportAction Action);

/// <summary>Uma linha com erro: o número da linha, o motivo em português e o que veio nela (para a planilha de correção).</summary>
public sealed record ProductImportError(int Line, string Message, IReadOnlyList<string?> Cells);

/// <summary>
/// Importação de produtos por planilha (<c>catalog_products.import_runs</c>, CAT-02): a prévia grava as linhas válidas e as com erro (jsonb, para
/// confirmar sem reenviar o arquivo e para baixar as linhas com erro) e os totais; a confirmação grava quem confirmou, se lança o saldo inicial e
/// em qual local, o progresso e o resultado (criados, atualizados, ignorados). Não é <c>IAuditable</c> (as linhas não vão para a auditoria): a
/// confirmação grava um <c>Imported</c> com os totais.
/// </summary>
public sealed class ProductImportRun : Entity
{
    public string FileName { get; init; } = string.Empty;

    public ProductImportStatus Status { get; set; } = ProductImportStatus.Previewed;

    public int TotalRows { get; init; }

    public int ToCreate { get; init; }

    public int ToUpdate { get; init; }

    public int ErrorRows { get; init; }

    /// <summary>Linhas com saldo inicial maior que zero.</summary>
    public int StockRows { get; init; }

    /// <summary>Quem enviou não tinha <c>catalog.edit_price</c>: a coluna de preço é ignorada (CA 5).</summary>
    public bool PricesIgnored { get; init; }

    /// <summary>Linhas válidas (jsonb, <see cref="ProductImportRow"/>).</summary>
    public string RowsJson { get; init; } = "[]";

    /// <summary>Linhas com erro (jsonb, <see cref="ProductImportError"/>); a execução acrescenta as que falharem ao gravar.</summary>
    public string ErrorsJson { get; set; } = "[]";

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? ConfirmedBy { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>A confirmação aplicou os preços (tinha <c>catalog.edit_price</c> ao confirmar).</summary>
    public bool ApplyPrices { get; set; }

    /// <summary>"Lançar saldo inicial" confirmado (CA 6), no <see cref="StockLocationId"/>.</summary>
    public bool LaunchStock { get; set; }

    public Guid? StockLocationId { get; set; }

    public bool InBackground { get; set; }

    /// <summary>Quantas linhas válidas já passaram pela execução (progresso; o job retoma daqui).</summary>
    public int Processed { get; set; }

    public int Created { get; set; }

    public int Updated { get; set; }

    /// <summary>Linhas válidas na prévia que não entraram na execução (o motivo vai para <see cref="ErrorsJson"/>).</summary>
    public int Failed { get; set; }

    public int StockPosted { get; set; }

    /// <summary>Saldos que já tinham sido lançados numa importação anterior (CA 6: reimportar não lança de novo).</summary>
    public int StockAlreadyPosted { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? FailureMessage { get; set; }
}

/// <summary>As colunas do modelo (CA 1), na ordem; o cabeçalho é achado pelo nome, sem diferença de maiúscula, acento ou espaço.</summary>
public static class ProductImportColumns
{
    public const int Code = 0;
    public const int Name = 1;
    public const int Unit = 2;
    public const int Ncm = 3;
    public const int Gtin = 4;
    public const int Price = 5;
    public const int Cost = 6;
    public const int Stock = 7;
    public const int Category = 8;

    public static readonly IReadOnlyList<string> Headers =
        ["Código interno", "Nome", "Unidade", "NCM", "Código de barras", "Preço de venda", "Custo", "Saldo inicial", "Categoria"];

    /// <summary>Índice da coluna pelo texto do cabeçalho; -1 se não for do modelo.</summary>
    public static int IndexOf(string? header)
    {
        var key = Key(header);
        for (var i = 0; i < Headers.Count; i++)
        {
            if (Key(Headers[i]) == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Key(string? text) => new(SearchText.Normalize(text ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>
/// Leitura e conferência de cada linha (CAT-02 CA 2), sem banco: números em pt-BR ("1.234,56") ou com ponto decimal ("12.5"), GTIN com o dígito
/// verificador, NCM de 8 dígitos, nome e código com os limites do cadastro. As mensagens já vêm com "Linha N:".
/// </summary>
public static class ProductImportRules
{
    public const int MaxRows = 20_000;
    public const int CodeMax = 30;
    public const string TooManyRowsMessage = "A planilha passa do limite de 20.000 linhas. Divida em arquivos menores.";
    public const decimal MaxQuantity = 99_999_999_999_999.9999m;
    public const decimal MaxCost = 999_999_999_999.999999m;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Linha toda vazia: não conta nem dá erro.</summary>
    public static bool IsBlank(IReadOnlyList<string?> cells) => cells.All(string.IsNullOrWhiteSpace);

    public static string? Cell(IReadOnlyList<string?> cells, int index) =>
        index < cells.Count && !string.IsNullOrWhiteSpace(cells[index]) ? cells[index]!.Trim() : null;

    /// <summary>
    /// Número da planilha: com vírgula, é pt-BR ("1.234,56"); sem vírgula, o ponto é o decimal ("12.5", como a célula numérica chega). Aceita
    /// "R$" e espaços. Null quando não é número.
    /// </summary>
    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var clean = text.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        var culture = clean.Contains(',', StringComparison.Ordinal) ? PtBr : CultureInfo.InvariantCulture;
        return decimal.TryParse(clean, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, culture, out var value) ? value : null;
    }

    /// <summary>NCM só com dígitos (tira pontos e espaços); null quando vazio.</summary>
    public static string? Digits(string? text) => string.IsNullOrWhiteSpace(text) ? null : new string(text.Where(char.IsAsciiDigit).ToArray());

    /// <summary>
    /// Confere os campos da linha que não dependem do banco. <paramref name="creating"/> = a linha cria o produto (o nome passa a ser obrigatório);
    /// <paramref name="pricesIgnored"/> = a coluna de preço não é lida (sem <c>catalog.edit_price</c>).
    /// </summary>
    public static string? Validate(int line, IReadOnlyList<string?> cells, bool creating, bool pricesIgnored)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var code = Cell(cells, ProductImportColumns.Code);
        var gtinText = Cell(cells, ProductImportColumns.Gtin);
        if (code is null && gtinText is null)
        {
            return $"Linha {line}: informe o código interno ou o código de barras, para a planilha poder ser importada de novo sem duplicar.";
        }

        if (code is { Length: > CodeMax })
        {
            return $"Linha {line}: código interno com mais de {CodeMax} caracteres.";
        }

        if (gtinText is not null && !GtinRules.IsValid(GtinRules.Normalize(gtinText)))
        {
            return $"Linha {line}: código de barras inválido.";
        }

        var name = Cell(cells, ProductImportColumns.Name);
        if (creating && name is null)
        {
            return $"Linha {line}: informe o nome do produto.";
        }

        if (name is { Length: > ProductRules.NameMax })
        {
            return $"Linha {line}: nome com mais de {ProductRules.NameMax} caracteres.";
        }

        if (Cell(cells, ProductImportColumns.Ncm) is { } ncmText && Digits(ncmText) is not { Length: 8 })
        {
            return $"Linha {line}: NCM inválido. Use os 8 dígitos.";
        }

        if (!pricesIgnored && Cell(cells, ProductImportColumns.Price) is { } priceText && !IsValidPrice(priceText))
        {
            return $"Linha {line}: preço de venda inválido. Use um valor maior que zero, com até 2 casas.";
        }

        var costText = Cell(cells, ProductImportColumns.Cost);
        decimal? cost = null;
        if (costText is not null)
        {
            cost = ParseNumber(costText);
            if (cost is not { } c || c < 0m || c > MaxCost || decimal.Round(c, 6) != c)
            {
                return $"Linha {line}: custo inválido. Use um valor de 0 para cima, com até 6 casas.";
            }
        }

        if (Cell(cells, ProductImportColumns.Stock) is { } stockText)
        {
            if (ParseNumber(stockText) is not { } stock || stock < 0m || stock > MaxQuantity || decimal.Round(stock, 4) != stock)
            {
                return $"Linha {line}: saldo inicial inválido. Use uma quantidade de 0 para cima, com até 4 casas.";
            }

            if (stock > 0m && cost is not > 0m)
            {
                return $"Linha {line}: para lançar o saldo inicial, informe o custo (maior que zero).";
            }
        }

        return Cell(cells, ProductImportColumns.Category) is { Length: > 80 } ? $"Linha {line}: categoria com mais de 80 caracteres." : null;
    }

    public static decimal? Price(IReadOnlyList<string?> cells) => ParseNumber(Cell(cells, ProductImportColumns.Price));

    public static decimal? Cost(IReadOnlyList<string?> cells) => ParseNumber(Cell(cells, ProductImportColumns.Cost));

    /// <summary>Saldo inicial maior que zero; null quando vazio ou zero.</summary>
    public static decimal? Stock(IReadOnlyList<string?> cells) => ParseNumber(Cell(cells, ProductImportColumns.Stock)) is > 0m and var s ? s : null;

    public static string? Gtin(IReadOnlyList<string?> cells) => Cell(cells, ProductImportColumns.Gtin) is { } g ? GtinRules.Normalize(g) : null;

    private static bool IsValidPrice(string text) => ParseNumber(text) is { } p && ProductRules.IsValidPrice(p);
}
