using System.Globalization;
using BemAqui.BuildingBlocks.Time;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.BuildingBlocks.Pdf;

/// <summary>
/// Tokens do PDF (ADR 0045 §1): as únicas cores e medidas dos documentos no <see cref="PdfDocumentFrame"/>. Documento novo não escreve cor
/// literal; usa estes nomes, como o front usa os tokens do Tailwind. Tons de aço frios, acento cobalto, âmbar e vermelho só para a marca.
/// </summary>
public static class PdfTheme
{
    /// <summary>Texto principal (aço escuro).</summary>
    public const string Ink = "#14202b";

    /// <summary>Texto secundário: rótulos, rodapé, filtros.</summary>
    public const string Muted = "#56636f";

    /// <summary>Linhas de tabela e de assinatura.</summary>
    public const string Line = "#9aa7b4";

    /// <summary>Fundo de bloco (dados do documento, cabeçalho da tabela).</summary>
    public const string Surface = "#eef2f6";

    /// <summary>Acento cobalto: traço da faixa de título.</summary>
    public const string Accent = "#1f4fa3";

    /// <summary>Marca d'água diagonal: clara para não esconder o conteúdo.</summary>
    public const string Watermark = "#d5dbe1";

    /// <summary>Faixa de estado em alerta (Rascunho, Simulação, Prévia): fundo e texto âmbar.</summary>
    public const string WarnSurface = "#fdf1d6";

    public const string WarnInk = "#7a4d00";

    /// <summary>Faixa de estado crítico (Cancelado): fundo e texto vermelhos.</summary>
    public const string CritSurface = "#fbe3e3";

    public const string CritInk = "#8f1d1d";

    /// <summary>Fundo escuro do indicador de etapa (o trilho do app).</summary>
    public const string Rail = "#1b2733";

    /// <summary>Âmbar do indicador de etapa: a letra da etapa atual e o segmento dela.</summary>
    public const string Amber = "#f0a500";

    /// <summary>Etiqueta do patrimônio (EXP-05 CA 21): preto puro e papel branco, para a impressora térmica não meio-tonar o texto.</summary>
    public const string LabelInk = "#000000";

    public const string Paper = "#ffffff";

    public const float Margin = 32;

    public const float FontSize = 9;

    public const float SmallFontSize = 8;

    public const float TitleFontSize = 14;

    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"R$ 1.234,56".</summary>
    public static string Money(decimal value) => value.ToString("C2", PtBr);

    /// <summary>Quantidade com até 4 casas ("12,5").</summary>
    public static string Quantity(decimal value) => value.ToString("#,##0.####", PtBr);

    public static string Percent(decimal value) => (value / 100m).ToString("P2", PtBr);

    public static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", PtBr);

    /// <summary>Data e hora no horário de São Paulo (o banco guarda UTC).</summary>
    public static string DateTime(DateTimeOffset utc) => BusinessTime.ToBusiness(utc).ToString("dd/MM/yyyy HH:mm", PtBr);
}

/// <summary>Marca padrão do documento (ADR 0045 §1): faixa no topo e marca d'água diagonal em todas as páginas.</summary>
public enum PdfMark
{
    None,
    Draft,
    Simulation,
    Cancelled,
    Preview,
}

public static class PdfMarks
{
    /// <summary>O texto de cada marca, igual em todos os documentos.</summary>
    public static string Label(PdfMark mark) => mark switch
    {
        PdfMark.Draft => "Rascunho",
        PdfMark.Simulation => "Simulação",
        PdfMark.Cancelled => "Cancelado",
        PdfMark.Preview => "Prévia, sem valor fiscal",
        _ => string.Empty,
    };

    /// <summary>A palavra da marca d'água (curta, cabe na diagonal da A4).</summary>
    public static string Watermark(PdfMark mark) => mark switch
    {
        PdfMark.Preview => "PRÉVIA",
        PdfMark.None => string.Empty,
        _ => Label(mark).ToUpper(PdfTheme.PtBr),
    };

    /// <summary>Texto da faixa: a marca e o aviso ("Rascunho · Pedido não aprovado: …"), só um deles, ou null sem nenhum.</summary>
    public static string? BandText(PdfMark mark, string? notice)
    {
        var label = Label(mark);
        var hasNotice = !string.IsNullOrWhiteSpace(notice);
        return (label.Length > 0, hasNotice) switch
        {
            (true, true) => label + " · " + notice!.Trim(),
            (true, false) => label,
            (false, true) => notice!.Trim(),
            _ => null,
        };
    }

    /// <summary>A marca d'água diagonal (nada sem marca). Pública para os layouts que só migram para o frame na EXP-05 (orçamento).</summary>
    public static void Watermark(PageDescriptor page, PdfMark mark)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (mark != PdfMark.None)
        {
            page.Foreground().AlignCenter().AlignMiddle().Rotate(-30).Text(Watermark(mark)).FontSize(54).Bold().FontColor(PdfTheme.Watermark);
        }
    }

    /// <summary>A faixa da marca e do aviso: vermelha no cancelado, âmbar nas demais; nada sem marca nem aviso.</summary>
    public static void Band(IContainer container, PdfMark mark, string? notice)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (BandText(mark, notice) is not { } text)
        {
            return;
        }

        var crit = mark == PdfMark.Cancelled;
        container.Background(crit ? PdfTheme.CritSurface : PdfTheme.WarnSurface).PaddingVertical(3).PaddingHorizontal(6)
            .Text(text).SemiBold().FontColor(crit ? PdfTheme.CritInk : PdfTheme.WarnInk);
    }
}
