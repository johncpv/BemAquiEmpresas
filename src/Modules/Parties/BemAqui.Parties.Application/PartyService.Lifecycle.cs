using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

public sealed partial class PartyService
{
    /// <summary>Inativar ou reativar (CA 11): o inativo some dos seletores e continua nos documentos que o usam.</summary>
    public async Task<Result<PartyDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var party = await db.Parties.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (party is null)
        {
            return NotFound();
        }

        party.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Excluir (CA 20, D10): só sem uso, e a exclusão é lógica. Com uso, 409 <c>party_in_use</c>, e a tela oferece inativar.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var party = await db.Parties.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (party is null)
        {
            return NotFound();
        }

        foreach (var usage in usages)
        {
            if (await usage.UsageAsync(id, cancellationToken) is { } use)
            {
                return Error.Conflict(InUseCode, $"O terceiro está em uso ({use}). Inative-o.");
            }
        }

        db.Parties.Remove(party);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Restaurar um cadastro excluído (CA 6): volta ativo, com o mesmo documento, se ninguém o reusou.</summary>
    public async Task<Result<PartyDetail>> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var party = await db.Parties.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted, cancellationToken);
        if (party is null)
        {
            return NotFound();
        }

        if (party.Document is { } document && await DuplicateAsync(document, id, cancellationToken) is { } duplicate)
        {
            return duplicate;
        }

        party.Restore();
        return await SaveAsync(id, party.Document, cancellationToken);
    }
}
