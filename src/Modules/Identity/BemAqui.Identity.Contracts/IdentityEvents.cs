using BemAqui.SharedKernel;

namespace BemAqui.Identity.Contracts;

/// <summary>Usuário aceitou o convite e passou a ter acesso (FND-04 CA 17). <c>IsOwner</c> marca o proprietário do provisionamento.</summary>
public sealed record UserActivated(Guid UserId, bool IsOwner) : IIntegrationEvent;
