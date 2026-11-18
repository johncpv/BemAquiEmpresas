using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application.Shopfloor;
using BemAqui.Identity.Domain;
using BemAqui.Pos.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>
/// Novo usuário de operação (ADR 0049): nome, grupos (sem ação reservada, com <c>pos.create</c>), as lojas onde trabalha, o funcionário (opcional) e o
/// PIN inicial, que ele troca no primeiro uso. Sem e-mail e sem senha.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOperationUserRequest(string Name, IReadOnlyList<Guid> GroupIds, IReadOnlyList<Guid> StoreIds, string Pin, string PinConfirmation, Guid? EmployeeId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateOperationUserRequest(string Name, IReadOnlyList<Guid> StoreIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResetOperationPinRequest(string Pin, string PinConfirmation);

public sealed record OperationUserCreated(Guid Id);

/// <summary>Loja que o usuário de operação pode escolher.</summary>
public sealed record OperationStoreOption(Guid Id, string Code, string Name);

/// <summary>
/// Usuários de operação (PDV-01 CA 12, ADR 0049): quem tem <c>identity.create</c> (o gerente) cria, troca as lojas e dá um PIN novo. Só no perfil
/// Comércio (recurso <c>identity.operation_users</c>) e com o <c>pos</c> liberado. A delegação da ADR 0008 vale nos grupos, e grupo com ação reservada
/// não aceita usuário de operação. Tudo auditado: criação, PIN inicial e PIN novo (sem o valor), troca de loja; a troca de grupo é a de sempre.
/// </summary>
public sealed class OperationUserService(
    IIdentityDb db,
    GroupService groups,
    UserService users,
    PosPinService pins,
    IPermissionService permissions,
    IPosRegisters registers,
    TenantContextHolder tenant,
    TimeProvider clock)
{
    public const string NotOperationUserMessage = "Este usuário não é de operação.";

    public async Task<Result<IReadOnlyList<OperationStoreOption>>> StoresAsync(CancellationToken cancellationToken = default)
    {
        if (NotReleased() is { } error)
        {
            return error;
        }

        return (await registers.ListStoresAsync(cancellationToken)).Where(s => s.IsActive).Select(s => new OperationStoreOption(s.Id, s.Code, s.Name)).ToList();
    }

    public async Task<Result<OperationUserCreated>> CreateAsync(CreateOperationUserRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (NotReleased() is { } notReleased)
        {
            return notReleased;
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 200)
        {
            return Invalid("name", "Informe o nome do operador (até 200 letras).");
        }

        if (await StoresErrorAsync(request.StoreIds, cancellationToken) is { } stores)
        {
            return stores;
        }

        var groupIds = (request.GroupIds ?? []).Distinct().ToList();
        if (groupIds.Count == 0)
        {
            return Invalid("groupIds", "Escolha o grupo do operador, por exemplo Operador de caixa.");
        }

        if (await groups.GroupGrantDeniedAsync(actorId, groupIds, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (await groups.ReservedActionOfGroupsAsync(groupIds, cancellationToken) is { } reserved)
        {
            return GroupService.OperationUserDenied(reserved);
        }

        if (!await GroupsSellAsync(groupIds, cancellationToken))
        {
            return Invalid("groupIds", "Nenhum dos grupos vende no caixa. Escolha um grupo com Caixa → Vender, por exemplo Operador de caixa.");
        }

        if (request.EmployeeId is { } employeeId && await users.EmployeeLinkErrorAsync(employeeId, cancellationToken) is { } link)
        {
            return link;
        }

        var user = User.CreateOperation(name, request.StoreIds!, request.EmployeeId);
        if (await pins.PinProblemAsync(user, request.Pin, request.PinConfirmation, cancellationToken) is { } pin)
        {
            return pin;
        }

        db.Users.Add(user);
        db.UserGroups.AddRange(groupIds.Select(g => new UserGroup { UserId = user.Id, GroupId = g }));
        Audit(user.Id, "OperationUserCreated", actorId, new { name, groups = groupIds, stores = user.OperationStoreIds, employeeId = request.EmployeeId });
        await pins.UpsertAsync(user, request.Pin, mustChange: true, actorId, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("employee_already_linked", UserService.EmployeeAlreadyLinkedMessage);
        }

        await permissions.BumpAsync([user.Id], cancellationToken);
        return new OperationUserCreated(user.Id);
    }

    /// <summary>Nome e lojas do usuário de operação; a sessão por PIN numa loja que saiu cai no próximo request.</summary>
    public async Task<Result> UpdateAsync(Guid id, UpdateOperationUserRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (NotReleased() is { } notReleased)
        {
            return notReleased;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null || !user.IsOperationUser)
        {
            return Error.NotFound(Error.Codes.NotFound, NotOperationUserMessage);
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 200)
        {
            return Invalid("name", "Informe o nome do operador (até 200 letras).");
        }

        if (await StoresErrorAsync(request.StoreIds, cancellationToken) is { } stores)
        {
            return stores;
        }

        if (await TargetDelegationDeniedAsync(user.Id, actorId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var before =new { name = user.Name, stores = user.OperationStoreIds.ToList() };
        user.Name = name;
        user.SetOperationStores(request.StoreIds!);
        Audit(user.Id, "OperationUserChanged", actorId, new { before, after = new { name, stores = user.OperationStoreIds } });
        await db.SaveChangesAsync(cancellationToken);
        await permissions.BumpAsync([user.Id], cancellationToken);
        return Result.Success();
    }

    /// <summary>"Novo PIN" (o operador esqueceu ou o PIN foi apagado): vale só para a próxima entrada, que pede a troca; a sessão no caixa cai.</summary>
    public async Task<Result> ResetPinAsync(Guid id, ResetOperationPinRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (NotReleased() is { } notReleased)
        {
            return notReleased;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null || !user.IsOperationUser)
        {
            return Error.NotFound(Error.Codes.NotFound, NotOperationUserMessage);
        }

        if (user.Status != UserStatus.Active)
        {
            return Error.Validation("user_not_active", "Reative o usuário antes de dar um PIN novo.");
        }

        if (await TargetDelegationDeniedAsync(user.Id, actorId, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (await pins.PinProblemAsync(user, request.Pin, request.PinConfirmation, cancellationToken) is { } pin)
        {
            return pin;
        }

        await pins.EraseAsync(user.Id, "reset", cancellationToken);
        await pins.UpsertAsync(user, request.Pin, mustChange: true, actorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Delegação sobre o operador (ADR 0008, revisão de segurança da PDV-01, M1): dar PIN novo ou trocar as lojas equivale a poder entrar no caixa
    /// como ele, então o ator precisa ter tudo o que os grupos atuais do operador dão (<see cref="GroupService.GroupGrantDeniedAsync"/>) e, como na
    /// troca de grupos (<see cref="UserService.SetGroupsAsync"/>), não pode ser membro desses grupos, salvo o Proprietário. Grupo inativo não dá
    /// permissão e fica fora da conta da delegação.
    /// </summary>
    private async Task<Error?> TargetDelegationDeniedAsync(Guid userId, Guid actorId, CancellationToken cancellationToken)
    {
        var current = await db.UserGroups.Where(ug => ug.UserId == userId).Select(ug => ug.GroupId).ToListAsync(cancellationToken);
        if (current.Count == 0)
        {
            return null;
        }

        if (!(await permissions.GetAsync(actorId, cancellationToken)).IsOwner
            && await db.UserGroups.AnyAsync(ug => ug.UserId == actorId && current.Contains(ug.GroupId), cancellationToken))
        {
            return Error.Forbidden(GroupService.DelegationDeniedCode, "Você não pode alterar um grupo do qual faz parte. Peça a alguém do grupo Proprietário.");
        }

        var active = await db.Groups.Where(g => current.Contains(g.Id) && g.IsActive).Select(g => g.Id).ToListAsync(cancellationToken);
        return await groups.GroupGrantDeniedAsync(actorId, active, cancellationToken);
    }

    private Error? NotReleased() =>
        tenant.Resolved?.IsReleased("pos") == true ? null : Error.Forbidden("module_not_released", ModuleEndpoints.NotReleasedMessage);

    private async Task<Error?> StoresErrorAsync(IReadOnlyList<Guid>? storeIds, CancellationToken cancellationToken)
    {
        var wanted = (storeIds ?? []).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return Invalid("storeIds", "Escolha ao menos uma loja onde o operador trabalha.");
        }

        var active = (await registers.ListStoresAsync(cancellationToken)).Where(s => s.IsActive).Select(s => s.Id).ToHashSet();
        return wanted.All(active.Contains) ? null : Invalid("storeIds", "Escolha lojas ativas da lista.");
    }

    private async Task<bool> GroupsSellAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        return await db.GroupPermissions.Where(p => groupIds.Contains(p.GroupId) && p.ModuleCode == "pos" && p.Action == "create")
            .Join(db.GroupModules.Where(m => m.ModuleCode == "pos" && m.Enabled), p => p.GroupId, m => m.GroupId, (p, _) => p)
            .AnyAsync(cancellationToken);
    }

    private void Audit(Guid userId, string action, Guid actorId, object changes) =>
        db.AuditEntries.Add(new AuditEntry { Entity = "User", EntityId = userId.ToString(), Action = action, UserId = actorId, At = clock.GetUtcNow(), Changes = JsonSerializer.Serialize(changes) });

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
