namespace BemAqui.BuildingBlocks.Export;

/// <summary>Formato de exportação de lista (ADR 0045 §3): <c>?format=xlsx|pdf</c>.</summary>
public enum ExportFormat
{
    Xlsx,
    Pdf,
}

/// <summary>Tipo da coluna: define o formato da célula no xlsx, o alinhamento e o texto no PDF e a largura.</summary>
public enum ExportColumnType
{
    Text,

    /// <summary>Dinheiro em reais, <c>decimal(18,2)</c>: "R$ #.##0,00".</summary>
    Money,

    /// <summary>Quantidade, <c>decimal(18,4)</c>: até 4 casas.</summary>
    Quantity,

    /// <summary>Data de negócio (<see cref="DateOnly"/>): dd/mm/aaaa.</summary>
    Date,

    /// <summary>Momento (UTC no banco), mostrado no horário de São Paulo: dd/mm/aaaa hh:mm.</summary>
    DateTime,

    /// <summary>Percentual em pontos (12,5 = 12,5%).</summary>
    Percent,
}

/// <summary>Ação que libera a coluna sensível (<c>modulo.acao</c>, ADR 0003); qualquer uma da lista basta.</summary>
public sealed record ExportSensitivity(IReadOnlyList<string> AnyOf, string Mask = ExportSensitivity.DefaultMask)
{
    /// <summary>Mesmo padrão da máscara de CPF da tela (<c>***</c>).</summary>
    public const string DefaultMask = "***";

    public ExportSensitivity(string permission) : this([permission])
    {
    }

    /// <summary>Sem a ação, a coluna sai do arquivo em vez de vir mascarada (EXP-04: custo e margem que a tela esconde).</summary>
    public bool Omit { get; init; }

    /// <summary>Coluna que some do arquivo sem a ação (EXP-04).</summary>
    public static ExportSensitivity Omitted(string permission) => new(permission) { Omit = true };
}

/// <summary>Coluna pronta para o exportador (depois da máscara). <c>Indented</c>: a coluna recebe o recuo da linha (<see cref="ExportRowStyle"/>).</summary>
public sealed record ExportColumn(string Header, ExportColumnType Type, bool Indented = false);

/// <summary>
/// Destaque de uma linha (EXP-02): recuo por nível na coluna marcada (e o agrupamento da planilha), negrito em subtotal e resultado e, com
/// <c>CriticalWhenNegative</c>, o número negativo no vermelho do <c>PdfTheme</c> (resultado do DRE).
/// </summary>
public sealed record ExportRowStyle(int Indent = 0, bool Bold = false, bool CriticalWhenNegative = false);

/// <summary>
/// Aba a mais da planilha (EXP-02: conciliação, DRE e fluxo), entre a aba principal e a "Filtros". As linhas já vêm como a tela mostra (a
/// máscara é a do serviço de leitura). O PDF de lista não a desenha; cada documento próprio decide o que mostrar.
/// </summary>
public sealed record ExportSheet(string Name, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public IReadOnlyList<ExportRowStyle?> RowStyles { get; init; } = [];

    /// <summary>Aba a partir de uma <see cref="ExportSpec{T}"/> sem coluna sensível (o dado já vem mascarado pela consulta da tela).</summary>
    public static ExportSheet From<T>(string name, ExportSpec<T> spec, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToList();
        return new ExportSheet(name, spec.OutputColumns(ExportFormat.Xlsx, _ => true), spec.Rows(ExportFormat.Xlsx, list, _ => true)) { RowStyles = spec.Styles(list) };
    }
}

/// <summary>Filtro aplicado, com o rótulo da tela (vai para a aba "Filtros", para a linha "Filtros:" do PDF e para a auditoria).</summary>
public sealed record ExportFilter(string Label, string? Value);

/// <summary>
/// Tabela a exportar: título da tela, colunas tipadas e linhas já mascaradas. Os valores são <c>string</c>, <c>decimal</c>, <c>int</c>,
/// <see cref="DateOnly"/>, <see cref="DateTimeOffset"/> ou null; coluna mascarada traz o texto da máscara.
/// </summary>
public sealed record ExportTable(
    string Title, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<ExportFilter> Filters, string IssuedBy, DateTimeOffset IssuedAt)
{
    /// <summary>Nome da aba principal; vazio é o título.</summary>
    public string? SheetName { get; init; }

    /// <summary>Destaque de cada linha (mesma ordem de <see cref="Rows"/>); vazio é sem destaque.</summary>
    public IReadOnlyList<ExportRowStyle?> RowStyles { get; init; } = [];

    /// <summary>Abas a mais da planilha, antes da "Filtros".</summary>
    public IReadOnlyList<ExportSheet> Sheets { get; init; } = [];

    /// <summary>Notas e destaques da tela: no topo do PDF de lista e na aba "Filtros".</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Paisagem forçada no PDF de lista (sem valor: acima de 6 colunas).</summary>
    public bool? Landscape { get; init; }
}

/// <summary>
/// O que a consulta da tela entrega para exportar (EXP-02): a definição das colunas (pode depender do resultado, ex.: os meses do DRE), os
/// filtros com os rótulos da tela, as linhas até o teto com o total, o acabamento da tabela (abas, notas) e, quando a tela tem documento
/// próprio, o PDF dele no lugar do PDF de lista.
/// </summary>
public sealed record ExportData<T>(ExportSpec<T> Spec, IReadOnlyList<ExportFilter> Filters, ExportRows<T> Rows)
{
    /// <summary>Acabamento da tabela pronta (abas a mais, notas, nome da aba, paisagem).</summary>
    public Func<ExportTable, ExportTable>? Decorate { get; init; }

    /// <summary>PDF próprio da tela (no <c>PdfDocumentFrame</c>) no lugar do PDF de lista.</summary>
    public Func<BemAqui.BuildingBlocks.Pdf.PdfFrame, ExportTable, QuestPDF.Infrastructure.IDocument>? Document { get; init; }
}

/// <summary>Linhas lidas para exportar: até o teto, com o total do filtro (para recusar acima do teto sem montar nada).</summary>
public sealed record ExportRows<T>(int Total, IReadOnlyList<T> Items);
