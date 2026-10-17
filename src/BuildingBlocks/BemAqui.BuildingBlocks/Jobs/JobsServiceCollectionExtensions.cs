using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Jobs;

/// <summary>Seção <c>Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string Section = "Jobs";

    /// <summary>Liga o servidor de jobs neste processo.</summary>
    public bool ServerEnabled { get; set; } = true;

    public int WorkerCount { get; set; } = 2;
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

        services.AddHangfire(c => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(catalog), new PostgreSqlStorageOptions { SchemaName = Schema, PrepareSchemaIfNecessary = true }));

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
