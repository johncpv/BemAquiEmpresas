namespace BemAqui.Commercial.Domain;

/// <summary>
/// Previsão de entrega do projeto (<c>commercial.project_forecasts</c>, PLN-01b CA 7, ADR 0029 D10): fora do documento (a linha de
/// <c>commercial.projects</c>, a versão e a auditoria do projeto não mudam a cada recálculo) e fora do <c>IAuditable</c>. Escritor único: o
/// consumidor do <c>ProjectForecastChanged</c>, que só grava o <see cref="ComputedAt"/> mais novo. O risco é calculado na leitura, pela
/// função única, porque a data prometida muda por aditivo sem novo cálculo.
/// </summary>
public sealed class ProjectForecast
{
    public Guid ProjectId { get; init; }

    public DateOnly? ForecastDate { get; set; }

    public DateOnly? FabricationEnd { get; set; }

    /// <summary>Código do motivo da previsão vazia (NoRouting, NoWorkCenter, NoCapacity, NoFabricationOrder).</summary>
    public string? Reason { get; set; }

    /// <summary>OS que impede a data, quando há.</summary>
    public Guid? ReasonOrderId { get; set; }

    public DateTimeOffset ComputedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
