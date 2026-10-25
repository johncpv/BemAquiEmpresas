namespace BemAqui.Production.Contracts;

/// <summary>
/// Dias úteis pelo calendário padrão do tenant (CAD-03), para os outros módulos: o painel do proprietário conta os dias úteis
/// que faltam no mês (DSH-01a CA 9) e a RH-01c reaproveita. Dia útil é dia de trabalho do calendário sem feriado ou parada
/// de 0 h; exceção com horas disponíveis conta como útil.
/// </summary>
public interface IBusinessCalendar
{
    /// <summary>Dias úteis de <paramref name="from"/> a <paramref name="to"/>, os dois inclusive; zero se o fim vem antes do início.</summary>
    Task<int> WorkingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
