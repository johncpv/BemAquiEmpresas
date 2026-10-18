using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application;

/// <summary>
/// Carga de uma entidade (MIG-01b e MIG-01c): lê as tabelas do snapshot e grava no destino pelo <c>I*Import</c> do módulo
/// dono, sempre por <see cref="ImportSession.ProcessAsync"/>, que cuida da idempotência, do conflito e do registro na staging.
/// </summary>
public interface IDolibarrLoader
{
    /// <summary>Nome usado em <c>--entities</c> e no relatório (ex.: <c>parties</c>).</summary>
    string Entity { get; }

    /// <summary>Ordem de execução (terceiros antes de contatos, unidades antes de itens).</summary>
    int Order { get; }

    /// <summary>Tabelas de origem que a carga lê (todas da lista fixa).</summary>
    IReadOnlyList<SnapshotTable> SourceTables { get; }

    Task LoadAsync(ImportSession session, CancellationToken cancellationToken);
}

/// <summary>Resultado da gravação de uma linha no destino, devolvido pelo <c>I*Import</c>.</summary>
/// <param name="Xmin">Versão da linha de destino (<c>xmin</c>) logo depois da gravação.</param>
public sealed record RowResult(ImportOutcome Outcome, string? TargetType = null, Guid? TargetId = null, long? Xmin = null, string? Message = null, string? Warning = null)
{
    public static RowResult Created(string targetType, Guid targetId, long xmin, string? warning = null) => new(ImportOutcome.Imported, targetType, targetId, xmin, Warning: warning);

    public static RowResult Changed(string targetType, Guid targetId, long xmin, string? warning = null) => new(ImportOutcome.Updated, targetType, targetId, xmin, Warning: warning);

    /// <summary>Ligado a um cadastro que já existia no BemAqui (ex.: mesmo CNPJ), sem criar outro.</summary>
    public static RowResult LinkedTo(string targetType, Guid targetId, long xmin, string? message = null) => new(ImportOutcome.Linked, targetType, targetId, xmin, message);

    public static RowResult MergedInto(string targetType, Guid targetId, long xmin, string? message = null) => new(ImportOutcome.Merged, targetType, targetId, xmin, message);

    public static RowResult Skip(string reason) => new(ImportOutcome.Skipped, Message: reason);

    public static RowResult Fail(string reason) => new(ImportOutcome.Error, Message: reason);
}

/// <summary>Campo que difere entre a origem e o BemAqui, no conflito (CA 13). Os valores já vêm mascarados quando pessoais.</summary>
public sealed record FieldDifference(string Field, string? Source, string? Target);

/// <summary>Gravação de uma linha no destino. O carregador implementa chamando o <c>I*Import</c> do módulo dono.</summary>
public interface IRowImporter
{
    Task<RowResult> CreateAsync(SourceRow row, CancellationToken cancellationToken);

    Task<RowResult> UpdateAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken);

    /// <summary><c>xmin</c> atual do registro de destino, ou null se ele não existe mais (excluído no BemAqui).</summary>
    Task<long?> VersionAsync(Guid targetId, CancellationToken cancellationToken);

    /// <summary>Diferenças entre a origem e o registro atual, para o relatório do conflito.</summary>
    Task<IReadOnlyList<FieldDifference>> DiffAsync(SourceRow row, Guid targetId, CancellationToken cancellationToken);
}
