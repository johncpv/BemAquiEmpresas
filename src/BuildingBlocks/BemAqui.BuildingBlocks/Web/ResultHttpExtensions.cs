using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>Converte <see cref="Result"/> em resposta HTTP; falha vira ProblemDetails (RFC 9457) com <c>code</c> e <c>errors</c>.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result, Func<IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? onSuccess?.Invoke() ?? Results.NoContent() : result.Error!.ToProblem();
    }

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value) : result.Error!.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Fields is { Count: > 0 })
        {
            extensions["errors"] = error.Fields;
        }

        return Results.Problem(
            detail: error.Message,
            statusCode: StatusCode(error.Type),
            title: Title(error.Type),
            extensions: extensions);
    }

    public static int StatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string Title(ErrorType type) => type switch
    {
        ErrorType.Validation => "Dados inválidos",
        ErrorType.NotFound => "Não encontrado",
        ErrorType.Conflict => "Conflito",
        ErrorType.Forbidden => "Acesso negado",
        ErrorType.Unavailable => "Indisponível no momento",
        _ => "Erro inesperado",
    };
}
