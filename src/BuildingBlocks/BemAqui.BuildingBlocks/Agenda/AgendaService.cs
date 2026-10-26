using System.Diagnostics;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Agenda;

public sealed class AgendaOptions
{
    public const string Section = "Agenda";

    /// <summary>Tempo limite de cada fonte (CA 10); passou, a categoria vira falha parcial.</summary>
    public int SourceTimeoutMs { get; set; } = AgendaLimits.DefaultSourceTimeoutMs;
}

/// <summary>Ordem da pauta (CA 4): nível; dentro do nível, o vencimento mais próximo (sem data no fim) e, no empate, o maior valor.</summary>
public static class AgendaOrdering
{
    public static IReadOnlyList<AgendaItem> Sort(IEnumerable<AgendaItem> items) =>
        [.. items
            .OrderBy(i => (int)i.Tier)
            .ThenBy(i => i.DueDate is null ? 1 : 0)
            .ThenBy(i => i.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(i => i.Amount ?? decimal.MinValue)
            .ThenBy(i => i.Category, StringComparer.Ordinal)];
}

/// <summary>
/// Agregador da Minha pauta (DSH-04): calcula na leitura, sem tabela nem cache entre usuários (CA 6, 13). Camada 1: só chama a fonte
/// com o módulo liberado no tenant; camadas 2 e 3: só pede as categorias cuja permissão está na permissão efetiva (que já é a união dos
/// grupos com o módulo habilitado, limitada aos liberados). As fontes rodam em sequência no mesmo escopo (um DbContext), cada uma com
/// tempo limite; a que falha ou demora vira falha parcial, com log e o tenant (CA 10).
/// </summary>
public sealed partial class AgendaService(
    IEnumerable<IAgendaSource> sources, ICurrentUser currentUser, IPermissionService permissions, TenantContextHolder tenant, TimeProvider clock,
    IOptions<AgendaOptions> options, ILogger<AgendaService> logger) : IAgendaService
{
    /// <summary>"Não foi possível carregar {categoria}." (CA 10).</summary>
    public static string FailureMessage(string label) => "Não foi possível carregar " + label + ".";

    private readonly IReadOnlyList<IAgendaSource> _sources = [.. sources];

    public IReadOnlyList<AgendaCategory> Registered => [.. _sources.SelectMany(s => s.Categories)];

    public async Task<AgendaView> ForUserAsync(IReadOnlyCollection<string>? categories = null, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId || tenant.Resolved is not { } resolved)
        {
            return new AgendaView([], [], 0);
        }

        var effective = await permissions.GetAsync(userId, cancellationToken);
        var today = BusinessTime.Today(clock);
        var timeout = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.SourceTimeoutMs));
        var items = new List<AgendaItem>();
        var failures = new List<AgendaFailure>();
        foreach (var source in _sources)
        {
            if (!resolved.IsReleased(source.Module))
            {
                continue; // camada 1: módulo não liberado, a fonte nem é chamada
            }

            var allowed = source.Categories
                .Where(c => ModuleCatalog.IsKnownPermission(c.Permission) && effective.Has(c.Permission) && (categories is null || categories.Contains(c.Code)))
                .ToList();
            if (allowed.Count == 0)
            {
                continue; // camadas 2 e 3: sem a permissão de nenhuma categoria
            }

            var request = new AgendaRequest(userId, effective, allowed.Select(c => c.Code).ToHashSet(StringComparer.Ordinal), today);
            var (result, error) = await RunAsync(source, request, timeout, cancellationToken);
            if (error is null)
            {
                error = Validate(result!, request);
            }

            if (error is not null)
            {
                LogSourceFailed(logger, source.Module, resolved.Tenant.Id, error);
                failures.AddRange(allowed.Select(c => new AgendaFailure(c.Code, c.Label, c.Tier, FailureMessage(c.Label))));
                continue;
            }

            items.AddRange(result!.Where(i => i.Count > 0));
        }

        var sorted = AgendaOrdering.Sort(items);
        return new AgendaView(sorted, failures, sorted.Sum(i => i.Count));
    }

    private static async Task<(IReadOnlyList<AgendaItem>? Items, string? Error)> RunAsync(IAgendaSource source, AgendaRequest request, TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await source.ItemsAsync(request, limit.Token);

            // Fonte que ignora o token e demora também conta como falha: a pauta não espera além do limite por uma categoria.
            return watch.Elapsed > timeout ? (null, $"passou de {timeout.TotalMilliseconds:0} ms ({watch.Elapsed.TotalMilliseconds:0} ms)") : (result, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, $"passou de {timeout.TotalMilliseconds:0} ms");
        }
#pragma warning disable CA1031 // falha de uma fonte não derruba a pauta (CA 10); vai para o log com o tenant
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return (null, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Item fora das categorias pedidas ou com link que não é do app é erro da fonte: não sai na resposta.</summary>
    private static string? Validate(IReadOnlyList<AgendaItem> items, AgendaRequest request)
    {
        foreach (var item in items)
        {
            if (!request.Wants(item.Category))
            {
                return "categoria não pedida: " + item.Category;
            }

            if ((item.Link is not null && !AgendaText.IsAppLink(item.Link)) || item.Entries.Any(e => !AgendaText.IsAppLink(e.Link)))
            {
                return "link fora do app em " + item.Category;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Minha pauta: a fonte {Module} falhou no tenant {TenantId}: {Reason}")]
    private static partial void LogSourceFailed(ILogger logger, string module, Guid tenantId, string reason);
}
