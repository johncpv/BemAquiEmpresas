using System.Text.Json;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// OFF-03: a subida do que foi feito sem rede. <c>pos.auth</c> leva a entrada sem rede (com a prova do PIN), o bloqueio por 5 erros e o relógio
/// do aparelho antes do piso; <c>pos.approval</c> leva a aprovação de supervisor sem rede (com a prova do código, PP-39), que o servidor reconfere
/// (prova, pessoas, permissão no instante e limites da loja) antes de gravar em <c>pos.supervisor_approvals</c> com <c>online = false</c>. Depois disso,
/// a venda que cita a aprovação passa pela mesma conferência da PDV-04. O que não confere fica na quarentena; a venda concluída nunca é recusada.
/// </summary>
public sealed partial class PosEventIngestion
{
    public const string AuthKind = "pos.auth";
    public const string ApprovalKind = "pos.approval";

    public const string OfflineLoginType = "OfflineLogin";
    public const string OfflineLockedType = "OfflineLocked";
    public const string ClockBehindType = "ClockBehind";

    /// <summary>A entrada sem rede vale como a sessão por PIN do caixa: até 12 h (a mesma da sessão com rede).</summary>
    public static readonly TimeSpan OfflineSessionLifetime = TimeSpan.FromHours(12);

    private async Task<Projection> ProjectAuthAsync(SyncContext context, Guid eventId, JsonElement root, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var type = PosSaleReplay.StringOf(root, "type");
        var userId = PosSaleReplay.GuidOf(root, "userId");
        var projection = new Projection { OperatorId = userId };
        switch (type)
        {
            case OfflineLoginType:
            {
                var loginId = PosSaleReplay.GuidOf(root, "loginId");
                var at = PosSaleReplay.StringOf(root, "at");
                if (userId is null || loginId is null || at is null || PosSaleReplay.DateOf(root, "at") is not { } when || (when - occurredAt).Duration() > ClockTolerance)
                {
                    return Hold(projection, SyncDivergenceKind.Validation, "Entrada sem rede sem a pessoa, o id ou o instante.");
                }

                var check = await offlineAccess.CheckLoginAsync(context.Device.DeviceId, context.Store.Id, userId.Value,
                    PosOfflineProof.LoginMessage(context.Device.DeviceId, userId.Value, loginId.Value, at), PosSaleReplay.StringOf(root, "proof"), ct);
                if (check != PosOfflineCheck.Valid)
                {
                    // CA 4: a entrada fica registrada e marcada; o que a pessoa fez nela não tem sessão conferida e vai para a quarentena.
                    projection.Divergences.Add(new Finding(SyncDivergenceKind.OfflineAccess, OfflineLoginProblem(check, when), new { userId, loginId, check = check.ToString() }));
                }

                return projection;
            }

            case OfflineLockedType:
                if (userId is null)
                {
                    return Hold(projection, SyncDivergenceKind.Validation, "Bloqueio sem rede sem a pessoa.");
                }

                await offlineAccess.RecordLockAsync(context.Device.DeviceId, userId.Value, PosSaleReplay.StringOf(root, "kind") ?? "operator",
                    PosSaleReplay.DateOf(root, "at") ?? occurredAt, ct);
                return projection;

            case ClockBehindType:
                projection.Divergences.Add(new Finding(SyncDivergenceKind.DeviceClock,
                    $"Sem rede, o relógio do aparelho voltou para antes do último horário conhecido ({Local(PosSaleReplay.DateOf(root, "floorAt") ?? occurredAt)}): o caixa não abriu turno novo até sincronizar.",
                    new { deviceAt = PosSaleReplay.StringOf(root, "deviceAt"), floorAt = PosSaleReplay.StringOf(root, "floorAt") }));
                return projection;

            default:
                return Hold(projection, SyncDivergenceKind.Validation, $"Evento de acesso desconhecido: {type}.");
        }
    }

    private static string OfflineLoginProblem(PosOfflineCheck check, DateTimeOffset at) => check switch
    {
        PosOfflineCheck.UserInactive => $"Entrada sem rede às {Local(at)} de quem já estava desativado, fora da loja ou sem PIN do caixa: confira o que a pessoa fez antes de liberar.",
        PosOfflineCheck.SecretChanged => $"Entrada sem rede às {Local(at)} com um PIN que já tinha sido trocado: confira o que a pessoa fez antes de liberar.",
        _ => $"Entrada sem rede às {Local(at)} com uma prova do PIN que não confere: confira o aparelho e o que foi feito nele antes de liberar.",
    };

    /// <summary>
    /// OFF-03 CA 2: entrada sem rede conferida deste usuário neste aparelho até 12 h antes do instante (um <c>pos.auth</c> aceito e sem pendência
    /// de acesso sem rede).
    /// </summary>
    private async Task<bool> HadOfflineSessionAsync(SyncContext context, Guid userId, DateTimeOffset at, CancellationToken ct)
    {
        var from = at - OfflineSessionLifetime;
        var until = at + ClockTolerance;
        var logins = await db.DeviceEvents.AsNoTracking()
            .Where(e => e.DeviceId == context.Device.DeviceId && e.Kind == AuthKind && e.Outcome != DeviceEventOutcome.Quarantined && e.OccurredAt >= from && e.OccurredAt <= until)
            .Select(e => new { e.EventId, e.Payload }).ToListAsync(ct);
        foreach (var login in logins)
        {
            using var document = JsonDocument.Parse(login.Payload);
            if (PosSaleReplay.StringOf(document.RootElement, "type") == OfflineLoginType && PosSaleReplay.GuidOf(document.RootElement, "userId") == userId
                && !await db.SyncDivergences.AnyAsync(d => d.EventId == login.EventId && d.Kind == SyncDivergenceKind.OfflineAccess, ct))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// OFF-03 CA 7, 13 e 15: a aprovação de supervisor sem rede. Confere o formato e a direção dos valores, supervisor diferente do operador, a ação
    /// permitida sem rede (a troca de preço precisa de rede), a sessão de caixa deste caixa, a prova do código do supervisor, o operador com sessão e o
    /// supervisor com a permissão no instante, e os limites da loja (por aprovação, soma e quantidade por sessão). Passou: grava a aprovação com o id do
    /// aparelho e <c>online = false</c>.
    /// </summary>
    private async Task<Projection> ProjectOfflineApprovalAsync(SyncContext context, JsonElement root, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var approvalId = PosSaleReplay.GuidOf(root, "approvalId");
        var operatorId = PosSaleReplay.GuidOf(root, "operatorId");
        var supervisorId = PosSaleReplay.GuidOf(root, "supervisorId");
        var saleId = PosSaleReplay.GuidOf(root, "saleId");
        var lineId = PosSaleReplay.GuidOf(root, "lineId");
        var sessionId = PosSaleReplay.GuidOf(root, "cashSessionId");
        var before = PosSaleReplay.LongOf(root, "beforeCents");
        var after = PosSaleReplay.LongOf(root, "afterCents");
        var atText = PosSaleReplay.StringOf(root, "at");
        var reason = PosSaleReplay.StringOf(root, "reason")?.Trim();
        var projection = new Projection { SaleId = saleId, OperatorId = operatorId };
        if (approvalId is null || operatorId is null || supervisorId is null || saleId is null || sessionId is null || before is not >= 0 || after is not >= 0
            || atText is null || PosSaleReplay.DateOf(root, "at") is not { } at || !Enum.TryParse<SupervisorAction>(PosSaleReplay.StringOf(root, "action"), out var action)
            || !Enum.IsDefined(action))
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Aprovação sem rede sem a ação, as pessoas, a venda, a sessão, os valores ou o instante.");
        }

        if (await db.SupervisorApprovals.AnyAsync(a => a.Id == approvalId, ct))
        {
            return projection; // liberada antes (ou o mesmo id): nada a fazer
        }

        var problems = new List<string>();
        if ((at - occurredAt).Duration() > ClockTolerance)
        {
            problems.Add("O instante da aprovação não bate com o do evento.");
        }

        if (supervisorId == operatorId)
        {
            problems.Add("Outra pessoa precisa aprovar.");
        }

        if (action == SupervisorAction.ChangePrice)
        {
            problems.Add("A troca de preço só é aprovada com internet.");
        }

        if (SupervisorApprovalService.DirectionProblem(action, before.Value / 100m, after.Value / 100m) is { } direction)
        {
            problems.Add(direction);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 3 || PosSaleReplay.TextProblem(reason, SupervisorApproval.MaxReasonLength, "motivo da aprovação") is not null)
        {
            problems.Add("Aprovação sem motivo, longo demais ou com sequência que parece número de cartão.");
        }

        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.RegisterId != context.Register.Id)
        {
            problems.Add("A sessão de caixa da aprovação não é deste caixa (ou a abertura não chegou).");
        }

        if (problems.Count > 0)
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Aprovação sem rede recusada na conferência: " + string.Join(" ", problems));
        }

        var impact = Math.Abs(before.Value - after.Value) / 100m;
        if (!context.Trusted)
        {
            await CheckPeopleAsync(context,
                [
                    new ReplayCheck(operatorId, at, "operador", null, NeedsPinSession: true),
                    new ReplayCheck(supervisorId, at, "supervisor", SupervisorActions.Permission(action), NeedsPinSession: false),
                ], problems, ct);
            var check = await offlineAccess.CheckApprovalAsync(context.Device.DeviceId, context.Store.Id, supervisorId.Value,
                PosOfflineProof.ApprovalMessage(context.Device.DeviceId, approvalId.Value, action.ToString(), operatorId.Value, supervisorId.Value, saleId.Value, lineId, before.Value,
                    after.Value, atText), PosSaleReplay.StringOf(root, "proof"), ct);
            if (check != PosOfflineCheck.Valid)
            {
                problems.Add(check switch
                {
                    PosOfflineCheck.UserInactive => "O supervisor já estava desativado ou fora da loja.",
                    PosOfflineCheck.SecretChanged => "O código sem internet do supervisor foi trocado depois que o aparelho baixou.",
                    _ => "A prova do código do supervisor não confere.",
                });
            }

            if (problems.Count > 0)
            {
                return Hold(projection, SyncDivergenceKind.OfflineAccess, "Aprovação sem rede recusada na conferência: " + string.Join(" ", problems));
            }

            if (OfflineLimitProblem(context.Store, action, impact, await SessionOfflineApprovalsAsync(sessionId.Value, ct)) is { } limit)
            {
                return Hold(projection, SyncDivergenceKind.OfflineAccess, "Aprovação sem rede acima do limite da loja: " + limit);
            }
        }

        db.SupervisorApprovals.Add(SupervisorApproval.Offline(approvalId.Value, context.Store.Id, context.Register.Id, context.Device.DeviceId, sessionId.Value, action,
            operatorId.Value, supervisorId.Value, Cut(reason!, SupervisorApproval.MaxReasonLength), saleId.Value, lineId, before.Value / 100m, after.Value / 100m, at));
        return projection;
    }

    /// <summary>O aparelho marcou o evento como feito sem rede (<c>"offline": true</c>).</summary>
    internal static bool OfflineFlag(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("offline", out var flag) && flag.ValueKind == JsonValueKind.True;

    private async Task<(int Count, decimal Amount)> SessionOfflineApprovalsAsync(Guid sessionId, CancellationToken ct)
    {
        var rows = await db.SupervisorApprovals.AsNoTracking().Where(a => a.CashSessionId == sessionId && !a.Online)
            .Select(a => new { a.BeforeAmount, a.AfterAmount }).ToListAsync(ct);
        return (rows.Count, rows.Sum(a => Math.Abs(a.BeforeAmount - a.AfterAmount)));
    }

    /// <summary>
    /// Os limites da loja para a aprovação sem rede (OFF-03 CA 13): os mesmos que o aparelho aplica antes de aceitar o código. Null quando cabe. O
    /// aparelho conta o que já fez na sessão; o servidor conta o que já chegou (as aprovações sobem em ordem, antes das vendas que as usam).
    /// </summary>
    public static string? OfflineLimitProblem(Store store, SupervisorAction action, decimal impact, (int Count, decimal Amount) session)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.OfflineApprovalMaxAmount <= 0 || store.OfflineApprovalSessionCount <= 0)
        {
            return "a loja não aceita aprovação sem internet.";
        }

        if (action == SupervisorAction.CancelSale && !store.OfflineCancelSaleAllowed)
        {
            return "a loja não aceita cancelamento de venda sem internet.";
        }

        if (impact > store.OfflineApprovalMaxAmount)
        {
            return $"{PosSaleReplay.Money((long)(impact * 100))} passa do teto de {PosSaleReplay.Money((long)(store.OfflineApprovalMaxAmount * 100))} por aprovação.";
        }

        if (session.Count + 1 > store.OfflineApprovalSessionCount)
        {
            return $"passa de {store.OfflineApprovalSessionCount} aprovações sem internet na sessão.";
        }

        return session.Amount + impact > store.OfflineApprovalSessionAmount
            ? $"a soma na sessão passa de {PosSaleReplay.Money((long)(store.OfflineApprovalSessionAmount * 100))}."
            : null;
    }
}
