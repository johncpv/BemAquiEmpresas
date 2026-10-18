using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Cache do security stamp: quem muda o stamp avisa, e o efeito vale no próximo request.</summary>
public interface ISecurityStampCache
{
    void Invalidate(Guid tenantId, Guid userId);
}

/// <summary>Desativação de usuário (a tela é da FND-06c): derruba as sessões já no próximo request (FND-04 CA 16).</summary>
public sealed class UserLifecycle(IIdentityDb db, AuthService auth, ISecurityStampCache stamps, ITenantContext tenant, TimeProvider clock)
{
    public async Task<Result> DeactivateAsync(Guid userId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Usuário não encontrado.");
        }

        user.Deactivate();
        db.AuditEntries.Add(new AuditEntry { Entity = "Auth", EntityId = userId.ToString(), Action = "Deactivated", UserId = byUserId, At = clock.GetUtcNow(), Changes = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        await auth.RevokeAllAsync(userId, cancellationToken);
        stamps.Invalidate(tenant.Tenant?.Id ?? Guid.Empty, userId);
        return Result.Success();
    }
}
