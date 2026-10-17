using System.Security.Claims;
using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Tenancy;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

namespace BemAqui.Host.Observability;

/// <summary>Seção <c>Logging:File</c>: log JSON em arquivo no volume, com rotação diária e por tamanho (FND-12 CA 9).</summary>
public sealed class FileLogOptions
{
    public const string Section = "Logging:File";

    public string? Path { get; set; }

    public long FileSizeLimitBytes { get; set; } = 100L * 1024 * 1024;

    public int RetainedDays { get; set; } = 30;
}

public static class LoggingSetup
{
    /// <summary>Serilog: máscara de segredo por tipo e por nome, JSON fora de Development, arquivo com rotação.</summary>
    public static LoggerConfiguration Configure(LoggerConfiguration logger, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(environment);
        logger
            .ReadFrom.Configuration(configuration)
            .Destructure.With<SecretDestructuringPolicy>()
            .Enrich.FromLogContext()
            .Enrich.With<SensitivePropertyMask>()
            .Enrich.WithProperty("Application", "bemaqui-empresas");

        if (environment.IsDevelopment())
        {
            logger.WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        }

        var file = configuration.GetSection(FileLogOptions.Section).Get<FileLogOptions>();
        if (!string.IsNullOrWhiteSpace(file?.Path))
        {
            logger.WriteTo.File(
                new RenderedCompactJsonFormatter(),
                file.Path,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: file.FileSizeLimitBytes,
                retainedFileTimeLimit: TimeSpan.FromDays(file.RetainedDays));
        }

        return logger;
    }

    /// <summary>Todo log do request leva TenantId, UserId e CorrelationId (FND-12 CA 1).</summary>
    public static IApplicationBuilder UseRequestLogContext(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var tenant = context.RequestServices.GetRequiredService<TenantContextHolder>().Tenant;
            using var correlation = LogContext.PushProperty("CorrelationId", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
            using var tenantId = LogContext.PushProperty("TenantId", tenant?.Id);
            using var userId = LogContext.PushProperty("UserId", context.User.FindFirstValue("sub"));
            await next(context);
        });

    /// <summary>Uma linha por request com caminho, status e tempo (sem cabeçalhos nem corpo).</summary>
    public static IApplicationBuilder UseBemAquiRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(o =>
        {
            o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} respondeu {StatusCode} em {Elapsed:0.0} ms";
            // Logger do próprio host (não o estático global): com vários hosts no processo, cada um loga no seu.
            o.Logger = app.ApplicationServices.GetService<Serilog.ILogger>();
            o.EnrichDiagnosticContext = (diagnostics, http) =>
            {
                diagnostics.Set("TenantId", http.RequestServices.GetRequiredService<TenantContextHolder>().Tenant?.Id);
                diagnostics.Set("UserId", http.User.FindFirstValue("sub"));
                diagnostics.Set("CorrelationId", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier);
            };
        });
}
