using System.Reflection;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Conflito de concorrência (ADR 0019, FND-09e CA 13): <see cref="DbUpdateConcurrencyException"/> vira 409 ProblemDetails
/// <c>concurrency_conflict</c>, com o nome do documento da entidade em conflito.
/// </summary>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public const string Code = "concurrency_conflict";

    public static Error ToError(DbUpdateConcurrencyException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var type = exception.Entries.Count > 0 ? exception.Entries[0].Metadata.ClrType : null;
        var name = type?.GetCustomAttribute<DocumentNameAttribute>()?.Name ?? "O registro";
        return Error.Conflict(Code, name + " mudou desde que você abriu. Recarregue.");
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not DbUpdateConcurrencyException concurrency)
        {
            return false;
        }

        await ToError(concurrency).ToProblem().ExecuteAsync(httpContext);
        return true;
    }
}
