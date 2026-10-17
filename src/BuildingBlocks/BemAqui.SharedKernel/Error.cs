namespace BemAqui.SharedKernel;

/// <summary>Tipo de erro, usado para escolher o status HTTP do ProblemDetails.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unexpected,
}

/// <summary>
/// Erro de negócio. <see cref="Code"/> é estável (contrato com o front);
/// <see cref="Message"/> é o texto em PT-BR mostrado ao usuário.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
