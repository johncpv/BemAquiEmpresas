using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

public sealed record FinanceDeadLetter(Guid Id, string Event, Guid? ContractId, string? ContractNumber, DateTimeOffset OccurredAt, DateTimeOffset DeadLetteredAt, string? Error);

/// <summary>
/// Eventos que o Finance consome (COM-04b CA 5): quando um vai para dead-letter, quem tem <c>finance.edit</c> é avisado e
/// pode reprocessar; a lista e o reprocessamento só alcançam estes tipos.
/// </summary>
public sealed class FinanceDeadLetters(DeadLetterRequeue queue, IEnumerable<IContracts> contracts, IUserDirectory users, INotificationService notifications) : IDeadLetterObserver
{
    public const string NotificationType = "finance.dead_letter";

    private static readonly Dictionary<string, string> Labels = new()
    {
        [IntegrationEventRegistry.NameOf(typeof(ContractSigned))] = "Parcelas do contrato",
        [IntegrationEventRegistry.NameOf(typeof(ProjectFabricated))] = "Fabricação concluída",
        [IntegrationEventRegistry.NameOf(typeof(ProjectDelivered))] = "Entrega",
        [IntegrationEventRegistry.NameOf(typeof(TechnicalAcceptance))] = "Aceite técnico",
        [IntegrationEventRegistry.NameOf(typeof(ContractCancelled))] = "Cancelamento do contrato",
    };

    public static IReadOnlyCollection<string> Types => Labels.Keys;

    public async Task OnDeadLetteredAsync(DeadLetteredMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!Labels.ContainsKey(message.Type))
        {
            return;
        }

        var number = ContractIdOf(message.Payload) is { } id ? (await ContractEvents.FindAsync(contracts, id, cancellationToken))?.Number : null;
        var title = message.Type == IntegrationEventRegistry.NameOf(typeof(ContractSigned))
            ? $"Não foi possível gerar as parcelas do contrato {number ?? "novo"}."
            : $"Não foi possível processar \"{Labels[message.Type]}\" do contrato {number ?? "—"}.";
        foreach (var user in await users.UsersWithPermissionAsync("finance.edit", cancellationToken))
        {
            notifications.Notify(user, NotificationType, title, "/app/financeiro/receber?reprocessar=1");
        }
    }

    public async Task<IReadOnlyList<FinanceDeadLetter>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<FinanceDeadLetter>();
        foreach (var m in await queue.ListAsync(Types, cancellationToken))
        {
            var payload = await queue.PayloadAsync(m.Id, cancellationToken);
            var contractId = payload is null ? null : ContractIdOf(payload);
            var number = contractId is { } id ? (await ContractEvents.FindAsync(contracts, id, cancellationToken))?.Number : null;
            list.Add(new FinanceDeadLetter(m.Id, Labels[m.Type], contractId, number, m.OccurredAt, m.DeadLetteredAt, m.Error));
        }

        return list;
    }

    public Task<Result> ReprocessAsync(Guid id, CancellationToken cancellationToken = default) => queue.RequeueAsync(id, Types, cancellationToken);

    private static Guid? ContractIdOf(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.TryGetProperty("ContractId", out var value) && value.TryGetGuid(out var id) ? id
                : json.RootElement.TryGetProperty("contractId", out var camel) && camel.TryGetGuid(out var id2) ? id2 : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
