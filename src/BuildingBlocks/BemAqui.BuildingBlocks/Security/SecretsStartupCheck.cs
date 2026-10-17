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
/// Antes de o Host aceitar requisições (StartingAsync): o key ring precisa estar acessível, não vazio
/// (salvo na criação autorizada) e decifrável pelo certificado. Assim o Host nunca cria um key ring
/// novo em silêncio, o que deixaria todos os segredos ilegíveis (ADR 0007).
/// </summary>
internal sealed partial class SecretsStartupCheck(
    IServiceScopeFactory scopes,
    IOptions<SecretsOptions> options,
    IKeyManager keyManager,
    IDataProtectionProvider provider,
    ILogger<SecretsStartupCheck> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var count = await CountKeysAsync(cancellationToken);

        if (count == 0 && !options.Value.AllowKeyRingCreation)
        {
            throw new InvalidOperationException(
                "Key ring do Data Protection vazio e DataProtection:AllowKeyRingCreation=false. Restaure o backup do key ring " +
                "(docs/operacao/segredos-e-chaves.md) ou, só no primeiro provisionamento, ligue a flag.");
        }

        foreach (var key in keyManager.GetAllKeys().Where(k => !k.IsRevoked))
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
        LogKeyRingReady(logger, count);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<int> CountKeysAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>();
        try
        {
            if (options.Value.AutoMigrate)
            {
                await db.Database.MigrateAsync(cancellationToken);
            }

            return await db.DataProtectionKeys.CountAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Key ring do Data Protection inacessível no banco de catálogo (security.data_protection_keys). " +
                "O Host não sobe sem ele; veja docs/operacao/segredos-e-chaves.md.", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Key ring do Data Protection pronto ({Count} chave(s) antes da subida).")]
    private static partial void LogKeyRingReady(ILogger logger, int count);
}
