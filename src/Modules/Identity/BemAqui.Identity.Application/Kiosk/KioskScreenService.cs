using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Kiosk;

/// <summary>
/// Linha de "Painel da TV" (CA 2): nome, linha (null = todas), visões, rotação, redes, quem criou e quando, último acesso, sessões
/// ativas e situação (<c>AwaitingConnection</c>, <c>Active</c>, <c>Revoked</c>). <see cref="NeedsNewLink"/>: pareada, mas sem sessão
/// viva (cookie apagado ou vencido): o propositivo oferece "Gerar novo link". Nunca leva o token nem o hash; só o prefixo.
/// </summary>
public sealed record KioskScreenRow(
    Guid Id,
    string Name,
    string? Line,
    IReadOnlyList<string> Views,
    int RotationSeconds,
    IReadOnlyList<string> AllowedNetworks,
    string TokenPrefix,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    int ActiveSessions,
    string Status,
    bool NeedsNewLink,
    DateTimeOffset? RevokedAt,
    string? RevokedByName,
    uint Version);

/// <summary>Lista e o que o formulário oferece: as linhas dos centros de trabalho ativos e as visões.</summary>
public sealed record KioskScreenList(IReadOnlyList<KioskScreenRow> Screens, IReadOnlyList<string> Lines, IReadOnlyList<string> Views);

/// <summary>Criar ou editar uma tela (CA 3 e 4). <c>Version</c> é obrigatória na edição (xmin).</summary>
public sealed record KioskScreenInput(string Name, string? Line, IReadOnlyList<string>? Views, int? RotationSeconds, IReadOnlyList<string>? AllowedNetworks, uint? Version = null);

/// <summary>Link mostrado uma única vez (criar e "Gerar novo link"): <c>https://{host}/tv#{token}</c>.</summary>
public sealed record KioskScreenLink(KioskScreenRow Screen, string Link);

/// <summary>Revogar exige a versão que a pessoa viu (xmin).</summary>
public sealed record KioskRevokeInput(uint Version);

/// <summary>
/// Telas de TV (PNL-01a CA 2, 3, 4, 6 e 14; ADR 0030 §1 e §4): criar e editar (<c>kiosk.create</c> e <c>production.view</c>), gerar novo
/// link (troca o token e derruba as sessões), revogar (<c>kiosk.delete</c>, a TV desliga em menos de 1 minuto). Cada mudança é auditada
/// pelo interceptor (sem o token nem o hash) e avisa o Proprietário.
/// </summary>
public sealed class KioskScreenService(
    IIdentityDb db,
    IWorkCenters workCenters,
    KioskNotices notices,
    KioskSessionVersions versions,
    IKioskRealtime realtime,
    ITenantContext tenant,
    TimeProvider clock)
{
    public async Task<KioskScreenList> ListAsync(CancellationToken cancellationToken = default)
    {
        var screens = await db.KioskScreens.AsNoTracking().OrderBy(s => s.RevokedAt != null).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        var rows = await RowsAsync(screens, cancellationToken);
        return new KioskScreenList(rows, await LinesAsync(cancellationToken), KioskViews.All);
    }

    public async Task<Result<KioskScreenLink>> CreateAsync(KioskScreenInput input, string host, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var valid = await ValidateAsync(input, null, cancellationToken);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, line, views, rotation, networks) = valid.Value;
        var token = KioskRules.NewToken();
        var screen = KioskScreen.Create(name, line, views, rotation, networks, token.Hash, token.Prefix, actorId, clock.GetUtcNow());
        db.KioskScreens.Add(screen);
        await notices.NotifyOwnersAsync($"Nova tela de TV {name} criada por {await notices.WhoAsync(actorId, cancellationToken)}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new KioskScreenLink(await RowAsync(screen, cancellationToken), KioskRules.Link(host, token.Value));
    }

    /// <summary>Editar (CA 4): grava, avisa o Proprietário e as TVs conectadas recebem a configuração nova, sem trocar o token.</summary>
    public async Task<Result<KioskScreenRow>> UpdateAsync(Guid id, KioskScreenInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var screen = await db.KioskScreens.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (screen is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Tela não encontrada.");
        }

        if (screen.IsRevoked)
        {
            return Error.Conflict("kiosk_revoked", "Esta tela foi revogada. Crie uma tela nova.");
        }

        if (input.Version is not { } version)
        {
            return Error.Validation(Error.Codes.Validation, "Recarregue a tela antes de salvar.");
        }

        var valid = await ValidateAsync(input, id, cancellationToken);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        db.Entry(screen).Property(s => s.Version).OriginalValue = version;
        var (name, line, views, rotation, networks) = valid.Value;
        screen.Configure(name, line, views, rotation, networks);
        await notices.NotifyOwnersAsync($"Tela de TV {name} alterada por {await notices.WhoAsync(actorId, cancellationToken)}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        Changed(screen.Id);
        if (tenant.Tenant?.Id is { } tenantId)
        {
            await realtime.ConfigChangedAsync(tenantId, screen.Id, cancellationToken);
        }

        return await RowAsync(screen, cancellationToken);
    }

    /// <summary>"Gerar novo link" (CA 6): token novo (o antigo deixa de valer), as sessões da tela caem e o link aparece uma única vez.</summary>
    public async Task<Result<KioskScreenLink>> NewLinkAsync(Guid id, uint version, string host, Guid actorId, CancellationToken cancellationToken = default)
    {
        var screen = await db.KioskScreens.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (screen is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Tela não encontrada.");
        }

        if (screen.IsRevoked)
        {
            return Error.Conflict("kiosk_revoked", "Esta tela foi revogada. Crie uma tela nova.");
        }

        db.Entry(screen).Property(s => s.Version).OriginalValue = version;
        var token = KioskRules.NewToken();
        screen.ReplaceToken(token.Hash, token.Prefix);
        var ended = await RevokeSessionsAsync(screen.Id, cancellationToken);
        notices.Audit(screen.Id, "KioskLinkReplaced", new { screen = screen.Id, sessions = ended });
        await notices.NotifyOwnersAsync($"Novo link da tela de TV {screen.Name} gerado por {await notices.WhoAsync(actorId, cancellationToken)}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        Changed(screen.Id);
        if (tenant.Tenant?.Id is { } tenantId)
        {
            await realtime.EndedAsync(tenantId, screen.Id, KioskEndReason.SessionEnded, notices.CompanyName, cancellationToken);
        }

        return new KioskScreenLink(await RowAsync(screen, cancellationToken), KioskRules.Link(host, token.Value));
    }

    /// <summary>
    /// Revogar (CA 14): a tela fica revogada (quem e quando), as sessões caem, a TV conectada recebe <c>KioskRevoked</c> e a conexão é
    /// fechada; a desconectada cai na próxima conferência (cache invalidado aqui). O Proprietário é avisado.
    /// </summary>
    public async Task<Result<KioskScreenRow>> RevokeAsync(Guid id, uint version, Guid actorId, CancellationToken cancellationToken = default)
    {
        var screen = await db.KioskScreens.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (screen is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Tela não encontrada.");
        }

        if (screen.IsRevoked)
        {
            return Error.Conflict("kiosk_already_revoked", "Esta tela já foi revogada.");
        }

        db.Entry(screen).Property(s => s.Version).OriginalValue = version;
        screen.Revoke(actorId, clock.GetUtcNow());
        await RevokeSessionsAsync(screen.Id, cancellationToken);
        await notices.NotifyOwnersAsync($"Tela de TV {screen.Name} revogada por {await notices.WhoAsync(actorId, cancellationToken)}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        Changed(screen.Id);
        if (tenant.Tenant?.Id is { } tenantId)
        {
            await realtime.EndedAsync(tenantId, screen.Id, KioskEndReason.Revoked, notices.CompanyName, cancellationToken);
        }

        return await RowAsync(screen, cancellationToken);
    }

    internal async Task<int> RevokeSessionsAsync(Guid screenId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.KioskSessions.Where(s => s.KioskId == screenId && s.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now);
        }

        return sessions.Count;
    }

    private void Changed(Guid screenId)
    {
        if (tenant.Tenant?.Id is { } tenantId)
        {
            versions.Bump(tenantId, screenId); // a conferência em cache da sessão relê o banco
        }
    }

    private async Task<Result<(string Name, string? Line, string[] Views, int Rotation, string[] Networks)>> ValidateAsync(KioskScreenInput input, Guid? id, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string[]>();
        if (KioskRules.NameProblem(input.Name) is { } nameProblem)
        {
            fields["name"] = [nameProblem];
        }

        var name = (input.Name ?? string.Empty).Trim();
        var rotation = input.RotationSeconds ?? KioskScreen.DefaultRotationSeconds;
        if (KioskRules.RotationProblem(rotation) is { } rotationProblem)
        {
            fields["rotationSeconds"] = [rotationProblem];
        }

        var (views, viewsProblem) = KioskRules.NormalizeViews(input.Views);
        if (viewsProblem is not null)
        {
            fields["views"] = [viewsProblem];
        }

        var (networks, networksProblem) = KioskRules.NormalizeNetworks(input.AllowedNetworks);
        if (networksProblem is not null)
        {
            fields["allowedNetworks"] = [networksProblem];
        }

        var line = string.IsNullOrWhiteSpace(input.Line) ? null : input.Line.Trim();
        if (line is not null && !(await LinesAsync(cancellationToken)).Contains(line, StringComparer.Ordinal))
        {
            fields["line"] = ["Escolha Todas ou uma das linhas dos centros de trabalho."];
        }

        if (fields.Count > 0)
        {
            return Error.Validation(Error.Codes.Validation, fields.Values.First()[0], fields);
        }

        // Nome único entre as ativas, sem diferença de maiúsculas (comparado na memória: poucas telas por tenant).
        var active = await db.KioskScreens.AsNoTracking().Where(s => s.RevokedAt == null && s.Id != (id ?? Guid.Empty)).Select(s => s.Name).ToListAsync(cancellationToken);
        if (active.Any(n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            const string message = "Já existe uma tela ativa com esse nome.";
            return Error.Validation("kiosk_name_taken", message, new Dictionary<string, string[]> { ["name"] = [message] });
        }

        return (name, line, views, rotation, networks);
    }

    private async Task<IReadOnlyList<string>> LinesAsync(CancellationToken cancellationToken) =>
        (await workCenters.ActiveAsync(cancellationToken)).Select(w => w.Line?.Trim()).OfType<string>().Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCulture).ToList();

    private async Task<KioskScreenRow> RowAsync(KioskScreen screen, CancellationToken cancellationToken) => (await RowsAsync([screen], cancellationToken))[0];

    private async Task<IReadOnlyList<KioskScreenRow>> RowsAsync(IReadOnlyList<KioskScreen> screens, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ids = screens.Select(s => s.Id).ToList();
        var sessions = await db.KioskSessions.AsNoTracking()
            .Where(s => ids.Contains(s.KioskId) && s.RevokedAt == null && s.ExpiresAt > now && s.AbsoluteExpiresAt > now)
            .GroupBy(s => s.KioskId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var people = screens.Select(s => s.CreatedBy).Concat(screens.Where(s => s.RevokedBy != null).Select(s => s.RevokedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        return screens.Select(s =>
        {
            var active = sessions.GetValueOrDefault(s.Id);
            return new KioskScreenRow(
                s.Id, s.Name, s.Line, s.Views, s.RotationSeconds, s.AllowedNetworks, s.TokenPrefix, names.GetValueOrDefault(s.CreatedBy, "Usuário removido"),
                s.CreatedAt, s.LastSeenAt, active, s.Status.ToString(), s.Status == KioskScreenStatus.Active && active == 0, s.RevokedAt,
                s.RevokedBy is { } by ? names.GetValueOrDefault(by, "Usuário removido") : null, s.Version);
        }).ToList();
    }
}
