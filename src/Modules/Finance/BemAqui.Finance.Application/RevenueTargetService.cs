using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Os 12 meses do ano, na ordem; nulo (ou ausente) deixa o mês sem meta.</summary>
public sealed record RevenueTargetsInput(IReadOnlyList<decimal?> Months);

/// <summary>
/// Metas de faturamento (DSH-01a CA 9, ADR 0028 §1 e ADR 0016 M6): <c>finance.revenue_targets</c>, lida pelo painel do
/// proprietário e editada em Configurações → Parâmetros → Financeiro com <c>settings.edit</c>; a auditoria grava antes e depois.
/// </summary>
public sealed class RevenueTargetService(IFinanceDb db, IPermissionChecker permissions, IEventPublisher events, TimeProvider clock) : IRevenueTargets
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;
    private const string UniqueIndex = "ix_revenue_targets_year_month";

    public async Task<RevenueTargetsYear> ForYearAsync(int year, CancellationToken cancellationToken = default)
    {
        var rows = await db.RevenueTargets.AsNoTracking().Where(t => t.Year == year).ToDictionaryAsync(t => t.Month, t => t.Amount, cancellationToken);
        return ToYear(year, rows);
    }

    public async Task<Result<RevenueTargetsYear>> GetAsync(int year, CancellationToken cancellationToken = default) =>
        year is < MinYear or > MaxYear ? InvalidYear() : await ForYearAsync(year, cancellationToken);

    public async Task<Result<RevenueTargetsYear>> SaveAsync(int year, RevenueTargetsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (year is < MinYear or > MaxYear)
        {
            return InvalidYear();
        }

        var months = input.Months ?? [];
        if (months.Count != 12)
        {
            return ReceivableService.Invalid("months", "Informe a meta dos 12 meses do ano.");
        }

        for (var i = 0; i < 12; i++)
        {
            if (months[i] is { } value && (value < 0 || value > MaxAmount))
            {
                return ReceivableService.Invalid($"months[{i}]", "Informe uma meta maior ou igual a zero.");
            }
        }

        var existing = await db.RevenueTargets.Where(t => t.Year == year).ToDictionaryAsync(t => t.Month, cancellationToken);
        var now = clock.GetUtcNow();
        for (var month = 1; month <= 12; month++)
        {
            var wanted = months[month - 1] is { } amount ? Math.Round(amount, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
            existing.TryGetValue(month, out var row);
            if (wanted is null)
            {
                if (row is not null)
                {
                    db.RevenueTargets.Remove(row);
                }
            }
            else if (row is null)
            {
                db.RevenueTargets.Add(new RevenueTarget { Year = year, Month = month, Amount = wanted.Value, UpdatedAt = now });
            }
            else if (row.Amount != wanted.Value)
            {
                row.Amount = wanted.Value;
                row.UpdatedAt = now;
            }
        }

        events.Publish(new RevenueTargetsChanged(year)); // o painel recalcula o faturamento (gravado junto, pelo outbox)
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, UniqueIndex))
        {
            return Error.Conflict("revenue_targets_conflict", "As metas foram salvas por outra pessoa ao mesmo tempo. Recarregue e confira.");
        }

        return await ForYearAsync(year, cancellationToken);
    }

    /// <summary>Os 12 meses, com nulo onde não há meta; o total do ano soma só os meses com meta.</summary>
    public static RevenueTargetsYear ToYear(int year, IReadOnlyDictionary<int, decimal> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        var months = Enumerable.Range(1, 12).Select(m => new RevenueTargetMonth(m, amounts.TryGetValue(m, out var a) ? a : null)).ToList();
        return new RevenueTargetsYear(year, months, amounts.Count == 0 ? null : amounts.Values.Sum());
    }

    private static Error InvalidYear() => ReceivableService.Invalid("year", $"Escolha um ano entre {MinYear} e {MaxYear}.");
}
