using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>Dados do módulo Pos (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>pos</c>).</summary>
public interface IPosDb
{
    DbSet<Store> Stores { get; }

    DbSet<Register> Registers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Schema do módulo.</summary>
public static class PosSchema
{
    public const string Name = "pos";
}
