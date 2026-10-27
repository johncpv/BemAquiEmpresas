using BemAqui.Production.Contracts;

namespace BemAqui.Production.Domain;

/// <summary>
/// Indicadores do painel do supervisor sem banco (DSH-03 CA 6, ponto 51): horas paradas = Σ minutos de pausa ÷ 60 por motivo, só a parte
/// da pausa dentro do período (a pausa ainda aberta conta até agora); taxa de NC = reprovações ÷ (aprovações + reprovações), em %, 1 casa.
/// </summary>
public static class ProductionIndicatorRules
{
    /// <summary>Motivo da pausa que entra nas horas paradas: Material, Máquina e Outro; intervalo e fim de turno não são parada.</summary>
    public static StoppageReason? StoppageOf(PauseReason reason) => reason switch
    {
        PauseReason.Material => StoppageReason.Material,
        PauseReason.Machine => StoppageReason.Machine,
        PauseReason.Other => StoppageReason.Other,
        _ => null,
    };

    /// <summary>Minutos da pausa dentro de [<paramref name="from"/>, <paramref name="to"/>); a pausa aberta termina em <paramref name="now"/>.</summary>
    public static decimal MinutesWithin(DateTimeOffset started, DateTimeOffset? ended, DateTimeOffset now, DateTimeOffset from, DateTimeOffset to)
    {
        var start = started > from ? started : from;
        var end = ended ?? now;
        if (end > to)
        {
            end = to;
        }

        return end <= start ? 0m : (decimal)(end - start).TotalMinutes;
    }

    /// <summary>Horas de uma soma de minutos, com 2 casas (a tela mostra 1).</summary>
    public static decimal Hours(decimal minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Taxa de NC (%) com 1 casa; nula sem decisão no período.</summary>
    public static decimal? NcRate(int approvals, int rejections)
    {
        var decisions = approvals + rejections;
        return decisions <= 0 ? null : Math.Round(rejections * 100m / decisions, 1, MidpointRounding.AwayFromZero);
    }
}
