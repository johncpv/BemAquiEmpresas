namespace BemAqui.SharedKernel;

/// <summary>
/// Evento entre módulos (ex.: <c>ContractSigned</c>), declarado no <c>*.Contracts</c> do módulo que publica.
/// Vai para o outbox na mesma transação do comando e é entregue depois, com retry e idempotência (ADR 0006).
/// </summary>
public interface IIntegrationEvent;
