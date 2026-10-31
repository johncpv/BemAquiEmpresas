using System.Globalization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Application.Certificates;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Settings;

/// <summary>
/// Configurações → Fiscal (FIS-02a CA 3 a 9, ADR 0042): valores pré-configurados pelo seed <c>fiscal-defaults</c>, editáveis com
/// <c>settings.edit</c> (no endpoint), cada um com a conferência "A confirmar com a contadora" / "Conferido" pelo hash do valor. Mudar
/// um valor conferido limpa a conferência. Produção só com a marca do catálogo, 2FA de novo e os campos críticos conferidos. IE, IM,
/// endereço e IBGE são lidos do perfil da empresa (CAD-06), nunca copiados.
/// </summary>
public sealed class FiscalSettingsService(
    IFiscalDb db,
    ITenantContext tenant,
    ITenantProfile catalog,
    ICompanyFiscalProfile company,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    IUserDirectory users,
    IStepUpTwoFactor stepUp,
    IEventPublisher events,
    TimeProvider clock) : IFiscalSettings
{
    public const string ScreenLink = "/app/configuracoes/fiscal";
    public const string CertificateLink = "/app/configuracoes/fiscal/certificado";
    public const string CompanyLink = "/app/configuracoes/empresa";
    public const string ProductionStepUpMessage = "Confirme com o código do aplicativo autenticador para ligar a emissão com valor fiscal.";
    public const string RegimeChangedNotice = "O regime mudou: as regras fiscais do regime anterior ficam inativas e precisam de revisão. Nenhuma nota já emitida muda.";

    /// <summary>Linha do tenant, rastreada; sem linha (seed ainda não rodou), nasce com os valores padrão.</summary>
    private async Task<(FiscalSettings Settings, bool Stored)> LoadAsync(bool track, CancellationToken ct)
    {
        var query = track ? db.FiscalSettings : db.FiscalSettings.AsNoTracking();
        var settings = await query.FirstOrDefaultAsync(ct);
        return settings is null ? (NewDefaults(clock.GetUtcNow()), false) : (settings, true);
    }

    /// <summary>Valores propostos (tabela "Valores pré-configurados" da FIS-02a): todos "A confirmar com a contadora".</summary>
    public static FiscalSettings NewDefaults(DateTimeOffset at)
    {
        var settings = new FiscalSettings { UpdatedAt = at };
        settings.SyncCrt();
        return settings;
    }

    public async Task<FiscalSettings> CurrentAsync(CancellationToken cancellationToken = default) => (await LoadAsync(false, cancellationToken)).Settings;

    public async Task<FiscalSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var (settings, stored) = await LoadAsync(false, cancellationToken);
        return await ViewAsync(settings, stored, cancellationToken);
    }

    private async Task<FiscalSettingsView> ViewAsync(FiscalSettings settings, bool stored, CancellationToken ct)
    {
        var confirmations = await db.SettingConfirmations.AsNoTracking().ToDictionaryAsync(c => c.SettingKey, StringComparer.Ordinal, ct);
        var names = new Dictionary<Guid, string?>();
        var reviews = new List<FiscalSettingReview>();
        foreach (var key in FiscalSettingKeys.All)
        {
            var confirmed = Confirmed(key, settings, confirmations);
            string? name = null;
            if (confirmed?.ConfirmedBy is { } by)
            {
                if (!names.TryGetValue(by, out name))
                {
                    name = await users.DisplayNameAsync(by, ct);
                    names[by] = name;
                }
            }

            reviews.Add(new FiscalSettingReview(key.Key, key.Section, key.Label, key.Critical, confirmed is null ? FiscalReviewState.ToConfirm : FiscalReviewState.Confirmed, name, confirmed?.ConfirmedAt));
        }

        var sections = FiscalSettingKeys.Sections.Select(section =>
        {
            var inSection = reviews.Where(r => r.Section == section.Code).ToList();
            var pending = inSection.Where(r => r.ReviewState == FiscalReviewState.ToConfirm).ToList();
            return new FiscalSettingSectionView(section.Code, section.Title, inSection.Count, pending.Count, pending.FirstOrDefault()?.Key);
        }).ToList();
        var profile = await company.GetFiscalProfileAsync(ct);
        var allowance = await catalog.FiscalEnvironmentAllowedAsync(TenantId(), ct);
        var canEdit = await permissions.HasPermissionAsync("settings", "edit", ct);
        var confirmedCount = reviews.Count(r => r.ReviewState == FiscalReviewState.Confirmed);
        return new FiscalSettingsView(
            stored ? settings.Id : null,
            FiscalSettingsMapping.ToValues(settings),
            FiscalSettings.CrtOf(settings.Regime),
            reviews,
            sections,
            confirmedCount,
            reviews.Count,
            confirmations.Count == 0 && (!stored || settings.UpdatedBy is null),
            new FiscalCompanyView(profile.LegalName, profile.Document, profile.StateRegistration, profile.MunicipalRegistration, profile.Address, profile.CityIbge),
            Preconditions(settings, profile, FiscalDocumentType.Nfe),
            CertificateSummary(settings),
            allowance.ToString(),
            canEdit,
            [.. PendingCritical(settings, confirmations, FiscalDocumentType.Nfe).Select(k => k.Key)]);
    }

    public FiscalCertificateSummary CertificateSummary(FiscalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var days = settings.CertificateDaysLeft(BusinessTime.Today(clock));
        var status = settings.CertificateNotAfter is null ? "None"
            : settings.CertificateExpired(clock.GetUtcNow()) ? "Expired"
            : days <= FiscalCertificateRules.WarnDays ? "Expiring" : "Valid";
        return new FiscalCertificateSummary(settings.CertificateNotAfter, days, status);
    }

    /// <summary>Pré-condições da emissão (CA 7): IE, IBGE e série para a NF-e; IBGE para a NFS-e.</summary>
    public static IReadOnlyList<FiscalPrecondition> Preconditions(FiscalSettings settings, CompanyFiscalProfile profile, FiscalDocumentType document)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profile);
        var list = new List<FiscalPrecondition>();
        if (document == FiscalDocumentType.Nfe && profile.StateRegistration is null)
        {
            list.Add(new("stateRegistration", "Informe a inscrição estadual em Dados da empresa.", CompanyLink));
        }

        if (profile.CityIbge is null)
        {
            list.Add(new("cityIbge", "Informe o código IBGE do município em Dados da empresa.", CompanyLink));
        }

        if (document == FiscalDocumentType.Nfe && settings.NfeSeries is null)
        {
            list.Add(new("nfeSeries", "Informe a série exclusiva da NF-e.", ScreenLink + "#campo-nfeSeries"));
        }

        return list;
    }

    private static SettingConfirmation? Confirmed(FiscalSettingKey key, FiscalSettings settings, IReadOnlyDictionary<string, SettingConfirmation> confirmations) =>
        confirmations.TryGetValue(key.Key, out var c) && string.Equals(c.ValueHash, key.HashOf(settings), StringComparison.Ordinal) ? c : null;

    private static IEnumerable<FiscalSettingKey> PendingCritical(FiscalSettings settings, IReadOnlyDictionary<string, SettingConfirmation> confirmations, FiscalDocumentType document) =>
        FiscalSettingKeys.CriticalFor(document).Where(k => Confirmed(k, settings, confirmations) is null);

    public async Task<Result<FiscalReadiness>> ReadinessAsync(FiscalDocumentType document, CancellationToken cancellationToken = default)
    {
        var settings = await CurrentAsync(cancellationToken);
        var profile = await company.GetFiscalProfileAsync(cancellationToken);
        if (Preconditions(settings, profile, document) is { Count: > 0 } missing)
        {
            return new Error(FiscalSettingsErrors.Incomplete, missing[0].Message, ErrorType.Conflict)
            {
                Data = new Dictionary<string, object?> { ["preconditions"] = missing.Select(m => m.Key).ToList(), ["link"] = missing[0].Link },
            };
        }

        if (settings.CertificateExpired(clock.GetUtcNow()))
        {
            return new Error(FiscalSettingsErrors.CertificateExpired,
                $"O certificado digital venceu em {BusinessTime.ToBusiness(settings.CertificateNotAfter!.Value):dd/MM/yyyy}. Envie o novo em Configurações → Fiscal → Certificado digital.", ErrorType.Conflict)
            {
                Data = new Dictionary<string, object?> { ["link"] = CertificateLink },
            };
        }

        var confirmations = await db.SettingConfirmations.AsNoTracking().ToDictionaryAsync(c => c.SettingKey, StringComparer.Ordinal, cancellationToken);
        var pending = PendingCritical(settings, confirmations, document).ToList();
        if (settings.Environment == FiscalEnvironment.Production && pending.Count > 0)
        {
            return NotConfirmed(pending);
        }

        IReadOnlyList<string> warnings = pending.Count == 0 ? [] : [$"{pending.Count} valores fiscais ainda a confirmar com a contadora. Em homologação, a nota sai mesmo assim."];
        return new FiscalReadiness(settings.Environment, [.. pending.Select(p => p.Key)], warnings);
    }

    private static readonly string[] ConfirmBeforeProduction = ["Confira com a contadora antes de ligar a produção."];

    private static Error NotConfirmed(List<FiscalSettingKey> pending) =>
        new(FiscalSettingsErrors.NotConfirmed, "Confira com a contadora e marque como conferidos: " + string.Join(", ", pending.Select(p => p.Label)) + ".", ErrorType.Conflict)
        {
            Data = new Dictionary<string, object?> { ["pending"] = pending.Select(p => p.Key).ToList(), ["link"] = ScreenLink + "#campo-" + pending[0].Key },
        };

    /// <summary>"Salvar configuração fiscal" (CA 4, 8 e 9).</summary>
    public async Task<Result<FiscalSettingsSaved>> SaveAsync(SaveFiscalSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var result = await SaveCoreAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            db.ChangeTracker.Clear(); // nada do que foi recusado fica rastreado no escopo
        }

        return result;
    }

    private async Task<Result<FiscalSettingsSaved>> SaveCoreAsync(SaveFiscalSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Values);
        // A conferência do 2FA grava no mesmo contexto: ela roda antes de qualquer valor ser aplicado à linha rastreada.
        var storedEnvironment = await db.FiscalSettings.AsNoTracking().Select(s => (FiscalEnvironment?)s.Environment).FirstOrDefaultAsync(cancellationToken) ?? FiscalEnvironment.Homologation;
        var turningOnProduction = storedEnvironment != FiscalEnvironment.Production && request.Values.Environment == FiscalEnvironment.Production;
        if (turningOnProduction)
        {
            // CA 8 (D6, ADR 0034, ADR 0042 §2): só com a marca do catálogo, com 2FA de novo e com os campos críticos conferidos.
            if (await catalog.FiscalEnvironmentAllowedAsync(TenantId(), cancellationToken) != FiscalEnvironmentAllowance.Production)
            {
                return Error.Forbidden(FiscalErrorCodes.EnvironmentNotAllowed, FiscalEmitterAccess.EnvironmentMessage);
            }

            if (currentUser.UserId is not { } actor || !await stepUp.VerifyAsync(actor, request.Code, cancellationToken))
            {
                return Error.Forbidden(string.IsNullOrWhiteSpace(request.Code) ? FiscalSettingsErrors.TwoFactorRequired : FiscalSettingsErrors.TwoFactorInvalid, ProductionStepUpMessage);
            }
        }

        var (settings, stored) = await LoadAsync(true, cancellationToken);
        var before = FiscalSettingKeys.All.ToDictionary(k => k.Key, k => k.Canonical(settings), StringComparer.Ordinal);
        var oldRegime = settings.Regime;
        var oldSeries = settings.NfeSeries;
        FiscalSettingsMapping.Apply(settings, request.Values);
        if (!Enum.IsDefined(request.Values.Environment))
        {
            return Invalid(new Dictionary<string, string[]> { ["environment"] = ["Escolha o ambiente."] });
        }

        settings.Environment = request.Values.Environment;
        var errors = FiscalSettingsRules.Validate(settings);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        var confirmations = await db.SettingConfirmations.ToListAsync(cancellationToken);
        if (turningOnProduction)
        {
            var byKey = confirmations.ToDictionary(c => c.SettingKey, StringComparer.Ordinal);
            var pending = FiscalSettingKeys.All.Where(k => k.Critical is FiscalCriticality.All or FiscalCriticality.Nfe && Confirmed(k, settings, byKey) is null).ToList();
            if (pending.Count > 0)
            {
                var problem = NotConfirmed(pending);
                return new Error(problem.Code, problem.Message, ErrorType.Validation) { Data = problem.Data, Fields = pending.ToDictionary(p => p.Key, _ => ConfirmBeforeProduction) };
            }
        }

        if (oldSeries is { } series && settings.NfeSeries != oldSeries && !request.ConfirmSeriesChange)
        {
            var last = await db.OutgoingInvoices.AsNoTracking().Where(i => i.Series == series && i.Number != null).OrderByDescending(i => i.Number).Select(i => i.Number).FirstOrDefaultAsync(cancellationToken);
            if (last is { } number)
            {
                return new Error(FiscalSettingsErrors.SeriesInUse,
                    $"Já há nota emitida na série {series.ToString(CultureInfo.InvariantCulture)} (a última é a nº {number.ToString(CultureInfo.InvariantCulture)}). Confirme a troca da série.", ErrorType.Conflict)
                {
                    Data = new Dictionary<string, object?> { ["series"] = series, ["lastNumber"] = number },
                };
            }
        }

        var changed = FiscalSettingKeys.All.Where(k => !string.Equals(before[k.Key], k.Canonical(settings), StringComparison.Ordinal)).Select(k => k.Key).ToList();
        foreach (var stale in confirmations.Where(c => changed.Contains(c.SettingKey, StringComparer.Ordinal)))
        {
            db.SettingConfirmations.Remove(stale); // a conferência vale para o valor (ADR 0042 §1): mudou, volta o chip
        }

        var regimeChanged = settings.Regime != oldRegime;
        if (changed.Count > 0 || !stored)
        {
            settings.UpdatedAt = clock.GetUtcNow();
            settings.UpdatedBy = currentUser.UserId;
            if (!stored)
            {
                db.FiscalSettings.Add(settings);
            }

            if (changed.Count > 0)
            {
                events.Publish(new FiscalSettingsChanged(changed, regimeChanged));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var view = await GetAsync(cancellationToken);
        return new FiscalSettingsSaved(view, changed, regimeChanged ? RegimeChangedNotice : null, []);
    }

    /// <summary>"Marcar como conferido" de um campo ou da seção (CA 5): grava quem, quando e o hash do valor de agora (auditado).</summary>
    public async Task<Result<FiscalSettingsView>> ConfirmAsync(ConfirmFiscalSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var keys = (request.Keys ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0 || keys.Any(k => FiscalSettingKeys.Find(k) is null))
        {
            return Error.Validation(Error.Codes.Validation, "Escolha os campos a marcar como conferidos.", new Dictionary<string, string[]> { ["keys"] = ["Campo desconhecido."] });
        }

        var (settings, stored) = await LoadAsync(true, cancellationToken);
        var now = clock.GetUtcNow();
        if (!stored)
        {
            db.FiscalSettings.Add(settings);
        }

        var existing = await db.SettingConfirmations.Where(c => keys.Contains(c.SettingKey)).ToDictionaryAsync(c => c.SettingKey, StringComparer.Ordinal, cancellationToken);
        foreach (var key in keys.Select(k => FiscalSettingKeys.Find(k)!))
        {
            var hash = key.HashOf(settings);
            if (existing.TryGetValue(key.Key, out var confirmation))
            {
                if (confirmation.ValueHash != hash)
                {
                    confirmation.ValueHash = hash;
                    confirmation.ConfirmedBy = currentUser.UserId;
                    confirmation.ConfirmedAt = now;
                }
            }
            else
            {
                db.SettingConfirmations.Add(new SettingConfirmation { SettingKey = key.Key, ValueHash = hash, ConfirmedBy = currentUser.UserId, ConfirmedAt = now });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>"Desfazer conferência" (CA 5): o chip volta para "A confirmar com a contadora", auditado.</summary>
    public async Task<Result<FiscalSettingsView>> UndoAsync(string key, CancellationToken cancellationToken = default)
    {
        if (FiscalSettingKeys.Find(key) is null)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, "Campo fiscal não encontrado.");
        }

        if (await db.SettingConfirmations.FirstOrDefaultAsync(c => c.SettingKey == key, cancellationToken) is { } confirmation)
        {
            db.SettingConfirmations.Remove(confirmation);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    private static Error Invalid(Dictionary<string, string[]> errors) => Error.Validation(Error.Codes.Validation, errors.Values.First()[0], errors);

    private Guid TenantId() => tenant.Tenant?.Id ?? throw new InvalidOperationException("Configuração fiscal fora do escopo de um tenant.");
}
