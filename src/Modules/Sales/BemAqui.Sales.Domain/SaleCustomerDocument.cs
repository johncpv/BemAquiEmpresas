using BemAqui.SharedKernel;

namespace BemAqui.Sales.Domain;

/// <summary>
/// CPF ou CNPJ do consumidor informado no caixa (<c>sales.sale_customer_documents</c>, OFF-02 CA 17, PP-26): cifrado com o key ring do tenant
/// (propósito com o id da linha) e com um índice cego (HMAC-SHA256 com a chave do tenant) para achar a venda pelo documento sem decifrar. Fica
/// <b>fora</b> do registro imutável de eventos do caixa, do log e do outbox: apagar (retenção ou pedido do titular) não quebra a cadeia de hash.
/// Uma linha por venda.
/// </summary>
public sealed class SaleCustomerDocument
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid SaleId { get; init; }

    /// <summary><c>cpf</c> ou <c>cnpj</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    [PersonalData]
    [ProtectedSecret]
    public string DocumentCipher { get; init; } = string.Empty;

    /// <summary>HMAC-SHA256 (hex) do documento normalizado com a chave do tenant: busca por igualdade, sem revelar o documento.</summary>
    public string BlindIndex { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Chave do índice cego do tenant (<c>sales.blind_index_keys</c>): 32 bytes aleatórios, guardados cifrados. Uma linha só.</summary>
public sealed class BlindIndexKey
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    [ProtectedSecret]
    public string KeyCipher { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}
