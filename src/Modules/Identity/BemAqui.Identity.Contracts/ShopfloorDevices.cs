namespace BemAqui.Identity.Contracts;

/// <summary>Aparelho registrado da fábrica, só o id e o nome (RH-01a: origem "PIN no {aparelho}" da marcação de ponto).</summary>
public sealed record ShopfloorDeviceRef(Guid Id, string Name);

/// <summary>
/// Aparelhos da fábrica para os outros módulos (ADR 0026, adendo de 21/10/2026; RH-01a CA 3 e 7): qual aparelho registrado fez o pedido
/// (pelo segredo do cookie <c>__Host-bq_dev</c>), os nomes para a lista e o fim da sessão por PIN depois da marcação de ponto.
/// </summary>
public interface IShopfloorDevices
{
    /// <summary>O aparelho ativo do segredo do cookie, ou null (sem cookie, desconhecido, revogado ou vencido).</summary>
    Task<ShopfloorDeviceRef?> ResolveAsync(string? deviceSecret, CancellationToken cancellationToken = default);

    /// <summary>Nomes dos aparelhos (inclusive os revogados, para o histórico).</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encerra no servidor a sessão por PIN do usuário no aparelho (RH-01a CA 7): revoga a família de refresh da sessão; o próximo request
    /// com o token responde 401 <c>pin_session_ended</c>. Grava a auditoria e devolve quantas sessões caíram.
    /// </summary>
    Task<int> EndPinSessionAsync(Guid userId, Guid deviceId, string reason, CancellationToken cancellationToken = default);
}

/// <summary>Aparelho de caixa para a tela "Lojas e caixas" (PDV-01): nome, último contato e a última sincronização.</summary>
public sealed record RegisterDeviceInfo(Guid Id, string Name, DateTimeOffset LastSeenAt, DateTimeOffset? LastSyncAt, DateTimeOffset? RevokedAt);

/// <summary>Segredo novo do aparelho depois de uma sincronização (vai só para o cookie <c>__Host-bq_pos</c>, nunca para o corpo).</summary>
public sealed record DeviceSecretRotation(string Secret, DateTimeOffset ExpiresAt);

/// <summary>Resultado da sincronização: segredo girado, aparelho revogado ou outra sincronização do mesmo aparelho ganhou a corrida.</summary>
public enum DeviceSyncStatus
{
    Rotated,
    Revoked,
    Conflict,
}

public sealed record DeviceSyncResult(DeviceSyncStatus Status, DeviceSecretRotation? Rotation);

/// <summary>
/// Aparelhos de caixa para o <c>Pos</c> (PDV-01, ADR 0048): o Identity é o dono do aparelho; o <c>Pos</c> mostra o último contato na tela das
/// lojas e, na sincronização, pede o segredo novo (o segredo do caixa gira a cada sincronização bem-sucedida, não a cada PIN).
/// </summary>
public interface IRegisterDevices
{
    Task<IReadOnlyDictionary<Guid, RegisterDeviceInfo>> FindAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sincronização bem-sucedida do aparelho ativo: grava o instante e gira o segredo, por UPDATE condicional ao segredo lido (duas sincronizações
    /// simultâneas: uma gira, a outra recebe <see cref="DeviceSyncStatus.Conflict"/>). Pedida com o segredo anterior, a rotação já aposenta o anterior
    /// novo (ADR 0048 §2).
    /// </summary>
    Task<DeviceSyncResult> CompleteSyncAsync(Guid deviceId, bool usedPreviousSecret, CancellationToken cancellationToken = default);
}

/// <summary>Quem pode entrar por PIN nos caixas de uma loja (OFF-01): só o id e o nome curto (primeiro nome e inicial). Nunca o PIN nem o hash.</summary>
public sealed record PosStoreOperator(Guid UserId, string DisplayName);

/// <summary>
/// Operadores da loja para a descida do catálogo do caixa (OFF-01 CA 10 e 12): ativos, com PIN do caixa, com perfil que usa o caixa e, se usuário
/// de operação, ligados à loja. O Identity é o dono; o <c>Pos</c> lê só por aqui.
/// </summary>
public interface IPosStoreOperators
{
    Task<IReadOnlyList<PosStoreOperator>> ForStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
}
