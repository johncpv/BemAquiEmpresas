using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Jobs;

/// <summary>Seção <c>Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string Section = "Jobs";

    /// <summary>Liga o servidor de jobs neste processo.</summary>
    public bool ServerEnabled { get; set; } = true;

    public int WorkerCount { get; set; } = 2;

    /// <summary>
    /// Intervalo de busca da fila sem job (segundos; padrão do Hangfire.PostgreSql: 15). PLN-01b CA 4: o recálculo pedido por evento chega à
    /// tela em até 10 s, então a fila é olhada a cada 2 s.
    /// </summary>
    public int QueuePollSeconds { get; set; } = 2;
}

public static class JobsServiceCollectionExtensions
{
    public const string Schema = "hangfire";

    /// <summary>Hangfire com storage no banco de catálogo (schema <c>hangfire</c>) e filas separadas (ADR 0006).</summary>
    public static IServiceCollection AddBemAquiJobs(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var catalog = configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog não configurada: o storage do Hangfire fica no catálogo.");
        var options = configuration.GetSection(JobsOptions.Section).Get<JobsOptions>() ?? new JobsOptions();

        services.TryAddSingleton<Observability.IAlertSink, Observability.LogOnlyAlertSink>();
        services.AddHangfire((sp, c) => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(catalog), new PostgreSqlStorageOptions
            {
                SchemaName = Schema, PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.FromSeconds(Math.Clamp(options.QueuePollSeconds, 1, 15)),
            })
            .UseFilter(new JobLogContextFilter())
            .UseFilter(new JobFailureAlertFilter(sp.GetRequiredService<Observability.IAlertSink>(), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Observability.AlertOptions>>().Value)));

        if (options.ServerEnabled)
        {
            services.AddHangfireServer(s =>
            {
                s.Queues = JobQueues.All;
                s.WorkerCount = Math.Max(1, options.WorkerCount);
            });
        }

        return services;
    }
}
