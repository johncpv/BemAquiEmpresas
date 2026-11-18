namespace BemAqui.BuildingBlocks.Modules;

/// <summary>Segmento de um perfil: troca dados iniciais e padrões de tela, nunca a liberação de módulo (ADR 0046 §4).</summary>
public sealed record BusinessSegment(string Code, string Name);

/// <summary>Perfil de negócio: os módulos que fazem sentido para esse tipo de empresa (ADR 0046).</summary>
public sealed record BusinessProfileDefinition(string Code, string Name, string Description, IReadOnlyList<string> Modules, IReadOnlyList<BusinessSegment> Segments)
{
    /// <summary>Vocabulário do perfil (PERF-03): chave → termo, com teto de <see cref="BusinessVocabulary.MaxTerms"/> chaves.</summary>
    public IReadOnlyDictionary<string, string> Vocabulary { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool HasSegment(string? segment) => segment is null || Segments.Any(s => string.Equals(s.Code, segment, StringComparison.Ordinal));
}

/// <summary>Como um módulo aparece para o tenant (ADR 0046 §2): liberado, com cadeado (no perfil, fora do plano) ou oculto (fora do perfil).</summary>
public enum ModuleVisibility
{
    Released,
    Locked,
    Hidden,
}

/// <summary>
/// Perfis de negócio declarados em código, ao lado do <see cref="ModuleCatalog"/> (PERF-01, ADR 0046). O perfil é uma entrada do
/// cálculo da liberação: <c>liberado = (no plano E em algum perfil do tenant) OU exceção manual</c>. A camada 1 continua lendo só
/// <c>tenant_modules</c>; o perfil só decide o que o plano grava lá e se o módulo não liberado aparece com cadeado ou some.
/// </summary>
public static class BusinessProfileCatalog
{
    public const string Factory = "factory";
    public const string Retail = "retail";
    public const string Services = "services";
    public const string Ecommerce = "ecommerce";

    // Módulos que todo perfil usa (PRD do PDV, seção 3.3). RH e Patrimônio entram como "opcional": no perfil, liberados pelo plano.
    private static readonly string[] Core = ["dashboard", "parties", "finance", "fiscal", "identity", "settings", "inventory", "purchasing", "hr", "assets"];

    public static IReadOnlyList<BusinessProfileDefinition> All { get; } =
    [
        // CA 3: exatamente os módulos que a Konebras tem hoje (os 16 da ADR 0010). Módulo novo só entra aqui por decisão explícita.
        new(Factory, "Fábrica", "Indústria sob encomenda: orçamento, projeto, engenharia, produção e chão de fábrica.",
            [.. Core, "quotes", "projects", "engineering", "production", "kiosk", "shopfloor"], [])
        {
            Vocabulary = BusinessVocabulary.Defaults, // a Fábrica fala como o sistema fala hoje: com ela, nenhum termo muda (PERF-03 CA 3)
        },
        new(Retail, "Comércio", "Venda no balcão e na loja: caixa, estoque e retaguarda.",
            [.. Core, "kiosk", "catalog"],
            [
                new("counter", "Balcão"),
                new("supermarket", "Supermercado"),
                new("bakery", "Padaria"),
                new("restaurant", "Restaurante"),
                new("snack_bar", "Lanchonete e hamburgueria"),
                new("food_truck", "Trailer"),
            ])
        {
            Vocabulary = BusinessVocabulary.Backoffice,
        },
        new(Services, "Prestação de serviços", "Serviço com orçamento e ordem de serviço.",
            [.. Core, "quotes", "projects", "catalog"],
            [new("workshop", "Oficina")])
        {
            Vocabulary = BusinessVocabulary.Backoffice,
        },
        // CAT-01: o catálogo entra em Comércio, Serviços e E-commerce (PRD do PDV, seção 3.3); na Fábrica, não (decisão explícita pendente).
        new(Ecommerce, "E-commerce", "Venda pela loja virtual.", [.. Core, "catalog"], []) { Vocabulary = BusinessVocabulary.Backoffice },
    ];

    /// <summary>Recurso de pagamentos da venda dentro do Financeiro (PAG-01): formas de pagamento e taxas da adquirente.</summary>
    public const string PaymentMethodsFeature = "finance.payment_methods";

    /// <summary>
    /// Recursos por perfil dentro de um módulo comum (adendo da ADR 0046, decisão do PO de 17/11/2026): o módulo continua liberado para
    /// todos, mas o recurso só existe para os perfis listados. Fora deles, o recurso some do menu e a API responde 403
    /// <c>module_not_released</c>, como a camada 1. Tenant sem perfil nenhum não tem o recurso. Nunca liste <c>factory</c> aqui sem decisão.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Features { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [PaymentMethodsFeature] = [Retail, Services, Ecommerce],
        [FirstStepsFeature] = [Retail],
    };

    /// <summary>Checklist "Primeiros passos" do Início (PERF-04): só o Comércio, por enquanto; a fábrica não vê o cartão nem a ajuda.</summary>
    public const string FirstStepsFeature = "dashboard.first_steps";

    public static bool HasFeature(IEnumerable<string> profiles, string feature)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return Features.TryGetValue(feature, out var allowed) && profiles.Any(p => allowed.Contains(p, StringComparer.Ordinal));
    }

    public static BusinessProfileDefinition? Find(string code) => All.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.Ordinal));

    /// <summary>Perfil e segmento válidos: o segmento, quando informado, tem de ser do perfil.</summary>
    public static bool IsKnown(string profile, string? segment) => Find(profile) is { } p && p.HasSegment(segment);

    /// <summary>
    /// União dos módulos dos perfis do tenant (CA 6). Sem nenhum perfil, devolve <c>null</c>: não há filtro e vale o comportamento
    /// anterior à ADR 0046 (só o plano e a exceção). O backfill e o provisionamento garantem pelo menos um perfil por tenant.
    /// </summary>
    public static IReadOnlySet<string>? ModulesOf(IEnumerable<string> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var codes = profiles.ToList();
        return codes.Count == 0
            ? null
            : codes.Select(Find).OfType<BusinessProfileDefinition>().SelectMany(p => p.Modules).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>No perfil: sem filtro (tenant sem perfil) ou o módulo está em algum perfil do tenant.</summary>
    public static bool InProfile(IReadOnlySet<string>? profileModules, string moduleCode) => profileModules is null || profileModules.Contains(moduleCode);

    /// <summary>Liberação herdada (sem exceção manual): no plano E em algum perfil (CA 5).</summary>
    public static bool Inherited(bool inPlan, bool inProfile) => inPlan && inProfile;

    /// <summary>
    /// Estado do módulo para o menu (CA 7, 8 e 10): liberado aparece; não liberado e no perfil fica com cadeado; não liberado e fora
    /// do perfil some. A exceção manual que libera aparece normalmente, porque está liberada. Oculto é só apresentação: a camada 1
    /// barra pelo <c>tenant_modules</c>.
    /// </summary>
    public static ModuleVisibility Visibility(bool released, bool inProfile) =>
        released ? ModuleVisibility.Released : inProfile ? ModuleVisibility.Locked : ModuleVisibility.Hidden;

    /// <summary>Módulos do catálogo ocultos para o tenant: não liberados e fora de todos os perfis.</summary>
    public static IReadOnlySet<string> Hidden(IEnumerable<string> profiles, IReadOnlySet<string> released)
    {
        ArgumentNullException.ThrowIfNull(released);
        var modules = ModulesOf(profiles);
        return ModuleCatalog.All
            .Where(m => Visibility(released.Contains(m.Code), InProfile(modules, m.Code)) == ModuleVisibility.Hidden)
            .Select(m => m.Code)
            .ToHashSet(StringComparer.Ordinal);
    }
}
