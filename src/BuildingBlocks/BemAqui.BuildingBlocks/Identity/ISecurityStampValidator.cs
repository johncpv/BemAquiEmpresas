namespace BemAqui.BuildingBlocks.Identity;

/// <summary>
/// O security stamp do token ainda é o do usuário? Muda ao desativar, redefinir senha ou mexer no 2FA: o
/// próximo request com o token antigo recebe 401 (ADR 0008, FND-04 CA 16). Implementado pelo módulo Identity.
/// </summary>
public interface ISecurityStampValidator
{
    Task<bool> IsCurrentAsync(Guid userId, string stamp, CancellationToken cancellationToken = default);
}
