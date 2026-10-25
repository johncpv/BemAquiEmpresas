using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.BuildingBlocks.Agenda;

/// <summary>
/// Nível da Minha pauta (DSH-04 CA 4, ponto 15 da revisão pré-Fases 4 a 8): primeiro o que destrava outra pessoa ou dinheiro,
/// depois o que já passou do prazo, depois o que vence em breve.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgendaTier>))]
public enum AgendaTier
{
    Unblocks = 1,
    Overdue = 2,
    DueSoon = 3,
}

/// <summary>
/// Categoria que uma fonte detecta (CA 1): código único (<c>modulo.algo</c>), módulo do catálogo, permissão <c>modulo.acao</c>
/// que a pessoa precisa ter para ver a categoria, nível e o nome usado na mensagem de falha ("Não foi possível carregar {Label}.").
/// </summary>
public sealed record AgendaCategory(string Code, string Module, string Permission, AgendaTier Tier, string Label);

/// <summary>Uma linha da categoria (CA 5): o documento, o link já na ação que resolve e a marca opcional ("Autoinspeção").</summary>
public sealed record AgendaEntry(string Title, string? Detail, string Link, DateOnly? DueDate, decimal? Amount, string? Badge = null);

/// <summary>
/// Item da pauta (CA 1 e 5): uma categoria com a contagem, o título ("3 ordens de pagamento aguardam você"), o porquê numa linha,
/// o link de "Ver todos" (a lista filtrada; nulo quando não há lista), o vencimento mais próximo e o valor total para a ordem, o verbo
/// da ação e até <see cref="AgendaLimits.EntriesPerCategory"/> linhas.
/// </summary>
public sealed record AgendaItem(
    string Category, string Title, string Why, string? Link, DateOnly? DueDate, decimal? Amount, AgendaTier Tier, int Count, string Action,
    IReadOnlyList<AgendaEntry> Entries);

/// <summary>Categoria que falhou ou passou do tempo limite (CA 10): a pauta responde com as outras.</summary>
public sealed record AgendaFailure(string Category, string Label, AgendaTier Tier, string Message);

/// <summary>Resposta de <c>GET /api/v1/me/agenda</c>: itens já ordenados, falhas parciais e a contagem total (título da aba).</summary>
public sealed record AgendaView(IReadOnlyList<AgendaItem> Items, IReadOnlyList<AgendaFailure> Failures, int Total);

/// <summary>
/// Pedido a uma fonte: o usuário do request, as permissões efetivas (já limitadas aos módulos liberados e aos grupos), as categorias
/// da fonte que ele pode ver e que foram pedidas, e o "hoje" de negócio (<c>BusinessTime.Today</c>).
/// </summary>
public sealed record AgendaRequest(Guid UserId, EffectivePermissions Permissions, IReadOnlySet<string> Categories, DateOnly Today)
{
    public bool Wants(string category) => Categories.Contains(category);
}

public static class AgendaLimits
{
    /// <summary>Linhas por categoria (CA 5); a contagem diz o total e "Ver todos" abre a lista.</summary>
    public const int EntriesPerCategory = 5;

    /// <summary>Tempo limite padrão por fonte (CA 10).</summary>
    public const int DefaultSourceTimeoutMs = 300;
}

/// <summary>
/// Detector de um módulo para a Minha pauta (DSH-04 CA 1 a 3). Implementado no Application do módulo dono, registrado no DI com
/// <c>TryAddEnumerable</c>, lendo só as tabelas do próprio módulo e os contratos dos outros. <see cref="Module"/> e
/// <see cref="Categories"/> são declarações estáticas (o teste de arquitetura lê sem construir a fonte). O agregador só chama a fonte
/// com o módulo liberado e só pede as categorias cuja permissão a pessoa tem; a fonte confere a regra de quem decide (alçada, autor,
/// segregação) e devolve um item por categoria com algo pendente.
/// </summary>
public interface IAgendaSource
{
    string Module { get; }

    IReadOnlyList<AgendaCategory> Categories { get; }

    Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Minha pauta do usuário do request (CA 11): as mesmas regras e a mesma ordem para o Início e para o card "Aguardando sua decisão"
/// do painel do dono (DSH-01a), que pede só as categorias do nível 1.
/// </summary>
public interface IAgendaService
{
    /// <summary>Categorias registradas (fontes entregues), para o teste do CA 3 e para o painel escolher as do nível 1.</summary>
    IReadOnlyList<AgendaCategory> Registered { get; }

    Task<AgendaView> ForUserAsync(IReadOnlyCollection<string>? categories = null, CancellationToken cancellationToken = default);
}
