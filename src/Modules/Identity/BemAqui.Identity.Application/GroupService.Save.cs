using BemAqui.BuildingBlocks.Modules;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed partial class GroupService
{
    public async Task<Result<GroupSaved>> CreateAsync(SaveGroupRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var group = new Group { CreatedAt = clock.GetUtcNow() };
        var applied = await ApplyAsync(group, request, actorId, isNew: true, cancellationToken);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        db.Groups.Add(group);
        Audit(group.Id, "GroupCreated", actorId, new { name = group.Name, permissions = applied.Value.After });
        await NotifyOwnersAsync(group.Id, "Grupo " + group.Name + " criado.", actorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new GroupSaved(group.Id, 0);
    }

    public async Task<Result<GroupSaved>> UpdateAsync(Guid id, SaveGroupRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        if (group.IsSystem)
        {
            return SystemGroup();
        }

        if (await OwnGroupDeniedAsync(group.Id, actorId, cancellationToken) is { } own)
        {
            return own;
        }

        var applied = await ApplyAsync(group, request, actorId, isNew: false, cancellationToken);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        Audit(group.Id, "PermissionsChanged", actorId, new { before = applied.Value.Before, after = applied.Value.After });
        await NotifyOwnersAsync(group.Id, "Permissões do grupo " + group.Name + " alteradas.", actorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new GroupSaved(group.Id, await InvalidateMembersAsync(group.Id, cancellationToken));
    }

    /// <summary>"Duplicar" (CA 8): "Cópia de {nome}", mesmas permissões, sem membros. A delegação vale para a cópia.</summary>
    public async Task<Result<GroupSaved>> DuplicateAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        var source = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.IsSystem)
        {
            return SystemGroup();
        }

        var (enabled, actions) = await GrantsAsync(source.Id, cancellationToken);
        var name = "Cópia de " + source.Name;
        for (var n = 2; await db.Groups.AnyAsync(g => g.IsActive && g.Name == name, cancellationToken); n++)
        {
            name = "Cópia de " + source.Name + " (" + n + ")";
        }

        var entries = enabled.Select(m => new MatrixEntry(m, true, actions.Where(a => a.Module == m).Select(a => a.Action).ToList())).ToList();
        return await CreateAsync(new SaveGroupRequest(name, source.Description, source.RequiresTwoFactor, entries), actorId, cancellationToken);
    }

    /// <summary>Inativar (CA 9): as permissões do grupo deixam de contar no próximo request dos membros.</summary>
    public async Task<Result<GroupSaved>> DeactivateAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        if (group.IsSystem)
        {
            return SystemGroup();
        }

        if (await OwnGroupDeniedAsync(group.Id, actorId, cancellationToken) is { } own)
        {
            return own;
        }

        group.IsActive = false;
        await NotifyOwnersAsync(group.Id, "Grupo " + group.Name + " inativado.", actorId, cancellationToken);
        Audit(group.Id, "GroupDeactivated", actorId, new { before = true, after = false });
        await db.SaveChangesAsync(cancellationToken);
        return new GroupSaved(group.Id, await InvalidateMembersAsync(group.Id, cancellationToken));
    }

    private async Task<Result<(IReadOnlyList<string> Before, IReadOnlyList<string> After)>> ApplyAsync(Group group, SaveGroupRequest request, Guid actorId, bool isNew, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            return Error.Validation(Error.Codes.Validation, "Informe o nome do grupo (até 100 caracteres).", new Dictionary<string, string[]> { ["name"] = ["Informe o nome do grupo."] });
        }

        if (await db.Groups.AnyAsync(g => g.IsActive && g.Id != group.Id && g.Name == name, cancellationToken))
        {
            return Error.Conflict(Error.Codes.Conflict, "Já existe um grupo ativo com esse nome.");
        }

        var entries = new Dictionary<string, MatrixEntry>(StringComparer.Ordinal);
        foreach (var raw in request.Modules ?? [])
        {
            var module = ModuleCatalog.Find(raw.ModuleCode);
            var unknown = raw.Actions.FirstOrDefault(a => module?.HasAction(a) != true && a != GroupMatrix.View);
            if (module is null || unknown is not null)
            {
                return Error.Validation(Error.Codes.Validation, $"Ação ou módulo fora do catálogo: {raw.ModuleCode}{(unknown is null ? string.Empty : "." + unknown)}.");
            }

            var entry = GroupMatrix.Normalize(raw);
            if (entry.Enabled)
            {
                entries[entry.ModuleCode] = entry;
            }
        }

        var before = isNew ? [] : await PermissionsOfGroupAsync(group, cancellationToken);
        var after = entries.Values.SelectMany(e => e.Actions.Select(a => e.ModuleCode + "." + a)).ToHashSet(StringComparer.Ordinal);
        var changedModules = after.Concat(before).Select(p => p[..p.IndexOf('.', StringComparison.Ordinal)])
            .Where(m => after.Where(p => p.StartsWith(m + ".", StringComparison.Ordinal)).ToHashSet().SetEquals(before.Where(p => p.StartsWith(m + ".", StringComparison.Ordinal))) == false)
            .Distinct();
        var notReleased = changedModules.FirstOrDefault(m => tenant.Resolved?.IsReleased(m) != true);
        if (notReleased is not null)
        {
            return Error.Validation("module_not_released", "Módulo não liberado nesta versão: " + ModuleCatalog.Find(notReleased)!.Name + ".");
        }

        if (await DelegationDeniedAsync(actorId, after.Except(before), cancellationToken) is { } denied)
        {
            return denied;
        }

        group.Name = name;
        group.Description = request.Description?.Trim() ?? string.Empty;
        group.RequiresTwoFactor = request.RequiresTwoFactor;
        if (!isNew)
        {
            db.GroupModules.RemoveRange(await db.GroupModules.Where(m => m.GroupId == group.Id).ToListAsync(cancellationToken));
            db.GroupPermissions.RemoveRange(await db.GroupPermissions.Where(p => p.GroupId == group.Id).ToListAsync(cancellationToken));
        }

        foreach (var entry in entries.Values)
        {
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = entry.ModuleCode, Enabled = true });
            db.GroupPermissions.AddRange(entry.Actions.Select(a => new GroupGrant { GroupId = group.Id, ModuleCode = entry.ModuleCode, Action = a }));
        }

        return (before.Order(StringComparer.Ordinal).ToList(), after.Order(StringComparer.Ordinal).ToList());
    }
}
