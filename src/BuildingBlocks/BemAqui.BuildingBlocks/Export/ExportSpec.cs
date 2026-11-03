namespace BemAqui.BuildingBlocks.Export;

/// <summary>
/// Definição da exportação de uma lista (ADR 0045 §3): entidade da auditoria, nome do arquivo (<c>{tela}-{AAAA-MM-DD}.{ext}</c>), título e
/// colunas tipadas com o valor de cada linha. Coluna sensível declara a ação que a libera; sem ela, sai com a máscara, como na tela.
/// </summary>
/// <remarks>
/// <c>entity</c>: entidade da auditoria (ex.: <c>Payable</c>); <c>fileBase</c>: começo do nome do arquivo, sem nome nem documento de pessoa
/// (ex.: <c>contas-a-pagar</c>); <c>title</c>: título da planilha e do PDF (ex.: "Contas a pagar").
/// </remarks>
public sealed class ExportSpec<T>(string entity, string fileBase, string title)
{
    private readonly List<Column> _columns = [];
    private Func<T, ExportRowStyle?>? _style;

    public string Entity { get; } = entity;

    public string FileBase { get; } = fileBase;

    public string Title { get; } = title;

    /// <summary>As ações que precisam ser conferidas (uma por coluna sensível).</summary>
    public IReadOnlyList<ExportSensitivity> Sensitivities => _columns.Where(c => c.Sensitivity is not null).Select(c => c.Sensitivity!).Distinct().ToList();

    public ExportSpec<T> Text(string header, Func<T, string?> value, ExportSensitivity? sensitivity = null) => Add(header, ExportColumnType.Text, r => value(r), sensitivity);

    public ExportSpec<T> Money(string header, Func<T, decimal?> value, ExportSensitivity? sensitivity = null) => Add(header, ExportColumnType.Money, r => value(r), sensitivity);

    public ExportSpec<T> Quantity(string header, Func<T, decimal?> value, ExportSensitivity? sensitivity = null) => Add(header, ExportColumnType.Quantity, r => value(r), sensitivity);

    public ExportSpec<T> Date(string header, Func<T, DateOnly?> value, ExportSensitivity? sensitivity = null) => Add(header, ExportColumnType.Date, r => value(r), sensitivity);

    public ExportSpec<T> DateTime(string header, Func<T, DateTimeOffset?> value, ExportSensitivity? sensitivity = null) => Add(header, ExportColumnType.DateTime, r => value(r), sensitivity);

    public ExportSpec<T> Percent(string header, Func<T, decimal?> value, ExportSensitivity? sensitivity = null) => Add(header, ExportColumnType.Percent, r => value(r), sensitivity);

    /// <summary>A última coluna só vai para a planilha (o PDF de lista fica legível na largura da página).</summary>
    public ExportSpec<T> NotInPdf()
    {
        _columns[^1] = _columns[^1] with { InPdf = false };
        return this;
    }

    /// <summary>
    /// Coluna tipada que, em algumas linhas, mostra o texto da tela no lugar do número ou da data (ex.: "Sem histórico" no atraso médio,
    /// "Aguarda: entrega" no vencimento da parcela prevista). O valor é <c>string</c> ou o tipo da coluna.
    /// </summary>
    public ExportSpec<T> Mixed(string header, ExportColumnType type, Func<T, object?> value) => Add(header, type, value, null);

    /// <summary>A última coluna recebe o recuo da linha (ex.: "Linha" do DRE, por nível).</summary>
    public ExportSpec<T> Indented()
    {
        _columns[^1] = _columns[^1] with { Indented = true };
        return this;
    }

    /// <summary>Destaque por linha (recuo e negrito), na planilha e no PDF de lista.</summary>
    public ExportSpec<T> Style(Func<T, ExportRowStyle?> style)
    {
        _style = style;
        return this;
    }

    /// <summary>O destaque de cada linha, na ordem; sem <see cref="Style"/>, vazio.</summary>
    public IReadOnlyList<ExportRowStyle?> Styles(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return _style is null ? [] : items.Select(_style).ToList();
    }

    /// <summary>Colunas de saída do formato: a coluna mascarada vira texto (a máscara não é número nem data).</summary>
    public IReadOnlyList<ExportColumn> OutputColumns(ExportFormat format, Func<ExportSensitivity, bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        return Of(format, allowed).Select(c => new ExportColumn(c.Header, Masked(c, allowed) ? ExportColumnType.Text : c.Type, c.Indented)).ToList();
    }

    /// <summary>
    /// Linhas prontas do formato: cada coluna sensível sem liberação (<paramref name="allowed"/> diz se o usuário tem alguma das ações) troca o
    /// valor pela máscara; a coluna continua no arquivo, para a planilha ter o mesmo formato para todos. A coluna com
    /// <see cref="ExportSensitivity.Omit"/> sem liberação sai do arquivo (EXP-04: custo e margem somem, como na tela).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object?>> Rows(ExportFormat format, IEnumerable<T> items, Func<ExportSensitivity, bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(allowed);
        var columns = Of(format, allowed);
        var masked = columns.Select(c => Masked(c, allowed)).ToArray();
        return items.Select(item => (IReadOnlyList<object?>)columns.Select((c, i) => masked[i] ? c.Sensitivity!.Mask : c.Value(item)).ToArray()).ToList();
    }

    private List<Column> Of(ExportFormat format, Func<ExportSensitivity, bool> allowed) =>
        _columns.Where(c => (format != ExportFormat.Pdf || c.InPdf) && !(c.Sensitivity is { Omit: true } s && !allowed(s))).ToList();

    private static bool Masked(Column column, Func<ExportSensitivity, bool> allowed) => column.Sensitivity is { } s && !allowed(s);

    private ExportSpec<T> Add(string header, ExportColumnType type, Func<T, object?> value, ExportSensitivity? sensitivity)
    {
        _columns.Add(new Column(header, type, value, sensitivity, true, false));
        return this;
    }

    private sealed record Column(string Header, ExportColumnType Type, Func<T, object?> Value, ExportSensitivity? Sensitivity, bool InPdf, bool Indented);
}
