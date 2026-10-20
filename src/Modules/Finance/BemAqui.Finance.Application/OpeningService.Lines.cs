using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class OpeningService
{
    private const int MaxLines = 500;

    private async Task<Result<OpeningBatchResult>> SaveAsync<T>(List<T> items, OpeningBatchKind kind, Func<T, decimal> amount, Action<T, string> number, string docType, DbSet<T> set, CancellationToken ct)
        where T : class
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var item in items)
        {
            number(item, await numbers.NextAsync(docType, ct));
            set.Add(item);
        }

        var total = items.Sum(amount);
        db.OpeningBatches.Add(new OpeningBatch { Kind = kind, Count = items.Count, Total = total, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new OpeningBatchResult(items.Count, total);
    }

    private async Task<Error?> GuardAsync(int count, CancellationToken ct)
    {
        var setup = await db.OpeningSetups.AsNoTracking().FirstOrDefaultAsync(ct);
        if (setup?.CompletedAt is { } done)
        {
            return Completed(done);
        }

        if (setup?.CutoverDate is null)
        {
            return Error.Conflict("cutover_missing", "Defina a data de corte antes.");
        }

        return count is 0 or > MaxLines ? ReceivableService.Invalid("lines", $"Informe de 1 a {MaxLines} títulos por lote.") : null;
    }

    private async Task<Error?> LineErrorAsync(int i, Guid partyId, PartyRoleKind? role, string? description, string? reference, decimal balance, Guid? costCenterId, CancellationToken ct)
    {
        var party = await parties.FindAsync(partyId, ct);
        var allowed = party is { IsActive: true, IsDeleted: false }
            && (role is { } r ? party.Roles.Contains(r) : party.Roles.Contains(PartyRoleKind.Supplier) || party.Roles.Contains(PartyRoleKind.Installer));
        if (!allowed)
        {
            return ReceivableService.Invalid($"lines[{i}].partyId", role is null ? "Escolha um fornecedor ou instalador." : "Escolha um terceiro com o papel Cliente.");
        }

        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 200)
        {
            return ReceivableService.Invalid($"lines[{i}].description", "Informe a descrição, com até 200 caracteres.");
        }

        if (reference is { Length: > 40 })
        {
            return ReceivableService.Invalid($"lines[{i}].reference", "O documento de origem tem até 40 caracteres.");
        }

        if (balance <= 0)
        {
            return ReceivableService.Invalid($"lines[{i}].balance", "Informe um saldo maior que zero.");
        }

        return costCenterId is { } center && !await db.CostCenters.AnyAsync(c => c.Id == center && c.IsActive, ct)
            ? ReceivableService.Invalid($"lines[{i}].costCenterId", "Escolha um centro de custo ativo.")
            : null;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
