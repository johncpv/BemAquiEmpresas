using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>
/// Regra que torna o 2FA obrigatório para um usuário (FND-05 CA 9). Basta uma regra dizer sim. Esta story registra
/// "o tenant exige para todos"; as FND-06b/06c registram "Proprietário" e "quem aprova ou paga".
/// </summary>
public interface ITwoFactorRequirementRule
{
    Task<bool> RequiresAsync(User user, CancellationToken cancellationToken);
}

/// <summary><c>security.require_2fa</c> ligado em <c>tenant_settings</c>: vale para todos os usuários do tenant.</summary>
public sealed class TenantRequiresTwoFactorRule(IIdentityDb db) : ITwoFactorRequirementRule
{
    public async Task<bool> RequiresAsync(User user, CancellationToken cancellationToken)
    {
        var value = await db.TenantSettings.Where(s => s.Key == TenantSetting.RequireTwoFactor).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        return bool.TryParse(value, out var required) && required;
    }
}

public sealed class TwoFactorRequirement(IEnumerable<ITwoFactorRequirementRule> rules)
{
    public async Task<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default)
    {
        foreach (var rule in rules)
        {
            if (await rule.RequiresAsync(user, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Membro de um grupo ativo com "Exigir verificação em duas etapas" (FND-06b CA 12).</summary>
public sealed class GroupRequiresTwoFactorRule(IIdentityDb db) : ITwoFactorRequirementRule
{
    public Task<bool> RequiresAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return db.UserGroups.Where(ug => ug.UserId == user.Id)
            .Join(db.Groups.Where(g => g.IsActive && g.RequiresTwoFactor), ug => ug.GroupId, g => g.Id, (ug, _) => ug)
            .AnyAsync(cancellationToken);
    }
}

/// <summary>
/// Quem aprova ou paga, e o Proprietário, precisa de 2FA (FND-06c CA 12; revisão §2.3): a permissão efetiva inclui
/// alguma <c>*.approve</c> ou <c>*.pay</c>, ou uma ação da lista <c>ModuleCatalog.TwoFactorActions</c> (EXE-01a CA 26).
/// </summary>
public sealed class ApproverRequiresTwoFactorRule(BemAqui.BuildingBlocks.Identity.IPermissionService permissions) : ITwoFactorRequirementRule
{
    public async Task<bool> RequiresAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var effective = await permissions.GetAsync(user.Id, cancellationToken);
        return effective.IsOwner || effective.Permissions.Any(Requires);
    }

    /// <summary>Permissão que exige 2FA no login: <c>*.approve</c>, <c>*.pay</c> ou da lista <c>TwoFactorActions</c> do catálogo.</summary>
    public static bool Requires(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return permission.EndsWith(".approve", StringComparison.Ordinal) || permission.EndsWith(".pay", StringComparison.Ordinal)
            || BemAqui.BuildingBlocks.Modules.ModuleCatalog.TwoFactorActions.Contains(permission);
    }
}
