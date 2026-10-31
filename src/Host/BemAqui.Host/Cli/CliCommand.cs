using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.Host.Observability;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Host.Persistence;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Endpoints;
using BemAqui.Host.Security;
using Serilog;

namespace BemAqui.Host.Cli;

/// <summary>
/// Modo CLI da imagem (ADR 0006): <c>migrate</c> (catálogo e todos os tenants, antes da subida da API) e
/// <c>tenant provision</c> (FND-03a), <c>platform-admin create</c> (FND-13) e <c>alert raise</c> (FND-10, falha do backup), <c>tenant invite-owner</c> (link do convite do proprietário sem e-mail) e <c>tenant import-dolibarr</c> (MIG-01a). Saída em PT-BR; código de saída 0 = sucesso.
/// </summary>
public static class CliCommand
{
    /// <summary>
    /// Módulos compostos no CLI. Todo módulo com <c>ITenantSeeder</c> precisa estar aqui, senão o seed não roda no
    /// <c>migrate</c> dos tenants existentes (teste <c>CliModulesTests</c>). A importação grava nos destinos pelos
    /// <c>I*Import</c> dos módulos donos (ADR 0017).
    /// </summary>
    public static IReadOnlyList<BemAqui.BuildingBlocks.Modules.IModule> Modules { get; } =
    [
        new PlatformModule(),
        new BemAqui.Identity.Endpoints.IdentityModule(),
        new BemAqui.Parties.Endpoints.PartiesModule(),
        new BemAqui.Finance.Endpoints.FinanceModule(),
        new BemAqui.Inventory.Endpoints.InventoryModule(),
        new BemAqui.Engineering.Endpoints.EngineeringModule(),
        new BemAqui.Production.Endpoints.ProductionModule(),
        new BemAqui.HR.Endpoints.HRModule(),
        new BemAqui.Fiscal.Endpoints.FiscalModule(),
        new BemAqui.Migrations.Endpoints.MigrationModule(),
    ];

    public static bool IsCli(string[] args) => args is ["migrate", ..] or ["tenant", "provision", ..] or ["tenant", "invite-owner", ..] or ["tenant", "import-dolibarr", ..] or ["platform-admin", "create", ..] or ["alert", "raise", ..];

    public static async Task<int> RunAsync(string[] args, Action<IConfigurationBuilder>? configure = null, TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        output ??= Console.Out;
        var migrate = args[0] == "migrate";
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Production,
        });
        if (migrate)
        {
            // O migrator é quem migra catálogo e key ring; a API em homolog/prod não migra sozinha.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:AutoMigrate"] = "true", ["DataProtection:AutoMigrate"] = "true" });
        }

        builder.Configuration.AddSecretFiles();
        configure?.Invoke(builder.Configuration);
        builder.Services.AddSerilog(lc => LoggingSetup.Configure(lc, builder.Configuration, builder.Environment));
        builder.Services.AddBemAquiEmail(builder.Configuration);
        // O convite do proprietário vai para o outbox do tenant; quem envia é a API (dispatcher desligado aqui).
        builder.Services.AddBemAquiMessaging(builder.Configuration, HostEvents.Register, runDispatcher: false);
        builder.Services.AddBemAquiSecrets(builder.Configuration, builder.Environment);
        foreach (var module in Modules)
        {
            module.AddServices(builder.Services, builder.Configuration);
        }

        builder.Services.AddTenantPersistence();
        builder.Services.Configure<BemAqui.Host.Tenancy.PlatformHostOptions>(builder.Configuration.GetSection(BemAqui.Host.Tenancy.PlatformHostOptions.Section));
        builder.Services.AddSingleton<ITenantDatabaseMigrator, TenantDatabaseMigrator>();
        builder.Services.AddScoped<TenantMigrationRunner>();

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            return args[0] switch
            {
                "migrate" => await MigrateAsync(scope.ServiceProvider, output),
                "tenant" when args[1] == "import-dolibarr" => await DolibarrImportCommand.RunAsync(scope.ServiceProvider, new CliArguments(args.Skip(2).ToList()), output),
                "tenant" when args[1] == "invite-owner" => await InviteOwnerAsync(scope.ServiceProvider, new CliArguments(args.Skip(2).ToList()), output),
                "alert" => await RaiseAlertAsync(scope.ServiceProvider, new CliArguments(args.Skip(2).ToList()), output),
                "platform-admin" => await CreatePlatformAdminAsync(scope.ServiceProvider, new CliArguments(args.Skip(2).ToList()), output),
                _ => await ProvisionAsync(scope.ServiceProvider, new CliArguments(args.Skip(2).ToList()), output),
            };
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<int> MigrateAsync(IServiceProvider services, TextWriter output)
    {
        await output.WriteLineAsync("Catálogo e key ring migrados. Migrando os bancos de tenant...");
        var reports = await services.GetRequiredService<TenantMigrationRunner>().MigrateAllAsync(CancellationToken.None);
        foreach (var r in reports)
        {
            await output.WriteLineAsync(r.Success
                ? $"  OK     {r.Tenant,-20} {r.From ?? "(vazio)"} -> {r.To}  ({r.Duration.TotalSeconds:0.0}s)"
                : $"  FALHOU {r.Tenant,-20} {r.Error}");
        }

        var failed = reports.Count(r => !r.Success);
        if (failed > 0)
        {
            var details = string.Join(Environment.NewLine, reports.Select(r => (r.Success ? "OK     " : "FALHOU ") + r.Tenant + " " + (r.Error ?? r.To)));
            await services.GetRequiredService<IAlertSink>().RaiseAsync(new Alert(AlertKinds.MigrationFailed, null, "Migração falhou em " + failed + " de " + reports.Count + " tenant(s)", details));
        }

        await output.WriteLineAsync(failed == 0
            ? $"Migração concluída: {reports.Count} tenant(s)."
            : $"Migração com falha em {failed} de {reports.Count} tenant(s). A API responde 503 para eles até migrar.");
        return failed == 0 ? 0 : 2;
    }

    /// <summary>
    /// Único jeito de criar super admin (FND-13 CA 1): não há rota pública. O link de definição de senha aparece só
    /// aqui, uma vez, para quem tem acesso SSH ao servidor; o 2FA é configurado no primeiro login.
    /// </summary>
    private static async Task<int> CreatePlatformAdminAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var result = await services.GetRequiredService<BemAqui.Platform.Application.Auth.PlatformAuthService>().InviteAsync(args.Get("email") ?? string.Empty);
        if (result.IsFailure)
        {
            await output.WriteLineAsync("Super admin não criado: " + result.Error!.Message);
            return 1;
        }

        var host = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BemAqui.Host.Tenancy.PlatformHostOptions>>().Value.Host;
        await output.WriteLineAsync("Super admin criado. Abra o link abaixo para definir a senha (vale até " + result.Value.ExpiresAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture) + " e não aparece de novo):");
        await output.WriteLineAsync("https://" + host + "/plataforma/definir-senha?token=" + Uri.EscapeDataString(result.Value.SetPasswordToken));
        await output.WriteLineAsync("No primeiro login, a configuração do 2FA é obrigatória.");
        return 0;
    }

    /// <summary>
    /// Alerta vindo de fora da API (FND-10 CA 14): o cron do backup chama <c>alert raise --kind backup_failed --summary ...</c>
    /// quando o backup falha, e o alerta segue o mesmo caminho dos outros (e-mail aos super admins, dedup de 30 min).
    /// </summary>
    private static async Task<int> RaiseAlertAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var kind = args.Get("kind");
        var summary = args.Get("summary");
        if (kind is null || !AlertKinds.External.Contains(kind) || string.IsNullOrWhiteSpace(summary))
        {
            await output.WriteLineAsync("Uso: alert raise --kind <" + string.Join("|", AlertKinds.External) + "> --summary \"texto\" [--details \"texto\"]");
            return 1;
        }

        await services.GetRequiredService<IAlertSink>().RaiseAsync(new Alert(kind, args.Get("tenant"), summary, args.Get("details")));
        await output.WriteLineAsync("Alerta enviado: " + kind + ".");
        return 0;
    }

    /// <summary>
    /// Link do convite do proprietário sem e-mail (FND-10b): como no <c>platform-admin create</c>, aparece só aqui, uma vez,
    /// para quem tem SSH no servidor. Gera um token novo; o link anterior (inclusive o do e-mail) deixa de valer.
    /// </summary>
    private static async Task<int> InviteOwnerAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var key = args.Get("key");
        var tenant = (await services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantDirectory>().GetActiveAsync()).FirstOrDefault(t => t.Key == key);
        if (tenant is null)
        {
            await output.WriteLineAsync("Tenant não encontrado ou não ativo. Uso: tenant invite-owner --key <chave>");
            return 1;
        }

        await using var scope = await services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>().CreateScopeAsync(tenant);
        var link = await scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Contracts.ITenantOwnerOnboarding>().IssueOwnerLinkAsync(scope.ServiceProvider, CancellationToken.None);
        if (link is null)
        {
            await output.WriteLineAsync("Não há convite pendente do proprietário: ele já criou a senha. Para recuperar o acesso, use \"Esqueci minha senha\".");
            return 1;
        }

        await output.WriteLineAsync("Convite do proprietário " + link.Email + ". Abra o link abaixo para criar a senha (vale até " + link.ExpiresAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture) + " e não aparece de novo; links anteriores deixam de valer):");
        await output.WriteLineAsync(link.Link);
        return 0;
    }

    private static async Task<int> ProvisionAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var request = new ProvisionRequest(
            args.Get("key") ?? string.Empty,
            args.Get("legal-name") ?? string.Empty,
            args.Get("document"),
            args.Get("host") ?? string.Empty,
            args.Get("owner-email") ?? string.Empty,
            args.Get("plan"),
            args.Has("resume"));

        var result = await services.GetRequiredService<ProvisioningService>().ProvisionAsync(request);
        foreach (var step in result.Steps)
        {
            await output.WriteLineAsync($"  {step.Status,-12} {step.Step,-15} {step.Duration.TotalSeconds:0.0}s {step.Message}");
        }

        await output.WriteLineAsync(result.Success ? "Tenant provisionado." : "Provisionamento não concluído: " + result.Error);
        if (result.NextStep is not null)
        {
            await output.WriteLineAsync(result.NextStep);
        }

        return result.Success ? 0 : 1;
    }
}
