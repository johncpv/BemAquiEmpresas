using BemAqui.SharedKernel;

namespace BemAqui.Migrations.Domain;

public enum ImportMode
{
    DryRun,
    Real,
}

public enum ImportRunStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>Resultado de uma linha de origem (MIG-01a CA 17). A soma por entidade fecha com o total lido.</summary>
public enum ImportOutcome
{
    Imported,
    Updated,
    Unchanged,
    Merged,
    Linked,
    Skipped,
    Conflict,
    Error,
}

/// <summary>
/// Uma execução da importação (<c>migration.import_runs</c>, ADR 0017). Guarda o modo, o operador, a data do snapshot e o
/// resumo por entidade; depois do expurgo (ADR 0018) fica só ela, sem conteúdo de origem.
/// </summary>
public sealed class ImportRun : Entity
{
    private ImportRun()
    {
    }

    public static ImportRun Start(ImportMode mode, IEnumerable<string> entities, string @operator, DateTimeOffset? snapshotTakenAt, DateTimeOffset now) => new()
    {
        Mode = mode,
        Entities = string.Join(',', entities),
        Operator = @operator,
        SnapshotTakenAt = snapshotTakenAt,
        StartedAt = now,
        Status = ImportRunStatus.Running,
    };

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public ImportMode Mode { get; private set; }

    /// <summary>Entidades pedidas, na ordem de execução, separadas por vírgula.</summary>
    public string Entities { get; private set; } = string.Empty;

    public ImportRunStatus Status { get; private set; }

    /// <summary>Resumo por entidade em JSON (<c>jsonb</c>): totais por resultado e avisos.</summary>
    public string? Summary { get; private set; }

    /// <summary>E-mail de quem rodou a CLI (<c>--operator</c>).</summary>
    public string Operator { get; private set; } = string.Empty;

    public DateTimeOffset? SnapshotTakenAt { get; private set; }

    /// <summary>Motivo da falha de infraestrutura, em PT-BR, sem credencial.</summary>
    public string? Failure { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? PurgedAt { get; private set; }

    public void Finish(string summary, string? failure, DateTimeOffset now)
    {
        Summary = summary;
        Failure = failure;
        Status = failure is null ? ImportRunStatus.Succeeded : ImportRunStatus.Failed;
        FinishedAt = now;
    }

    /// <summary>Aceite da carga pelo PO: libera o expurgo antes dos 30 dias (ADR 0018).</summary>
    public void Accept(DateTimeOffset now) => AcceptedAt ??= now;

    public void MarkPurged(DateTimeOffset now) => PurgedAt = now;
}

/// <summary>
/// Linha de origem lida numa execução (<c>migration.import_items</c>, CA 9): tabela, id, conteúdo e hash, com o resultado.
/// É a staging: expurgada depois do aceite ou em até 30 dias (ADR 0018, CA 22).
/// </summary>
public sealed class ImportItem : Entity
{
    public Guid RunId { get; init; }

    /// <summary>Entidade do BemAqui (ex.: <c>parties</c>), para agrupar o relatório.</summary>
    public string Entity { get; init; } = string.Empty;

    public string SourceTable { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    /// <summary>Conteúdo de origem (<c>jsonb</c>), já com os campos pessoais mascarados.</summary>
    public string? Payload { get; set; }

    public string Hash { get; init; } = string.Empty;

    public ImportOutcome Outcome { get; init; }

    public string? TargetType { get; init; }

    public Guid? TargetId { get; init; }

    /// <summary>Motivo do erro, do ignorado ou do conflito, em PT-BR.</summary>
    public string? Message { get; init; }

    /// <summary>Aviso que não impede a gravação (fica à parte na soma, CA 17).</summary>
    public string? Warning { get; init; }

    /// <summary>No conflito, os dois valores por campo (<c>jsonb</c>), mascarados (CA 13).</summary>
    public string? Differences { get; set; }
}

/// <summary>
/// Chave externa (<c>migration.external_refs</c>, CA 10): liga a linha de origem ao registro do BemAqui, com o hash da origem e o
/// <c>xmin</c> da linha de destino na última gravação. É o que torna a reexecução idempotente (CA 11 a 13).
/// </summary>
public sealed class ExternalRef : Entity
{
    public const string Dolibarr = "dolibarr";

    public string Source { get; init; } = Dolibarr;

    public string SourceTable { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public string SourceHash { get; set; } = string.Empty;

    /// <summary><c>xmin</c> da linha de destino logo depois da gravação da importação (versão da linha no PostgreSQL).</summary>
    public long TargetXmin { get; set; }

    public DateTimeOffset ImportedAt { get; init; }

    public Guid LastRunId { get; set; }

    /// <summary>Valor do <c>import_source</c> no destino (ex.: <c>dolibarr:llx_societe:42</c>).</summary>
    public static string ImportSource(string sourceTable, string sourceId) => Dolibarr + ":" + sourceTable + ":" + sourceId;
}
