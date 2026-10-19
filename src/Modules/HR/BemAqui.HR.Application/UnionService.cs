using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Sindicatos (CAD-04a CA 17): CNPJ validado e mês da data-base; em uso por funcionário (CAD-04b), só inativa.</summary>
public sealed class UnionService(IHrDb db, IEnumerable<IUnionUsageProvider> usages, TimeProvider clock)
{
    public async Task<IReadOnlyList<UnionDetail>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.LaborUnions.AsNoTracking().OrderBy(u => u.Name).ToListAsync(cancellationToken)).Select(ToDetail).ToList();

    public async Task<Result<UnionDetail>> CreateAsync(UnionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var union = new LaborUnion { CreatedAt = clock.GetUtcNow() };
        if (await ApplyAsync(union, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.LaborUnions.Add(union);
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(union);
    }

    public async Task<Result<UnionDetail>> UpdateAsync(Guid id, UnionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var union = await db.LaborUnions.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (union is null)
        {
            return NotFound();
        }

        if (await ApplyAsync(union, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(union);
    }

    public async Task<Result<UnionDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var union = await db.LaborUnions.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (union is null)
        {
            return NotFound();
        }

        union.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(union);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var union = await db.LaborUnions.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (union is null)
        {
            return NotFound();
        }

        foreach (var usage in usages)
        {
            if (await usage.InUseAsync(id, cancellationToken))
            {
                return Error.Conflict("union_in_use", "O sindicato está em uso por funcionários. Inative-o.");
            }
        }

        db.LaborUnions.Remove(union);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ApplyAsync(LaborUnion union, UnionInput input, CancellationToken ct)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 150)
        {
            return PositionService.Invalid("name", "Informe o nome do sindicato, com até 150 caracteres.");
        }

        var document = BrazilianDocuments.Normalize(input.Document);
        if (document.Length > 0 && !BrazilianDocuments.IsValidCnpj(document))
        {
            return PositionService.Invalid("document", "CNPJ inválido. Confira os dígitos.");
        }

        if (input.BaseMonth is < 1 or > 12)
        {
            return PositionService.Invalid("baseMonth", "Escolha o mês da data-base.");
        }

        if (await db.LaborUnions.AnyAsync(u => u.Id != union.Id && u.Name == name, ct))
        {
            return Error.Conflict("union_name_taken", "Já existe o sindicato " + name + ".");
        }

        union.Name = name;
        union.Document = document.Length == 0 ? null : document;
        union.BaseMonth = input.BaseMonth;
        return null;
    }

    public static UnionDetail ToDetail(LaborUnion u) => new(u.Id, u.Name, u.Document, u.BaseMonth, u.IsActive);

    private static Error NotFound() => Error.NotFound("union_not_found", "Sindicato não encontrado.");
}
