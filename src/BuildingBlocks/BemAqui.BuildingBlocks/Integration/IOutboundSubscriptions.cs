namespace BemAqui.BuildingBlocks.Integration;

/// <summary>
/// Inscrição de avisos de saída da conexão (INT-02 CA 1 e 2, ADR 0053): chamada pelo Identity na troca do código, <b>na mesma transação</b> que
/// grava a conexão nova e revoga a anterior. Devolve o segredo de assinatura em claro, uma única vez (vai no <c>webhook_secret</c> do
/// <c>/oauth/token</c>), ou null quando não há inscrição. Até a INT-02 existir, a implementação padrão não cria nada.
/// </summary>
public interface IOutboundSubscriptions
{
    Task<string?> CreateForConnectionAsync(Guid connectionId, Guid? replacedConnectionId, IReadOnlyCollection<string> grantedScopes, CancellationToken cancellationToken = default);

    /// <summary>Segredo novo ainda não entregue (troca de segredo, INT-02 CA 6): sai na próxima renovação do token, uma vez só.</summary>
    Task<string?> TakePendingSecretAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

/// <summary>Sem a INT-02: nenhuma inscrição, nenhum segredo.</summary>
public sealed class NoOutboundSubscriptions : IOutboundSubscriptions
{
    public Task<string?> CreateForConnectionAsync(Guid connectionId, Guid? replacedConnectionId, IReadOnlyCollection<string> grantedScopes, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> TakePendingSecretAsync(Guid connectionId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
