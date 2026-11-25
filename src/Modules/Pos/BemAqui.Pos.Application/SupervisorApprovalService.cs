using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// "Chamar supervisor" (PDV-04 CA 10): a ação, o supervisor e o PIN dele, o motivo, a venda (e a linha) e os valores antes e depois, em reais. O PIN
/// nunca vai para log nem auditoria.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record SupervisorApprovalInput(SupervisorAction Action, Guid SupervisorId, string Pin, string Reason, Guid SaleId, Guid? LineId, decimal BeforeAmount, decimal AfterAmount)
{
    public override string ToString() => $"SupervisorApprovalInput {{ Action = {Action}, SupervisorId = {SupervisorId}, SaleId = {SaleId} }}";
}

/// <summary>Aprovação aceita: o id vai no evento da ação, que só passa na subida com ele.</summary>
public sealed record SupervisorApprovalGranted(Guid ApprovalId, SupervisorAction Action, Guid SupervisorId, string SupervisorName, DateTimeOffset ApprovedAt);

public sealed record SupervisorApprovalFilter(DateOnly From, DateOnly To, Guid? StoreId = null, SupervisorAction? Action = null, Guid? OperatorId = null, Guid? SupervisorId = null);

public sealed record SupervisorApprovalRow(
    Guid Id, DateTimeOffset ApprovedAt, Guid StoreId, string StoreName, int RegisterNumber, SupervisorAction Action, string ActionLabel, Guid OperatorId, string OperatorName,
    Guid SupervisorId, string SupervisorName, string Reason, Guid SaleId, decimal BeforeAmount, decimal AfterAmount, bool Used, string? RefundReference);

/// <summary>Contagem e valor (a diferença entre antes e depois) por pessoa ou por tipo.</summary>
public sealed record SupervisorApprovalGroup(string Key, string Label, int Count, decimal Amount);

public sealed record SupervisorApprovalReport(
    IReadOnlyList<SupervisorApprovalRow> Rows, IReadOnlyList<SupervisorApprovalGroup> ByOperator, IReadOnlyList<SupervisorApprovalGroup> BySupervisor,
    IReadOnlyList<SupervisorApprovalGroup> ByAction, bool Truncated);

/// <summary>
/// Aprovação de supervisor no balcão (PDV-04, exceção nomeada da PP-11). O Identity confere o PIN, o aparelho, a sessão do operador e a permissão do
/// supervisor; aqui ficam a validação do pedido, a trilha <c>pos.supervisor_approvals</c> (auditada) e o relatório da retaguarda (CA 13).
/// </summary>
public sealed class SupervisorApprovalService(IPosDb db, IPosSupervisorPins pins, IPosOperatorHistory operators, ISaleLookup sales, IUserDirectory users, TimeProvider clock)
{
    public const string Link = "/app/loja/aprovacoes";
    public const string RateLimitPolicy = "pos-supervisor-approvals";
    public const int MaxRows = 2000;
    public const int MaxDays = 366;
    private const decimal MaxAmount = 9_999_999.99m;

    private static readonly string[] CsvHeader = ["Data", "Loja", "Caixa", "Tipo", "Operador", "Supervisor", "Motivo", "Antes", "Depois", "Usada", "NSU do estorno"];

    public Task<IReadOnlyList<PosSupervisorOption>> SupervisorsAsync(string? posCookie, Guid operatorId, CancellationToken cancellationToken = default) =>
        pins.SupervisorsAsync(posCookie, operatorId, cancellationToken);

    public async Task<Result<SupervisorApprovalGranted>> ApproveAsync(Guid operatorId, string? posCookie, SupervisorApprovalInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (!Enum.IsDefined(input.Action) || input.SaleId == Guid.Empty || input.SupervisorId == Guid.Empty)
        {
            return Invalid("action", "Informe a ação, a venda e o supervisor.");
        }

        if (reason.Length is < 3 or > SupervisorApproval.MaxReasonLength || PosText.LooksLikeCardNumber(reason))
        {
            return Invalid("reason", $"Informe o motivo (de 3 a {SupervisorApproval.MaxReasonLength} caracteres, sem número de cartão).");
        }

        if (input.BeforeAmount is < 0 or > MaxAmount || input.AfterAmount is < 0 or > MaxAmount
            || decimal.Round(input.BeforeAmount, 2) != input.BeforeAmount || decimal.Round(input.AfterAmount, 2) != input.AfterAmount)
        {
            return Invalid("beforeAmount", "Informe os valores antes e depois em reais, com até 2 casas.");
        }

        if (DirectionProblem(input.Action, input.BeforeAmount, input.AfterAmount) is { } direction)
        {
            return Invalid("afterAmount", direction);
        }

        // CA 11: o supervisor nunca aprova a própria ação (conferido de novo no Identity e na subida, e no banco por CHECK).
        if (input.SupervisorId == operatorId)
        {
            return Error.Validation("supervisor_self", "Outra pessoa precisa aprovar.");
        }

        // CA 6: cancelar a venda concluída pede pos.cancel_sale do operador e, ainda assim, o supervisor; CA 8: só a venda da sessão aberta deste caixa.
        var now = clock.GetUtcNow();
        if (input.Action == SupervisorAction.CancelSale && !await operators.HadPermissionAsync(operatorId, "pos.cancel_sale", now, cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem a permissão de cancelar venda no caixa.");
        }

        var check = await pins.VerifyAsync(new PosSupervisorPinRequest(posCookie, operatorId, input.SupervisorId, input.Pin ?? string.Empty, SupervisorActions.Permission(input.Action)),
            cancellationToken);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        if (input.Action == SupervisorAction.CancelSale && await OtherRegisterAsync(input.SaleId, check.Value.RegisterId, cancellationToken))
        {
            return Error.Validation("sale_other_register", PosEventIngestion.OtherRegisterMessage);
        }

        var approval = SupervisorApproval.Create(check.Value.StoreId, check.Value.RegisterId, check.Value.DeviceId, input.Action, operatorId, input.SupervisorId, reason,
            input.SaleId, input.LineId, input.BeforeAmount, input.AfterAmount, now);
        db.SupervisorApprovals.Add(approval);
        await db.SaveChangesAsync(cancellationToken);
        var name = await users.DisplayNameAsync(input.SupervisorId, cancellationToken) ?? "Supervisor";
        return new SupervisorApprovalGranted(approval.Id, approval.Action, approval.SupervisorId, name, now);
    }

    /// <summary>
    /// Revisão de segurança, achado 7: os valores combinam com a ação. Desconto: o depois não passa do antes; troca de preço: o preço muda; cancelamento
    /// de item: o valor da linha não sobe; cancelamento de venda: o depois é zero. Null quando está bom (os dois já são maiores ou iguais a zero).
    /// </summary>
    public static string? DirectionProblem(SupervisorAction action, decimal before, decimal after) => action switch
    {
        SupervisorAction.Discount when after > before => "No desconto, o valor depois não pode passar do valor antes.",
        SupervisorAction.ChangePrice when after == before => "Na troca de preço, o preço novo tem de ser diferente do atual.",
        SupervisorAction.CancelItem when after > before => "No cancelamento de item, o valor depois não pode passar do valor antes.",
        SupervisorAction.CancelSale when after != 0 => "No cancelamento de venda, o valor depois tem de ser zero.",
        SupervisorAction.Return when after != 0 || before <= 0 => "Na devolução, o valor antes é o da devolução e o valor depois é zero.",
        _ => null,
    };

    /// <summary>A venda já está no servidor e não é da sessão aberta deste caixa (sem a venda ainda, a subida confere depois).</summary>
    private async Task<bool> OtherRegisterAsync(Guid saleId, Guid registerId, CancellationToken cancellationToken)
    {
        if (await sales.FindAsync(saleId, cancellationToken) is not { } sale)
        {
            return false;
        }

        var session = sale.CashSessionId is { } id ? await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken) : null;
        return sale.RegisterId != registerId || session is null || session.Status != CashSessionStatus.Open || session.CountStartedAt is not null;
    }

    /// <summary>CA 13: aprovações no período (data em Brasília), com os totais por operador, por supervisor e por tipo.</summary>
    public async Task<Result<SupervisorApprovalReport>> ReportAsync(SupervisorApprovalFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.To < filter.From || filter.To.DayNumber - filter.From.DayNumber >= MaxDays)
        {
            return Error.Validation(Error.Codes.Validation, "Escolha um período de até um ano, com a data final igual ou depois da inicial.");
        }

        var all = await Filtered(filter).OrderByDescending(a => a.ApprovedAt).Take(MaxRows + 1).ToListAsync(cancellationToken);
        var rows = await RowsAsync(all.Take(MaxRows).ToList(), cancellationToken);
        return new SupervisorApprovalReport(
            rows,
            Group(rows, r => r.OperatorId.ToString(), r => r.OperatorName),
            Group(rows, r => r.SupervisorId.ToString(), r => r.SupervisorName),
            Group(rows, r => r.Action.ToString(), r => r.ActionLabel),
            all.Count > MaxRows);
    }

    public async Task<Result<SyncExportFile>> ExportAsync(SupervisorApprovalFilter filter, CancellationToken cancellationToken = default)
    {
        var report = await ReportAsync(filter, cancellationToken);
        if (report.IsFailure)
        {
            return report.Error!;
        }

        var lines = report.Value.Rows.Select(r => (IReadOnlyList<string?>)[
            PosEventIngestion.Local(r.ApprovedAt), r.StoreName, r.RegisterNumber.ToString("00", CultureInfo.InvariantCulture), r.ActionLabel, r.OperatorName, r.SupervisorName,
            r.Reason, r.BeforeAmount.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")), r.AfterAmount.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")),
            r.Used ? "Sim" : "Não", r.RefundReference]);
        return new SyncExportFile(SafeCsv.Bytes(SafeCsv.Text(CsvHeader, lines)), $"aprovacoes-de-supervisor-{filter.From:yyyy-MM-dd}-a-{filter.To:yyyy-MM-dd}.csv");
    }

    private IQueryable<SupervisorApproval> Filtered(SupervisorApprovalFilter filter)
    {
        var start = BusinessTime.StartOfDayUtc(filter.From);
        var end = BusinessTime.StartOfDayUtc(filter.To.AddDays(1));
        var query = db.SupervisorApprovals.AsNoTracking().Where(a => a.ApprovedAt >= start && a.ApprovedAt < end);
        if (filter.StoreId is { } store)
        {
            query = query.Where(a => a.StoreId == store);
        }

        if (filter.Action is { } action)
        {
            query = query.Where(a => a.Action == action);
        }

        if (filter.OperatorId is { } op)
        {
            query = query.Where(a => a.OperatorId == op);
        }

        if (filter.SupervisorId is { } supervisor)
        {
            query = query.Where(a => a.SupervisorId == supervisor);
        }

        return query;
    }

    private async Task<List<SupervisorApprovalRow>> RowsAsync(IReadOnlyList<SupervisorApproval> approvals, CancellationToken cancellationToken)
    {
        var storeIds = approvals.Select(a => a.StoreId).Distinct().ToList();
        var registerIds = approvals.Select(a => a.RegisterId).Distinct().ToList();
        var stores = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var registers = await db.Registers.AsNoTracking().Where(r => registerIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);
        var names = new Dictionary<Guid, string>();
        foreach (var id in approvals.SelectMany(a => new[] { a.OperatorId, a.SupervisorId }).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, cancellationToken) ?? "Usuário removido";
        }

        return approvals.Select(a => new SupervisorApprovalRow(
            a.Id, a.ApprovedAt, a.StoreId, stores.GetValueOrDefault(a.StoreId, "Loja removida"), registers.GetValueOrDefault(a.RegisterId), a.Action, SupervisorActions.Label(a.Action),
            a.OperatorId, names[a.OperatorId], a.SupervisorId, names[a.SupervisorId], a.Reason, a.SaleId, a.BeforeAmount, a.AfterAmount, a.UsedAt is not null, a.RefundReference)).ToList();
    }

    private static List<SupervisorApprovalGroup> Group(IEnumerable<SupervisorApprovalRow> rows, Func<SupervisorApprovalRow, string> key, Func<SupervisorApprovalRow, string> label) =>
        rows.GroupBy(key).Select(g => new SupervisorApprovalGroup(g.Key, label(g.First()), g.Count(), g.Sum(r => Math.Abs(r.BeforeAmount - r.AfterAmount))))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Label, StringComparer.CurrentCulture).ToList();

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
