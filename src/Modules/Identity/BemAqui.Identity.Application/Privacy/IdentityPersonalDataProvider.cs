using BemAqui.BuildingBlocks.Privacy;
using BemAqui.SharedKernel;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>
/// Titular usuário do sistema (LGPD-01a CA 1 e 9, ADR 0037 §1): nome, e-mail, situação, grupos, os acessos registrados (sessões: quando, de
/// qual IP, tipo e fim) e os convites. Hash de senha, segredo TOTP, código de recuperação e token nunca saem (CA 10, teste de canário). O
/// pedido do titular (<c>settings.privacy_requests</c>) também é dado pessoal do titular "não encontrado", coberto aqui.
/// </summary>
[PersonalDataCovers(typeof(User), typeof(UserInvite), typeof(PrivacyRequest))]
public sealed class IdentityPersonalDataProvider(IIdentityDb db) : IPersonalDataProvider
{
    public string Module => "identity";

    public string Title => "Usuário do sistema";

    public async Task<PersonalDataSubject?> ResolveAsync(PersonalDataSubjectKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        if (kind != PersonalDataSubjectKind.User)
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? null : new PersonalDataSubject(kind, id, user.Name.Length > 0 ? user.Name : user.Email, [user.NormalizedEmail], EmployeeId: user.EmployeeId, UserId: id);
    }

    public async Task<IReadOnlyList<PersonalDataSubjectMatch>> SearchAsync(PersonalDataSubjectKind kind, string term, CancellationToken cancellationToken = default)
    {
        if (kind != PersonalDataSubjectKind.User || string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return [];
        }

        var like = "%" + term.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var users = await db.Users.AsNoTracking().Where(u => EF.Functions.ILike(u.Name, like) || EF.Functions.ILike(u.Email, like))
            .OrderBy(u => u.Name).Take(20).Select(u => new { u.Id, u.Name, u.Email }).ToListAsync(cancellationToken);
        return users.Select(u => new PersonalDataSubjectMatch(kind, u.Id, u.Name.Length > 0 ? u.Name : PersonalDataMask.Email(u.Email)!, PersonalDataMask.Email(u.Email))).ToList();
    }

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Subject.UserId is not { } id)
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var groups = await (from ug in db.UserGroups.AsNoTracking()
                            join g in db.Groups.AsNoTracking() on ug.GroupId equals g.Id
                            where ug.UserId == id
                            orderby g.Name
                            select g.Name).ToListAsync(cancellationToken);
        var sessions = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == id).OrderBy(t => t.CreatedAt)
            .Select(t => new { t.CreatedAt, t.CreatedByIp, t.SessionKind, t.Host, t.ExpiresAt, t.RevokedAt }).ToListAsync(cancellationToken);
        var invites = await db.UserInvites.AsNoTracking().Where(i => i.UserId == id).OrderBy(i => i.CreatedAt)
            .Select(i => new { i.Email, i.CreatedAt, i.AcceptedAt, i.CanceledAt }).ToListAsync(cancellationToken);
        return new PersonalDataSection(Module, Title,
        [
            new PersonalDataBlock("Usuário", "identity.users", [PersonalDataRecord.Of(
                ("Nome", user.Name), ("E-mail", user.Email), ("Situação", user.Status switch { UserStatus.Active => "Ativo", UserStatus.Inactive => "Desativado", _ => "Convidado" }),
                ("Verificação em duas etapas", PersonalDataMask.YesNo(user.TotpEnabled)), ("Último acesso", PersonalDataMask.At(user.LastLoginAt)),
                ("Grupos", string.Join(", ", groups)))]),
            new PersonalDataBlock("Acessos registrados", "identity.refresh_tokens", sessions.Select(s => PersonalDataRecord.Of(s.CreatedAt,
                ("Entrada em", PersonalDataMask.At(s.CreatedAt)), ("IP", s.CreatedByIp), ("Tipo", s.SessionKind == SessionKind.Pin ? "PIN no aparelho da fábrica" : "E-mail e senha"),
                ("Endereço", s.Host), ("Encerrada em", PersonalDataMask.At(s.RevokedAt)))).ToList()),
            new PersonalDataBlock("Convites", "identity.user_invites", invites.Select(i => PersonalDataRecord.Of(i.CreatedAt,
                ("E-mail", i.Email), ("Enviado em", PersonalDataMask.At(i.CreatedAt)), ("Aceito em", PersonalDataMask.At(i.AcceptedAt)), ("Cancelado em", PersonalDataMask.At(i.CanceledAt)))).ToList()),
        ]);
    }
}
