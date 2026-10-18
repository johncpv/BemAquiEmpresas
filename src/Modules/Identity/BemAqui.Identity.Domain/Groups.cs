using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>
/// Grupo de acesso (<c>identity.groups</c>, FND-06b). Sem exclusão: inativa (há histórico). O Proprietário é do
/// sistema: tem tudo o que está liberado, não grava linhas de permissão e não pode ser restringido.
/// </summary>
public sealed class Group : Entity, IAuditable
{
    public const string OwnerSeedKey = "owner";

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsSystem { get; init; }

    public bool IsActive { get; set; } = true;

    /// <summary>Membros sem 2FA são levados à configuração no próximo login (FND-05, regra por grupo).</summary>
    public bool RequiresTwoFactor { get; set; }

    /// <summary>Marca os grupos criados pelo seed: o seed não recria um grupo que o tenant renomeou ou inativou.</summary>
    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsOwner => IsSystem && SeedKey == OwnerSeedKey;
}

/// <summary>Módulo habilitado no grupo (<c>identity.group_modules</c>): camada 2 do controle de acesso.</summary>
public sealed class GroupModule : Entity
{
    public Guid GroupId { get; init; }

    public required string ModuleCode { get; init; }

    public bool Enabled { get; set; }
}

/// <summary>Ação marcada no grupo (<c>identity.group_permissions</c>): camada 3; só vale com o módulo habilitado.</summary>
public sealed class GroupGrant : Entity
{
    public Guid GroupId { get; init; }

    public required string ModuleCode { get; init; }

    public required string Action { get; init; }
}

/// <summary>Usuário em grupo (<c>identity.user_groups</c>, N:N). Permissão efetiva = união dos grupos ativos.</summary>
public sealed class UserGroup : Entity
{
    public Guid UserId { get; init; }

    public Guid GroupId { get; init; }
}

/// <summary>Linha da matriz de um grupo: módulo, chave "Habilitado" e ações marcadas.</summary>
public sealed record MatrixEntry(string ModuleCode, bool Enabled, IReadOnlyCollection<string> Actions);

/// <summary>Regras da matriz (FND-06b CA 1 a 3), aplicadas no servidor como fonte da verdade.</summary>
public static class GroupMatrix
{
    public const string View = "view";

    /// <summary>
    /// Qualquer ação marcada liga "Ver" e "Habilitado" (CA 3); habilitar sem ações marca "Ver" (CA 1); sem nenhuma ação
    /// o módulo fica desabilitado (CA 2, desmarcar "Ver" limpa tudo no cliente).
    /// </summary>
    public static MatrixEntry Normalize(MatrixEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var actions = entry.Actions.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (actions.Count > 0 || entry.Enabled)
        {
            actions.Add(View);
            return entry with { Enabled = true, Actions = actions.Order(StringComparer.Ordinal).ToList() };
        }

        return entry with { Enabled = false, Actions = [] };
    }
}
