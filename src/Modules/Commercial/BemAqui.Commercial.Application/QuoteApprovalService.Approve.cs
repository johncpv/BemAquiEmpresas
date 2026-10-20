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

        var accounts = await chart.PostableAsync(AccountType.Revenue, cancellationToken);
        if (!accounts.Any(a => a.Id == input.RevenueAccountId && a.IsActive && a.AcceptsEntries))
        {
            return QuoteService.Invalid("revenueAccountId", "Escolha uma conta de receita analítica e ativa.");
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

        var (contract, project) = await CreateAsync(quote, input, projectName, now, cancellationToken);
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

    private static Error AlreadyApproved() => Error.Conflict("quote_approved", "O orçamento já foi aprovado.");
}
