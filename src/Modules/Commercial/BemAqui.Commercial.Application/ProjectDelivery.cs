using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Entrega prevista do projeto no Projeto 360 e na lista (PLN-01b CA 8 e 9): a data de <c>commercial.project_forecasts</c>, o fim da
/// fabricação, o motivo (código) e a OS quando está vazia, a folga em dias corridos (prometido − previsto), o risco e o atraso pela função
/// única, quando foi calculada e se o risco já é só da instalação (projeto <c>Fabricated</c> ou <c>Installing</c>, CA 6).
/// </summary>
public sealed record ProjectForecastView(
    DateOnly? ForecastDate, DateOnly? FabricationEnd, string? Reason, Guid? ReasonOrderId, string? ReasonOrderNumber, int? SlackDays, DeliveryRisk Risk, int? DelayDays,
    DateTimeOffset? ComputedAt, bool InstallationOnly);

/// <summary>
/// Leitura da previsão do projeto com a função única de risco (<see cref="DeliveryRiskRules"/>, PLN-01b CA 9). A partir de <c>Fabricated</c>
/// a previsão gravada fica como estava e o risco passa a ser da instalação: vale o maior fim digitado das OS de instalação não canceladas,
/// quando há (CA 6). Projeto entregue, encerrado ou cancelado não tem prazo.
/// </summary>
public static class ProjectDelivery
{
    public static bool Finished(ProjectStatus status) => status is ProjectStatus.Delivered or ProjectStatus.Closed or ProjectStatus.Cancelled;

    public static bool InstallationPhase(ProjectStatus status) => status is ProjectStatus.Fabricated or ProjectStatus.Installing;

    public static ProjectForecastView Assess(
        ProjectStatus status, DateOnly? promised, ProjectForecast? row, DateOnly? installationEnd, DateOnly today, string? reasonOrderNumber = null)
    {
        var installationOnly = InstallationPhase(status) && installationEnd is not null;
        var forecast = installationOnly ? installationEnd : row?.ForecastDate;
        var finished = Finished(status);
        var assessment = DeliveryRiskRules.Evaluate(promised, forecast, today, finished);
        return new ProjectForecastView(
            forecast, row?.FabricationEnd, forecast is null ? row?.Reason : null, forecast is null ? row?.ReasonOrderId : null, forecast is null ? reasonOrderNumber : null,
            finished ? null : DeliveryRiskRules.SlackDays(promised, forecast), assessment.Risk, assessment.DelayDays, row?.ComputedAt, installationOnly);
    }
}
