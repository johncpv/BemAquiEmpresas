using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Certificates;

/// <summary>Uma troca no histórico do certificado (CA 13): só metadados.</summary>
public sealed record FiscalCertificateChange(DateTimeOffset At, string Source, string? Subject, string Thumbprint, DateTimeOffset? NotAfter, string? ByName);

/// <summary>
/// Configurações → Fiscal → Certificado digital (CA 13): situação lida do BemAqui e conferida no <c>GET /me</c> do Emissor (cache de 1 h).
/// <c>Status</c>: <c>None</c>, <c>Valid</c>, <c>Expiring</c> (até 30 dias) ou <c>Expired</c>.
/// </summary>
public sealed record FiscalCertificateView(
    bool IntegrationReady,
    string? IntegrationMessage,
    string Status,
    string? Subject,
    string? Document,
    string? Thumbprint,
    DateTimeOffset? NotAfter,
    int? DaysLeft,
    DateTimeOffset? UploadedAt,
    string? UploadedByName,
    DateTimeOffset? ChangedOutsideAt,
    DateTimeOffset? EmitterCheckedAt,
    bool EmitterUnavailable,
    bool CanEdit,
    IReadOnlyList<FiscalCertificateChange> History);

/// <summary>Envio do A1 (CA 11): o arquivo e a senha vêm do formulário e só vivem na memória do request.</summary>
public sealed record UploadCertificateCommand(byte[] Pfx, Secret Password, string? Code);

/// <summary>
/// Certificado digital A1 (FIS-02a CA 11 a 13, ADR 0027 §3, D12): o BemAqui confere o arquivo em memória (<c>EphemeralKeySet</c>), confere
/// o CNPJ do titular com o da empresa, repassa ao cofre do Emissor (<see cref="IFiscalEmitter.UploadCertificateAsync"/>) e guarda só a
/// validade, o titular, o CNPJ e o thumbprint. O <c>.pfx</c> e a senha nunca vão para disco, log, auditoria, outbox ou resposta.
/// </summary>
public sealed partial class FiscalCertificateService(
    IFiscalDb db,
    ITenantContext tenant,
    ITenantProfile catalog,
    FiscalEmitterAccess access,
    IStepUpTwoFactor stepUp,
    ICurrentUser currentUser,
    IUserDirectory users,
    BemAqui.BuildingBlocks.Authorization.IPermissionChecker permissions,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<FiscalCertificateService> logger)
{
    public const string RateLimitPolicy = "fiscal-certificate";
    public const int UploadsPerHour = 5;
    public const string StepUpMessage = "Confirme com o código do aplicativo autenticador para enviar o certificado.";
    public const string NotConfiguredMessage = "Conecte o Emissor Fiscal antes de enviar o certificado.";
    public static readonly TimeSpan EmitterCacheFor = TimeSpan.FromHours(1);

    private sealed record EmitterSnapshot(EmitterCertificate? Certificate, DateTimeOffset CheckedAt);

    private string CacheKey => "fiscal-certificate-me:" + TenantId().ToString("N");

    public async Task<FiscalCertificateView> GetAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        var settings = await db.FiscalSettings.FirstOrDefaultAsync(cancellationToken);
        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        EmitterSnapshot? snapshot = null;
        var unavailable = false;
        if (emitter.IsSuccess)
        {
            if (refresh)
            {
                cache.Remove(CacheKey);
            }

            snapshot = await cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                var account = await emitter.Value.GetAccountAsync(cancellationToken);
                if (account.IsFailure)
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                    return null;
                }

                entry.AbsoluteExpirationRelativeToNow = EmitterCacheFor;
                var document = await catalog.DocumentAsync(TenantId(), cancellationToken);
                var issuer = account.Value.Issuers.FirstOrDefault(i => string.Equals(i.Document, document, StringComparison.Ordinal));
                return new EmitterSnapshot(issuer?.Certificate, clock.GetUtcNow());
            });
            unavailable = snapshot is null;
            if (snapshot?.Certificate is { Present: true, Thumbprint: { Length: > 0 } remote } certificate
                && !string.Equals(remote, settings?.CertificateThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                settings = await RecordRemoteAsync(settings, certificate, cancellationToken);
            }
        }

        var days = settings?.CertificateDaysLeft(BusinessTime.Today(clock));
        var status = settings?.CertificateNotAfter is null ? "None"
            : settings.CertificateExpired(clock.GetUtcNow()) ? "Expired"
            : days <= FiscalCertificateRules.WarnDays ? "Expiring" : "Valid";
        var history = await db.CertificateUploads.AsNoTracking().OrderByDescending(u => u.At).Take(10).ToListAsync(cancellationToken);
        var changes = new List<FiscalCertificateChange>();
        foreach (var h in history)
        {
            changes.Add(new FiscalCertificateChange(h.At, h.Source, h.Subject, h.Thumbprint, h.NotAfter, h.UploadedBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null));
        }

        return new FiscalCertificateView(
            emitter.IsSuccess,
            emitter.IsSuccess ? null : emitter.Error!.Code == FiscalErrorCodes.NotConfigured ? NotConfiguredMessage : emitter.Error.Message,
            status,
            settings?.CertificateSubject,
            settings?.CertificateDocument,
            settings?.CertificateThumbprint,
            settings?.CertificateNotAfter,
            days,
            settings?.CertificateUploadedAt,
            settings?.CertificateUploadedBy is { } uploader ? await users.DisplayNameAsync(uploader, cancellationToken) : null,
            settings?.CertificateChangedOutsideAt,
            snapshot?.CheckedAt,
            unavailable,
            await permissions.HasPermissionAsync("settings", "edit", cancellationToken),
            changes);
    }

    /// <summary>O Emissor tem outro certificado que o guardado (trocado fora do BemAqui, CA 13): os dados passam a ser os de lá.</summary>
    private async Task<FiscalSettings> RecordRemoteAsync(FiscalSettings? settings, EmitterCertificate remote, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (settings is null)
        {
            settings = FiscalSettingsService.NewDefaults(now);
            db.FiscalSettings.Add(settings);
        }

        var hadLocal = settings.CertificateThumbprint is not null;
        settings.RecordOutsideChange(remote.Holder, remote.Thumbprint!, remote.ValidUntil, now);
        if (!hadLocal)
        {
            settings.RecordCertificate(remote.Holder ?? string.Empty, await catalog.DocumentAsync(TenantId(), ct) ?? string.Empty, remote.Thumbprint!, remote.ValidUntil ?? now, null, now);
        }

        db.CertificateUploads.Add(new CertificateUpload { Thumbprint = remote.Thumbprint!.ToUpperInvariant(), Subject = remote.Holder, NotAfter = remote.ValidUntil, Source = FiscalCertificateSources.Emissor, At = now });
        await db.SaveChangesAsync(ct);
        return settings;
    }

    /// <summary>"Enviar certificado" (CA 11 e 12).</summary>
    public async Task<Result<FiscalCertificateView>> UploadAsync(UploadCertificateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            if (command.Pfx.Length is 0 or > FiscalCertificateRules.MaxBytes)
            {
                return Invalid("pfx", "Escolha o arquivo .pfx do certificado A1 (até 50 KB).");
            }

            if (string.IsNullOrEmpty(command.Password.Reveal()))
            {
                return Invalid("password", "Informe a senha do certificado.");
            }

            if (currentUser.UserId is not { } userId || !await stepUp.VerifyAsync(userId, command.Code, cancellationToken))
            {
                return Error.Forbidden(string.IsNullOrWhiteSpace(command.Code) ? FiscalSettingsErrors.TwoFactorRequired : FiscalSettingsErrors.TwoFactorInvalid, StepUpMessage);
            }

            var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
            if (emitter.IsFailure)
            {
                return emitter.Error!.Code == FiscalErrorCodes.NotConfigured ? Error.Conflict(FiscalErrorCodes.NotConfigured, NotConfiguredMessage) : emitter.Error;
            }

            var now = clock.GetUtcNow();
            var inspected = FiscalCertificateInspector.Inspect(command.Pfx, command.Password, now);
            if (inspected.IsFailure)
            {
                return inspected.Error!;
            }

            var info = inspected.Value;
            var document = await catalog.DocumentAsync(TenantId(), cancellationToken);
            if (!string.Equals(document, info.Document, StringComparison.Ordinal))
            {
                return Invalid("pfx", "O certificado é de outro CNPJ: " + FiscalCertificateRules.Mask(info.Document) + ".");
            }

            // O Emissor confere de novo (CNPJ do emitente, sem aceitar falha, lacuna L-h) e guarda no cofre dele.
            var sent = await emitter.Value.UploadCertificateAsync(document!, command.Pfx, command.Password, cancellationToken);
            if (sent.IsFailure)
            {
                var rejectedTenant = TenantId();
                LogRejected(logger, rejectedTenant, sent.Error!.Code);
                return sent.Error!.Type == ErrorType.Validation || sent.Error.Code == FiscalErrorCodes.Validation
                    ? Invalid("pfx", "O Emissor Fiscal recusou o certificado: " + sent.Error.Message)
                    : sent.Error;
            }

            var thumbprint = string.IsNullOrEmpty(sent.Value.Thumbprint) ? info.Thumbprint : sent.Value.Thumbprint;
            var settings = await db.FiscalSettings.FirstOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                settings = FiscalSettingsService.NewDefaults(now);
                db.FiscalSettings.Add(settings);
            }

            settings.RecordCertificate(info.Subject, info.Document, thumbprint, sent.Value.ValidUntil == default ? info.NotAfter : sent.Value.ValidUntil, userId, now);
            db.CertificateUploads.Add(new CertificateUpload
            {
                Thumbprint = thumbprint.ToUpperInvariant(), Subject = info.Subject, Document = info.Document, NotAfter = settings.CertificateNotAfter, Source = FiscalCertificateSources.BemAqui,
                UploadedBy = userId, At = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            cache.Remove(CacheKey);
            var tenantId = TenantId();
            LogUploaded(logger, tenantId, settings.CertificateNotAfter!.Value);
            return await GetAsync(false, cancellationToken);
        }
        finally
        {
            Array.Clear(command.Pfx); // o arquivo não sobrevive ao request
        }
    }

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private Guid TenantId() => tenant.Tenant?.Id ?? throw new InvalidOperationException("Certificado fiscal fora do escopo de um tenant.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificado digital do tenant {TenantId} trocado, válido até {NotAfter}.")]
    private static partial void LogUploaded(ILogger logger, Guid tenantId, DateTimeOffset notAfter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "O Emissor Fiscal recusou o certificado do tenant {TenantId} ({Code}).")]
    private static partial void LogRejected(ILogger logger, Guid tenantId, string code);
}
