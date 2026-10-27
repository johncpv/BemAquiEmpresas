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
