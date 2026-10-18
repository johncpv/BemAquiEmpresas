namespace BemAqui.BuildingBlocks.Identity;

/// <summary>
/// Permissão efetiva do usuário no tenant do escopo (FND-06b CA 17, <c>04-fluxos</c> §3.7): união das ações dos grupos
/// ativos com módulo habilitado, limitada aos módulos liberados; o Proprietário tem tudo o que está liberado.
/// </summary>
public interface IPermissionService
{
    Task<EffectivePermissions> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Alteração de grupo, vínculo ou usuário: sobe a versão; o próximo request recalcula (ADR 0008).</summary>
    Task BumpAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}

/// <summary>Permissões <c>modulo.acao</c> efetivas; <see cref="IsOwner"/> marca membro do grupo Proprietário.</summary>
/// <see cref="Version"/> vai no cabeçalho <c>X-Perm-Version</c>: mudou, o front recarrega <c>/me/permissions</c> (FND-06c CA 15).
public sealed record EffectivePermissions(IReadOnlySet<string> Permissions, bool IsOwner, string Version = "0")
{
    public const string Header = "X-Perm-Version";

    public static EffectivePermissions None { get; } = new(new HashSet<string>(StringComparer.Ordinal), false);

    public bool Has(string permission) => Permissions.Contains(permission);
}
