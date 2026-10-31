using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

public sealed partial class EmployeeService
{
    /// <summary>Excluir (CA 18): só sem linha do tempo, sem marcação de ponto (RH-01a) e sem usuário vinculado.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (await db.EmployeeHistory.AnyAsync(h => h.EmployeeId == id, cancellationToken) || await db.TimeEntries.AnyAsync(t => t.EmployeeId == id, cancellationToken) || await links.UserOfEmployeeAsync(id, cancellationToken) is not null)
        {
            return Error.Conflict("employee_has_history", "Funcionário com histórico não pode ser excluído. Registre o desligamento.");
        }

        // PAT-01a CA 14: outro módulo guarda o id (ex.: responsável por bem do patrimônio): só desliga.
        foreach (var usage in usages)
        {
            if (await usage.UsageAsync(id, cancellationToken) is { } used)
            {
                return Error.Conflict("employee_in_use", $"Funcionário em uso ({used}) não pode ser excluído. Registre o desligamento.");
            }
        }

        db.Employees.Remove(employee);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<LinkedUserView>> LinkedUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return new LinkedUserView(await links.UserOfEmployeeAsync(id, cancellationToken));
    }

    /// <summary>Usuários ativos sem funcionário, para o vínculo (CA 19): exige também identity.edit.</summary>
    public async Task<Result<IReadOnlyList<LinkableUser>>> LinkableUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("identity", "edit", cancellationToken))
        {
            return LinkForbidden();
        }

        var users = await links.LinkableAsync(cancellationToken);
        return Result<IReadOnlyList<LinkableUser>>.Success(users);
    }

    /// <summary>Vincular (CA 19): hr.edit na rota e identity.edit aqui; um usuário por funcionário.</summary>
    public async Task<Result<LinkableUser>> LinkUserAsync(Guid id, UserLinkInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("identity", "edit", cancellationToken))
        {
            return LinkForbidden();
        }

        if (!await db.Employees.AnyAsync(e => e.Id == id && e.Status == EmployeeStatus.Active, cancellationToken))
        {
            return NotFound();
        }

        return await links.LinkAsync(input.UserId, id, cancellationToken);
    }

    public async Task<Result> UnlinkUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("identity", "edit", cancellationToken))
        {
            return LinkForbidden();
        }

        if (!await db.Employees.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return await links.UnlinkAsync(id, cancellationToken);
    }

    private static Error LinkForbidden() => Error.Forbidden(Error.Codes.Forbidden, "Vincular usuário exige também editar usuários e acessos.");
}
