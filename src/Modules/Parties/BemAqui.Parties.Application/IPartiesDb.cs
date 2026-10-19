using BemAqui.Parties.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Parties.Application;

/// <summary>Dados do módulo Parties (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IPartiesDb
{
    DbSet<Party> Parties { get; }

    DbSet<PartyRole> PartyRoles { get; }

    DbSet<PartyAddress> PartyAddresses { get; }

    DbSet<PartyContact> PartyContacts { get; }

    DbSet<CnpjLookupCache> CnpjLookups { get; }

    /// <summary>Para trocar o endereço padrão em dois passos numa transação (o índice único parcial é checado a cada comando).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
