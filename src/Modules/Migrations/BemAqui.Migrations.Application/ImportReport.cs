using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application;

/// <summary>Pedido da CLI (ADR 0017), já validado quanto ao tenant pelo Host.</summary>
public sealed record ImportRequest(ImportMode Mode, IReadOnlyList<string> Entities, string Operator, ImportMapping Mapping);

/// <summary>
/// Totais de uma entidade (CA 17): a soma de importados, atualizados, sem alteração, mesclados, vinculados, ignorados,
/// conflitos e erros é exatamente o total lido; avisos ficam à parte.
/// </summary>
public sealed record EntitySummary(string Entity, int Read, int Imported, int Updated, int Unchanged, int Merged, int Linked, int Skipped, int Conflicts, int Errors, int Warnings)
{
    public int Total => Imported + Updated + Unchanged + Merged + Linked + Skipped + Conflicts + Errors;

    public static EntitySummary From(string entity, int read, IEnumerable<ImportItem> items)
    {
        var list = items.ToList();
        int Count(ImportOutcome outcome) => list.Count(i => i.Outcome == outcome);
        return new EntitySummary(entity, read, Count(ImportOutcome.Imported), Count(ImportOutcome.Updated), Count(ImportOutcome.Unchanged),
            Count(ImportOutcome.Merged), Count(ImportOutcome.Linked), Count(ImportOutcome.Skipped), Count(ImportOutcome.Conflict),
            Count(ImportOutcome.Error), list.Count(i => i.Warning is not null));
    }
}

/// <summary>Relatório de uma execução: o mesmo no console, no JSON, no CSV e na tela.</summary>
public sealed record ImportReport(
    Guid RunId,
    ImportMode Mode,
    ImportRunStatus Status,
    string Operator,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? SnapshotTakenAt,
    IReadOnlyList<EntitySummary> Entities,
    string? Failure);

/// <summary>Resultado da CLI: código de saída (0 ok, 1 recusado antes de gravar, 2 falha de infraestrutura) e mensagem em PT-BR.</summary>
public sealed record ImportRunResult(int ExitCode, string Message, ImportReport? Report = null, IReadOnlyList<ImportItem>? Items = null)
{
    public static ImportRunResult Refused(string message) => new(1, message);
}
