using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary><see cref="IPaymentTermImport"/> (MIG-01b CA 13): mesmas validações da tela (soma 100%, dias crescentes).</summary>
public sealed class PaymentTermImport(IFinanceDb db, TimeProvider clock) : IPaymentTermImport
{
    public async Task<Guid?> FindBySeedKeyAsync(string seedKey, CancellationToken cancellationToken = default) =>
        await db.PaymentTerms.Where(t => t.SeedKey == seedKey).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        await db.PaymentTerms.Where(t => t.Name == name.Trim()).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<FinanceImportWrite>> CreateAsync(PaymentTermImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (FinanceValidation.PaymentTerm(new PaymentTermInput(data.Name, data.Installments)) is { } invalid)
        {
            return invalid;
        }

        var term = new PaymentTerm { Name = data.Name.Trim(), Installments = [.. data.Installments], IsActive = data.IsActive, ImportSource = data.ImportSource, CreatedAt = clock.GetUtcNow() };
        db.PaymentTerms.Add(term);
        await db.SaveChangesAsync(cancellationToken);
        return new FinanceImportWrite(term.Id, await VersionAsync(term.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<FinanceImportWrite>> UpdateAsync(Guid termId, PaymentTermImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var term = await db.PaymentTerms.FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
        {
            return NotFound();
        }

        if (FinanceValidation.PaymentTerm(new PaymentTermInput(data.Name, data.Installments)) is { } invalid)
        {
            return invalid;
        }

        // Condição do seed ligada pelo de-para: a importação não muda as parcelas dela.
        if (term.SeedKey is null)
        {
            term.Name = data.Name.Trim();
            term.Installments = [.. data.Installments];
            term.IsActive = data.IsActive;
        }

        term.ImportSource ??= data.ImportSource;
        await db.SaveChangesAsync(cancellationToken);
        return new FinanceImportWrite(term.Id, await VersionAsync(term.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<FinanceImportWrite>> LinkAsync(Guid termId, string importSource, CancellationToken cancellationToken = default)
    {
        var term = await db.PaymentTerms.FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
        {
            return NotFound();
        }

        term.ImportSource ??= importSource;
        await db.SaveChangesAsync(cancellationToken);
        return new FinanceImportWrite(term.Id, await VersionAsync(term.Id, cancellationToken) ?? 0);
    }

    public async Task<PaymentTermImportData?> CurrentAsync(Guid termId, CancellationToken cancellationToken = default)
    {
        var term = await db.PaymentTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        return term is null ? null : new PaymentTermImportData(term.Name, term.Installments, term.IsActive, term.ImportSource ?? string.Empty);
    }

    public Task<long?> VersionAsync(Guid termId, CancellationToken cancellationToken = default) =>
        db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM finance.payment_terms WHERE id = {termId} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken);

    private static Error NotFound() => Error.NotFound("payment_term_not_found", "Condição de pagamento não encontrada.");
}
