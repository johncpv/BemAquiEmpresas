using BemAqui.SharedKernel;

namespace BemAqui.Identity.Contracts;

/// <summary>Usuário aceitou o convite e passou a ter acesso (FND-04 CA 17). <c>IsOwner</c> marca o proprietário do provisionamento.</summary>
public sealed record UserActivated(Guid UserId, bool IsOwner) : IIntegrationEvent;

/// <summary>% de encargos estimados mudou (CAD-06 CA 16): a CAD-04a recalcula o custo-hora dos cargos.</summary>
public sealed record PayrollChargesChanged(Guid TenantId, decimal OldPct, decimal NewPct, DateTimeOffset ChangedAt) : IIntegrationEvent;
