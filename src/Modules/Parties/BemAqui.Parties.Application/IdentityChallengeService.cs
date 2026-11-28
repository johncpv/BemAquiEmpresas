using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

/// <summary>Desafio criado: o id, o canal e o destino mascarado (nunca o completo).</summary>
public sealed record IdentityChallengeStarted(Guid ChallengeId, string Channel, string MaskedDestination);

/// <summary>Resultado da conferência: conferiu, tentativas restantes, ou o desafio venceu (vencido, usado ou morto: 410).</summary>
public sealed record IdentityVerification(bool Verified, int AttemptsLeft, bool Gone);

/// <summary>
/// Código de identidade (INT-04 CA 12 e 13, decisão do PO 1): o aplicativo pede um código de 6 dígitos que vai ao e-mail ou ao telefone que já está no
/// cadastro e <b>não</b> veio de aplicativo; o cliente diz o código ao atendente e o aplicativo confere. Até 3 desafios por cliente e 20 por conexão por
/// hora; cada desafio vale 10 min e aceita 5 tentativas, comparadas em tempo constante. Desafio e conferência vão para a auditoria do cliente; o código
/// nunca vai para log, auditoria nem resposta (no <c>email_logs</c>, só até o envio).
/// </summary>
public sealed class IdentityChallengeService(IPartiesDb db, IEmailSender email, IAuditWriter audit, ConnectedAppContext app, TimeProvider clock)
{
    public const string Template = "identity-code";
    public const int PerCustomerPerHour = 3;
    public const int PerConnectionPerHour = 20;
    public const string NoVerifiedContactCode = "no_verified_contact";
    public const string ChannelUnavailableCode = "channel_unavailable";
    public const string ExpiredCode = "identity_challenge_expired";

    /// <summary>Espera até a vaga mais próxima quando o limite por hora estoura (para o <c>Retry-After</c>).</summary>
    public sealed record Limited(TimeSpan RetryAfter);

    public async Task<(Result<IdentityChallengeStarted> Result, Limited? Limit)> StartAsync(Guid partyId, string? channel, CancellationToken cancellationToken = default)
    {
        var grant = app.Current ?? throw new InvalidOperationException("Código de identidade fora de uma chamada de aplicativo.");
        var kind = channel?.Trim().ToLowerInvariant() switch
        {
            "email" => IdentityChallengeChannel.Email,
            "phone" => IdentityChallengeChannel.Phone,
            _ => (IdentityChallengeChannel?)null,
        };
        if (kind is null)
        {
            const string message = "Informe o canal: email ou phone.";
            return (Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["channel"] = [message] }), null);
        }

        var party = await db.Parties.AsNoTracking().Where(p => p.Id == partyId)
            .Select(p => new { p.LegalName, p.Email, p.Phone, p.EmailSetByApp, p.PhoneSetByApp }).FirstOrDefaultAsync(cancellationToken);
        if (party is null)
        {
            return (Error.NotFound(Error.Codes.NotFound, IntegrationApi.NotFoundMessage), null);
        }

        var destination = kind == IdentityChallengeChannel.Email
            ? party.EmailSetByApp ? null : party.Email
            : party.PhoneSetByApp ? null : party.Phone;
        if (string.IsNullOrWhiteSpace(destination))
        {
            var what = kind == IdentityChallengeChannel.Email ? "e-mail" : "telefone";
            return (Error.Validation(NoVerifiedContactCode, $"O cliente não tem {what} confirmado no ERP para receber o código."), null);
        }

        if (kind == IdentityChallengeChannel.Phone)
        {
            // O telefone sai pela mensagem de autenticação do WhatsApp do Conecta (ATD-06a CA 10); até lá, só e-mail.
            return (Error.Validation(ChannelUnavailableCode, "O envio do código por telefone ainda não está disponível. Use o e-mail."), null);
        }

        var now = clock.GetUtcNow();
        var since = now - TimeSpan.FromHours(1);
        var recentForParty = await db.IdentityChallenges.AsNoTracking().Where(c => c.PartyId == partyId && c.CreatedAt > since).Select(c => c.CreatedAt).ToListAsync(cancellationToken);
        var recentForConnection = await db.IdentityChallenges.AsNoTracking().Where(c => c.ConnectionId == grant.ConnectionId && c.CreatedAt > since).Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        if (recentForParty.Count >= PerCustomerPerHour || recentForConnection.Count >= PerConnectionPerHour)
        {
            var oldest = (recentForParty.Count >= PerCustomerPerHour ? recentForParty : recentForConnection).Min();
            return (Error.TooManyRequests("rate_limited", "Muitos códigos pedidos. Tente de novo mais tarde."), new Limited(oldest + TimeSpan.FromHours(1) - now));
        }

        var code = IdentityChallenge.NewCode();
        var challenge = IdentityChallenge.Create(partyId, kind.Value, grant.AppId, grant.ConnectionId, code, now);
        db.IdentityChallenges.Add(challenge);
        email.Enqueue(Template, destination, new Dictionary<string, object?>
        {
            ["first_name"] = party.LegalName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), ["code"] = code,
            ["minutes"] = (int)IdentityChallenge.Lifetime.TotalMinutes,
        }, nameof(Party), partyId);
        var masked = IntegrationData.MaskEmail(destination);
        await audit.RecordEventAsync(nameof(Party), partyId.ToString(), AuditActions.IdentityChallenge,
            new Dictionary<string, object?> { ["challengeId"] = challenge.Id, ["channel"] = "email", ["destination"] = masked }, cancellationToken);
        return (new IdentityChallengeStarted(challenge.Id, "email", masked), null);
    }

    /// <summary>
    /// Confere (CA 13): cada chamada gasta uma tentativa antes de comparar (atômico: chamadas paralelas não ganham tentativas extras); na 5ª errada o desafio
    /// morre. Desafio de outro cliente, de outro aplicativo ou inexistente: null (404).
    /// </summary>
    public async Task<IdentityVerification?> VerifyAsync(Guid partyId, Guid challengeId, string? code, CancellationToken cancellationToken = default)
    {
        var grant = app.Current ?? throw new InvalidOperationException("Código de identidade fora de uma chamada de aplicativo.");
        var challenge = await db.IdentityChallenges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.PartyId == partyId && c.AppId == grant.AppId, cancellationToken);
        if (challenge is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var consumed = challenge.Status == IdentityChallengeStatus.Pending && !challenge.IsExpired(now)
            ? await db.IdentityChallenges.Where(c => c.Id == challengeId && c.Status == IdentityChallengeStatus.Pending && c.Attempts < IdentityChallenge.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken)
            : 0;
        IdentityVerification outcome;
        if (consumed == 0)
        {
            outcome = new IdentityVerification(false, 0, true);
        }
        else if (challenge.Matches(code))
        {
            var verified = await db.IdentityChallenges.Where(c => c.Id == challengeId && c.Status == IdentityChallengeStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, IdentityChallengeStatus.Verified).SetProperty(c => c.VerifiedAt, now), cancellationToken);
            outcome = verified == 1 ? new IdentityVerification(true, 0, false) : new IdentityVerification(false, 0, true);
        }
        else
        {
            var attempts = await db.IdentityChallenges.AsNoTracking().Where(c => c.Id == challengeId).Select(c => c.Attempts).FirstAsync(cancellationToken);
            var left = Math.Max(0, IdentityChallenge.MaxAttempts - attempts);
            if (left == 0)
            {
                await db.IdentityChallenges.Where(c => c.Id == challengeId && c.Status == IdentityChallengeStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, IdentityChallengeStatus.Dead), cancellationToken);
            }

            outcome = new IdentityVerification(false, left, false);
        }

        await audit.RecordEventAsync(nameof(Party), partyId.ToString(), AuditActions.IdentityVerification, new Dictionary<string, object?>
        {
            ["challengeId"] = challengeId, ["verified"] = outcome.Verified, ["attemptsLeft"] = outcome.AttemptsLeft, ["expired"] = outcome.Gone,
        }, cancellationToken);
        return outcome;
    }
}
