namespace BemAqui.SharedKernel;

/// <summary>Tipo de erro: define o status HTTP do ProblemDetails.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unavailable,

    /// <summary>Limite de uso atingido (429), ex.: envios de orçamento por dia (COM-03 CA 11).</summary>
    TooManyRequests,
    Unexpected,
}

/// <summary>
/// Erro de negócio. <see cref="Code"/> é estável (contrato com o front); <see cref="Message"/> é o texto em PT-BR.
/// <see cref="Fields"/> traz os erros por campo de uma validação.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Códigos reservados (CLAUDE.md e FND-07).</summary>
    public static class Codes
    {
        public const string Validation = "validation";
        public const string NotFound = "not_found";
        public const string Conflict = "conflict";
        public const string Forbidden = "forbidden";
        public const string ModuleNotReleased = "module_not_released";
        public const string TenantSuspended = "tenant_suspended";
        public const string TenantSchemaOutdated = "tenant_schema_outdated";
    }

    public IReadOnlyDictionary<string, string[]>? Fields { get; init; }

    /// <summary>Dados extras para o front, que vão como extensões do ProblemDetails (ex.: id do terceiro existente no 409).</summary>
    public IReadOnlyDictionary<string, object?>? Data { get; init; }

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fields = null) =>
        new(code, message, ErrorType.Validation) { Fields = fields };

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);

    public static Error TooManyRequests(string code, string message) => new(code, message, ErrorType.TooManyRequests);
}
