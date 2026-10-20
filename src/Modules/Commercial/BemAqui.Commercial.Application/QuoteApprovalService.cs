using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// "Aprovar e gerar contrato" (COM-04a): numa transação, o orçamento vira Aprovado e nascem o contrato com o snapshot, os marcos,
/// o projeto, os itens e o orçado por natureza; <c>ContractSigned</c> leva ao Finance a conta de receita das parcelas.
/// </summary>
public sealed partial class QuoteApprovalService(
    ICommercialDb db, QuoteService quotes, IParties parties, IChartOfAccounts chart, INumberSequence numbers, IPermissionChecker permissions, ICurrentUser currentUser,
    IEventPublisher events, IFinancialOpening opening, IUserDirectory users, BemAqui.BuildingBlocks.Notifications.INotificationService notifications, TimeProvider clock)
{
    private static readonly string[] SuggestedAccounts = ["1.1", "1.9"];

    public async Task<Result<ApprovalPreview>> PreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteService.NotFound();
        }

        var conditions = (await quotes.ConditionsAsync(id, cancellationToken)).Value;
        var detail = (await quotes.GetAsync(id, cancellationToken)).Value;
        var accounts = await chart.PostableAsync(AccountType.Revenue, cancellationToken);
        var suggested = SuggestedAccounts.Select(code => accounts.FirstOrDefault(a => a.Code == code && a.IsActive)).FirstOrDefault(a => a is not null);
        return new ApprovalPreview(
            await PreconditionsAsync(quote, conditions, cancellationToken), quote.TotalPrice, detail.Items.Count, quote.DeliveryDays,
            conditions.Milestones.Select(m => new ApprovalMilestone(m.Description, m.Percent, m.Amount, When(m), m.Id, m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent)).ToList(),
            accounts.Where(a => a.IsActive).Select(a => new ApprovalAccount(a.Id, $"{a.Code} {a.Name}")).ToList(), suggested?.Id, await ProjectNameAsync(quote, detail, cancellationToken),
            await opening.StatusAsync(cancellationToken) is var status ? new ApprovalOnboarding(status.CutoverDate, status.CompletedAt) : null);
    }

    /// <summary>Itens de atenção para quem vê custo (CA 3): margem negativa e preço digitado à mão; não bloqueia.</summary>
    public async Task<Result<ApprovalCostView>> CostAlertsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("quotes", "view_cost", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Custo e margem do orçamento são restritos. Fale com o administrador.");
        }

        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteService.NotFound();
        }

        var items = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        return new ApprovalCostView(quote.MarginPct, items.Where(i => i.UnitPrice < i.UnitCost || i.PriceOverridden)
            .Select(i => new ApprovalAlert(i.Sequence, i.Description, i.UnitPrice < i.UnitCost, i.PriceOverridden)).ToList());
    }

    private async Task<List<ApprovalPrecondition>> PreconditionsAsync(Quote quote, QuoteConditions conditions, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var party = await parties.FindAsync(quote.PartyId, ct);
        var hasPrice = await db.QuoteItems.AnyAsync(i => i.QuoteId == quote.Id && i.UnitPrice > 0 && i.Quantity > 0, ct);
        var sum = conditions.Milestones.Sum(m => m.Percent);
        return
        [
            new(quote.IsCurrent, "Só a revisão mais recente pode ser aprovada.", "revisoes"),
            new(quote.Status == QuoteStatus.Sent, quote.Status == QuoteStatus.Approved ? "O orçamento já foi aprovado." : "Só o orçamento enviado pode ser aprovado.", "dados"),
            new(quote.ValidUntil is null || quote.ValidUntil >= today, $"A validade venceu em {quote.ValidUntil:dd/MM/yyyy}. Reabra com nova revisão.", "condicoes"),
            new(hasPrice, "Orçamento sem item com preço.", "itens"),
            new(conditions.Milestones.Count > 0, "Defina ao menos um marco de pagamento.", "condicoes"),
            new(conditions.Milestones.Count == 0 || sum == 100m, $"Os marcos somam {sum.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}%. Precisam somar 100%.", "condicoes"),
            new(party is { IsActive: true, IsDeleted: false } && party.Roles.Contains(PartyRoleKind.Customer), "O cliente precisa estar ativo, com o papel Cliente.", "dados"),
        ];
    }

    private async Task<string> ProjectNameAsync(Quote quote, QuoteDetail detail, CancellationToken ct)
    {
        var party = await parties.FindAsync(quote.PartyId, ct);
        var name = party is null ? detail.PartyName : party.TradeName ?? party.LegalName;
        var first = detail.Items.OrderBy(i => i.Sequence).FirstOrDefault()?.Description;
        var full = first is null ? name : $"{name} — {first}";
        return full.Length <= 200 ? full : full[..200];
    }

    private static string When(QuoteMilestoneView m) => m.Trigger == MilestoneTrigger.Date
        ? $"em {m.DueDate:dd/MM/yyyy}"
        : $"{(m.DaysAfterEvent is > 0 ? $"{m.DaysAfterEvent} dias após " : "na ")}{m.TriggerEvent switch
        {
            TriggerEvent.Signature => "assinatura do contrato",
            TriggerEvent.FabricationDone => "fabricação concluída",
            TriggerEvent.Delivered => "entrega",
            _ => "aceite técnico",
        }}";
}
