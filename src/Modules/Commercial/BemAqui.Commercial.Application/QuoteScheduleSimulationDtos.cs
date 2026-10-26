namespace BemAqui.Commercial.Application;

/// <summary>Posição na fila (PLN-05 CA 3): null = no fim; "Na posição {n}" de 1 até o tamanho da fila + 1.</summary>
public sealed record QuoteScheduleSimulationInput(int? QueuePosition);

/// <summary>Projeto que passa a atrasar (CA 4), só com <c>projects.view</c>: número e nome, sem datas nem horas.</summary>
public sealed record QuoteSimulationLateProject(Guid ProjectId, string Number, string Name);

/// <summary>
/// Resultado da simulação de prazo (PLN-05 CA 4, 7 e 9): <b>só datas e dias</b>. Nenhum custo, custo-hora, cargo nem hora por centro sai
/// aqui, nem a fila ou a ocupação de outros projetos além da contagem de quem passa a atrasar (e número e nome com <c>projects.view</c>).
/// <c>Simulated</c> = false: o orçamento não tem horas por centro (CA 7) e o motor não foi chamado.
/// </summary>
public sealed record QuoteScheduleSimulationView(
    bool Simulated,
    int QueueSize,
    int QueuePosition,
    bool AtEnd,
    DateOnly Today,
    DateOnly? FabricationEnd,
    DateOnly? DeliveryDate,
    int? DeliveryDays,
    string? NoForecastReason,
    bool IncludesInstallation,
    int? PromisedDays,
    DateOnly? PromisedDate,
    int? DaysBeyondPromise,
    int NewlyLateCount,
    IReadOnlyList<QuoteSimulationLateProject>? NewlyLateProjects);
