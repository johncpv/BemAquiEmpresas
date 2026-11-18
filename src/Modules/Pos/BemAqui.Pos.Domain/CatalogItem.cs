using System.Text.Json.Serialization;

namespace BemAqui.Pos.Domain;

/// <summary>Coleção da linha do modelo de leitura do caixa (OFF-01): produto (com códigos de barras e preço da loja) ou forma de pagamento.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CatalogItemKind>))]
public enum CatalogItemKind
{
    Product,
    PaymentMethod,
}

/// <summary>
/// Modelo de leitura do caixa (<c>pos.catalog_items</c>, OFF-01 CA 9 a 12): uma linha por loja, coleção e entidade, com o que desce para o aparelho
/// já resolvido para a loja (o preço é o da tabela da loja). Alimentado pelos eventos do Catálogo e do Financeiro e pela carga inicial de cada
/// loja; o <c>Pos</c> nunca lê tabela de outro módulo. <see cref="ChangeSeq"/> vem da sequência <c>pos.catalog_change_seq</c> e só muda quando o
/// conteúdo muda (consumidor idempotente); as gravações são em série (trava do tenant), então o cursor do aparelho nunca pula uma mudança.
/// <see cref="Payload"/> é jsonb sem custo nem margem. Removido = <see cref="Deleted"/> (a linha fica para o delta avisar o aparelho).
/// </summary>
public sealed class CatalogItem
{
    public Guid StoreId { get; init; }

    public CatalogItemKind Kind { get; init; }

    public Guid EntityId { get; init; }

    /// <summary>Conteúdo da linha em JSON (camelCase), conforme a coleção.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Versão da tabela de preço da loja quando a linha foi montada (só produto).</summary>
    public long? PriceListVersion { get; set; }

    public bool Deleted { get; set; }

    public long ChangeSeq { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Carga inicial do catálogo de uma loja (<c>pos.catalog_store_builds</c>, OFF-01): feita para a tabela de preço indicada. Loja nova, loja que
/// existia antes da OFF-01 ou loja que trocou de tabela é (re)montada na primeira sincronização; a montagem é idempotente.
/// </summary>
public sealed class CatalogStoreBuild
{
    public Guid StoreId { get; init; }

    public Guid PriceListId { get; set; }

    public DateTimeOffset BuiltAt { get; set; }
}
