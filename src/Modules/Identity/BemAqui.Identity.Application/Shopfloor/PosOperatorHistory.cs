using BemAqui.BuildingBlocks.Identity;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>
/// Sessões por PIN do caixa e retratos das permissões (OFF-02). A sessão por PIN é a família de refresh tokens <c>PosPin</c> do usuário no
/// aparelho: começa no primeiro token e termina na primeira revogação (sair, PIN apagado, usuário desativado, reuso) ou no fim das 12 h.
/// </summary>
public sealed class PosOperatorHistory(IIdentityDb db, IPermissionService permissions) : IPosOperatorHistory
{
    /// <summary>Folga de relógio do aparelho (ADR 0026: 2 min).</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(2);

    public async Task<bool> HadPinSessionAsync(Guid userId, Guid deviceId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        var latest = at + ClockTolerance;
        var tokens = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.DeviceId == deviceId && t.SessionKind == SessionKind.PosPin && t.CreatedAt <= latest)
            .Select(t => new { t.FamilyId, t.CreatedAt, t.RevokedAt, t.AbsoluteExpiresAt })
            .ToListAsync(cancellationToken);
        foreach (var family in tokens.GroupBy(t => t.FamilyId))
        {
            var start = family.Min(t => t.CreatedAt);
            var revoked = family.Where(t => t.RevokedAt is not null).Select(t => t.RevokedAt!.Value).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            var expires = family.Select(t => t.AbsoluteExpiresAt ?? start + RefreshToken.PinSessionLifetime).Max();
            var end = revoked < expires ? revoked : expires;
            if (start - ClockTolerance <= at && at <= end + ClockTolerance)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> HadPermissionAsync(Guid userId, string permission, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        if (userId == Guid.Empty)
        {
            return false;
        }

        var snapshot = await SnapshotAtAsync(userId, at, cancellationToken);
        if (snapshot is null)
        {
            // Nenhum retrato ainda: avaliar agora grava o da versão atual, que passa a ser o mais antigo conhecido.
            await permissions.GetAsync(userId, cancellationToken);
            snapshot = await SnapshotAtAsync(userId, at, cancellationToken);
        }

        return snapshot?.Has(permission) == true;
    }

    /// <summary>O retrato que valia no instante; sem retrato anterior, o mais antigo (a melhor informação que existe sobre aquele momento).</summary>
    private async Task<PermissionSnapshot?> SnapshotAtAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken) =>
        await db.PermissionSnapshots.AsNoTracking().Where(s => s.UserId == userId && s.ValidFrom <= at).OrderByDescending(s => s.ValidFrom).FirstOrDefaultAsync(cancellationToken)
        ?? await db.PermissionSnapshots.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.ValidFrom).FirstOrDefaultAsync(cancellationToken);
}
