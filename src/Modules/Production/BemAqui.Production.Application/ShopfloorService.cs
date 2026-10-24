using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// App do operador (PRD-03a, <c>/api/v1/shopfloor/*</c>): fila, busca pelo número, etapa, anexos e os comandos de apontamento. O
/// funcionário é sempre o do usuário logado (<see cref="IUserEmployeeLink.EmployeeOfUserAsync"/>, ponto 67), ativo hoje
/// (<see cref="IEmployees"/>, CAD-04c1); nunca vem do corpo (CA 29). Nenhum DTO traz custo (CA 25).
/// </summary>
public sealed partial class ShopfloorService(
    IProductionDb db, LaborWorkflow workflow, IBomGate bom, IUserEmployeeLink links, IEmployees employees, IUserDirectory users, ICurrentUser currentUser,
    INotificationService notifications, IProjects projects, IParties parties, IItems items, IWorkCenters workCenters, AttachmentService attachments, TimeProvider clock,
    IMaterialRequisitions requisitions)
{
    public const string MaterialPauseType = "production.material_pause";

    private static readonly string[] ViewableTypes = ["application/pdf", "image/png", "image/jpeg"];

    public static Error EmployeeNotActive() =>
        Error.Forbidden("employee_not_active", "Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor.");

    public static Error StepNotFound() => Error.NotFound("step_not_found", "Etapa não encontrada na sua fila.");

    /// <summary>Funcionário do usuário logado, ativo hoje (CA 4): sem vínculo, afastado ou desligado, 403 <c>employee_not_active</c>.</summary>
    public async Task<Result<EmployeeSummary>> OperatorAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId || await links.EmployeeOfUserAsync(userId, ct) is not { } employeeId)
        {
            return EmployeeNotActive();
        }

        return await workflow.ActiveEmployeeAsync(employeeId, ct) is { } employee ? employee : EmployeeNotActive();
    }

    /// <summary>
    /// A fila (CA 1): a etapa em andamento do operador em destaque e as etapas prontas das OS não canceladas do tenant (mais as
    /// pausadas sem ninguém, para alguém retomar), as do centro do cargo primeiro, pela prioridade da OS e pela data prometida.
    /// A etapa de qualidade fica fora até a PRD-05 (CA 16).
    /// </summary>
    public async Task<Result<ShopfloorQueueView>> QueueAsync(CancellationToken cancellationToken = default)
    {
        var me = await OperatorAsync(cancellationToken);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        var employee = me.Value;
        var mine = await db.LaborEntries.AsNoTracking().Where(e => e.EmployeeId == employee.Id && e.EndedAt == null).Select(e => (Guid?)e.OrderStepId).FirstOrDefaultAsync(cancellationToken);
        var ready = await QueueStepIdsAsync(cancellationToken);
        var ids = ready.Concat(mine is { } m ? [m] : []).Distinct().ToList();
        var views = await BuildAsync(ids, employee, cancellationToken);
        var ordered = LaborRules.QueueOrder(views.Values.Where(v => v.View.StepId != mine).Select(v => v.Key)).Select(id => views[id].View).ToList();
        return new ShopfloorQueueView(await OperatorViewAsync(employee, cancellationToken), mine is { } current && views.TryGetValue(current, out var c) ? c.View : null, ordered, clock.GetUtcNow());
    }

    /// <summary>
    /// "Buscar OS pelo número" (CA 3): o número inteiro (OS-2026-00041) ou só os dígitos finais (41, 00041); devolve as etapas
    /// prontas, em andamento ou pausadas das OS de fabricação não canceladas. Nada encontrado: "OS não encontrada.".
    /// </summary>
    public async Task<Result<IReadOnlyList<ShopfloorQueueStep>>> SearchAsync(string? number, CancellationToken cancellationToken = default)
    {
        var me = await OperatorAsync(cancellationToken);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        var text = (number ?? string.Empty).Trim().ToUpperInvariant();
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        if (text.Length == 0 || text.Length > 30 || (digits.Length == 0 && !text.Contains('-', StringComparison.Ordinal)))
        {
            return OrderNotFound();
        }

        var orders = OpenFabrication(db.ProductionOrders.AsNoTracking());
        var suffix = "-" + digits.PadLeft(5, '0');
        var found = text.Contains('-', StringComparison.Ordinal)
            ? orders.Where(o => o.Number == text)
            : orders.Where(o => o.Number.EndsWith(suffix));
        var ids = await (from s in db.ProductionOrderSteps.AsNoTracking()
                         join o in found on s.OrderId equals o.Id
                         where !s.IsQualityStep && s.AwaitingApprovalSince == null
                            && (s.Status == ProductionStepStatus.Ready || s.Status == ProductionStepStatus.InProgress || s.Status == ProductionStepStatus.Paused)
                         select s.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return OrderNotFound();
        }

        var views = await BuildAsync(ids, me.Value, cancellationToken);
        return LaborRules.QueueOrder(views.Values.Select(v => v.Key)).Select(id => views[id].View).ToList();
    }

    /// <summary>
    /// Etapa aberta no app (CA 5): só a que está na fila (OS de fabricação não cancelada nem concluída, etapa pronta, em andamento
    /// ou pausada) ou a do apontamento aberto do operador; fora disso, 404 (IDOR, CA 29).
    /// </summary>
    public async Task<Result<ShopfloorStepDetail>> GetStepAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var me = await OperatorAsync(cancellationToken);
        return me.IsFailure ? me.Error! : await DetailAsync(stepId, me.Value, cancellationToken);
    }

    /// <summary>
    /// Anexos técnicos da OS da etapa (CA 6), só leitura: conferida a fila do operador, sem exigir <c>production.view</c>. PDF, PNG
    /// e JPG abrem na tela; DWG e DXF só baixam.
    /// </summary>
    public async Task<Result<IReadOnlyList<ShopfloorAttachment>>> AttachmentsAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var me = await OperatorAsync(cancellationToken);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        return await VisibleOrderAsync(stepId, me.Value.Id, cancellationToken) is { } orderId ? (await AttachmentViewsAsync(orderId, cancellationToken)).ToList() : StepNotFound();
    }

    /// <summary>Download pelo app (CA 6): etapa na fila do operador e anexo daquela OS; senão 404.</summary>
    public async Task<Result<AttachmentContent>> DownloadAsync(Guid stepId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var me = await OperatorAsync(cancellationToken);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        return await VisibleOrderAsync(stepId, me.Value.Id, cancellationToken) is { } orderId
            ? await attachments.DownloadCheckedAsync(InstallationOrderService.AttachmentOwner, orderId, attachmentId, cancellationToken)
            : Error.NotFound(Error.Codes.NotFound, "Anexo não encontrado.");
    }

    private static Error OrderNotFound() => Error.NotFound("order_not_found", "OS não encontrada.");

    private static IQueryable<ProductionOrder> OpenFabrication(IQueryable<ProductionOrder> orders) =>
        orders.Where(o => o.Type == ProductionOrderType.Fabrication && o.Status != ProductionOrderStatus.Cancelled && o.Status != ProductionOrderStatus.Completed);

    /// <summary>Etapas prontas (e as pausadas sem ninguém), fora a de qualidade, das OS de fabricação abertas.</summary>
    private async Task<List<Guid>> QueueStepIdsAsync(CancellationToken ct) =>
        await (from s in db.ProductionOrderSteps.AsNoTracking()
               join o in OpenFabrication(db.ProductionOrders.AsNoTracking()) on s.OrderId equals o.Id
               where !s.IsQualityStep && s.AwaitingApprovalSince == null
                  && (s.Status == ProductionStepStatus.Ready
                      || (s.Status == ProductionStepStatus.Paused && !db.LaborEntries.Any(e => e.OrderStepId == s.Id && e.EndedAt == null)))
               select s.Id).ToListAsync(ct);

    /// <summary>OS da etapa quando ela está na fila do operador ou ele tem apontamento aberto nela (CA 5, 6); senão null.</summary>
    private async Task<Guid?> VisibleOrderAsync(Guid stepId, Guid employeeId, CancellationToken ct) =>
        await (from s in db.ProductionOrderSteps.AsNoTracking()
               join o in OpenFabrication(db.ProductionOrders.AsNoTracking()) on s.OrderId equals o.Id
               where s.Id == stepId && !s.IsQualityStep
                  && (s.Status == ProductionStepStatus.Ready || s.Status == ProductionStepStatus.InProgress || s.Status == ProductionStepStatus.Paused
                      || db.LaborEntries.Any(e => e.OrderStepId == s.Id && e.EmployeeId == employeeId && e.EndedAt == null))
               select (Guid?)o.Id).FirstOrDefaultAsync(ct);

    private async Task<IEnumerable<ShopfloorAttachment>> AttachmentViewsAsync(Guid orderId, CancellationToken ct) =>
        (await attachments.ListCheckedAsync(InstallationOrderService.AttachmentOwner, orderId, ct))
            .Where(a => a.ScanStatus == ScanStatus.Clean)
            .Select(a => new ShopfloorAttachment(a.Id, a.FileName, a.ContentType, a.Size, ViewableTypes.Contains(a.ContentType)));

    private async Task<ShopfloorOperator> OperatorViewAsync(EmployeeSummary employee, CancellationToken ct)
    {
        var center = employee.WorkCenterId is { } id && await workCenters.FindAsync([id], ct) is [var found, ..] ? found : null;
        var first = employee.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var f, ..] ? f : employee.Name;
        return new ShopfloorOperator(employee.Id, employee.Name, first, employee.WorkCenterId, center?.Name, center?.Line, BusinessTime.Today(clock));
    }

    private async Task<Result<ShopfloorStepDetail>> DetailAsync(Guid stepId, EmployeeSummary employee, CancellationToken ct)
    {
        if (await VisibleOrderAsync(stepId, employee.Id, ct) is not { } orderId)
        {
            return StepNotFound();
        }

        var views = await BuildAsync([stepId], employee, ct);
        var view = views[stepId].View;
        var order = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId, ct);
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == orderId).OrderBy(s => s.Sequence).ToListAsync(ct);
        var step = steps.Single(s => s.Id == stepId);
        var index = steps.IndexOf(step);
        var checklist = await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.StepId == stepId).OrderBy(c => c.Sequence).ToListAsync(ct);
        var markers = new Dictionary<Guid, string?>();
        foreach (var by in checklist.Where(c => c.MarkedBy is not null).Select(c => c.MarkedBy!.Value).Distinct())
        {
            markers[by] = await users.DisplayNameAsync(by, ct);
        }

        var now = clock.GetUtcNow();
        var open = await db.LaborEntries.AsNoTracking().Where(e => e.OrderStepId == stepId && e.EndedAt == null).OrderBy(e => e.StartedAt).ToListAsync(ct);
        var openIds = open.Select(e => e.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => openIds.Contains(p.LaborEntryId)).ToListAsync(ct)).ToLookup(p => p.LaborEntryId);
        var names = (await employees.FindAsync(open.Select(e => e.EmployeeId).Distinct().ToList(), ct)).ToDictionary(e => e.Id, e => e.Name);
        var people = open.Select(e =>
        {
            var current = pauses[e.Id].FirstOrDefault(p => p.EndedAt is null);
            return new ShopfloorPerson(names.GetValueOrDefault(e.EmployeeId, "Funcionário"), current is not null, current?.Reason, e.StartedAt, e.EmployeeId == employee.Id);
        }).ToList();
        var mineEntry = open.FirstOrDefault(e => e.EmployeeId == employee.Id);
        ShopfloorMyEntry? mine = null;
        if (mineEntry is not null)
        {
            var my = pauses[mineEntry.Id].ToList();
            var current = my.FirstOrDefault(p => p.EndedAt is null);
            var closedSeconds = (int)Math.Round(my.Where(p => p.EndedAt is not null).Sum(p => (p.EndedAt!.Value - p.StartedAt).TotalSeconds));
            mine = new ShopfloorMyEntry(mineEntry.Id, mineEntry.StartedAt, closedSeconds, current?.StartedAt, current?.Reason, current?.Notes,
                LaborRules.NetMinutes(mineEntry.StartedAt, null, my.Select(p => new PauseSpan(p.StartedAt, p.EndedAt)), now));
        }

        var product = order.ProductId is { } productId && await items.FindAsync([productId], ct) is [var item, ..] ? item : null;
        var scope = new LaborScope(order, steps, step);
        var blocked = mine is not null ? null : await LaborWorkflow.CanStartAsync(scope, bom, ct);
        var suggested = await db.StepPauses.AsNoTracking()
            .Where(p => db.LaborEntries.Any(e => e.Id == p.LaborEntryId && e.EmployeeId == employee.Id))
            .GroupBy(p => p.Reason).OrderByDescending(g => g.Count()).Select(g => (PauseReason?)g.Key).FirstOrDefaultAsync(ct) ?? PauseReason.Material;
        return new ShopfloorStepDetail(
            view, product?.Code, order.Quantity.GetValueOrDefault(),
            checklist.Select(c => new ShopfloorChecklistItem(c.Id, c.Text, c.Result is not null, c.MarkedAt, c.MarkedBy is { } by ? markers.GetValueOrDefault(by) : null)).ToList(),
            people, mine, (await AttachmentViewsAsync(orderId, ct)).ToList(), step.RequiresApproval, step.IsQualityStep,
            index + 1 < steps.Count ? steps[index + 1].Name : null, suggested, mine is null && blocked is null, blocked?.Message, now);
    }

    private sealed record Built(ShopfloorQueueStep View, QueueKey Key);

    /// <summary>
    /// Cartões das etapas (CA 1, 5): etapa, OS, letras, centro, produto, projeto, cliente, prometido, horas padrão e apontadas (todos
    /// os operadores) e quem está nela, numa consulta por tabela e os nomes de uma vez por contrato (sem N+1).
    /// </summary>
    private async Task<Dictionary<Guid, Built>> BuildAsync(IReadOnlyCollection<Guid> stepIds, EmployeeSummary me, CancellationToken ct)
    {
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => stepIds.Contains(s.Id)).ToListAsync(ct);
        var orderIds = steps.Select(s => s.OrderId).Distinct().ToList();
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var all = (await db.ProductionOrderSteps.AsNoTracking().Where(s => orderIds.Contains(s.OrderId)).OrderBy(s => s.Sequence)
            .Select(s => new { s.OrderId, s.Id, s.Code, s.Name }).ToListAsync(ct)).ToLookup(s => s.OrderId);
        var entries = await db.LaborEntries.AsNoTracking().Where(e => stepIds.Contains(e.OrderStepId)).ToListAsync(ct);
        var entryIds = entries.Select(e => e.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => entryIds.Contains(p.LaborEntryId)).ToListAsync(ct)).ToLookup(p => p.LaborEntryId);
        var names = (await employees.FindAsync(entries.Where(e => e.EndedAt is null).Select(e => e.EmployeeId).Distinct().ToList(), ct)).ToDictionary(e => e.Id, e => e.Name);
        var centers = (await workCenters.FindAsync(steps.Where(s => s.WorkCenterId is not null).Select(s => s.WorkCenterId!.Value).Distinct().ToList(), ct)).ToDictionary(c => c.Id);
        var byProject = (await projects.FindManyAsync(orders.Values.Select(o => o.ProjectId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        var partyNames = (await parties.FindManyAsync(byProject.Values.Select(p => p.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var products = (await items.FindAsync(orders.Values.Where(o => o.ProductId is not null).Select(o => o.ProductId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id, i => i.Name);
        var projectItems = (await projects.FindItemsAsync(orders.Values.Where(o => o.ProjectItemId is not null).Select(o => o.ProjectItemId!.Value).Distinct().ToList(), ct))
            .ToDictionary(i => i.Id, i => i.Description);
        var now = clock.GetUtcNow();
        var result = new Dictionary<Guid, Built>();
        foreach (var s in steps)
        {
            var order = orders[s.OrderId];
            var siblings = all[s.OrderId].ToList();
            var project = byProject.GetValueOrDefault(order.ProjectId);
            var mineEntries = entries.Where(e => e.OrderStepId == s.Id).ToList();
            var minutes = mineEntries.Sum(e => LaborRules.NetMinutes(e.StartedAt, e.EndedAt, pauses[e.Id].Select(p => new PauseSpan(p.StartedAt, p.EndedAt)), now));
            var open = mineEntries.Where(e => e.EndedAt is null).ToList();
            var openPauses = open.Select(e => pauses[e.Id].FirstOrDefault(p => p.EndedAt is null)).ToList();
            var pausedReason = open.Count > 0 && openPauses.All(p => p is not null) ? PauseLabel(openPauses[0]!.Reason) : null;
            var center = s.WorkCenterId is { } c ? centers.GetValueOrDefault(c) : null;
            var isMine = s.WorkCenterId is not null && s.WorkCenterId == me.WorkCenterId;
            var product = order.ProductId is { } p ? products.GetValueOrDefault(p) : null;
            var view = new ShopfloorQueueStep(
                s.Id, order.Id, order.Number, s.Code ?? string.Empty, s.Name, siblings.FindIndex(x => x.Id == s.Id), siblings.Select(x => x.Code ?? string.Empty).ToList(),
                siblings.Select(x => x.Name).ToList(), s.Status ?? ProductionStepStatus.Waiting, s.WorkCenterId, center?.Name, isMine,
                product ?? (order.ProjectItemId is { } pi ? projectItems.GetValueOrDefault(pi, string.Empty) : string.Empty), project?.Number ?? string.Empty,
                project?.Name ?? string.Empty, project is null ? string.Empty : partyNames.GetValueOrDefault(project.PartyId, string.Empty), project?.PromisedDate,
                s.StandardHours.GetValueOrDefault(), LaborRules.Hours(minutes), open.Select(e => names.GetValueOrDefault(e.EmployeeId, "Funcionário")).ToList(), pausedReason,
                s.AwaitingApprovalSince is not null);
            result[s.Id] = new Built(view, new QueueKey(s.Id, isMine, order.Priority, project?.PromisedDate, order.Number, s.Sequence));
        }

        return result;
    }

    /// <summary>Rótulo do motivo da pausa (CA 11), também no aviso ao almoxarifado e no cartão do Kanban.</summary>
    public static string PauseLabel(PauseReason reason) => reason switch
    {
        PauseReason.Material => "Falta de material",
        PauseReason.Machine => "Máquina parada",
        PauseReason.Break => "Intervalo",
        PauseReason.EndOfShift => "Fim do turno",
        _ => "Outro",
    };
}