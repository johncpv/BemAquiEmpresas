using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BemAqui.BuildingBlocks.Persistence;

/// <summary>Erros do PostgreSQL que viram regra de negócio (ex.: duas gravações do mesmo documento ao mesmo tempo).</summary>
public static class DbErrors
{
    /// <summary>Violação de índice único (23505); com <paramref name="constraint"/>, só a desse índice.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string? constraint = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && (constraint is null || string.Equals(pg.ConstraintName, constraint, StringComparison.Ordinal));
    }

    /// <summary>
    /// Erro do dado, e não da infraestrutura (MIG-01a CA 15): restrição (classe 23) ou valor inválido (classe 22).
    /// Conexão caída, timeout e permissão não entram aqui.
    /// </summary>
    public static bool IsDataError(Exception exception, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var pg = exception as PostgresException ?? exception.InnerException as PostgresException;
        detail = pg?.ConstraintName ?? pg?.SqlState;
        return pg?.SqlState is { Length: 5 } state && (state.StartsWith("22", StringComparison.Ordinal) || state.StartsWith("23", StringComparison.Ordinal));
    }
}
