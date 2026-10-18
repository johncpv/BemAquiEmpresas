using System.Diagnostics;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Identity.Application.Ai;

/// <summary>Configuração lida pela tela: a chave nunca volta, só a data em que foi salva (FND-15 CA 10).</summary>
public sealed record AiSettingsDto(bool Configured, string? Provider, string? Model, DateTimeOffset? KeySetAt, DateTimeOffset? UpdatedAt, IReadOnlyList<AiProviderInfo> Providers);

/// <summary>Salvar ou testar. <c>ApiKey</c> vazia mantém a chave salva (só vale para o mesmo provedor).</summary>
public sealed record AiSettingsRequest(string Provider, string Model, Secret? ApiKey);

public sealed record AiTestResult(bool Ok, string Message);

/// <summary>Provedor pronto para uma chamada, com a chave já decifrada (fica só na memória do request).</summary>
public sealed record ActiveAiProvider(IAiProvider Provider, string Model, Secret ApiKey);

/// <summary>Resultado de uma chamada: a resposta ou a falha traduzida.</summary>
public sealed record AiCallOutcome(AiCompletion? Completion, AiProviderException? Failure, int DurationMs);

/// <summary>
/// Configurações → Inteligência artificial (FND-15 CA 9 a 13, R-AJD-8). Lida do banco a cada pergunta, então trocar de
/// provedor vale na próxima pergunta, sem deploy. A chave é cifrada com <see cref="SecretKind.AiApiKey"/> no escopo do tenant.
/// </summary>
public sealed partial class AiSettingsService(
    IIdentityDb db,
    IEnumerable<IAiProvider> providers,
    ISecretProtector secrets,
    ITenantContext tenant,
    IntegrationHealthTracker health,
    IOptions<AiOptions> options,
    TimeProvider clock)
{
    public const string ConnectionOk = "Conexão OK";

    public async Task<AiSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? new AiSettingsDto(false, null, null, null, null, AiProviders.All)
            : new AiSettingsDto(true, row.Provider, row.Model, row.KeySetAt, row.UpdatedAt, AiProviders.All);
    }

    public async Task<Result<AiSettingsDto>> SaveAsync(AiSettingsRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = await db.AiSettings.FirstOrDefaultAsync(cancellationToken);
        if (Validate(request, row) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        if (row is null)
        {
            row = new AiSettings();
            db.AiSettings.Add(row);
        }

        row.Provider = request.Provider;
        row.Model = request.Model.Trim();
        if (request.ApiKey is { } key)
        {
            row.ApiKeyCipher = secrets.Protect(Scope(), SecretKind.AiApiKey, key);
            row.KeySetAt = now;
        }

        row.UpdatedAt = now;
        row.UpdatedBy = actorId;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Remove o provedor: a ajuda volta a funcionar só por busca.</summary>
    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.AiSettings.FirstOrDefaultAsync(cancellationToken);
        if (row is not null)
        {
            db.AiSettings.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>"Testar conexão" (CA 11): chamada mínima com a configuração preenchida, sem salvar. Conta no registro de uso.</summary>
    public async Task<Result<AiTestResult>> TestAsync(AiSettingsRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (Validate(request, row) is { } invalid)
        {
            return invalid;
        }

        var active = new ActiveAiProvider(Resolve(request.Provider)!, request.Model.Trim(), request.ApiKey ?? Decrypt(row!));
        var outcome = await CallAsync(active, actorId, "Responda só com a palavra OK.", "Teste de conexão.", 16, AiUsageLog.TestOk, AiUsageLog.TestFailed, cancellationToken);
        return outcome.Failure is { } failure ? new AiTestResult(false, failure.UserMessage) : new AiTestResult(true, ConnectionOk);
    }

    /// <summary>Provedor configurado, ou null (sem configuração, a ajuda funciona só por busca, CA 4).</summary>
    public async Task<ActiveAiProvider?> ActiveAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return row is null || Resolve(row.Provider) is not { } provider ? null : new ActiveAiProvider(provider, row.Model, Decrypt(row));
    }

    /// <summary>
    /// Uma chamada com tempo limite e sem nova tentativa. Falha conta para o alerta de integração (CA 5) e toda chamada
    /// fica registrada (CA 14): a falha aqui; o sucesso aqui quando <paramref name="successOutcome"/> vem, senão pelo chamador.
    /// </summary>
    public async Task<AiCallOutcome> CallAsync(ActiveAiProvider active, Guid? userId, string system, string user, int maxTokens, string? successOutcome, string failureOutcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(active);
        var integration = "ia:" + active.Provider.Code;
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.Timeout);
        try
        {
            var completion = await active.Provider.CompleteAsync(new AiRequest(active.Model, active.ApiKey, system, user, maxTokens), timeout.Token);
            health.RecordSuccess(integration);
            var elapsed = (int)watch.ElapsedMilliseconds;
            if (successOutcome is not null)
            {
                await LogAsync(active, userId, completion, elapsed, successOutcome, cancellationToken);
            }

            return new AiCallOutcome(completion, null, elapsed);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is AiProviderException or OperationCanceledException or HttpRequestException)
        {
            var failure = ex as AiProviderException ?? new AiProviderException(ex is OperationCanceledException ? AiFailureKind.Timeout : AiFailureKind.Network, ex.GetType().Name);
            var elapsed = (int)watch.ElapsedMilliseconds;
            await health.RecordFailureAsync(integration, tenant.Tenant?.Key, $"{failure.Kind}: {failure.Message}", CancellationToken.None);
            await LogAsync(active, userId, null, elapsed, failureOutcome, CancellationToken.None);
            return new AiCallOutcome(null, failure, elapsed);
        }
    }

    public async Task LogAsync(ActiveAiProvider active, Guid? userId, AiCompletion? completion, int durationMs, string outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(active);
        db.AiUsageLogs.Add(new AiUsageLog
        {
            At = clock.GetUtcNow(),
            UserId = userId,
            Provider = active.Provider.Code,
            Model = active.Model,
            InputTokens = completion?.InputTokens,
            OutputTokens = completion?.OutputTokens,
            DurationMs = durationMs,
            Outcome = outcome,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private Error? Validate(AiSettingsRequest request, AiSettings? saved)
    {
        var fields = new Dictionary<string, string[]>();
        if (Resolve(request.Provider) is null)
        {
            fields["provider"] = ["Escolha o provedor."];
        }

        var model = request.Model?.Trim() ?? string.Empty;
        if (model.Length is 0 or > 100 || !ModelPattern().IsMatch(model))
        {
            fields["model"] = ["Informe o nome do modelo (letras, números, ponto, hífen ou barra, até 100 caracteres)."];
        }

        if (request.ApiKey is null && (saved is null || saved.Provider != request.Provider))
        {
            fields["apiKey"] = [saved is null ? "Informe a chave de API." : "Informe a chave de API do novo provedor."];
        }
        else if (request.ApiKey is { } key && (key.Reveal().Length is < 8 or > 400 || key.Reveal().Any(char.IsWhiteSpace)))
        {
            fields["apiKey"] = ["A chave parece incompleta. Copie a chave inteira, sem espaços."];
        }

        return fields.Count == 0 ? null : Error.Validation(Error.Codes.Validation, fields.Values.First()[0], fields);
    }

    private IAiProvider? Resolve(string? code) => AiProviders.Find(code) is null ? null : providers.FirstOrDefault(p => p.Code == code);

    private Secret Decrypt(AiSettings row) => secrets.Unprotect(Scope(), SecretKind.AiApiKey, row.ApiKeyCipher);

    private SecretScope Scope() => SecretScope.ForTenant(tenant.Tenant?.Id ?? throw new InvalidOperationException("Configuração de IA sem tenant resolvido."));

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]*$")]
    private static partial Regex ModelPattern();
}
