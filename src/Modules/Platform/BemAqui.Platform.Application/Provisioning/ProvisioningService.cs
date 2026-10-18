using System.Diagnostics;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Contracts;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Platform.Application.Provisioning;

/// <summary>
/// Provisiona um tenant em etapas idempotentes e retomáveis (FND-03a): Register, CreateRole, CreateDatabase,
/// Migrate, Seed, CreateOwner, SendInvite e Activate. Cada etapa registra Started, Succeeded ou Failed. Na
/// retomada, as etapas já concluídas são puladas. A senha do role nunca sai do processo em texto.
/// </summary>
public sealed partial class ProvisioningService(
    IProvisioningCatalog catalog,
    ITenantDatabaseAdmin admin,
    ITenantDatabaseMigrator migrator,
    ITenantScopeFactory scopes,
    ITenantResolver resolver,
    IEnumerable<ITenantSeeder> seeders,
    ITenantOwnerOnboarding onboarding,
    BemAqui.Platform.Application.Modules.TenantModuleService modules,
    ISecretProtector protector,
    TimeProvider clock)
{
    [GeneratedRegex("^[a-z][a-z0-9-]{1,29}[a-z0-9]$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?[.])+[a-z][a-z0-9-]*$")]
    private static partial Regex HostPattern();

    public static string DatabaseName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return "tenant_" + key.Replace("-", "_", StringComparison.Ordinal);
    }

    public async Task<ProvisioningResult> ProvisionAsync(ProvisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var host = string.IsNullOrWhiteSpace(request.Host) ? string.Empty : TenantDomain.Normalize(request.Host);
        var invalid = Validate(request, host);
        if (invalid is not null)
        {
            return Failure(invalid);
        }

        var tenant = await catalog.FindByKeyAsync(request.Key, cancellationToken);
        if (tenant is not null && !request.Resume)
        {
            return Failure("Já existe um tenant com a chave " + request.Key + ". Para continuar um provisionamento interrompido, use --resume.");
        }

        if (tenant is null && request.Resume)
        {
            return Failure("Não há provisionamento em andamento para a chave " + request.Key + ".");
        }

        if (tenant is null && await catalog.HostInUseAsync(host, cancellationToken))
        {
            return Failure("O endereço " + host + " já está em uso por outro tenant.");
        }

        var steps = new List<ProvisioningStepResult>();
        if (tenant is null)
        {
            var registerStarted = Stopwatch.GetTimestamp();
            tenant = await RegisterAsync(request, host, cancellationToken);
            steps.Add(new ProvisioningStepResult(ProvisioningSteps.Register, "Concluída", Stopwatch.GetElapsedTime(registerStarted), null));
        }

        var connection = protector.Unprotect(SecretScope.ForTenant(tenant.Id), SecretKind.TenantConnection, tenant.ConnectionSecret).Reveal();
        var completed = await catalog.CompletedStepsAsync(tenant.Id, cancellationToken);

        foreach (var step in ProvisioningSteps.Ordered.Skip(1))
        {
            if (completed.Contains(step))
            {
                steps.Add(new ProvisioningStepResult(step, "Já concluída", TimeSpan.Zero, null));
                continue;
            }

            var started = Stopwatch.GetTimestamp();
            await catalog.LogAsync(tenant.Id, step, ProvisioningStatus.Started, null, cancellationToken);
            try
            {
                await RunStepAsync(step, tenant, request, connection, cancellationToken);
                await catalog.LogAsync(tenant.Id, step, ProvisioningStatus.Succeeded, null, cancellationToken);
                steps.Add(new ProvisioningStepResult(step, "Concluída", Stopwatch.GetElapsedTime(started), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = Sanitize(ex.Message, connection);
                await catalog.LogAsync(tenant.Id, step, ProvisioningStatus.Failed, message, cancellationToken);
                steps.Add(new ProvisioningStepResult(step, "Falhou", Stopwatch.GetElapsedTime(started), message));
                return new ProvisioningResult(false, tenant.Id, steps, "A etapa " + step + " falhou: " + message,
                    "Corrija o problema e rode de novo com: tenant provision --key " + request.Key + " --resume");
            }
        }

        var primary = tenant.Domains.FirstOrDefault(d => d.IsPrimary)?.Host ?? host;
        return new ProvisioningResult(true, tenant.Id, steps, null,
            "Convite enviado para " + request.OwnerEmail + ". Acesse https://" + primary + " para concluir o cadastro.");
    }

    private async Task<Tenant> RegisterAsync(ProvisionRequest request, string host, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var database = DatabaseName(request.Key);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var connection = admin.TenantConnectionString(database, database, password);
        var tenant = Tenant.Create(id, request.Key, request.LegalName, database,
            protector.Protect(SecretScope.ForTenant(id), SecretKind.TenantConnection, new Secret(connection)), clock.GetUtcNow());
        tenant.Document = request.Document;
        tenant.PlanId = await catalog.EnsurePlanAsync(request.Plan, cancellationToken);
        tenant.Domains.Add(new TenantDomain { TenantId = id, Host = host, IsPrimary = true });
        await catalog.AddTenantAsync(tenant, cancellationToken);
        await modules.ApplyPlanAsync(id, tenant.PlanId, cancellationToken); // FND-06 CA 7: o tenant herda os módulos do plano
        await catalog.LogAsync(id, ProvisioningSteps.Register, ProvisioningStatus.Succeeded, null, cancellationToken);
        resolver.Invalidate();
        return tenant;
    }

    private async Task RunStepAsync(string step, Tenant tenant, ProvisionRequest request, string connection, CancellationToken cancellationToken)
    {
        var info = new TenantInfo(tenant.Id, tenant.Key, TenantStatus.Provisioning);
        switch (step)
        {
            case ProvisioningSteps.CreateRole:
                await admin.EnsureRoleAsync(tenant.DbName, connection, cancellationToken);
                break;
            case ProvisioningSteps.CreateDatabase:
                await admin.EnsureDatabaseAsync(tenant.DbName, tenant.DbName, cancellationToken);
                break;
            case ProvisioningSteps.Migrate:
                var (_, to) = await migrator.MigrateAsync(connection, cancellationToken);
                tenant.SchemaVersion = to;
                tenant.MigratedAt = clock.GetUtcNow();
                await catalog.SaveAsync(cancellationToken);
                resolver.Invalidate();
                break;
            case ProvisioningSteps.Seed:
                await using (var scope = await scopes.CreateScopeAsync(info, cancellationToken))
                {
                    foreach (var seeder in seeders.OrderBy(s => s.Order))
                    {
                        await seeder.SeedAsync(scope.ServiceProvider, cancellationToken);
                    }
                }

                break;
            case ProvisioningSteps.CreateOwner:
                await using (var scope = await scopes.CreateScopeAsync(info, cancellationToken))
                {
                    await onboarding.CreateOwnerAsync(tenant.Id, tenant.Key, request.OwnerEmail, scope.ServiceProvider, cancellationToken);
                }

                break;
            case ProvisioningSteps.SendInvite:
                await using (var scope = await scopes.CreateScopeAsync(info, cancellationToken))
                {
                    await onboarding.SendInviteAsync(tenant.Id, tenant.Key, request.OwnerEmail, scope.ServiceProvider, cancellationToken);
                }

                break;
            case ProvisioningSteps.Activate:
                tenant.Activate();
                await catalog.SaveAsync(cancellationToken);
                resolver.Invalidate();
                break;
        }
    }

    private static string? Validate(ProvisionRequest request, string host)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || !KeyPattern().IsMatch(request.Key))
        {
            return "Chave inválida: use de 3 a 31 letras minúsculas, números ou hífen, começando por letra.";
        }

        if (request.Resume)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.LegalName))
        {
            return "Informe a razão social (--legal-name).";
        }

        if (host != "localhost" && !HostPattern().IsMatch(host))
        {
            return "Endereço inválido: " + request.Host + ".";
        }

        if (string.IsNullOrWhiteSpace(request.OwnerEmail) || !MailAddress.TryCreate(request.OwnerEmail, out _))
        {
            return "Informe um e-mail válido para o proprietário (--owner-email).";
        }

        if (request.Document is { } document && document.Count(char.IsDigit) is not (11 or 14))
        {
            return "CPF/CNPJ inválido (--document).";
        }

        return null;
    }

    // Defesa extra: nenhuma mensagem de erro sai com a connection string do tenant.
    private static string Sanitize(string message, string connection)
    {
        var safe = message.Replace(connection, "***", StringComparison.Ordinal);
        return safe.Length > 1000 ? safe[..1000] : safe;
    }

    private static ProvisioningResult Failure(string error) => new(false, null, [], error, null);
}
