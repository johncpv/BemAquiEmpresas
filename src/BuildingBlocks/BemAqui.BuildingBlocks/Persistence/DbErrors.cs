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
}
