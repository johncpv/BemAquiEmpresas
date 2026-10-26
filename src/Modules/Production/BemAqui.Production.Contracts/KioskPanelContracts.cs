using BemAqui.SharedKernel;

namespace BemAqui.Production.Contracts;

/// <summary>
/// Coluna do quadro da TV (PNL-01b CA 1): a chave do Kanban (código da etapa, <c>pronta</c> ou <c>sem-roteiro</c>), a letra para o
/// <c>StageIndicator size="tv"</c>, o nome e quantas OS estão nela. Só as colunas com OS da linha.
/// </summary>
public sealed record KioskColumn(string Key, string? Code, string Name, int Count);

/// <summary>
/// Cartão da TV (PNL-01b CA 2 a 4, 13; ADR 0030 §5): allowlist fechada. Número da OS, número e nome curto do projeto (28 caracteres),
/// etapa atual, operador pelo primeiro nome e inicial ("João S. +1") ou o motivo da pausa, início real da etapa, horas apontadas e
/// padrão, fim previsto da etapa (PLN-01a) e o atraso ou risco (função única da PLN-01b). <b>Nunca</b> o cliente, o produto, valores,
/// custos nem contato.
/// </summary>
public sealed record KioskCard(
    Guid Id, string Number, string ProjectNumber, string ProjectName, string ColumnKey, string? StepCode, string? StepName, string? Operator,
    string? PausedReason, DateTimeOffset? StepStartedAt, decimal AppliedHours, decimal StandardHours, DateOnly? PlannedEnd, DeliveryRisk Risk, int? DelayDays);

/// <summary>
/// Quadro por etapa da TV (CA 1): a linha do escopo (null = todas), "{n} OS em andamento, {m} atrasadas", as colunas e os cartões
/// (já na ordem do CA 3: atrasadas, em risco, fim previsto) e a hora do servidor para o relógio do tempo na etapa.
/// </summary>
public sealed record KioskBoard(string? Line, int InProgressCount, int LateCount, int AtRiskCount, IReadOnlyList<KioskColumn> Columns, IReadOnlyList<KioskCard> Cards, DateTimeOffset ServerNow);

/// <summary>
/// Linha da visão "Fila e previsões" (CA 5): posição na fila (PLN-02), número e nome curto do projeto, início previsto da primeira
/// etapa na linha, previsão de entrega (PLN-01b; null = "Previsão em cálculo"), prometido, folga em dias e o chip (no prazo, em risco,
/// atrasado). Sem cliente.
/// </summary>
public sealed record KioskQueueRow(
    int Position, Guid ProjectId, string ProjectNumber, string ProjectName, DateOnly? FirstStart, DateOnly? ForecastDate, DateOnly? PromisedDate, int? SlackDays,
    DeliveryRisk Risk);

/// <summary>Visão "Fila e previsões": até 6 projetos da fila com OS na linha e o total que tem OS na linha.</summary>
public sealed record KioskQueueView(string? Line, IReadOnlyList<KioskQueueRow> Rows, int Total);

/// <summary>
/// Linha da visão "Atrasos e riscos" (CA 6): a OS, o projeto (número e nome curto), a etapa atual, o operador (primeiro nome e inicial),
/// atrasada ou em risco, quantos dias e a etapa que define a entrega (a que termina por último no plano) com o fim previsto.
/// </summary>
public sealed record KioskDelayRow(
    Guid Id, string Number, string ProjectNumber, string ProjectName, string? StepCode, string? StepName, string? Operator, DeliveryRisk Risk, int Days,
    string? DefiningStepName, DateOnly? DefiningStepEnd);

/// <summary>Visão "Atrasos e riscos": até 6 OS (do maior atraso para o menor) e o total; vazia, a TV pula a visão (CA 6 e 7).</summary>
public sealed record KioskDelays(string? Line, IReadOnlyList<KioskDelayRow> Rows, int Total);

/// <summary>
/// Dados do Painel da TV (PNL-01b, ADR 0030 §5), do Production: as três visões e o cartão de uma OS, sempre com a linha do escopo (do
/// token na TV, da tela na prévia; null = todas). Só leitura; mesmo DTO para a TV e para a prévia do backoffice.
/// </summary>
public interface IKioskPanel
{
    Task<KioskBoard> BoardAsync(string? line, CancellationToken cancellationToken = default);

    /// <summary>Cartão de uma OS aberta da linha; null se não existe, é de outro tenant, não está aberta ou não é da linha (404 único).</summary>
    Task<KioskCard?> CardAsync(Guid orderId, string? line, CancellationToken cancellationToken = default);

    Task<KioskQueueView> QueueAsync(string? line, CancellationToken cancellationToken = default);

    Task<KioskDelays> DelaysAsync(string? line, CancellationToken cancellationToken = default);
}
