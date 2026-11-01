using System.Globalization;
using System.Text.RegularExpressions;

namespace BemAqui.Assets.Domain;

/// <summary>Tamanho da etiqueta em milímetros (<c>assets.label_size</c>, PAT-01b CA 1): largura × altura.</summary>
public readonly record struct LabelSize(int WidthMm, int HeightMm)
{
    /// <summary>Texto gravado no parâmetro e mostrado no histórico: <c>50x25</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{WidthMm}x{HeightMm}");
}

/// <summary>Grade da folha A4 (CA 3): colunas e linhas que cabem com a margem e o espaço entre as etiquetas.</summary>
public readonly record struct LabelGrid(int Columns, int Rows)
{
    public int PerPage => Columns * Rows;
}

/// <summary>
/// Regras puras da etiqueta e da leitura (PAT-01b, ADR 0038 §5). O QR leva só a URL curta <c>/b/{etiqueta}</c> no host primário do tenant:
/// sem id interno, sem token e sem dado do bem (CA 2). O tamanho é parâmetro (seed 50 × 25 mm, P-13); a folha A4 calcula a grade.
/// </summary>
public static partial class AssetLabelRules
{
    public const string SizeKey = "assets.label_size";
    public const int MinWidthMm = 30;
    public const int MaxWidthMm = 150;
    public const int MinHeightMm = 15;
    public const int MaxHeightMm = 150;

    /// <summary>Até 50 etiquetas o PDF sai na hora; acima, em segundo plano com o aviso no sino (CA 3).</summary>
    public const int InlineLimit = 50;

    /// <summary>"Todos do filtro" vai até 500 bens por lote (CA 3).</summary>
    public const int BatchMax = 500;

    /// <summary>A4 retrato, em milímetros, com a margem da folha e o espaço entre as etiquetas (corte).</summary>
    public const int SheetWidthMm = 210;
    public const int SheetHeightMm = 297;
    public const int SheetMarginMm = 8;
    public const int SheetGapMm = 2;

    /// <summary>O PDF do lote fica guardado como arquivo do sistema por 7 dias.</summary>
    public static readonly TimeSpan BatchRetention = TimeSpan.FromDays(7);

    public static readonly LabelSize DefaultSize = new(50, 25);

    /// <summary>Caminho curto que vai no QR (CA 2): só a etiqueta, nada do bem.</summary>
    public static string ShortPath(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return "/b/" + tag;
    }

    /// <summary>Parâmetro gravado (<c>50x25</c>); vazio, ilegível ou fora da faixa vale o padrão.</summary>
    public static LabelSize ParseOrDefault(string? value)
    {
        var match = value is null ? null : SizePattern().Match(value.Trim());
        if (match is null || !match.Success)
        {
            return DefaultSize;
        }

        var size = new LabelSize(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        return SizeError(size.WidthMm, size.HeightMm) is null ? size : DefaultSize;
    }

    /// <summary>Faixa aceita: largura de 30 a 150 mm e altura de 15 a 150 mm (o QR precisa de ao menos ~12 mm para o celular ler).</summary>
    public static string? SizeError(int widthMm, int heightMm) =>
        widthMm is < MinWidthMm or > MaxWidthMm || heightMm is < MinHeightMm or > MaxHeightMm
            ? $"Informe a largura de {MinWidthMm} a {MaxWidthMm} mm e a altura de {MinHeightMm} a {MaxHeightMm} mm."
            : null;

    /// <summary>Grade da folha A4 (CA 3): quantas etiquetas cabem por linha e por coluna, ao menos uma.</summary>
    public static LabelGrid Grid(LabelSize size)
    {
        static int Fit(int available, int item) => Math.Max(1, (available + SheetGapMm) / (item + SheetGapMm));
        return new LabelGrid(Fit(SheetWidthMm - (2 * SheetMarginMm), size.WidthMm), Fit(SheetHeightMm - (2 * SheetMarginMm), size.HeightMm));
    }

    /// <summary>
    /// Etiqueta digitada ou lida (CA 7 e 9): sem espaços, em maiúsculas; até 20 caracteres de letras, números e hífen. Qualquer outra coisa
    /// vira vazio (a busca roda do mesmo jeito e não acha nada: resposta e tempo iguais).
    /// </summary>
    public static string NormalizeTag(string? tag)
    {
        var text = (tag ?? string.Empty).Trim().ToUpperInvariant();
        return text.Length is > 0 and <= 20 && TagPattern().IsMatch(text) ? text : string.Empty;
    }

    [GeneratedRegex(@"^(\d{2,3})\s*[xX×]\s*(\d{2,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex SizePattern();

    [GeneratedRegex("^[A-Z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
