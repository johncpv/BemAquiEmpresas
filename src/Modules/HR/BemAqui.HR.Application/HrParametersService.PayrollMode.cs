using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Modo oficial da folha por tipo (FLH-06, <c>hr.payroll_mode</c>): a Mensal pelo parâmetro interno da FLH-02a
/// (<see cref="CalculatedOfficialKey"/>), férias, 13º e rescisão pelos parâmetros do tipo. Valor 1 é a calculada oficial; o texto guarda a
/// competência a partir da qual ela vale ("2026-08"; sem texto, desde sempre). Só a virada e a volta da FLH-06 gravam (auditadas); nada aqui
/// aparece em Configurações.
/// </summary>
public sealed partial class HrParametersService
{
    /// <summary>Competências seguidas aceitas para a virada (CA 5, padrão 3).</summary>
    public const string ParallelMonthsKey = "hr.parallel_months";

    /// <summary>O parâmetro do modo de cada tipo: a Mensal é o da FLH-02a.</summary>
    public static string ModeKey(PayrollParallelType type) => type switch
    {
        PayrollParallelType.Monthly => CalculatedOfficialKey,
        PayrollParallelType.Vacation => CalculatedOfficialKey + ".vacation",
        PayrollParallelType.Thirteenth => CalculatedOfficialKey + ".thirteenth",
        _ => CalculatedOfficialKey + ".termination",
    };

    /// <summary>Quantas competências seguidas aceitas a virada exige (de 3 a 12).</summary>
    public async Task<int> ParallelMonthsAsync(CancellationToken cancellationToken = default) =>
        await NumberAsync(ParallelMonthsKey, cancellationToken) is { } months and >= PayrollParallelRules.DefaultMonths and <= PayrollParallelRules.MaxMonths
            ? (int)months : PayrollParallelRules.DefaultMonths;

    /// <summary>
    /// A folha calculada é a oficial para o tipo e a competência (FLH-06 CA 5 e 6): a Mensal (e o adiantamento) depois da virada, a partir
    /// da competência seguinte às aceitas; férias, 13º e rescisão só com a Mensal virada e o tipo virado (um caso conferido).
    /// </summary>
    public async Task<bool> CalculatedOfficialAsync(PayrollRunType type, DateOnly period, CancellationToken cancellationToken = default)
    {
        var parallel = PayrollParallelRules.TypeOf(type) ?? PayrollParallelType.Monthly;
        var keys = new[] { ModeKey(PayrollParallelType.Monthly), ModeKey(parallel) }.Distinct().ToList();
        var stored = await db.HrParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        return keys.All(k => stored.GetValueOrDefault(k) is { Value: 1m } p && (PayrollParallelRules.ParseMonth(p.Text) is not { } from || period >= from));
    }

    /// <summary>O modo atual de cada tipo, com a competência a partir da qual vale (null: desde sempre ou lançada).</summary>
    public async Task<IReadOnlyDictionary<PayrollParallelType, (bool Calculated, DateOnly? From)>> ModesAsync(CancellationToken cancellationToken = default)
    {
        var keys = Enum.GetValues<PayrollParallelType>().Select(ModeKey).ToList();
        var stored = await db.HrParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        return Enum.GetValues<PayrollParallelType>().ToDictionary(t => t, t =>
        {
            var p = stored.GetValueOrDefault(ModeKey(t));
            return (p?.Value == 1m, p?.Value == 1m ? PayrollParallelRules.ParseMonth(p.Text) : null);
        });
    }

    /// <summary>Grava o modo do tipo (a virada ou a volta, numa transação de quem chama): o parâmetro é auditado como os outros.</summary>
    internal async Task SetModeAsync(PayrollParallelType type, bool calculated, DateOnly from, CancellationToken cancellationToken = default)
    {
        var key = ModeKey(type);
        var parameter = await db.HrParameters.FirstOrDefaultAsync(p => p.Key == key, cancellationToken);
        var now = clock.GetUtcNow();
        var text = calculated ? PayrollParallelRules.Month(from) : null;
        if (parameter is null)
        {
            db.HrParameters.Add(new HrParameter { Key = key, Value = calculated ? 1m : 0m, Text = text, UpdatedAt = now });
            return;
        }

        parameter.Value = calculated ? 1m : 0m;
        parameter.Text = text;
        parameter.UpdatedAt = now;
    }
}
