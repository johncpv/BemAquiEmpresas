using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Antes de o Host aceitar requisições (StartingAsync): o key ring precisa estar acessível, ter chave válida
/// (salvo na criação autorizada) e ser decifrável pelo certificado. Assim o Host nunca cria um key ring
/// novo em silêncio, o que deixaria todos os segredos ilegíveis (ADR 0007).
/// </summary>
internal sealed partial class SecretsStartupCheck(
    IServiceScopeFactory scopes,
    IOptions<SecretsOptions> options,
    IKeyManager keyManager,
    IDataProtectionProvider provider,
    ProtectionCertificateInfo certificate,
    TimeProvider clock,
    ILogger<SecretsStartupCheck> logger) : IHostedLifecycleService
{
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(180);

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await EnsureKeyRingReachableAsync(cancellationToken);

        var activeKeys = keyManager.GetAllKeys().Where(k => !k.IsRevoked).ToList();
        if (activeKeys.Count == 0 && !options.Value.AllowKeyRingCreation)
        {
            throw new InvalidOperationException(
                "Key ring do Data Protection vazio (sem chave não revogada) e DataProtection:AllowKeyRingCreation=false. Restaure o backup " +
                "do key ring (docs/operacao/segredos-e-chaves.md) ou, só no primeiro provisionamento, ligue a flag.");
        }

        if (options.Value.AllowKeyRingCreation)
        {
            LogCreationAllowed(logger, activeKeys.Count);
        }

        foreach (var key in activeKeys)
        {
            try
            {
                _ = key.Descriptor; // força decifrar a chave com o certificado configurado
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"A chave {key.KeyId} do key ring não decifra com o certificado configurado (certificado errado ou ausente?). " +
                    "Veja docs/operacao/segredos-e-chaves.md.", ex);
            }
        }

        var probe = provider.CreateProtector(SecretProtector.RootPurpose, "startup-check");
        _ = probe.Unprotect(probe.Protect("ok"));

        if (certificate.NotAfter - clock.GetUtcNow() < ExpiryWarning)
        {
            LogCertificateExpiring(logger, certificate.Subject, certificate.Thumbprint, certificate.NotAfter);
        }

        LogKeyRingReady(logger, activeKeys.Count, certificate.Thumbprint);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureKeyRingReachableAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>();
        try
        {
            if (options.Value.AutoMigrate)
            {
                await db.Database.MigrateAsync(cancellationToken);
            }

            _ = await db.DataProtectionKeys.AnyAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Key ring do Data Protection inacessível no banco de catálogo (security.data_protection_keys). " +
                "O Host não sobe sem ele; veja docs/operacao/segredos-e-chaves.md.", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Key ring do Data Protection pronto ({Count} chave(s) ativa(s) na subida, certificado {Thumbprint}).")]
    private static partial void LogKeyRingReady(ILogger logger, int count, string thumbprint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DataProtection:AllowKeyRingCreation está ligada ({Count} chave(s) ativa(s)). Desligue depois do primeiro provisionamento.")]
    private static partial void LogCreationAllowed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Certificado de proteção do key ring {Subject} ({Thumbprint}) vence em {NotAfter:yyyy-MM-dd}: planeje a troca (docs/operacao/segredos-e-chaves.md).")]
    private static partial void LogCertificateExpiring(ILogger logger, string subject, string thumbprint, DateTimeOffset notAfter);
}
