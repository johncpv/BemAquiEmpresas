using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Previsão do projeto vinda do plano (PLN-01b CA 7 e 10, ADR 0029 D10): grava em <c>commercial.project_forecasts</c> só se o
/// <c>ComputedAt</c> for mais novo que o gravado (o outbox não garante ordem); a linha de <c>commercial.projects</c> não é tocada (versão,
/// 409 e auditoria do projeto intactos). Idempotente pela inbox e pelo <c>ComputedAt</c>. Ao <b>entrar em risco</b> (antes não estava) ou
/// <b>piorar</b> a data prevista, avisa o responsável do projeto e quem tem <c>production.approve</c>, dentro do consumidor (ADR 0019 §18),
/// uma vez por projeto e data (<c>dedup_key</c>); sair do risco não avisa.
/// </summary>
public sealed class ProjectForecastConsumer(ICommercialDb db, IUserDirectory users, INotificationService notifications, TimeProvider clock)
    : IIntegrationEventConsumer<ProjectForecastChanged>
{
    public const string NotificationType = "project-at-risk";

    public const string ApproverPermission = "production.approve";

    public async Task HandleAsync(ProjectForecastChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == integrationEvent.ProjectId)
            .Select(p => new { p.Id, p.Number, p.Name, p.PromisedDate, p.ManagerUserId, p.Status }).FirstOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return;
        }

        var row = await db.ProjectForecasts.FirstOrDefaultAsync(f => f.ProjectId == integrationEvent.ProjectId, cancellationToken);
        if (row is not null && row.ComputedAt >= integrationEvent.ComputedAt)
        {
            return;
        }

        var previous = row?.ForecastDate;
        if (row is null)
        {
            row = new ProjectForecast { ProjectId = integrationEvent.ProjectId };
            db.ProjectForecasts.Add(row);
        }

        row.ForecastDate = integrationEvent.ForecastDate;
        row.FabricationEnd = integrationEvent.FabricationEnd;
        row.Reason = integrationEvent.Reason is { Length: > 0 and <= 32 } reason ? reason : null;
        row.ReasonOrderId = integrationEvent.ReasonOrderId;
        row.ComputedAt = integrationEvent.ComputedAt;
        row.UpdatedAt = clock.GetUtcNow();

        if (!ProjectDelivery.Finished(project.Status) && ShouldWarn(project.PromisedDate, previous, integrationEvent.ForecastDate))
        {
            var forecast = integrationEvent.ForecastDate!.Value;
            var promised = project.PromisedDate!.Value;
            var title = Title(string.IsNullOrWhiteSpace(project.Name) ? project.Number : project.Name, forecast, promised);
            var recipients = (await users.UsersWithPermissionAsync(ApproverPermission, cancellationToken)).ToHashSet();
            if (project.ManagerUserId is { } manager)
            {
                recipients.Add(manager);
            }

            foreach (var user in recipients.Order())
            {
                await notifications.NotifyOnceAsync(user, NotificationType, title, Link(project.Id), DedupKey(project.Id, forecast), cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Avisa quando a nova previsão passa da prometida (sem tolerância) e antes não passava, ou quando piorou (CA 10); sem previsão ou saindo
    /// do risco, não.
    /// </summary>
    public static bool ShouldWarn(DateOnly? promised, DateOnly? previous, DateOnly? next) =>
        promised is { } p && next is { } n && n > p && (previous is not { } before || before <= p || n > before);

    /// <summary>"{projeto} vai atrasar {n} dias: previsão {data}, prometido ao cliente {data}." (protótipo do Painel).</summary>
    public static string Title(string project, DateOnly forecast, DateOnly promised)
    {
        var days = forecast.DayNumber - promised.DayNumber;
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        return string.Create(culture, $"{project} vai atrasar {days} {(days == 1 ? "dia" : "dias")}: previsão {forecast:dd/MM/yyyy}, prometido ao cliente {promised:dd/MM/yyyy}.");
    }

    /// <summary>
    /// O aviso abre o Cronograma do projeto no Projeto 360 (PLN-03), com a etapa que define a entrega; sem <c>production.view</c>, a aba não
    /// existe e o Projeto 360 abre no Resumo, com a entrega prevista e a folga.
    /// </summary>
    public static string Link(Guid projectId) => "/app/projetos/" + projectId + "?aba=cronograma";

    public static string DedupKey(Guid projectId, DateOnly forecast) => string.Create(CultureInfo.InvariantCulture, $"project-at-risk:{projectId}:{forecast:yyyy-MM-dd}");
}
