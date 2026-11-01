using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>
/// Prévia da anonimização (LGPD-01b CA 2 e 3): um bloco por módulo, com o que será anonimizado, o que fica retido (até quando e por quê) e os
/// documentos mantidos; o texto a digitar para confirmar (o nome do titular), a eliminação atendida por anonimização e o aviso dos backups.
/// <c>Blockers</c>: o que impede anonimizar agora (ex.: último membro do Proprietário).
/// </summary>
public sealed record PrivacyAnonymizationPreview(
    Guid RequestId,
    string RequestNumber,
    string ConfirmationText,
    string Marker,
    IReadOnlyList<PersonalDataAnonymizationPlan> Modules,
    IReadOnlyList<string> Blockers,
    string? DeletionNotice,
    string BackupNotice,
    uint Version);

/// <summary>"Anonimizar" e "Tentar de novo" (CA 4 e 5): o nome do titular digitado, o código do 2FA e a versão do pedido.</summary>
public sealed record AnonymizePrivacySubjectInput(string? Confirmation, string? TwoFactorCode, uint Version);

/// <summary>Uma etapa (um módulo) da anonimização, gravada no pedido (CA 4 e 5): sem dado pessoal.</summary>
public sealed record PrivacyAnonymizationStep(
    string Module,
    string Title,
    PrivacyAnonymizationStepStatus Status,
    DateTimeOffset? At,
    string? Error,
    IReadOnlyList<PersonalDataPlanItem> Items);

/// <summary>Resumo da anonimização (CA 4): quantos registros foram anonimizados, retidos e mantidos e até quando vai a retenção mais longa.</summary>
public sealed record PrivacyAnonymizationSummary(int Anonymized, int Retained, int Kept, DateOnly? RetainedUntil, bool RetainedWithoutDate);

/// <summary>A anonimização foi para a fila (202).</summary>
public sealed record PrivacyAnonymizationStarted(PrivacyRequestDetail Request, string Message);

/// <summary>Linha da tabela de retenção (CA 1): o grupo, o rótulo, a regra e a conferência.</summary>
public sealed record RetentionRuleRow(
    Guid Id,
    PersonalDataSubjectKind SubjectType,
    string FieldGroup,
    string Label,
    RetentionRuleKind Rule,
    int Years,
    RetentionBase BaseDate,
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    string? ConfirmedByName,
    uint Version);

public sealed record SaveRetentionRuleInput(RetentionRuleKind Rule, int Years, uint Version);

public sealed record ConfirmRetentionRuleInput(uint Version);

/// <summary>Leitura e gravação das colunas jsonb da anonimização no pedido.</summary>
public static class PrivacyAnonymizationJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static List<PrivacyAnonymizationStep> Steps(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : System.Text.Json.JsonSerializer.Deserialize<List<PrivacyAnonymizationStep>>(json, Options) ?? [];

    public static string Write(IReadOnlyList<PrivacyAnonymizationStep> steps) => System.Text.Json.JsonSerializer.Serialize(steps, Options);

    public static PrivacyAnonymizationSummary? Summary(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : System.Text.Json.JsonSerializer.Deserialize<PrivacyAnonymizationSummary>(json, Options);

    public static string Write(PrivacyAnonymizationSummary summary) => System.Text.Json.JsonSerializer.Serialize(summary, Options);

    /// <summary>Resumo pelos itens das etapas: anonimizados, retidos (com a maior data) e mantidos.</summary>
    public static PrivacyAnonymizationSummary Summarize(IEnumerable<PrivacyAnonymizationStep> steps)
    {
        var items = steps.SelectMany(s => s.Items).ToList();
        var retained = items.Where(i => i.Action == AnonymizationAction.Retain).ToList();
        return new PrivacyAnonymizationSummary(
            items.Where(i => i.Action == AnonymizationAction.Anonymize).Sum(i => i.Count),
            retained.Sum(i => i.Count),
            items.Where(i => i.Action == AnonymizationAction.Keep).Sum(i => i.Count),
            retained.Where(i => i.RetainedUntil is not null).Select(i => i.RetainedUntil).Max(),
            retained.Any(i => i.RetainedUntil is null));
    }
}
