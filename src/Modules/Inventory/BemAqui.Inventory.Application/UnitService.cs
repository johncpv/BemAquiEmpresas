using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Unidades de medida (CAD-02a CA 1 e 2): código único em maiúsculas; em uso por item não inativa nem exclui.</summary>
public sealed partial class UnitService(IInventoryDb db, TimeProvider clock)
{
    public const string CodeIndex = "ix_units_code";

    [GeneratedRegex("^[A-Z0-9]{1,6}$")]
    private static partial Regex CodePattern();

    public async Task<IReadOnlyList<UnitDetail>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        await db.Units.AsNoTracking().Where(u => includeInactive || u.IsActive).OrderBy(u => u.Code)
            .Select(u => new UnitDetail(u.Id, u.Code, u.Name, u.IsActive, u.SeedKey, db.Items.Count(i => i.UnitId == u.Id))).ToListAsync(cancellationToken);

    public async Task<Result<UnitDetail>> CreateAsync(UnitInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        var unit = new MeasureUnit { CreatedAt = clock.GetUtcNow() };
        Apply(unit, input);
        db.Units.Add(unit);
        return await SaveAsync(unit, cancellationToken);
    }

    public async Task<Result<UnitDetail>> UpdateAsync(Guid id, UnitInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (unit is null)
        {
            return NotFound();
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        Apply(unit, input);
        return await SaveAsync(unit, cancellationToken);
    }

    public async Task<Result<UnitDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (unit is null)
        {
            return NotFound();
        }

        if (!active && await InUseAsync(id, cancellationToken) is { } inUse)
        {
            return inUse;
        }

        unit.IsActive = active;
        return await SaveAsync(unit, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (unit is null)
        {
            return NotFound();
        }

        if (await InUseAsync(id, cancellationToken) is { } inUse)
        {
            return inUse;
        }

        db.Units.Remove(unit);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> InUseAsync(Guid id, CancellationToken cancellationToken)
    {
        // Conta também os itens excluídos: documento antigo ainda mostra a unidade.
        var count = await db.Items.IgnoreQueryFilters().CountAsync(i => i.UnitId == id, cancellationToken);
        return count == 0 ? null : Error.Conflict("unit_in_use", count == 1 ? "A unidade está em uso por 1 item." : $"A unidade está em uso por {count} itens.");
    }

    private static Error? Validate(UnitInput input)
    {
        if (!CodePattern().IsMatch((input.Code ?? string.Empty).Trim().ToUpperInvariant()))
        {
            return ItemRules.Invalid("code", "O código tem de 1 a 6 letras ou números, sem espaço nem acento (ex.: KG).");
        }

        return string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 60 ? ItemRules.Invalid("name", "Informe o nome da unidade, com até 60 caracteres.") : null;
    }

    private static void Apply(MeasureUnit unit, UnitInput input)
    {
        unit.Code = input.Code.Trim().ToUpperInvariant();
        unit.Name = input.Name.Trim();
    }

    private async Task<Result<UnitDetail>> SaveAsync(MeasureUnit unit, CancellationToken cancellationToken)
    {
        if (await db.Units.AnyAsync(u => u.Code == unit.Code && u.Id != unit.Id, cancellationToken))
        {
            return Duplicate(unit.Code);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CodeIndex))
        {
            return Duplicate(unit.Code);
        }

        return new UnitDetail(unit.Id, unit.Code, unit.Name, unit.IsActive, unit.SeedKey, await db.Items.CountAsync(i => i.UnitId == unit.Id, cancellationToken));
    }

    private static Error Duplicate(string code) => Error.Conflict("unit_code_taken", $"Já existe a unidade {code}.");

    private static Error NotFound() => Error.NotFound("unit_not_found", "Unidade não encontrada.");
}
