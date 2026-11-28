using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>Tipo do projeto (EXE-01a): fabricação (P E F I T C) ou serviço (P I E A C). Decidido na aprovação; nunca trocado à mão.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExecutionKind>))]
public enum ExecutionKind
{
    Fabrication,
    Service,
}

/// <summary>De onde veio o tipo (EXE-01a CA 1): padrão da migration, aprovação do orçamento ou o ajuste automático (backfill).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExecutionKindSource>))]
public enum ExecutionKindSource
{
    Default,
    Approval,
    Backfill,
}

/// <summary>Como o aceite nasceu (EXE-01a): registrado à mão, junto com a entrega ("também aceitou") ou herdado da entrega no ajuste automático.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AcceptanceKind>))]
public enum AcceptanceKind
{
    Manual,
    Inherited,
    FromDelivery,
}

/// <summary>
/// Aceite do cliente no projeto de serviço (<c>commercial.project_acceptances</c>, EXE-01a CA 13 a 17): registro, não estado. "Aceito" = existe
/// aceite vigente (sem <see cref="RevertedAt"/>). A versão sobe a cada novo aceite; o desfeito fica guardado. Nome e papel de quem aceitou são dado
/// pessoal (LGPD art. 7º V: execução do contrato), mascarados na auditoria e fora de eventos, logs, PDF e exportações.
/// </summary>
[DocumentName("O aceite do cliente")]
public sealed class ProjectAcceptance : Entity, IAuditable
{
    public const int NameMaxLength = 120;
    public const int RoleMaxLength = 80;
    public const int ReasonMaxLength = 200;

    public Guid ProjectId { get; init; }

    public int Version { get; init; }

    public AcceptanceKind Kind { get; init; }

    /// <summary>Data do fato (o cliente aceitou), separada de <see cref="RecordedAt"/>.</summary>
    public DateOnly OccurredOn { get; init; }

    public DateTimeOffset RecordedAt { get; init; }

    public Guid? RecordedBy { get; init; }

    [PersonalData]
    public string? AcceptedByName { get; set; }

    [PersonalData]
    public string? AcceptedByRole { get; set; }

    public Guid? AttachmentId { get; init; }

    public string? BackdateReason { get; init; }

    public DateTimeOffset? RevertedAt { get; set; }

    public Guid? RevertedBy { get; set; }

    public string? RevertReason { get; set; }

    public bool IsActive => RevertedAt is null;
}

/// <summary>Regra única do tipo do projeto (EXE-01a CA 2): a mesma função serve à aprovação e ao ajuste automático.</summary>
public static class ExecutionKindRule
{
    /// <summary>Serviço se e só se o tenant tem o recurso <c>projects.service_execution</c>, há ao menos um item e todos os itens são serviço.</summary>
    public static ExecutionKind For(bool tenantHasServiceExecution, IReadOnlyCollection<QuoteItemKind> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return tenantHasServiceExecution && items.Count > 0 && items.All(k => k == QuoteItemKind.Service) ? ExecutionKind.Service : ExecutionKind.Fabrication;
    }
}

/// <summary>Etapa do projeto para o display de andar (EXE-01a CA 29): letras, índice da atual (-1 = cancelado) e o nome; "A" dispensado no serviço sem aceite.</summary>
public sealed record ProjectStage(IReadOnlyList<string> Letters, int Index, string Label, bool AcceptanceWaived = false)
{
    public string? Letter => Index >= 0 && Index < Letters.Count ? Letters[Index] : null;
}

/// <summary>Função única das etapas por tipo (EXE-01a CA 29): fabricação P E F I T C; serviço P I E A C (Planejado, Em execução, Entregue, Aceito, Encerrado).</summary>
public static class ProjectStageMap
{
    public static readonly IReadOnlyList<string> FabricationLetters = ["P", "E", "F", "I", "T", "C"];

    public static readonly IReadOnlyList<string> ServiceLetters = ["P", "I", "E", "A", "C"];

    private static readonly ProjectStatus[] FabricationOrder =
        [ProjectStatus.Planned, ProjectStatus.InProgress, ProjectStatus.Fabricated, ProjectStatus.Installing, ProjectStatus.Delivered, ProjectStatus.Closed];

    public static ProjectStage For(ExecutionKind kind, ProjectStatus status, bool accepted = false, bool acceptanceRequired = false)
    {
        if (kind == ExecutionKind.Fabrication)
        {
            return new ProjectStage(FabricationLetters, Array.IndexOf(FabricationOrder, status), FabricationLabel(status));
        }

        var waived = !acceptanceRequired && !accepted;
        var (index, label) = status switch
        {
            ProjectStatus.Planned => (0, "Planejado"),
            ProjectStatus.InProgress or ProjectStatus.Fabricated or ProjectStatus.Installing => (1, "Em execução"),
            ProjectStatus.Delivered when accepted => (3, "Aceito"),
            ProjectStatus.Delivered => (2, "Entregue"),
            ProjectStatus.Closed => (4, "Encerrado"),
            _ => (-1, "Cancelado"),
        };
        return new ProjectStage(ServiceLetters, index, label, waived);
    }

    public static string FabricationLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.Planned => "Planejado",
        ProjectStatus.InProgress => "Em produção",
        ProjectStatus.Fabricated => "Fabricado",
        ProjectStatus.Installing => "Instalando",
        ProjectStatus.Delivered => "Entregue",
        ProjectStatus.Closed => "Encerrado",
        _ => "Cancelado",
    };
}

/// <summary>Ações do projeto de serviço (EXE-01a, tabela de transições).</summary>
public enum ServiceAction
{
    Start,
    Deliver,
    Accept,
    RevertAcceptance,
}

/// <summary>Tabela de transições do projeto <c>Service</c> (EXE-01a): fora dela, 409 <c>invalid_project_transition</c>.</summary>
public static class ServiceTransitions
{
    public static bool Allows(ProjectStatus from, ServiceAction action, bool accepted) => action switch
    {
        ServiceAction.Start => from == ProjectStatus.Planned,
        ServiceAction.Deliver => from is ProjectStatus.Planned or ProjectStatus.InProgress,
        ServiceAction.Accept => from == ProjectStatus.Delivered && !accepted,
        ServiceAction.RevertAcceptance => from == ProjectStatus.Delivered && accepted,
        _ => false,
    };

    public static string StatusText(ProjectStatus status, bool accepted) => status switch
    {
        ProjectStatus.Planned => "planejado",
        ProjectStatus.InProgress or ProjectStatus.Fabricated or ProjectStatus.Installing => "em execução",
        ProjectStatus.Delivered => accepted ? "aceito" : "entregue",
        ProjectStatus.Closed => "encerrado",
        _ => "cancelado",
    };

    public static string ActionText(ServiceAction action) => action switch
    {
        ServiceAction.Start => "iniciar a execução",
        ServiceAction.Deliver => "registrar a entrega",
        ServiceAction.Accept => "registrar o aceite do cliente",
        _ => "desfazer o aceite",
    };

    /// <summary>"Este projeto está {situação} e não pode {ação}."</summary>
    public static string Refusal(ProjectStatus status, bool accepted, ServiceAction action) =>
        $"Este projeto está {StatusText(status, accepted)} e não pode {ActionText(action)}.";
}

/// <summary>Onde o marco é validado (EXE-01a CA 27 e 28).</summary>
public enum MilestoneValidationMode
{
    Quote,
    Approval,
    Amendment,

    /// <summary>Marcos antigos do contrato: continuam válidos e são liberados pela entrega (CA 12).</summary>
    Legacy,
}

/// <summary>Marco recusado pelo tipo: índice (base 0) e descrição.</summary>
public sealed record MilestoneViolation(int Index, string Description)
{
    public string Field => $"milestones[{Index}].triggerEvent";

    public string Message => $"Marco {Index + 1} ({Description}): projeto de serviço não tem fabricação. Troque o gatilho para entrega, aceite do cliente ou data.";
}

/// <summary>Validador único de marco por tipo (EXE-01a CA 27 e 28): orçamento, aprovação e aditivo; o legado passa.</summary>
public static class MilestoneTriggerPolicy
{
    public static MilestoneViolation? Validate(ExecutionKind kind, IReadOnlyList<(TriggerEvent? Event, string Description)> milestones, MilestoneValidationMode mode)
    {
        ArgumentNullException.ThrowIfNull(milestones);
        if (kind != ExecutionKind.Service || mode == MilestoneValidationMode.Legacy)
        {
            return null;
        }

        for (var i = 0; i < milestones.Count; i++)
        {
            if (milestones[i].Event == TriggerEvent.FabricationDone)
            {
                return new MilestoneViolation(i, milestones[i].Description);
            }
        }

        return null;
    }
}

/// <summary>Conclusão do contrato (EXE-01a CA 20): o mesmo método serve à conclusão, ao desfazer e à EXE-01b.</summary>
public static class CompletionRules
{
    /// <summary>Aceite exigido = projeto <c>Service</c> e contrato com algum marco "aceite do cliente".</summary>
    public static bool RequiresAcceptance(ExecutionKind kind, IEnumerable<TriggerEvent?> milestones)
    {
        ArgumentNullException.ThrowIfNull(milestones);
        return kind == ExecutionKind.Service && milestones.Any(m => m == TriggerEvent.TechnicalAcceptance);
    }
}
