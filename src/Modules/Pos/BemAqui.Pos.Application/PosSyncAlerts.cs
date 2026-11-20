using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Pos.Application;

/// <summary>
/// Aviso de caixa sem enviar vendas (OFF-02 CA 16): "Caixa 02 sem enviar vendas há N horas." para quem aprova vendas (<c>sales.approve</c>),
/// uma vez por caixa e por período de silêncio. Dois caminhos: o aparelho faz contato e diz o evento mais antigo da fila; ou o prazo vence no
/// servidor com o caixa aberto e sem contato (job).
/// </summary>
public sealed class PosSyncAlerts(IPosDb db, IUserDirectory users, INotificationService notifications, IRegisterDevices devices, TimeProvider clock)
{
    public const string NotificationType = "pos.sync_delay";
    public const string Link = "/app/loja/pendencias";

    public static string Message(int registerNumber, int hours) =>
        string.Create(CultureInfo.InvariantCulture, $"Caixa {registerNumber:00} sem enviar vendas há {hours} {(hours == 1 ? "hora" : "horas")}.");

    /// <summary>O aparelho fez contato: a fila dele tem evento mais velho que o aviso da loja.</summary>
    public async Task OnContactAsync(Store store, Register register, DateTimeOffset? oldestPendingAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(register);
        if (oldestPendingAt is not { } oldest)
        {
            return;
        }

        var age = clock.GetUtcNow() - oldest;
        if (age >= TimeSpan.FromHours(store.SyncAlertHours))
        {
            await NotifyAsync(register, (int)age.TotalHours, Window(store), cancellationToken);
        }
    }

    /// <summary>O prazo venceu no servidor: caixa com sessão aberta e sem contato (nem evento, nem sincronização) há mais que o aviso da loja.</summary>
    public async Task<int> CheckSilentRegistersAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var open = await (
            from s in db.CashSessions.AsNoTracking()
            where s.Status == CashSessionStatus.Open
            join r in db.Registers.AsNoTracking() on s.RegisterId equals r.Id
            join st in db.Stores.AsNoTracking() on r.StoreId equals st.Id
            where r.IsActive && st.IsActive && r.ActiveDeviceId != null
            select new { Register = r, st.SyncAlertHours, s.OpenedAt }).ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return 0;
        }

        var deviceInfo = await devices.FindAsync(open.Select(o => o.Register.ActiveDeviceId!.Value).Distinct().ToList(), cancellationToken);
        var sent = 0;
        foreach (var row in open)
        {
            var registerId = row.Register.Id;
            var lastEvent = await db.DeviceEvents.AsNoTracking().Where(e => e.RegisterId == registerId).MaxAsync(e => (DateTimeOffset?)e.ReceivedAt, cancellationToken);
            var info = deviceInfo.GetValueOrDefault(row.Register.ActiveDeviceId!.Value);
            var lastContact = new[] { lastEvent, info?.LastSyncAt, info?.LastSeenAt, row.OpenedAt }.Where(d => d is not null).Max()!.Value;
            var silence = now - lastContact;
            if (silence >= TimeSpan.FromHours(row.SyncAlertHours))
            {
                await NotifyAsync(row.Register, (int)silence.TotalHours, Window(row.SyncAlertHours), cancellationToken);
                sent++;
            }
        }

        return sent;
    }

    /// <summary>
    /// Janela do servidor para o aviso (revisão de segurança M3): um aviso por caixa a cada período do aviso da loja, contado no relógio do servidor;
    /// o aparelho não escolhe a chave (antes, o instante da fila dele).
    /// </summary>
    private string Window(Store store) => Window(store.SyncAlertHours);

    private string Window(int hours) =>
        (clock.GetUtcNow().ToUnixTimeSeconds() / (Math.Max(hours, 1) * 3600L)).ToString(CultureInfo.InvariantCulture);

    private async Task NotifyAsync(Register register, int hours, string window, CancellationToken cancellationToken)
    {
        foreach (var user in await users.UsersWithPermissionAsync("sales.approve", cancellationToken))
        {
            await notifications.NotifyOnceAsync(user, NotificationType, Message(register.Number, Math.Max(hours, 1)), Link + "?registerId=" + register.Id,
                "pos-sync:" + register.Id.ToString("N") + ":" + window, cancellationToken);
        }
    }
}

public sealed record PosSyncAlertArgs;

/// <summary>Execução por tenant (recebe o <c>tenantId</c>, ADR 0006): o aviso de caixa aberto e sem contato (OFF-02 CA 16).</summary>
public sealed partial class PosSyncAlertJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PosSyncAlertJob> logger)
    : TenantJob<PosSyncAlertArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, PosSyncAlertArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        var sent = await services.GetRequiredService<PosSyncAlerts>().CheckSilentRegistersAsync(cancellationToken);
        if (sent > 0)
        {
            LogSent(logger, sent, tenant.Key);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} aviso(s) de caixa sem enviar vendas no tenant {Tenant}.")]
    private static partial void LogSent(ILogger logger, int count, string tenant);
}

/// <summary>Recorrente global a cada 30 minutos que enfileira a conferência por tenant ativo.</summary>
public sealed class PosSyncAlertRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<PosSyncAlertJob, PosSyncAlertArgs>(directory, jobs)
{
    public const string Id = "pos-sync-alerts";
    public const string Cron = "*/30 * * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override PosSyncAlertArgs ArgsFor(TenantInfo tenant) => new();
}
