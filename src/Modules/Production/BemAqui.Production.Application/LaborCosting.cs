using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Custo do apontamento sem banco (PRD-04 CA 1 e 2, ADR 0019 §9): minutos líquidos (todas as pausas fora) ÷ 60 × custo-hora,
/// arredondado só no total do apontamento, com 2 casas e <see cref="MidpointRounding.AwayFromZero"/>. Sem custo-hora (nulo ou zero),
/// o custo é 0 e o apontamento fica "Sem custo-hora".
/// </summary>
public static class LaborCostCalculator
{
    public static bool HasRate(decimal? hourlyCost) => hourlyCost is > 0;

    public static decimal Cost(int minutes, decimal? hourlyCost) =>
        HasRate(hourlyCost) && minutes > 0 ? Math.Round(minutes * hourlyCost!.Value / 60m, 2, MidpointRounding.AwayFromZero) : 0m;

    /// <summary>Aplica uma versão do custo ao apontamento (snapshot, valor, marca e versão) e devolve o valor anterior.</summary>
    public static decimal Apply(LaborEntry entry, int minutes, decimal? hourlyCost, Guid? positionId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var previous = entry.Cost;
        var rate = HasRate(hourlyCost) ? hourlyCost : null;
        entry.HourlyCostSnapshot = rate;
        entry.Cost = Cost(minutes, rate);
        entry.CostMissing = rate is null;
        entry.CostPositionId = positionId;
        entry.CostVersion++;
        return previous;
    }
}

/// <summary>
/// Custo de mão de obra do apontamento (PRD-04): snapshot do custo-hora do cargo no fechamento (ponto 73), correção com o mesmo
/// snapshot (CA 4), recálculo do "Sem custo-hora" (CA 6) e recomposição (D3, CA 12). Cada versão grava uma linha no histórico
/// (CA 17) e devolve o evento com a versão e os valores antes e depois (ADR 0025 §2), publicado por quem chamou, na mesma transação.
/// O custo-hora vem só do cargo (<see cref="IHourlyCost"/>), nunca do salário (CA 15).
/// </summary>
public sealed class LaborCosting(
    IProductionDb db, IEmployees employees, IHourlyCost hourlyCosts, IPositions positions, IUserDirectory users, INotificationService notifications, ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string NoCostType = "production.labor_no_cost";
    public const string PositionLink = "/app/rh/cargos?cargo=";

    private readonly Dictionary<Guid, Guid?> _positionOf = [];
    private readonly Dictionary<Guid, decimal?> _rateOf = [];

    /// <summary>Cargo atual do funcionário e o custo-hora atual do cargo (nulo sem cargo ou sem custo-hora).</summary>
    public async Task<(Guid? PositionId, decimal? Rate)> CurrentRateAsync(Guid employeeId, CancellationToken ct)
    {
        if (!_positionOf.TryGetValue(employeeId, out var position))
        {
            position = (await employees.FindAsync([employeeId], ct)) is [var found, ..] && found.PositionId != Guid.Empty ? found.PositionId : null;
            _positionOf[employeeId] = position;
        }

        return (position, position is { } id ? await RateAsync(id, ct) : null);
    }

    public async Task<decimal?> RateAsync(Guid positionId, CancellationToken ct)
    {
        if (!_rateOf.TryGetValue(positionId, out var rate))
        {
            rate = await hourlyCosts.ForPositionAsync(positionId, ct);
            _rateOf[positionId] = rate;
        }

        return rate;
    }

    /// <summary>
    /// Fechamento (CA 1, 2, 5) ou recomposição (CA 12, <paramref name="origin"/> = <c>Backfill</c>): custo-hora atual do cargo como
    /// snapshot, versão 1, e o <see cref="LaborEntryClosed"/> com 0 → valor. Sem custo-hora, avisa quem tem <c>hr.edit</c>.
    /// </summary>
    public async Task<LaborEntryClosed> CloseAsync(LaborEntry entry, Guid projectId, int minutes, LaborCostOrigin origin, DateOnly occurredOn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var (position, rate) = await CurrentRateAsync(entry.EmployeeId, ct);
        entry.CostVersion = 0;
        var previous = LaborCostCalculator.Apply(entry, minutes, rate, position);
        entry.CostBackfilled = origin == LaborCostOrigin.Backfill;
        Record(entry, origin, minutes, previous);
        if (entry.CostMissing)
        {
            await NotifyMissingAsync(entry, position, ct);
        }

        return new LaborEntryClosed(entry.Id, entry.OrderId, projectId, entry.OrderStepId, entry.EmployeeId, minutes, entry.CostVersion, previous, entry.Cost, occurredOn, origin);
    }

    /// <summary>
    /// Correção (CA 4): o mesmo snapshot e as horas novas; versão + 1. Apontamento fechado antes da PRD-04 (versão 0) ainda não tem
    /// snapshot e recebe o custo-hora atual, com a marca "Calculado na implantação".
    /// </summary>
    public async Task<LaborEntryCorrected> CorrectAsync(LaborEntry entry, Guid projectId, int minutes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var legacy = entry.CostVersion == 0;
        var (position, rate) = legacy ? await CurrentRateAsync(entry.EmployeeId, ct) : (entry.CostPositionId, entry.HourlyCostSnapshot);
        var previous = LaborCostCalculator.Apply(entry, minutes, rate, position);
        entry.CostBackfilled = entry.CostBackfilled || legacy;
        Record(entry, LaborCostOrigin.Correction, minutes, previous);
        if (entry.CostMissing)
        {
            await NotifyMissingAsync(entry, position, ct);
        }

        return Corrected(entry, projectId, minutes, previous, LaborCostOrigin.Correction);
    }

    /// <summary>Recálculo do "Sem custo-hora" (CA 6): o custo-hora atual do cargo vira o snapshot; versão + 1; perde a marca.</summary>
    public LaborEntryCorrected Recalculate(LaborEntry entry, Guid projectId, int minutes, Guid positionId, decimal rate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var previous = LaborCostCalculator.Apply(entry, minutes, rate, positionId);
        Record(entry, LaborCostOrigin.Recalculation, minutes, previous);
        return Corrected(entry, projectId, minutes, previous, LaborCostOrigin.Recalculation);
    }

    private LaborEntryCorrected Corrected(LaborEntry entry, Guid projectId, int minutes, decimal previous, LaborCostOrigin origin) =>
        new(entry.Id, entry.OrderId, projectId, minutes, entry.CostVersion, previous, entry.Cost, BusinessTime.Today(clock), origin);

    private void Record(LaborEntry entry, LaborCostOrigin origin, int minutes, decimal previous) =>
        db.LaborCostHistory.Add(new LaborCostRecord
        {
            LaborEntryId = entry.Id, OrderId = entry.OrderId, CostVersion = entry.CostVersion, Origin = origin, Minutes = minutes, HourlyCostSnapshot = entry.HourlyCostSnapshot,
            PreviousAmount = previous, Amount = entry.Cost, CostMissing = entry.CostMissing, RecordedBy = currentUser.UserId, RecordedAt = clock.GetUtcNow(),
        });

    /// <summary>
    /// "Cargo {nome} sem custo-hora: {n} apontamentos sem custo." para quem tem <c>hr.edit</c>, com link para o cargo, uma vez por
    /// cargo (<c>labor-no-cost:{positionId}</c>). Funcionário sem cargo: link para o funcionário.
    /// </summary>
    private async Task NotifyMissingAsync(LaborEntry entry, Guid? position, CancellationToken ct)
    {
        var recipients = await users.UsersWithPermissionAsync("hr.edit", ct);
        if (recipients.Count == 0)
        {
            return;
        }

        string title, link, key;
        if (position is { } id)
        {
            var pending = await db.LaborEntries.AsNoTracking().CountAsync(e => e.CostMissing && e.CostPositionId == id && e.Id != entry.Id, ct) + 1;
            var name = (await positions.FindAsync([id], ct)) is [var found, ..] ? found.Name : "sem nome";
            title = $"Cargo {name} sem custo-hora: {pending} {(pending == 1 ? "apontamento" : "apontamentos")} sem custo.";
            link = PositionLink + id;
            key = $"labor-no-cost:{id}";
        }
        else
        {
            title = "Funcionário sem cargo: apontamento sem custo. Defina o cargo para calcular o custo da hora.";
            link = "/app/rh/funcionarios/" + entry.EmployeeId;
            key = $"labor-no-cost:employee:{entry.EmployeeId}";
        }

        foreach (var user in recipients)
        {
            await notifications.NotifyOnceAsync(user, NoCostType, title, link, key, ct);
        }
    }
}