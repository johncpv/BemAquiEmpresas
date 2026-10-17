using System.Text.Json;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Messaging;

/// <summary>
/// Processa um lote do outbox de um tenant. As mensagens do lote ficam bloqueadas com
/// FOR UPDATE SKIP LOCKED, então dois dispatchers nunca pegam a mesma. Cada handler roda em
/// escopo e transação próprios, junto com a inbox: a falha de um não desfaz o outro, e uma
/// reentrega não repete o efeito.
/// </summary>
public sealed partial class OutboxProcessor(
    BemAquiDbContext db,
    ITenantScopeFactory scopes,
    IntegrationEventRegistry registry,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    IAlertSink alerts,
    ILogger<OutboxProcessor> logger)
{
    /// <summary>Processa até BatchSize mensagens; devolve quantas pegou.</summary>
    public async Task<int> ProcessBatchAsync(TenantInfo tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var settings = options.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var batchSize = settings.BatchSize;
        var batch = await db.OutboxMessages
            .FromSql($"SELECT * FROM shared.outbox_messages WHERE processed_at IS NULL AND dead_lettered_at IS NULL AND next_attempt_at <= {now} ORDER BY occurred_at, id LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            var error = await DeliverAsync(tenant, message, cancellationToken);
            if (error is null)
            {
                message.ProcessedAt = clock.GetUtcNow();
                message.Error = null;
                continue;
            }

            message.Attempts++;
            message.Error = error;
            if (message.Attempts >= settings.MaxAttempts)
            {
                message.DeadLetteredAt = clock.GetUtcNow();
                LogDeadLettered(logger, message.Id, message.Type, tenant.Key, message.Attempts, error);
                await alerts.RaiseAsync(new Alert(AlertKinds.OutboxDeadLetter, tenant.Key, "Evento " + message.Type + " foi para dead-letter", error), cancellationToken);
            }
            else
            {
                message.NextAttemptAt = clock.GetUtcNow() + settings.BackoffFor(message.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task<string?> DeliverAsync(TenantInfo tenant, OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!registry.TryGet(message.Type, out var registered))
        {
            return "Tipo de evento não registrado: " + message.Type;
        }

        object integrationEvent;
        try
        {
            integrationEvent = JsonSerializer.Deserialize(message.Payload, registered.EventType)
                ?? throw new JsonException("payload vazio");
        }
        catch (JsonException ex)
        {
            return "Payload inválido: " + ex.Message;
        }

        var errors = new List<string>();
        foreach (var handler in registered.Handlers)
        {
            try
            {
                await RunHandlerAsync(tenant, message.Id, handler, integrationEvent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogHandlerFailed(logger, ex, handler.Name, message.Id, tenant.Key);
                errors.Add(handler.Name + ": " + ex.Message);
            }
        }

        return errors.Count == 0 ? null : string.Join(" | ", errors);
    }

    private async Task RunHandlerAsync(TenantInfo tenant, Guid messageId, RegisteredHandler handler, object integrationEvent, CancellationToken cancellationToken)
    {
        using var tenantLog = Serilog.Context.LogContext.PushProperty("TenantId", tenant.Id);
        using var messageLog = Serilog.Context.LogContext.PushProperty("MessageId", messageId);
        using var handlerLog = Serilog.Context.LogContext.PushProperty("Handler", handler.Name);
        await using var scope = await scopes.CreateScopeAsync(tenant, cancellationToken);
        var handlerDb = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        await using var transaction = await handlerDb.Database.BeginTransactionAsync(cancellationToken);

        var alreadyDelivered = await handlerDb.InboxMessages.AnyAsync(i => i.MessageId == messageId && i.Handler == handler.Name, cancellationToken);
        if (alreadyDelivered)
        {
            return;
        }

        await handler.Invoke(scope.ServiceProvider.GetRequiredService(handler.HandlerType), integrationEvent, cancellationToken);
        handlerDb.InboxMessages.Add(new InboxMessage { MessageId = messageId, Handler = handler.Name, ProcessedAt = clock.GetUtcNow() });
        await handlerDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler {Handler} falhou para a mensagem {MessageId} do tenant {Tenant}.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string handler, Guid messageId, string tenant);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mensagem {MessageId} ({Type}) do tenant {Tenant} foi para dead-letter após {Attempts} tentativas: {Error}")]
    private static partial void LogDeadLettered(ILogger logger, Guid messageId, string type, string tenant, int attempts, string error);
}
