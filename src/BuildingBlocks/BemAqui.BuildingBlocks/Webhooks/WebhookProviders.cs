using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Modules;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>
/// Um provedor externo que avisa por webhook (FND-16, ADR 0031): o módulo dono, o validador de origem, de onde vem o id da
/// entrega, o <c>Content-Type</c> aceito, a fila do processamento e o handler (no módulo dono). Nesta story só existe o
/// provedor de teste, registrado pelos testes de integração; o <c>emissor-fiscal</c> entra na FIS-01, o <c>inter</c> na BNK-02.
/// </summary>
public sealed partial record WebhookProvider
{
    /// <summary>Código na URL (<c>/webhooks/{code}/{tenantKey}</c>): minúsculas, dígitos e hífen.</summary>
    public required string Code { get; init; }

    /// <summary>Nome para a tela "Eventos recebidos" (ex.: "Banco Inter").</summary>
    public required string Name { get; init; }

    /// <summary>Módulo dono (ex.: <c>finance</c>, <c>fiscal</c>): libera o processamento e dá as permissões da tela.</summary>
    public required string OwnerModule { get; init; }

    /// <summary>Validador de origem obrigatório (CA 4): HMAC do corpo cru ou token na URL.</summary>
    public required IWebhookOriginValidator Validator { get; init; }

    /// <summary>Handler do provedor (<see cref="IWebhookHandler"/>), no módulo dono; resolvido do escopo do tenant.</summary>
    public required Type HandlerType { get; init; }

    /// <summary>Tipo de mídia aceito; diferente dele, 415 (CA 8).</summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>Fila do Hangfire do <c>ProcessWebhookEventJob</c> (ex.: <see cref="JobQueues.Fiscal"/>).</summary>
    public string Queue { get; init; } = JobQueues.Default;

    /// <summary>Cabeçalho com o id da entrega (ex.: <c>X-BemAqui-Delivery-Id</c>); é o <c>external_id</c> (CA 10).</summary>
    public string? DeliveryIdHeader { get; init; }

    /// <summary>Sem id de entrega: a chave composta que o provedor declara (o Inter: cobrança + situação + data, BNK-02).</summary>
    public Func<WebhookRequest, string?>? CompositeKey { get; init; }

    /// <summary>Cabeçalho com o tipo do evento (ex.: <c>X-BemAqui-Event</c>).</summary>
    public string? EventTypeHeader { get; init; }

    /// <summary>GET de verificação no padrão da Meta (<c>hub.challenge</c>), só para quem declara (CA 1, WPP-01).</summary>
    public bool SupportsVerificationGet { get; init; }

    /// <summary>Rotação do segredo pela tela de Configurações nesta story (só o provedor de teste, até a BNK-01a).</summary>
    public bool RotationInSettings { get; init; }

    /// <summary>Aceito também no host interno da API, pela rede interna (só o EmissorFiscal, FIS-01).</summary>
    public bool AllowInternalHost { get; init; }

    public string ViewPermission => OwnerModule + "." + StandardActions.View;

    public string EditPermission => OwnerModule + "." + StandardActions.Edit;

    /// <summary>
    /// Ver o corpo mascarado (CA 15): a aprovação do módulo dono; sem ela no catálogo (<c>fiscal.approve</c> nasce na FIS-02a),
    /// só os membros do Proprietário.
    /// </summary>
    public string? ContentPermission =>
        ModuleCatalog.IsKnownPermission(OwnerModule + "." + StandardActions.Approve) ? OwnerModule + "." + StandardActions.Approve : null;

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant)]
    internal static partial Regex CodeFormat();
}

/// <summary>
/// Provedores conhecidos (BuildingBlocks, ADR 0031 §1). A subida falha com provedor sem validador, sem handler, de módulo fora do
/// catálogo ou repetido; o teste de arquitetura confere o mesmo no registro do Host.
/// </summary>
public sealed class WebhookProviderRegistry
{
    private readonly Dictionary<string, WebhookProvider> _providers = new(StringComparer.Ordinal);

    public WebhookProviderRegistry(IEnumerable<WebhookProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers)
        {
            Add(provider);
        }
    }

    public IReadOnlyCollection<WebhookProvider> All => _providers.Values;

    public bool TryGet(string? code, [NotNullWhen(true)] out WebhookProvider? provider)
    {
        provider = null;
        return code is not null && _providers.TryGetValue(code, out provider);
    }

    /// <summary>Motivos para recusar o registro do provedor; vazio quando está certo.</summary>
    public static IReadOnlyList<string> Problems(WebhookProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var problems = new List<string>();
        if (string.IsNullOrEmpty(provider.Code) || !WebhookProvider.CodeFormat().IsMatch(provider.Code))
        {
            problems.Add("código inválido: " + provider.Code);
        }

        if (provider.Validator is null)
        {
            problems.Add("sem validador de origem");
        }

        if (provider.HandlerType is null || !typeof(IWebhookHandler).IsAssignableFrom(provider.HandlerType) || provider.HandlerType.IsAbstract)
        {
            problems.Add("handler não implementa IWebhookHandler");
        }

        if (ModuleCatalog.Find(provider.OwnerModule ?? string.Empty) is null || !ModuleCatalog.IsKnownPermission(provider.OwnerModule + "." + StandardActions.View)
            || !ModuleCatalog.IsKnownPermission(provider.OwnerModule + "." + StandardActions.Edit))
        {
            problems.Add("módulo dono fora do catálogo ou sem ver e editar: " + provider.OwnerModule);
        }

        if (!JobQueues.All.Contains(provider.Queue, StringComparer.Ordinal))
        {
            problems.Add("fila desconhecida: " + provider.Queue);
        }

        if (provider.SupportsVerificationGet && provider.Validator is not UrlTokenValidator)
        {
            problems.Add("GET de verificação exige o token guardado como hash (UrlTokenValidator)");
        }

        return problems;
    }

    private void Add(WebhookProvider provider)
    {
        var problems = Problems(provider);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Provedor de webhook " + provider.Code + " inválido: " + string.Join("; ", problems));
        }

        if (!_providers.TryAdd(provider.Code, provider))
        {
            throw new InvalidOperationException("Provedor de webhook repetido: " + provider.Code);
        }
    }
}
