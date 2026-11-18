namespace BemAqui.BuildingBlocks.Modules;

/// <summary>
/// Pacotes de dados iniciais por perfil e segmento (PERF-04, adendo da ADR 0046). Cada módulo é dono do próprio pacote (um
/// <see cref="Tenancy.IProfileSeedPackage"/> registrado pelo módulo, com a chave daqui); o perfil só diz quais pacotes rodam. A fábrica
/// não tem pacote nenhum: com ela nada é criado nem alterado (CA 5). Nunca liste pacote em <c>factory</c> sem decisão explícita.
/// </summary>
public static class BusinessProfileSeeds
{
    /// <summary>Terceiro adquirente "Maquininha" (Parties), para as formas de cartão apontarem para ele.</summary>
    public const string PartiesRetail = "parties.retail";

    /// <summary>Contas Caixa PDV 01, Cofre e Quebra de caixa e as formas de pagamento da PAG-01 com a Maquininha (Finance).</summary>
    public const string FinanceRetail = "finance.retail";

    /// <summary>Locais Loja e Depósito (Inventory).</summary>
    public const string InventoryRetail = "inventory.retail";

    /// <summary>Grupos Gerente de loja, Operador de caixa e Estoquista (Identity), só com permissões de módulos que já existem.</summary>
    public const string IdentityRetail = "identity.retail";

    /// <summary>Tabela de preço "Balcão" e categoria "Geral" (Catalog): os mesmos cadastros padrão que a CAT-01 cria no primeiro uso.</summary>
    public const string CatalogRetail = "catalog.retail";

    /// <summary>Unidades do segmento de alimentação (Inventory).</summary>
    public const string InventoryFood = "inventory.food";

    /// <summary>Segmentos de alimentação (PRD do PDV, seção 3.3: os que terão restaurante e KDS na Fase 15).</summary>
    public static readonly IReadOnlySet<string> FoodSegments = new HashSet<string>(StringComparer.Ordinal) { "bakery", "restaurant", "snack_bar", "food_truck" };

    /// <summary>Pacotes de cada perfil, na ordem em que rodam (o terceiro antes das formas que apontam para ele).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByProfile { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [BusinessProfileCatalog.Factory] = [],
        [BusinessProfileCatalog.Retail] = [PartiesRetail, FinanceRetail, InventoryRetail, IdentityRetail, CatalogRetail],
        [BusinessProfileCatalog.Services] = [], // Fase 14
        [BusinessProfileCatalog.Ecommerce] = [], // Fase 17
    };

    /// <summary>Pacotes de cada segmento, depois dos do perfil.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BySegment { get; } =
        FoodSegments.ToDictionary(s => s, _ => (IReadOnlyList<string>)[InventoryFood], StringComparer.Ordinal);

    /// <summary>
    /// Pacotes que rodam para os perfis e segmentos do tenant, sem repetição, na ordem do catálogo de perfis (não na ordem de aplicação):
    /// primeiro os dos perfis, depois os dos segmentos. Sem perfil nenhum, ou só com <c>factory</c>, a lista é vazia.
    /// </summary>
    public static IReadOnlyList<string> PackagesFor(IEnumerable<string> profiles, IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(segments);
        var mine = profiles.ToHashSet(StringComparer.Ordinal);
        var mySegments = segments.ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var profile in BusinessProfileCatalog.All.Where(p => mine.Contains(p.Code)))
        {
            result.AddRange(ByProfile.GetValueOrDefault(profile.Code, []));
            foreach (var segment in profile.Segments.Where(s => mySegments.Contains(s.Code)))
            {
                result.AddRange(BySegment.GetValueOrDefault(segment.Code, []));
            }
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }
}
