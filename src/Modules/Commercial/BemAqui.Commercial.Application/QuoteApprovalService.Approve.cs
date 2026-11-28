using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteApprovalService
{
    public const string ContractDocType = "CTR";
    public const string ProjectDocType = "PRJ";
    public const string QuoteIndex = "ix_contracts_quote_id";

    /// <summary>Aprovar e gerar contrato (CA 4 a 9): tudo numa transação; a segunda aprovação concorrente recebe 409.</summary>
    public async Task<Result<ApprovalResult>> ApproveAsync(Guid id, ApproveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var quote = await db.Quotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteService.NotFound();
        }

        if (quote.Status == QuoteStatus.Approved)
        {
            return AlreadyApproved();
        }

        if (quote.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O orçamento mudou desde que você abriu. Recarregue.");
        }

        if (input.SignedAt > BusinessTime.Today(clock))
        {
            return QuoteService.Invalid("signedAt", "A data de assinatura não pode ser no futuro.");
        }

        var conditions = (await quotes.ConditionsAsync(id, cancellationToken)).Value;
        var failing = (await PreconditionsAsync(quote, conditions, cancellationToken)).Where(p => !p.Ok).Select(p => p.Text).ToArray();
        if (failing.Length > 0)
        {
            return Error.Validation("approval_preconditions", string.Join(" ", failing), new Dictionary<string, string[]> { ["preconditions"] = failing });
        }

        // EXE-01a CA 27: o tipo é recalculado pelos itens; marco de fabricação em projeto de serviço volta para a aba Condições.
        if (await MilestonePolicyErrorAsync(quote.Id, MilestoneValidationMode.Approval, cancellationToken) is { } milestoneError)
        {
            return milestoneError;
        }

        var accounts = await chart.PostableAsync(AccountType.Revenue, cancellationToken);
        if (!accounts.Any(a => a.Id == input.RevenueAccountId && a.IsActive && a.AcceptsEntries))
        {
            return QuoteService.Invalid("revenueAccountId", "Escolha uma conta de receita analítica e ativa.");
        }

        var onboarding = await OnboardingAsync(input, conditions, cancellationToken);
        if (onboarding.IsFailure)
        {
            return onboarding.Error!;
        }

        var detail = (await quotes.GetAsync(id, cancellationToken)).Value;
        var projectName = await ProjectNameAsync(quote, detail, cancellationToken);
        db.Entry(quote).Property(x => x.Version).OriginalValue = input.Version;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        quote.Status = QuoteStatus.Approved;
        quote.ApprovedAt = now;
        quote.ApprovedBy = currentUser.UserId;
        try
        {
            // Primeiro a transição do orçamento (xmin): a aprovação concorrente para aqui, antes de numerar o contrato.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AlreadyApproved();
        }

        var (contract, project) = await CreateAsync(quote, input, onboarding.Value, projectName, now, cancellationToken);
        await NotifyOnboardingAsync(contract, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, QuoteIndex))
        {
            return AlreadyApproved();
        }

        await transaction.CommitAsync(cancellationToken);
        return new ApprovalResult(contract.Id, contract.Number, project.Id, project.Number);
    }

    /// <summary>Recurso por perfil do projeto de serviço no tenant do request (adendo de 27/11/2026 da ADR 0046).</summary>
    private bool ServiceExecutionReleased => tenant.Resolved?.HasFeature(BemAqui.BuildingBlocks.Modules.BusinessProfileCatalog.ServiceExecutionFeature) == true;

    /// <summary>Validador único de marco (EXE-01a CA 27) com o tipo calculado pelos itens do orçamento; na aprovação, o 422 leva a aba e o índice.</summary>
    private async Task<Error?> MilestonePolicyErrorAsync(Guid quoteId, MilestoneValidationMode mode, CancellationToken ct)
    {
        var kinds = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == quoteId).Select(i => i.Kind).ToListAsync(ct);
        var kind = ExecutionKindRule.For(ServiceExecutionReleased, kinds);
        var milestones = await db.QuoteMilestones.AsNoTracking().Where(m => m.QuoteId == quoteId).OrderBy(m => m.Sequence)
            .Select(m => new { m.TriggerEvent, m.Description }).ToListAsync(ct);
        return MilestoneTriggerPolicy.Validate(kind, milestones.Select(m => (m.TriggerEvent, m.Description)).ToList(), mode) is { } violation
            ? MilestonePolicyError(violation, mode == MilestoneValidationMode.Approval)
            : null;
    }

    /// <summary>422 <c>milestone_trigger_not_allowed</c> no campo do gatilho; com <paramref name="withTab"/>, a aba "condicoes" e o índice do marco.</summary>
    internal static Error MilestonePolicyError(MilestoneViolation violation, bool withTab) =>
        Error.Validation("milestone_trigger_not_allowed", violation.Message, new Dictionary<string, string[]> { [violation.Field] = [violation.Message] }) with
        {
            Data = withTab ? new Dictionary<string, object?> { ["tab"] = "condicoes", ["milestoneIndex"] = violation.Index } : null,
        };

    private static Error AlreadyApproved() => Error.Conflict("quote_approved", "O orçamento já foi aprovado.");
}
