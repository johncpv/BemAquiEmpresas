using System.Globalization;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// PAT-03b: o valor combinado com o prestador vira o título a pagar previsto (<c>IPayables</c>, origem <c>Maintenance</c>, chamada síncrona na
/// transação da OS, ADR 0024 §1), com a conta e o centro de custo dos parâmetros do Patrimônio (CA 5); a OS de um bem com centro de trabalho
/// publica a parada (<c>MaintenanceScheduled</c>/<c>Unscheduled</c>/<c>Completed</c>) com as horas por dia informadas (CA 8 e 9). O
/// valor e o custo só com <c>assets.edit</c> (CA 7).
/// </summary>
public sealed partial class MaintenanceService
{
    public const string PayableConfirmationCode = "maintenance_payable_confirmation_required";
    public const string ValueRequiredCode = "maintenance_contract_value_required";
    public const string FinanceModule = "finance";

    /// <summary>Sugestões da OS (propositivo): prestador do plano, valor da última OS do plano (com <c>assets.edit</c>) e as horas paradas do centro.</summary>
    public async Task<Result<MaintenanceDefaults>> DefaultsAsync(Guid assetId, Guid? planId, CancellationToken cancellationToken = default)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return AssetService.NotFound();
        }

        var plan = planId is { } p ? await db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p && x.AssetId == assetId, cancellationToken) : null;
        decimal? value = null;
        if (plan is not null && await CanEditAsync(cancellationToken))
        {
            value = await db.MaintenanceOrders.AsNoTracking().Where(o => o.PlanId == plan.Id && o.ContractValue != null && o.Status != MaintenanceOrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedAt).Select(o => o.ContractValue).FirstOrDefaultAsync(cancellationToken);
        }

        var supplier = plan?.DefaultSupplierPartyId;
        var supplierName = supplier is { } s ? (await PartyNamesAsync([s], cancellationToken)).GetValueOrDefault(s) : null;
        var center = await CenterAsync(asset.WorkCenterId, cancellationToken);
        var machines = asset.WorkCenterId is { } c ? await ActiveMachinesAsync(c, cancellationToken) : 0;
        var hours = center is null ? (decimal?)null : MaintenanceRules.SuggestedHoursPerDay(center.HoursPerDay, machines);
        return new MaintenanceDefaults(supplier, supplierName, value, asset.WorkCenterId, center?.Name, center?.HoursPerDay, machines, hours is > 0 ? hours : null, hours is > 0 ? hours : null);
    }

    /// <summary>
    /// Reagendar e planejar (CA 1, 2 e 8): só a OS aberta ou em andamento. A data e o prestador mudam o título previsto (vencimento estimado
    /// e favorecido); o valor novo atualiza o título <c>Planned</c> e avisa quem tem <c>finance.edit</c>; as horas e a duração republicam a
    /// parada. Tudo numa transação, com o <c>xmin</c> da OS.
    /// </summary>
    public async Task<Result<MaintenanceOrderDetail>> ScheduleAsync(Guid id, MaintenanceScheduleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (order, asset) = await LoadAsync(id, input.Version, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        if (!MaintenanceRules.IsOpen(order.Status))
        {
            return Error.Conflict(StatusCode, "OS concluída ou cancelada não é reagendada. Ajuste no título a pagar.");
        }

        var scheduledOn = input.ScheduledOn ?? order.ScheduledOn;
        if (scheduledOn.Year < 2000 || scheduledOn > Today.AddYears(5))
        {
            return AssetService.Invalid("scheduledOn", "Informe a data agendada.");
        }

        if (input.SupplierPartyId is { } supplier && supplier != order.SupplierPartyId && !await IsActiveSupplierAsync(supplier, cancellationToken))
        {
            return AssetService.Invalid("supplierPartyId", "Escolha um fornecedor ativo da lista.");
        }

        var before = order.ContractValue;
        if (await ValueErrorAsync(input.ContractValue, input.SupplierPartyId, before, cancellationToken) is { } badValue)
        {
            return badValue;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var payableChanged = order.ScheduledOn != scheduledOn || order.SupplierPartyId != input.SupplierPartyId || before != input.ContractValue;
        order.ScheduledOn = scheduledOn;
        order.SupplierPartyId = input.SupplierPartyId;
        order.ContractValue = input.ContractValue;
        if (await PlanDowntimeAsync(order, asset!, input.HoursPerDay, input.ExpectedHours, cancellationToken) is { } badDowntime)
        {
            return badDowntime;
        }

        if (payableChanged && await SyncPlannedPayableAsync(order, asset!, cancellationToken) is { } badPayable)
        {
            return badPayable;
        }

        if (before is { } old && input.ContractValue is { } now && old != now)
        {
            await NotifyValueChangedAsync(order, old, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(order, cancellationToken);
    }

    /// <summary>Valor combinado (CA 1, 12): maior que zero, em centavos; só com <c>assets.edit</c>, com prestador e com o Financeiro liberado.</summary>
    private async Task<Error?> ValueErrorAsync(decimal? value, Guid? supplier, decimal? current, CancellationToken ct)
    {
        if (value is not null && supplier is null)
        {
            return AssetService.Invalid("contractValue", "Escolha o prestador para lançar o valor combinado.");
        }

        if (value == current)
        {
            return null;
        }

        if (!await CanEditAsync(ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "O valor combinado é lançado por quem edita o patrimônio.");
        }

        if (value is null)
        {
            return null;
        }

        if (!MaintenanceRules.IsValidMoney(value.Value))
        {
            return AssetService.Invalid("contractValue", "Informe um valor combinado maior que zero, em centavos.");
        }

        return FinanceReleased() ? null : Error.Forbidden(Error.Codes.ModuleNotReleased, ModuleEndpoints.NotReleasedMessage);
    }

    /// <summary>
    /// CA 8: as horas paradas por dia (> 0 e até as horas do centro; vazio = a sugestão) e a duração prevista (vazio = um dia de parada) dão o
    /// período da parada pelos dias úteis do calendário padrão, a partir do início (máquina parada: o dia da abertura). Mudou o período ou as
    /// horas, publica <c>MaintenanceScheduled</c>. Bem sem centro de trabalho: sem parada.
    /// </summary>
    private async Task<Error?> PlanDowntimeAsync(MaintenanceOrder order, Asset asset, decimal? hoursPerDay, decimal? expectedHours, CancellationToken ct)
    {
        var center = await CenterAsync(asset.WorkCenterId, ct);
        if (center is null)
        {
            order.HoursPerDay = null;
            order.ExpectedHours = null;
            return null;
        }

        var hours = hoursPerDay ?? order.HoursPerDay ?? MaintenanceRules.SuggestedHoursPerDay(center.HoursPerDay, await ActiveMachinesAsync(center.Id, ct));
        if (hours <= 0 && hoursPerDay is null)
        {
            // Centro sem horas (sem turno): não há capacidade a descontar.
            return null;
        }

        if (!MaintenanceRules.IsValidHoursPerDay(hours, center.HoursPerDay))
        {
            return AssetService.Invalid("hoursPerDay", $"Informe as horas paradas por dia, maiores que zero e até {MaintenanceRules.MeterText(center.HoursPerDay)} h (as horas do centro).");
        }

        var expected = expectedHours ?? order.ExpectedHours ?? hours;
        if (!MaintenanceRules.IsValidExpectedHours(expected))
        {
            return AssetService.Invalid("expectedHours", "Informe a duração prevista em horas paradas, maior que zero.");
        }

        var changed = order.HoursPerDay != hours;
        order.HoursPerDay = hours;
        order.ExpectedHours = expected;
        await PublishScheduledAsync(order, asset, changed, ct);
        return null;
    }

    /// <summary>Recalcula o período da parada e publica <c>MaintenanceScheduled</c> se mudou (ou se ainda não foi publicada).</summary>
    private async Task PublishScheduledAsync(MaintenanceOrder order, Asset asset, bool hoursChanged, CancellationToken ct)
    {
        if (asset.WorkCenterId is not { } center || order.HoursPerDay is not { } hours || order.ExpectedHours is not { } expected)
        {
            return;
        }

        var from = MaintenanceRules.DowntimeStart(order.MachineStopped, BusinessDate(order.CreatedAt), order.ScheduledOn, order.StartedAt is { } s ? BusinessDate(s) : null);
        var days = MaintenanceRules.DowntimeDays(expected, hours);
        var working = await calendar.WorkingDatesAsync(from, from.AddDays(Math.Min(days * 7 + 31, 3 * 365)), ct);
        var to = MaintenanceRules.DowntimeEnd(from, days, working);
        if (order.DowntimeRevision > 0 && order.DowntimeFrom == from && order.DowntimeTo == to && !hoursChanged)
        {
            return;
        }

        order.DowntimeFrom = from;
        order.DowntimeTo = to;
        order.DowntimeRevision++;
        events.Publish(new MaintenanceScheduled(order.Id, asset.Id, center, from, to, hours, MaintenanceRules.HoldsAsset(order.Status, order.MachineStopped), order.DowntimeRevision));
    }

    /// <summary>Conclusão ou cancelamento: a parada termina (ou sai) e o Production reconcilia.</summary>
    private void PublishDowntimeEnd(MaintenanceOrder order, Asset asset, DateOnly endedOn, bool cancelled)
    {
        if (asset.WorkCenterId is not { } center || order.DowntimeFrom is null)
        {
            return;
        }

        order.DowntimeRevision++;
        if (cancelled)
        {
            events.Publish(new MaintenanceUnscheduled(order.Id, asset.Id, center, endedOn, order.DowntimeRevision));
        }
        else
        {
            events.Publish(new MaintenanceCompleted(order.Id, asset.Id, center, endedOn, order.DowntimeRevision));
        }
    }

    /// <summary>
    /// CA 1 e 2: com prestador e valor, o título <c>Planned</c> nasce (ou é trocado: valor, vencimento estimado, favorecido, conta e centro) com
    /// a data estimada = data agendada + a primeira parcela da condição padrão do prestador; sem valor ou sem prestador, o previsto é
    /// cancelado pela origem. Sem projeto: nenhum custo de manutenção vai para projeto (CA 5).
    /// </summary>
    private async Task<Error?> SyncPlannedPayableAsync(MaintenanceOrder order, Asset asset, CancellationToken ct)
    {
        var existing = await payables.FindByOriginAsync(DocumentOrigin.Maintenance, order.Id, ct);
        var active = existing is not null && existing.Status != PayableStatus.Cancelled;
        if (order.SupplierPartyId is not { } supplier || order.ContractValue is not { } amount)
        {
            if (active && existing!.Status == PayableStatus.Planned)
            {
                var cancelled = await payables.CancelFromOriginAsync(DocumentOrigin.Maintenance, order.Id, $"valor combinado da OS {order.Number} retirado", ct);
                if (cancelled.IsFailure)
                {
                    return cancelled.Error;
                }

                order.PayableId = null;
            }

            return active && existing!.Status != PayableStatus.Planned ? PayableNotPlanned(existing) : null;
        }

        var target = await parameters.CostTargetAsync(asset.WorkCenterId is not null, ct);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var due = await SupplierDueAsync(supplier, amount, order.ScheduledOn, ct);
        var plan = PlannedPayables.For(Payee.Party(supplier), target.Value.AccountId, target.Value.CostCenterId,
            [new PlannedInstallment(1, amount, due, MaintenanceRules.PayableDescription(order.Number, asset.Tag, asset.Name))]);
        var result = active ? await payables.ReplacePlannedAsync(DocumentOrigin.Maintenance, order.Id, plan, ct) : await payables.CreatePlannedInstallmentsAsync(DocumentOrigin.Maintenance, order.Id, plan, ct);
        if (result.IsFailure)
        {
            return result.Error!.Code == "payable_not_planned" && existing is not null ? PayableNotPlanned(existing) : result.Error;
        }

        order.PayableId = result.Value[0].Id;
        return null;
    }

    /// <summary>
    /// CA 3: na conclusão com prestador, o título é confirmado (valor final, vencimento real = a conclusão + a primeira parcela da condição) e
    /// passa a <c>Open</c>, para ordem de pagamento; sem a confirmação do diálogo, a OS não conclui. Devolve o título confirmado.
    /// </summary>
    private async Task<Result<PayableRef?>> ConfirmPayableAsync(MaintenanceOrder order, Asset asset, MaintenanceCompleteInput input, DateOnly doneOn, CancellationToken ct)
    {
        if (order.SupplierPartyId is not { } supplier)
        {
            return Result.Success<PayableRef?>(null);
        }

        if (input.ContractValue is { } final && final != order.ContractValue && await ValueErrorAsync(final, supplier, order.ContractValue, ct) is { } badValue)
        {
            return badValue;
        }

        var amount = input.ContractValue ?? order.ContractValue;
        if (amount is null)
        {
            return Error.Validation(ValueRequiredCode, "Informe o valor combinado com o prestador para concluir.", new Dictionary<string, string[]> { ["contractValue"] = ["Informe o valor combinado com o prestador."] });
        }

        if (!FinanceReleased())
        {
            return Error.Forbidden(Error.Codes.ModuleNotReleased, ModuleEndpoints.NotReleasedMessage);
        }

        if (!await CanEditAsync(ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "O título do prestador é confirmado por quem edita o patrimônio.");
        }

        var due = await SupplierDueAsync(supplier, amount.Value, doneOn, ct);
        if (!input.ConfirmPayable)
        {
            var name = (await PartyNamesAsync([supplier], ct)).GetValueOrDefault(supplier) ?? "o prestador";
            return Error.Validation(PayableConfirmationCode, ConfirmationText(amount.Value, name, due));
        }

        var changed = order.ContractValue != amount;
        var before = order.ContractValue;
        order.ContractValue = amount;
        if (changed || order.PayableId is null)
        {
            if (await SyncPlannedPayableAsync(order, asset, ct) is { } badPayable)
            {
                return badPayable;
            }
        }

        var confirmed = await payables.ConfirmAsync(DocumentOrigin.Maintenance, order.Id, amount.Value, due, ct);
        if (confirmed.IsFailure)
        {
            return confirmed.Error!;
        }

        if (before is { } old && old != amount)
        {
            await NotifyValueChangedAsync(order, old, amount.Value, ct);
        }

        order.PayableId = confirmed.Value.Id;
        return confirmed.Value;
    }

    /// <summary>
    /// CA 4: OS cancelada com o título ainda previsto, o título cai pela origem; já em aberto (ou numa OP), fica para o financeiro decidir, com o
    /// aviso a quem tem <c>finance.edit</c> ("Origem cancelada").
    /// </summary>
    private async Task CancelPayableAsync(MaintenanceOrder order, CancellationToken ct)
    {
        var existing = await payables.FindByOriginAsync(DocumentOrigin.Maintenance, order.Id, ct);
        if (existing is null || existing.Status == PayableStatus.Cancelled)
        {
            return;
        }

        if (existing.Status == PayableStatus.Planned && (await payables.CancelFromOriginAsync(DocumentOrigin.Maintenance, order.Id, $"OS {order.Number} cancelada", ct)).IsSuccess)
        {
            return;
        }

        foreach (var user in await users.UsersWithPermissionAsync("finance.edit", ct))
        {
            notifications.Notify(user, "assets.maintenance_cancelled", $"Origem cancelada: OS {order.Number} cancelada com o título {existing.Number} do prestador. Decida o título.",
                "/app/financeiro/pagar/" + existing.Id);
        }
    }

    /// <summary>CA 2: "Valor da manutenção {MAN} mudou de {antes} para {depois} por {usuário}." a quem tem <c>finance.edit</c>.</summary>
    private async Task NotifyValueChangedAsync(MaintenanceOrder order, decimal before, decimal after, CancellationToken ct)
    {
        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "um usuário" : "o sistema";
        foreach (var user in await users.UsersWithPermissionAsync("finance.edit", ct))
        {
            notifications.Notify(user, "assets.maintenance_value", $"Valor da manutenção {order.Number} mudou de {Money(before)} para {Money(after)} por {who}.", OrderLink + order.Id);
        }
    }

    /// <summary>A data base + a primeira parcela da condição padrão do prestador (CA 1 e 3); sem condição, a própria data.</summary>
    private async Task<DateOnly> SupplierDueAsync(Guid supplier, decimal amount, DateOnly baseDate, CancellationToken ct) =>
        await parties.FindAsync(supplier, ct) is { DefaultPaymentTermId: { } term } && await terms.SplitAsync(term, amount, baseDate, ct) is { Count: > 0 } split
            ? split[0].DueDate : MaintenanceRules.EstimatedDue(baseDate, null);

    /// <summary>Texto do diálogo de conclusão (CA 3), também a mensagem do 422 sem a confirmação.</summary>
    public static string ConfirmationText(decimal amount, string supplierName, DateOnly due) =>
        $"Confirma o título a pagar de {Money(amount)} ao prestador {supplierName}, com vencimento em {due.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, que segue para ordem de pagamento.";

    private static Error PayableNotPlanned(PayableRef payable) =>
        Error.Conflict("payable_not_planned", $"O título {payable.Number} já não está previsto. Ajuste no título a pagar.");

    private bool FinanceReleased() => tenantHolder.Resolved?.IsReleased(FinanceModule) == true;

    private async Task<bool> CanEditAsync(CancellationToken ct) => await permissions.HasPermissionAsync(AssetService.Module, StandardActions.Edit, ct);

    private async Task<BemAqui.Production.Contracts.WorkCenterOption?> CenterAsync(Guid? id, CancellationToken ct) =>
        id is { } c && await workCenters.FindAsync([c], ct) is [var center, ..] ? center : null;

    /// <summary>Máquinas ativas do centro (CA 8): bens do tipo máquina não baixados nem excluídos com o centro.</summary>
    private Task<int> ActiveMachinesAsync(Guid workCenterId, CancellationToken ct) =>
        db.Assets.AsNoTracking().CountAsync(a => a.WorkCenterId == workCenterId && a.Type == AssetType.Machine && a.Status != AssetStatus.Disposed, ct);

    private static DateOnly BusinessDate(DateTimeOffset at) => DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime);

    private static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));
}
