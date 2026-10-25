using System.Text.Json.Serialization;

namespace BemAqui.Production.Contracts;

/// <summary>
/// Faixa da ocupação de uma célula (PLN-04 CA 4 e 6, pontos 40 e 41): <c>Idle</c> abaixo de 60% ("Folga"), <c>Normal</c> de 60% a menos de
/// 95% (sem destaque), <c>Full</c> de 95% a 100% ("Lotado") e <c>Over</c> quando as comprometidas passam da capacidade lida agora ("Acima da
/// capacidade": a capacidade caiu depois da última execução do plano). <c>NoCapacity</c>: sem capacidade e sem horas ("—").
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FactoryLoadBand>))]
public enum FactoryLoadBand
{
    NoCapacity,
    Idle,
    Normal,
    Full,
    Over,
}

/// <summary>
/// Semana ISO (segunda a domingo) da ocupação: início, fim, número da semana e o primeiro dia contado (na semana atual, hoje: o plano
/// começa hoje).
/// </summary>
public sealed record FactoryLoadWeek(DateOnly Start, DateOnly End, int IsoWeek, DateOnly From);

/// <summary>
/// Célula da ocupação (PLN-04 CA 1 e 2): horas comprometidas pelo plano, capacidade lida agora, ocupação % com 1 casa (null com
/// capacidade 0, "—") e a faixa. Horas em <c>numeric(18,4)</c>; arredondamento só na exibição.
/// </summary>
public sealed record FactoryLoadCell(DateOnly WeekStart, decimal CommittedHours, decimal CapacityHours, decimal? Percent, FactoryLoadBand Band);

/// <summary>
/// Linha da ocupação: um centro de trabalho ativo (ou a linha "Fábrica", sem id), as células por semana, as horas já planejadas depois da
/// última semana mostrada e o último dia planejado ("Corte ocupado até 19/02", CA 3).
/// </summary>
public sealed record FactoryLoadRow(Guid? WorkCenterId, string Name, string? Line, IReadOnlyList<FactoryLoadCell> Cells, decimal HoursAfter, DateOnly? BusyUntil);

/// <summary>Ocupação por semana e centro (PLN-04 CA 10): hoje, quando o plano lido foi calculado, as semanas, os centros e a linha "Fábrica".</summary>
public sealed record FactoryLoadWeekly(DateOnly Today, DateTimeOffset? PlanComputedAt, IReadOnlyList<FactoryLoadWeek> Weeks, IReadOnlyList<FactoryLoadRow> Centers, FactoryLoadRow Factory);

/// <summary>
/// Ocupação da fábrica (PLN-04 CA 10) para o painel do dono (DSH-01b, card Produção) e o do supervisor (DSH-03, bloco Capacidade): lê o plano
/// gravado (nada é recalculado) e a capacidade de cada centro agora; os mesmos números da tela Planejamento → Ocupação. Sem custo.
/// </summary>
public interface IFactoryLoad
{
    /// <summary>
    /// <paramref name="weeks"/> semanas a partir da atual (de 1 a 26), dos centros ativos (ou só dos pedidos), com a linha "Fábrica" =
    /// Σ comprometidas ÷ Σ capacidades dos centros devolvidos.
    /// </summary>
    Task<FactoryLoadWeekly> WeeklyAsync(int weeks = 5, IReadOnlyCollection<Guid>? workCenterIds = null, CancellationToken cancellationToken = default);
}
