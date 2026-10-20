using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    private const int MaxMilestones = 30;

    /// <summary>Aba Condições (CA 1 a 5): marcos com % e valor, soma, aviso e a condição padrão do cliente.</summary>
    public async Task<Result<QuoteConditions>> ConditionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        var milestones = await db.QuoteMilestones.AsNoTracking().Where(m => m.QuoteId == id).OrderBy(m => m.Sequence).ToListAsync(cancellationToken);
        var party = await parties.FindAsync(quote.PartyId, cancellationToken);
        return Conditions(quote, milestones, party?.DefaultPaymentTermId);
    }

    /// <summary>
    /// Salvar condições (CA 1 a 4, 7 e 8): substitui os marcos (soma diferente de 100% grava com aviso; a aprovação recusa) e a
    /// validade, que não pode ser no passado.
    /// </summary>
    public async Task<Result<QuoteConditions>> SaveConditionsAsync(Guid id, QuoteConditionsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var editable = await EditableAsync(id, input.Version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        var today = BusinessTime.Today(clock);
        if (input.ValidUntil < today)
        {
            return Invalid("validUntil", "A validade não pode ser no passado.");
        }

        if (input.Milestones.Count > MaxMilestones)
        {
            return Invalid("milestones", $"Informe até {MaxMilestones} marcos.");
        }

        for (var i = 0; i < input.Milestones.Count; i++)
        {
            if (MilestoneError(i, input.Milestones[i], today) is { } error)
            {
                return error;
            }
        }

        db.QuoteMilestones.RemoveRange(await db.QuoteMilestones.Where(m => m.QuoteId == id).ToListAsync(cancellationToken));
        var amounts = PercentSplit.Amounts(quote.TotalPrice, input.Milestones.Select(m => Math.Round(m.Percent, 4, MidpointRounding.AwayFromZero)).ToList());
        var created = input.Milestones.Select((m, i) => new QuoteMilestone
        {
            QuoteId = id, Sequence = i + 1, Description = m.Description.Trim(), Percent = Math.Round(m.Percent, 4, MidpointRounding.AwayFromZero), Amount = amounts[i], Trigger = m.Trigger,
            TriggerEvent = m.Trigger == MilestoneTrigger.Event ? m.TriggerEvent : null, DueDate = m.Trigger == MilestoneTrigger.Date ? m.DueDate : null,
            DaysAfterEvent = m.Trigger == MilestoneTrigger.Event ? m.DaysAfterEvent ?? 0 : null,
        }).ToList();
        db.QuoteMilestones.AddRange(created);
        quote.ValidUntil = input.ValidUntil ?? quote.ValidUntil;
        await db.SaveChangesAsync(cancellationToken);
        var party = await parties.FindAsync(quote.PartyId, cancellationToken);
        return Conditions(quote, created, party?.DefaultPaymentTermId);
    }

    /// <summary>"Preencher pela condição de pagamento" (CA 5): cada parcela vira um marco no evento Assinatura, sem gravar.</summary>
    public async Task<Result<IReadOnlyList<QuoteMilestoneInput>>> FromPaymentTermAsync(Guid id, Guid termId, CancellationToken cancellationToken = default)
    {
        if (!await db.Quotes.AnyAsync(x => x.Id == id, cancellationToken))
        {
            return NotFound();
        }

        if (await paymentTerms.FindAsync(termId, cancellationToken) is not { } term)
        {
            return Invalid("termId", "Condição de pagamento não encontrada.");
        }

        IReadOnlyList<QuoteMilestoneInput> list = term.Installments.Select((p, i) => new QuoteMilestoneInput(
            term.Installments.Count == 1 ? term.Name : $"Parcela {i + 1} ({p.Days} dias)", p.Percent, MilestoneTrigger.Event, TriggerEvent.Signature, null, p.Days)).ToList();
        return Result<IReadOnlyList<QuoteMilestoneInput>>.Success(list);
    }

    private static Error? MilestoneError(int i, QuoteMilestoneInput m, DateOnly today)
    {
        var field = $"milestones[{i}]";
        if (string.IsNullOrWhiteSpace(m.Description) || m.Description.Trim().Length > 120)
        {
            return Invalid(field + ".description", "Informe a descrição, com até 120 caracteres.");
        }

        if (m.Percent <= 0 || m.Percent > 100)
        {
            return Invalid(field + ".percent", "O percentual vai de 0,0001% a 100%.");
        }

        if (m.Trigger == MilestoneTrigger.Date)
        {
            return m.DueDate is not { } due ? Invalid(field + ".dueDate", "Informe a data.") : due < today ? Invalid(field + ".dueDate", "A data não pode ser no passado.") : null;
        }

        if (m.TriggerEvent is null)
        {
            return Invalid(field + ".triggerEvent", "Escolha o evento.");
        }

        return m.DaysAfterEvent is < 0 or > 3650 ? Invalid(field + ".daysAfterEvent", "O prazo após o evento vai de 0 a 3.650 dias.") : null;
    }

    private static QuoteConditions Conditions(Quote quote, List<QuoteMilestone> milestones, Guid? defaultTerm)
    {
        var sum = milestones.Sum(m => m.Percent);
        var warning = milestones.Count > 0 && sum != 100m
            ? $"A soma dos marcos está em {sum.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}%. Para aprovar, precisa ser 100%."
            : null;
        return new QuoteConditions(
            milestones.OrderBy(m => m.Sequence).Select(m => new QuoteMilestoneView(m.Id, m.Sequence, m.Description, m.Percent, m.Amount, m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent)).ToList(),
            sum, warning, quote.ValidUntil, defaultTerm, quote.Version);
    }

    /// <summary>Preço mudou: os valores dos marcos seguem os percentuais (CA 6).</summary>
    private async Task RecalculateMilestonesAsync(Quote quote, CancellationToken ct)
    {
        var milestones = await db.QuoteMilestones.Where(m => m.QuoteId == quote.Id).OrderBy(m => m.Sequence).ToListAsync(ct);
        var amounts = PercentSplit.Amounts(quote.TotalPrice, milestones.Select(m => m.Percent).ToList());
        for (var i = 0; i < milestones.Count; i++)
        {
            milestones[i].Amount = amounts[i];
        }
    }
}
