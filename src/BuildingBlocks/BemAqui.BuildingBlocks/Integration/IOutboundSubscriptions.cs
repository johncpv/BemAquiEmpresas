namespace BemAqui.BuildingBlocks.Integration;

/// <summary>
/// Inscrição de avisos de saída da conexão (INT-02 CA 1 a 3, ADR 0053): chamada pelo Identity na troca do código, <b>na mesma transação</b> que
/// grava a conexão nova e revoga a anterior, e pelos consumidores de <c>AppConnectionRevoked</c> e <c>AppConnectionSuspended</c>, que só encerram
/// ou suspendem. Implementada no BuildingBlocks (<c>OutboundSubscriptionService</c>), onde ficam as tabelas <c>shared.outbound_webhook_*</c>.
/// </summary>
public interface IOutboundSubscriptions
{
    /// <summary>
    /// Cria a inscrição da conexão nova (CA 1) e encerra a da conexão substituída (CA 2). Devolve o segredo de assinatura em claro e a versão (vai no
    /// <c>webhook_secret</c> do <c>/oauth/token</c> e fica pendente até a confirmação, INT-04 CA 5), ou null quando o ambiente não tem a URL de avisos
    /// configurada. Só adiciona ao DbContext: quem chama grava na própria transação; erro de configuração lança e desfaz a conexão.
    /// </summary>
    Task<PendingWebhookSecret?> CreateForConnectionAsync(Guid connectionId, string clientId, Guid? replacedConnectionId, IReadOnlyCollection<string> grantedScopes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Segredo pendente (conexão nova ou troca, INT-04 CA 5): vai em toda resposta do <c>/oauth/token</c> até <see cref="ConfirmSecretAsync"/>. Não grava.
    /// </summary>
    Task<PendingWebhookSecret?> PendingSecretAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// O aplicativo renovou com o <c>bqr_</c> de uma resposta que levou o segredo <paramref name="version"/>: baixa o pendente se ainda é essa versão (grava).
    /// </summary>
    Task ConfirmSecretAsync(Guid connectionId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tipos de aviso que a concessão permitiria e a inscrição ativa não tem (INT-04 CA 8, "O Conecta pode ganhar"); vazio sem inscrição.
    /// </summary>
    Task<IReadOnlyList<string>> MissingEventsAsync(Guid connectionId, IReadOnlyCollection<string> grantedScopes, CancellationToken cancellationToken = default);

    /// <summary>Conexão revogada (CA 3): último <c>app.disconnected</c> assinado e a inscrição <c>Ended</c>; pendentes viram <c>Skipped</c>. Não grava.</summary>
    Task EndForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>Conexão suspensa (CA 3): inscrição <c>Held</c>; entregas pendentes ficam <c>Held</c>, sem tentar. Não grava.</summary>
    Task HoldForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);
}


/// <summary>Segredo de assinatura em claro (nunca vai para log) e a versão dele.</summary>
public sealed record PendingWebhookSecret(string Secret, int Version);

/// <summary>
/// Reavaliação da conexão antes de cada envio (INT-02 CA 8) e concessão para a mudança de avisos (CA 4). Implementada pelo Identity: conexão
/// <c>Active</c>, autorizador ativo e o escopo na concessão ∩ permissões atuais do autorizador (ADR 0053 §6).
/// </summary>
public interface IConnectedAppAccess
{
    /// <summary>Null quando pode; senão o motivo em português ("Sem permissão para ver notas").</summary>
    Task<string?> DenyReasonAsync(Guid connectionId, string? requiredScope, CancellationToken cancellationToken = default);

    /// <summary>Escopos concedidos à conexão (vazio se não existe).</summary>
    Task<IReadOnlyList<string>> GrantedScopesAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Aviso público de saída (INT-02 CA 7): o módulo dono traduz o seu evento de contrato para o tipo do catálogo, com o <c>data</c> mínimo (ids,
/// número, situação, valor e datas; nunca nome, documento, contato, custo, imposto ou margem). Chamado dentro do consumidor (outbox + inbox): só
/// adiciona ao DbContext, na mesma transação.
/// </summary>
public interface IOutboundWebhookPublisher
{
    Task PublishAsync(OutboundWebhookMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Tipo público, id do recurso (para o <c>resource</c>), quando aconteceu e o <c>data</c> (chaves em snake_case do catálogo).</summary>
public sealed record OutboundWebhookMessage(string Type, Guid ResourceId, DateTimeOffset OccurredAt, IReadOnlyDictionary<string, object?> Data);
