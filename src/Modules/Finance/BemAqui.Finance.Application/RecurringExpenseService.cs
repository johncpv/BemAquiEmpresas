using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

/// <summary>
/// Despesas recorrentes (FIN-02 CA 16 a 20): cadastro com as próximas ocorrências e a geração diária de um título por
/// competência, <c>lead_days</c> antes do vencimento. A competência é única por despesa: rodar de novo, ou em paralelo, não duplica.
/// </summary>
public sealed partial class RecurringExpenseService(IFinanceDb db, IParties parties, IChartOfAccounts chart, INumberSequence numbers, ICurrentUser currentUser, TimeProvider clock, ILogger<RecurringExpenseService> logger)
{
    public async Task<IReadOnlyList<RecurringExpenseDetail>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.RecurringExpenses.AsNoTracking().OrderBy(r => r.Description).ToListAsync(cancellationToken);
        return await DetailsAsync(items, cancellationToken);
    }

    public async Task<Result<RecurringExpenseDetail>> CreateAsync(RecurringExpenseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var expense = new RecurringExpense { CreatedAt = clock.GetUtcNow() };
        if (await ApplyAsync(expense, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.RecurringExpenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([expense], cancellationToken))[0];
    }

    /// <summary>Mudança vale só para as competências ainda não geradas (CA 20).</summary>
    public async Task<Result<RecurringExpenseDetail>> UpdateAsync(Guid id, RecurringExpenseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var expense = await db.RecurringExpenses.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (await ApplyAsync(expense, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([expense], cancellationToken))[0];
    }

    /// <summary>Com título gerado, não exclui: encerra pelo fim (CA 20).</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await db.RecurringExpenses.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (await db.Payables.AnyAsync(p => p.RecurringExpenseId == id, cancellationToken))
        {
            return Error.Conflict("recurring_has_payables", "A despesa já gerou títulos. Encerre-a informando o fim.");
        }

        db.RecurringExpenses.Remove(expense);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Gera os títulos devidos até hoje (CA 17 a 19), inclusive as competências que ficaram para trás.</summary>
    public async Task<int> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var created = 0;
        foreach (var expense in await db.RecurringExpenses.AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken))
        {
            var done = (await db.Payables.Where(p => p.RecurringExpenseId == expense.Id).Select(p => p.Competence).ToListAsync(cancellationToken)).OfType<DateOnly>().ToHashSet();
            for (var competence = expense.Start; expense.DueDateOf(competence).AddDays(-expense.LeadDays) <= today && (expense.End is not { } end || competence <= end); competence = competence.AddMonths(1))
            {
                if (done.Contains(competence) || !await TryCreateAsync(expense, competence, cancellationToken))
                {
                    continue;
                }

                created++;
                if (expense.DueDateOf(competence).AddDays(-expense.LeadDays) < today.AddDays(-1))
                {
                    LogLate(logger, expense.Id, competence);
                }
            }
        }

        return created;
    }

    private async Task<bool> TryCreateAsync(RecurringExpense expense, DateOnly competence, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payable = new Payable
        {
            Number = await numbers.NextAsync(PayableService.DocType, ct), PartyId = expense.PartyId, Description = $"{expense.Description} — {competence:MM/yyyy}", AccountId = expense.AccountId,
            CostCenterId = expense.CostCenterId, Amount = expense.Amount, IssueDate = BusinessTime.Today(clock), DueDate = expense.DueDateOf(competence), Status = PayableStatus.Open,
            Origin = DocumentOrigin.Recurring, RecurringExpenseId = expense.Id, Competence = competence, PaymentMethod = expense.PaymentMethod, PaymentDetails = expense.PaymentDetails,
            PaymentMethodChangedAt = expense.PaymentMethod is null ? null : clock.GetUtcNow(), CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.Payables.Add(payable);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, PayableService.CompetenceIndex))
        {
            // Outra execução gerou a mesma competência ao mesmo tempo (CA 17).
            await transaction.RollbackAsync(ct);
            db.Entry(payable).State = EntityState.Detached;
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recorrente {Expense}: competência {Competence} gerada com atraso.")]
    private static partial void LogLate(ILogger logger, Guid expense, DateOnly competence);

    private static Error NotFound() => Error.NotFound("recurring_not_found", "Despesa recorrente não encontrada.");
}
