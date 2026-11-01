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
public sealed class IdentityPersonalDataProvider(
    IIdentityDb db, BemAqui.BuildingBlocks.Identity.IPermissionService permissions, ISecurityStampCache stamps, BemAqui.BuildingBlocks.Tenancy.ITenantContext tenant, TimeProvider clock)
    : IPersonalDataProvider
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

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 4: o usuário titular fica bloqueado (desativado, sem senha, sem 2FA, fora dos grupos e sem vínculo com funcionário), com o
    /// nome trocado pelo marcador e o e-mail por um endereço <c>.invalid</c>; as sessões são encerradas e o IP delas sai; os convites ficam com
    /// o mesmo e-mail trocado. O último membro do Proprietário não pode ser anonimizado (o grupo não fica sem membros). Só o tipo usuário: o
    /// funcionário ligado é outro titular (pedido próprio).
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Subject.Kind != PersonalDataSubjectKind.User || context.Subject.UserId is not { } id)
        {
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return null;
        }

        var plan = new PersonalDataPlanBuilder(Module, Title);
        var owner = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).FirstOrDefaultAsync(ct);
        var memberships = await db.UserGroups.Where(ug => ug.UserId == id).ToListAsync(ct);
        if (memberships.Any(m => m.GroupId == owner))
        {
            var others = await (from ug in db.UserGroups
                                join u in db.Users on ug.UserId equals u.Id
                                where ug.GroupId == owner && ug.UserId != id && u.Status == UserStatus.Active
                                select ug.UserId).AnyAsync(ct);
            if (!others)
            {
                plan.Block("O usuário é o último membro ativo do Proprietário. Inclua outro membro no grupo Proprietário antes de anonimizar.");
            }
        }

        var sessions = await db.RefreshTokens.Where(t => t.UserId == id).ToListAsync(ct);
        var invites = await db.UserInvites.Where(i => i.UserId == id).ToListAsync(ct);
        var decision = context.Decide(RetentionGroups.Account);
        if (apply && !decision.Retained)
        {
            var now = clock.GetUtcNow();
            var email = PersonalDataMarker.Email(context.RequestNumber, id);
            user.Anonymize(context.Marker, context.RequestNumber, now);
            db.Entry(user).Property(u => u.Email).CurrentValue = email;
            db.Entry(user).Property(u => u.NormalizedEmail).CurrentValue = email;
            foreach (var session in sessions)
            {
                session.Revoke(now);
                db.Entry(session).Property(t => t.CreatedByIp).CurrentValue = null;
            }

            foreach (var invite in invites)
            {
                db.Entry(invite).Property(i => i.Email).CurrentValue = email;
                invite.CanceledAt ??= invite.AcceptedAt is null ? now : null;
                invite.TokenHash = string.Empty;
            }

            db.UserGroups.RemoveRange(memberships);
            await db.SaveChangesAsync(ct);
            await permissions.BumpAsync([id], ct);
            stamps.Invalidate(tenant.Tenant?.Id ?? Guid.Empty, id);
        }

        return plan.Fields(decision, "Nome, e-mail, senha e verificação em duas etapas do usuário", "identity.users", 1)
            .Fields(decision, "Sessões (encerradas, sem o IP)", "identity.refresh_tokens", sessions.Count)
            .Fields(decision, "Convites", "identity.user_invites", invites.Count)
            .Keep("Usuário: fica bloqueado e marcado \"Anonimizado\"; o que ele fez continua na auditoria pelo id", "identity.users", 1)
            .Build();
    }
}
