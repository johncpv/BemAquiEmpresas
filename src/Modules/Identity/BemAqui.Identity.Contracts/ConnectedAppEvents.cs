using BemAqui.SharedKernel;

namespace BemAqui.Identity.Contracts;

/// <summary>Aplicativo conectado autorizado (INT-01): a conexão nova nasceu na troca do código. A inscrição de avisos já nasceu síncrona (INT-02).</summary>
public sealed record AppConnected(Guid ConnectionId, Guid AppId, string ClientId, Guid GrantorUserId, Guid? ReplacedConnectionId) : IIntegrationEvent;

/// <summary>Conexão revogada (Desconectar, revogação pelo aplicativo, reuso de credencial, substituição ou módulo desligado). Só ids (ADR 0037 §2): o motivo fica na conexão e na auditoria. Os consumidores só encerram.</summary>
public sealed record AppConnectionRevoked(Guid ConnectionId, Guid AppId) : IIntegrationEvent;

/// <summary>Conexão suspensa (autorizador inativo ou autorização vencida). Os consumidores só suspendem.</summary>
public sealed record AppConnectionSuspended(Guid ConnectionId, Guid AppId) : IIntegrationEvent;
