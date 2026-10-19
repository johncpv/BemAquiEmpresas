using BemAqui.BuildingBlocks.Paging;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Condições de pagamento (CAD-05b CA 11 a 17): parcelas somando 100%, dias crescentes; com uso, só inativa.</summary>
public sealed class PaymentTermService(IFinanceDb db, IEnumerable<IPaymentTermUsageProvider> usages, TimeProvider clock)
{
    public async Task<PagedList<PaymentTermDetail>> ListAsync(bool includeInactive, PageRequest page, CancellationToken cancellationToken = default)
    {
        var result = await db.PaymentTerms.AsNoTracking().Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Name).ToPagedListAsync(page, cancellationToken);
        return new PagedList<PaymentTermDetail>(result.Items.Select(ToDetail).ToList(), result.Page, result.PageSize, result.Total);
    }

    public async Task<Result<PaymentTermDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var term = await db.PaymentTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return term is null ? NotFound() : ToDetail(term);
    }

    public async Task<Result<PaymentTermDetail>> CreateAsync(PaymentTermInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (FinanceValidation.PaymentTerm(input) is { } invalid)
        {
            return invalid;
        }

        var term = new PaymentTerm { Name = input.Name.Trim(), Installments = [.. input.Installments], CreatedAt = clock.GetUtcNow() };
        db.PaymentTerms.Add(term);
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(term);
    }

    public async Task<Result<PaymentTermDetail>> UpdateAsync(Guid id, PaymentTermInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var term = await db.PaymentTerms.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (term is null)
        {
            return NotFound();
        }

        if (FinanceValidation.PaymentTerm(input) is { } invalid)
        {
            return invalid;
        }

        term.Name = input.Name.Trim();
        term.Installments = [.. input.Installments];
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(term);
    }

    public async Task<Result<PaymentTermDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var term = await db.PaymentTerms.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (term is null)
        {
            return NotFound();
        }

        term.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(term);
    }

    /// <summary>Em uso (ex.: condição padrão de um terceiro), não exclui: 409 e o diálogo oferece "Inativar" (CA 17).</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var term = await db.PaymentTerms.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (term is null)
        {
            return NotFound();
        }

        foreach (var usage in usages)
        {
            if (await usage.UsageAsync(id, cancellationToken) is { } where)
            {
                return Error.Conflict("payment_term_in_use", "A condição está em uso. Inative-a.") with { Data = new Dictionary<string, object?> { ["usage"] = where } };
            }
        }

        db.PaymentTerms.Remove(term);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Prévia da divisão com as parcelas da tela (CA 13): a mesma conta do <see cref="IPaymentTerms.SplitAsync"/>.</summary>
    public static Result<IReadOnlyList<InstallmentDue>> Preview(SplitPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (FinanceValidation.Installments(request.Installments) is { } invalid)
        {
            return invalid;
        }

        if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount)
        {
            const string message = "Informe um valor maior que zero, com até 2 casas.";
            return Error.Validation("validation", message, new Dictionary<string, string[]> { ["amount"] = [message] });
        }

        return Result<IReadOnlyList<InstallmentDue>>.Success(PaymentTermSplitter.Split(request.Installments, request.Amount, request.BaseDate));
    }

    private static Error NotFound() => Error.NotFound("payment_term_not_found", "Condição de pagamento não encontrada.");

    public static PaymentTermDetail ToDetail(PaymentTerm t) => new(t.Id, t.Name, t.Installments, t.IsActive, t.SeedKey, t.ImportSource, t.CreatedAt);
}
