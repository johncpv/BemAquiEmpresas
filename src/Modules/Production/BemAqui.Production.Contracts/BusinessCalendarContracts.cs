namespace BemAqui.Production.Contracts;

/// <summary>
/// Dias úteis pelo calendário padrão do tenant (CAD-03), para os outros módulos: o painel do proprietário conta os dias úteis
/// que faltam no mês (DSH-01a CA 9), a folha antecipa o vencimento das guias que caem em dia não útil (RH-02a CA 3) e a RH-02c e a
/// RH-01c reaproveitam. Dia útil é dia de trabalho do calendário sem feriado ou parada de 0 h; exceção com horas disponíveis conta
/// como útil. Nenhum módulo lê as tabelas do Production.
/// </summary>
public interface IBusinessCalendar
{
    /// <summary>Dias úteis de <paramref name="from"/> a <paramref name="to"/>, os dois inclusive; zero se o fim vem antes do início.</summary>
    Task<int> WorkingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Os dias úteis de <paramref name="from"/> a <paramref name="to"/>, em ordem (vazio se o fim vem antes do início): o painel do supervisor
    /// conta em quantos dias úteis a falta de material bloqueia (DSH-03 CA 5) numa leitura só.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> WorkingDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>O dia é útil no calendário padrão (RH-02a)?</summary>
    Task<bool> IsBusinessDayAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// O <paramref name="n"/>-ésimo dia útil do mês (1 = o primeiro; o 5º dia útil é o pagamento dos salários da RH-02c); null se o mês
    /// não tem tantos dias úteis.
    /// </summary>
    Task<DateOnly?> NthBusinessDayOfMonthAsync(int year, int month, int n, CancellationToken cancellationToken = default);

    /// <summary>
    /// O próprio dia, se útil; senão o dia útil anterior mais próximo ("dia não útil antecipa", RH-02a CA 3). Sem dia útil nos 31 dias
    /// anteriores (calendário sem dia de trabalho), devolve o próprio dia.
    /// </summary>
    Task<DateOnly> PreviousBusinessDayAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Exceções do calendário padrão (feriados, paradas e dias especiais) de <paramref name="from"/> a <paramref name="to"/>, em ordem.</summary>
    Task<IReadOnlyList<BusinessCalendarExceptionDay>> ExceptionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>Exceção do calendário padrão: a data, as horas disponíveis (0 = não útil) e a descrição.</summary>
public sealed record BusinessCalendarExceptionDay(DateOnly Date, decimal HoursAvailable, string Description);
