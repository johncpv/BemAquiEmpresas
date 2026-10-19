using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Locais de estoque (CAD-03 CA 2 a 7). Código único, um só padrão (trocado na mesma transação), inativar e excluir conforme
/// o uso informado pelos <see cref="ILocationUsageProvider"/> (D10).
/// </summary>
public sealed partial class LocationService(IInventoryDb db, IEnumerable<ILocationUsageProvider> usages, TimeProvider clock)
{
    public const string CodeIndex = "ix_locations_code";
    public const string DefaultIndex = "ix_locations_default";

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,19}$")]
    private static partial Regex CodePattern();

    public async Task<PagedList<LocationDetail>> ListAsync(LocationListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var locations = db.Locations.AsNoTracking().Where(l => query.IncludeInactive || l.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = "%" + query.Q.Trim() + "%";
            locations = locations.Where(l => EF.Functions.ILike(l.Code, term) || EF.Functions.ILike(l.Name, term));
        }

        var result = await locations.OrderByDescending(l => l.IsDefault).ThenBy(l => l.Code).ToPagedListAsync(page, cancellationToken);
        return new PagedList<LocationDetail>(result.Items.Select(LocationsContract.ToDetail).ToList(), result.Page, result.PageSize, result.Total);
    }

    public async Task<Result<LocationDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return location is null ? NotFound() : LocationsContract.ToDetail(location);
    }

    public async Task<Result<LocationDetail>> CreateAsync(LocationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        var location = new Location { CreatedAt = clock.GetUtcNow() };
        Apply(location, input);
        // O primeiro local ativo já nasce padrão.
        var makeDefault = input.IsDefault || !await db.Locations.AnyAsync(l => l.IsActive && l.IsDefault, cancellationToken);
        return await SaveAsync(location, makeDefault, cancellationToken);
    }

    public async Task<Result<LocationDetail>> UpdateAsync(Guid id, LocationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return NotFound();
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        Apply(location, input);
        return await SaveAsync(location, input.IsDefault && !location.IsDefault, cancellationToken);
    }

    /// <summary>Inativar ou reativar (CA 5): o padrão e o local com saldo não são inativados.</summary>
    public async Task<Result<LocationDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return NotFound();
        }

        if (!active && location.IsDefault)
        {
            return DefaultLocked();
        }

        if (!active && await HasBalanceAsync(id, cancellationToken))
        {
            return Error.Conflict("location_has_balance", "O local tem saldo. Transfira o saldo antes.");
        }

        location.IsActive = active;
        return await SaveAsync(location, false, cancellationToken);
    }

    /// <summary>Exclusão lógica só sem uso nem saldo (CA 6); o padrão não é excluído.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return NotFound();
        }

        if (location.IsDefault)
        {
            return DefaultLocked();
        }

        if (await HasBalanceAsync(id, cancellationToken) || await UsageAsync(id, cancellationToken) is not null)
        {
            return Error.Conflict("location_in_use", "O local está em uso. Inative-o.");
        }

        db.Locations.Remove(location);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Error? Validate(LocationInput input)
    {
        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code))
        {
            return Invalid("code", "Informe o código com até 20 letras, números ou hífen, sem espaço nem acento.");
        }

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            return Invalid("name", "Informe o nome do local, com até 100 caracteres.");
        }

        return null;
    }

    private static void Apply(Location location, LocationInput input)
    {
        location.Code = input.Code.Trim().ToUpperInvariant();
        location.Name = input.Name.Trim();
    }

    /// <summary>
    /// Grava; com <paramref name="makeDefault"/>, desmarca o padrão anterior e marca este na mesma transação, em dois comandos,
    /// porque o índice único parcial é checado a cada um (CA 4).
    /// </summary>
    private async Task<Result<LocationDetail>> SaveAsync(Location location, bool makeDefault, CancellationToken cancellationToken)
    {
        if (makeDefault && !location.IsActive)
        {
            return Error.Conflict("location_inactive", "Reative o local antes de torná-lo padrão.");
        }

        if (await db.Locations.AnyAsync(l => l.Code == location.Code && l.Id != location.Id, cancellationToken))
        {
            return Duplicate(location.Code);
        }

        if (db.Entry(location).State == EntityState.Detached)
        {
            db.Locations.Add(location);
        }

        try
        {
            await using var transaction = makeDefault && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            if (makeDefault)
            {
                foreach (var previous in await db.Locations.Where(l => l.IsDefault && l.Id != location.Id).ToListAsync(cancellationToken))
                {
                    previous.IsDefault = false;
                }

                await db.SaveChangesAsync(cancellationToken);
                location.IsDefault = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CodeIndex))
        {
            return Duplicate(location.Code);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DefaultIndex))
        {
            return Error.Conflict("location_default_race", "Outro local acabou de virar padrão. Atualize a tela e tente de novo.");
        }

        return LocationsContract.ToDetail(location);
    }

    private async Task<bool> HasBalanceAsync(Guid id, CancellationToken cancellationToken)
    {
        foreach (var usage in usages)
        {
            if (await usage.HasBalanceAsync(id, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string?> UsageAsync(Guid id, CancellationToken cancellationToken)
    {
        foreach (var usage in usages)
        {
            if (await usage.UsageAsync(id, cancellationToken) is { } where)
            {
                return where;
            }
        }

        return null;
    }

    private static Error Duplicate(string code) => Error.Conflict("location_code_taken", $"Já existe o local {code}.");

    private static Error DefaultLocked() => Error.Conflict("location_is_default", "Escolha outro local padrão antes.");

    private static Error NotFound() => Error.NotFound("location_not_found", "Local não encontrado.");

    private static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
