namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>Linha de <c>audit.audit_log</c>: imutável (o banco recusa UPDATE, DELETE e TRUNCATE).</summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Entity { get; init; }

    public required string EntityId { get; init; }

    public required string Action { get; init; }

    public Guid? UserId { get; init; }

    public DateTimeOffset At { get; init; }

    /// <summary>JSONB: <c>{ "campo": { "old": ..., "new": ... } }</c>, só dos campos alterados.</summary>
    public required string Changes { get; init; }

    public string? Ip { get; init; }

    /// <summary>Origem da alteração feita por processo, sem usuário logado (ex.: "Importação Dolibarr · operador · execução").</summary>
    public string? Origin { get; init; }
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    /// <summary>Leitura de dado sensível pela rota <c>/sensitive</c> (ADR 0016): quem e quando, sem os valores.</summary>
    public const string SensitiveRead = "SensitiveRead";

    /// <summary>Exportação de dados (EST-04 CA 4): quem, quando, o quê e quantas linhas, sem o conteúdo exportado.</summary>
    public const string Exported = "Exported";

    /// <summary>Importação de arquivo (RH-02a CA 9): quem, quando, onde, o formato e quantas linhas, sem o arquivo nem os valores.</summary>
    public const string Imported = "Imported";

    /// <summary>Anonimização do titular (LGPD-01b CA 6): quais campos foram trocados pelo marcador, sem o valor anterior nem o novo.</summary>
    public const string Anonymized = "Anonymized";

    /// <summary>Prévia da anonimização (LGPD-01b CA 11): quem pediu, quando e o resumo por módulo, sem dado pessoal.</summary>
    public const string AnonymizationPreviewed = "AnonymizationPreviewed";
}
