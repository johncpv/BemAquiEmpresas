namespace BemAqui.Identity.Domain;

/// <summary>
/// Retrato das permissões de um usuário numa versão (<c>identity.permission_snapshots</c>, OFF-02): o que o grupo dava a ele a partir de
/// <see cref="ValidFrom"/>. Serve para conferir, na subida dos eventos do caixa, se quem fez ou autorizou a ação tinha a permissão <b>no instante
/// do evento</b> (e não só hoje). Nasce quando a versão é avaliada pela primeira vez (cache das permissões) e logo depois de cada mudança de grupo
/// ou de vínculo. Nunca é alterado. Não é dado pessoal: só o id do usuário e os códigos das ações.
/// </summary>
public sealed class PermissionSnapshot
{
    public Guid UserId { get; init; }

    public int PermVersion { get; init; }

    public DateTimeOffset ValidFrom { get; init; }

    /// <summary>Do grupo Proprietário: tem todas as ações dos módulos liberados.</summary>
    public bool IsOwner { get; init; }

    /// <summary>Ações <c>modulo.acao</c> dadas pelos grupos ativos do usuário naquela versão.</summary>
    public List<string> Permissions { get; init; } = [];

    public bool Has(string permission) => IsOwner || Permissions.Contains(permission, StringComparer.Ordinal);
}
