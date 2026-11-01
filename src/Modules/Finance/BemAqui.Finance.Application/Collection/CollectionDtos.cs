using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

/// <summary>Passo na tela (CA 1): dias, rótulo da linha do tempo e o texto literal.</summary>
public sealed record CollectionStepView(int Days, string Label, string Text);

/// <summary>
/// Seção "Régua de cobrança" de Configurações → Cobrança (FIN-09 CA 1): a régua (pré-configurada e desligada até alguém ligar), os campos
/// do texto, quantos títulos recebem lembrete no próximo dia útil e se quem abre pode editar. <see cref="Id"/> nulo: ainda não gravada.
/// </summary>
public sealed record CollectionRuleView(
    Guid? Id,
    bool IsActive,
    string SendTime,
    IReadOnlyList<CollectionStepView> Steps,
    string? ReplyTo,
    string? CompanyEmail,
    IReadOnlyList<string> Placeholders,
    int MaxSteps,
    int NextRunCount,
    DateOnly NextRunDate,
    bool CanEdit,
    uint Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CollectionStepInput(int Days, string? Text);

/// <summary>"Salvar régua" (CA 1): passos, horário "HH:mm" (07:00 a 20:00), a chave "Régua ligada", o Reply-To e a versão aberta.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CollectionRuleInput(bool IsActive, string? SendTime, IReadOnlyList<CollectionStepInput>? Steps, string? ReplyTo, uint? Version);

/// <summary>Prévia (CA 2): o texto de um passo montado com um título de exemplo.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CollectionPreviewInput(int Days, string? Text);

public sealed record CollectionPreview(string Subject, string Body, string? DigitableLine, string SampleNote);

/// <summary>Lembrete na tela (CA 8, padrão 6): situação do e-mail (Enviado, Aguardando, Ignorado, Falhou ou Adiado) e o motivo.</summary>
public sealed record CollectionNoticeView(
    Guid Id, Guid ReceivableId, string ReceivableNumber, int Step, string StepLabel, DateOnly ScheduledFor, string Status, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);

public sealed record CollectionPauseView(Guid Id, string Target, string Reason, DateOnly From, DateOnly? Until, string? CreatedByName, DateTimeOffset CreatedAt, string? ResumedByName, DateTimeOffset? ResumedAt, bool Active);

/// <summary>Próximo lembrete previsto do título, ou por que não há (régua desligada, pausada, título fora da régua).</summary>
public sealed record CollectionNext(DateOnly? Date, int? Step, string? StepLabel, string? Blocked);

/// <summary>Bloco "Lembretes" do título (CA 6 e 8).</summary>
public sealed record ReceivableCollectionView(
    bool RuleActive, CollectionNext Next, IReadOnlyList<CollectionNoticeView> Notices, CollectionPauseView? ActivePause, CollectionPauseView? PartyPause, IReadOnlyList<CollectionPauseView> Pauses, bool CanEdit);

/// <summary>Lembretes do cliente (CA 8): os do mês, os recentes e a pausa do cliente.</summary>
public sealed record PartyCollectionView(
    bool RuleActive, int SentThisMonth, IReadOnlyList<CollectionNoticeView> Notices, CollectionPauseView? ActivePause, IReadOnlyList<CollectionPauseView> Pauses, bool CanEdit);

/// <summary>"Pausar cobrança" (CA 6): motivo obrigatório (até 200 caracteres) e o último dia opcional.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CollectionPauseInput(string? Reason, DateOnly? Until);

/// <summary>Textos da situação do lembrete na tela.</summary>
public static class CollectionStatusText
{
    public const string Sent = "Sent";
    public const string Queued = "Queued";
    public const string Deferred = "Deferred";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";

    /// <summary>A situação do e-mail manda (enviado, falhou, ignorado pela guarda); sem e-mail, a do lembrete.</summary>
    public static (string Status, string? Reason) Of(CollectionNotice notice, EmailLogStatus? email)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.Status == CollectionNoticeStatus.Skipped)
        {
            return (Skipped, notice.SkipReason);
        }

        if (notice.Status == CollectionNoticeStatus.Deferred)
        {
            return (Deferred, notice.SkipReason);
        }

        return email?.Status switch
        {
            EmailStatus.Sent => (Sent, null),
            EmailStatus.Failed => (Failed, email.Error),
            EmailStatus.Skipped => (Skipped, email.Error),
            _ => (Queued, null),
        };
    }
}
