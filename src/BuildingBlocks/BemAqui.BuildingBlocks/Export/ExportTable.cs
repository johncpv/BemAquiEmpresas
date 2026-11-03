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
}

/// <summary>Coluna pronta para o exportador (depois da máscara).</summary>
public sealed record ExportColumn(string Header, ExportColumnType Type);

/// <summary>Filtro aplicado, com o rótulo da tela (vai para a aba "Filtros", para a linha "Filtros:" do PDF e para a auditoria).</summary>
public sealed record ExportFilter(string Label, string? Value);

/// <summary>
/// Tabela a exportar: título da tela, colunas tipadas e linhas já mascaradas. Os valores são <c>string</c>, <c>decimal</c>, <c>int</c>,
/// <see cref="DateOnly"/>, <see cref="DateTimeOffset"/> ou null; coluna mascarada traz o texto da máscara.
/// </summary>
public sealed record ExportTable(
    string Title, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<ExportFilter> Filters, string IssuedBy, DateTimeOffset IssuedAt);

/// <summary>Linhas lidas para exportar: até o teto, com o total do filtro (para recusar acima do teto sem montar nada).</summary>
public sealed record ExportRows<T>(int Total, IReadOnlyList<T> Items);
