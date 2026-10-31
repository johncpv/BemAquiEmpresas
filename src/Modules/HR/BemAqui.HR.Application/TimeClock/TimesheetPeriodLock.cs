using System.Globalization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>
/// Período de apuração do ponto (RH-01b CA 4 e 16, RH-01c): o início do período mais antigo em aberto e a recusa do dia num período
/// fechado. A RH-01c registra a implementação real (pelos <c>hr.timesheets</c> fechados); até lá, <see cref="NoTimesheetClosings"/>:
/// nenhum período fechado.
/// </summary>
public interface ITimesheetPeriodLock
{
    /// <summary>Primeiro dia do período mais antigo em aberto; null quando nenhum período foi fechado.</summary>
    Task<DateOnly?> OldestOpenStartAsync(CancellationToken cancellationToken = default);

    /// <summary>409 <c>timesheet_period_closed</c> quando o dia está num período fechado; null quando está aberto.</summary>
    Task<Error?> CheckAsync(DateOnly day, CancellationToken cancellationToken = default);
}

/// <summary>Antes da RH-01c: nenhum período de ponto fechado.</summary>
public sealed class NoTimesheetClosings : ITimesheetPeriodLock
{
    public Task<DateOnly?> OldestOpenStartAsync(CancellationToken cancellationToken = default) => Task.FromResult<DateOnly?>(null);

    public Task<Error?> CheckAsync(DateOnly day, CancellationToken cancellationToken = default) => Task.FromResult<Error?>(null);
}

public static class TimesheetPeriods
{
    public const string ClosedCode = "timesheet_period_closed";

    /// <summary>CA 16: "O período 10/2026 está fechado. Peça a reabertura ao RH."</summary>
    public static Error Closed(DateOnly periodMonth) =>
        Error.Conflict(ClosedCode, $"O período {periodMonth.ToString("MM/yyyy", CultureInfo.InvariantCulture)} está fechado. Peça a reabertura ao RH.");
}
