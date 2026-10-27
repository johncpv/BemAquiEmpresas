using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Boleto com Pix do título a receber (BNK-01b): diálogo, emissão (só grava <c>Requested</c> e enfileira o job), lote, nova tentativa,
/// envio por e-mail e cancelamento. Sempre lido pelo título da URL (IDOR). O boleto nunca muda valor, situação nem vencimento do título.
/// </summary>
public sealed partial class BillingSlipService(
    IFinanceDb db,
    IParties parties,
    IFinanceParameters parameters,
    BillingSettingsService settings,
    BankIntegrationService integrations,
    BillingSlipMailer mailer,
    IBillingSlipJobs jobs,
    EmailLogReader emailLogs,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    IUserDirectory users,
    TimeProvider clock)
{
    public const string AttachmentOwner = "BillingSlip";
    public const string ActiveIndex = "ux_billing_slips_receivable_active";
    public const string ReferenceIndex = "ux_billing_slips_account_reference";
    public const string RequestIndex = "ux_billing_slips_client_request";
    public const string NotificationType = "finance.billing_slip";
    public const string ExistsCode = "billing_slip_exists";
    public const string FailedCode = "billing_slip_failed";
    public const string ApprovalCode = "approval_required";
    public const string MissingIntegrationCode = "bank_integration_missing";
    public const string PreconditionsCode = "billing_slip_preconditions";
    public const string RequestedMessage = "Boleto solicitado ao Inter. Assim que o banco confirmar, ele aparece aqui.";
    public const string ApprovalMessage = "Desconto acima do limite: só quem aprova no Financeiro pode emitir.";
    public const string NoIntegrationMessage = "Conecte a conta do Inter para emitir boletos.";

    /// <summary>Tamanho máximo do PDF do banco guardado como arquivo do sistema.</summary>
    public const int MaxPdfBytes = 2 * 1024 * 1024;

    public static string Link(Guid receivableId) => "/app/financeiro/receber/" + receivableId + "?aba=boleto";

    public static string PartyLink(Guid partyId) => "/app/cadastros/terceiros/" + partyId;

    private DateOnly Today => BusinessTime.Today(clock);

    /// <summary>Boletos do título, do mais novo ao mais antigo (aba "Boleto" e histórico).</summary>
    public async Task<Result<IReadOnlyList<BillingSlipView>>> ListAsync(Guid receivableId, CancellationToken cancellationToken = default)
    {
        if (!await db.Receivables.AnyAsync(r => r.Id == receivableId, cancellationToken))
        {
            return ReceivableService.NotFound();
        }

        var slips = await db.BillingSlips.AsNoTracking().Where(s => s.ReceivableId == receivableId).OrderByDescending(s => s.RequestedAt).ToListAsync(cancellationToken);
        var views = new List<BillingSlipView>();
        foreach (var slip in slips)
        {
            views.Add(await ViewAsync(slip, cancellationToken));
        }

        return views;
    }

    public async Task<Result<BillingSlipView>> GetAsync(Guid receivableId, Guid slipId, CancellationToken cancellationToken = default)
    {
        var slip = await db.BillingSlips.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slipId && s.ReceivableId == receivableId, cancellationToken);
        return slip is null ? NotFound() : await ViewAsync(slip, cancellationToken);
    }

    /// <summary>Diálogo "Emitir boleto" (CA 4, 5, 10, 11 e 15): valor, vencimento, multa, juros, desconto e e-mail pelos padrões.</summary>
    public async Task<Result<BillingSlipDraft>> DraftAsync(Guid receivableId, CancellationToken cancellationToken = default)
    {
        var receivable = await db.Receivables.AsNoTracking().FirstOrDefaultAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable is null)
        {
            return ReceivableService.NotFound();
        }

        var defaults = await settings.DefaultsAsync(cancellationToken);
        var limits = await parameters.LimitsAsync(cancellationToken);
        var party = await parties.FindAsync(receivable.PartyId, cancellationToken);
        var accounts = await AccountsAsync(cancellationToken);
        var (preconditions, email) = await PreconditionsAsync(receivable, party, accounts, cancellationToken);
        var amount = BillingSlipRules.AmountFor(receivable);
        var due = BillingSlipRules.SuggestedDueDate(receivable.DueDate, Today);
        var discount = BillingSlipRules.DefaultDiscount(amount, defaults.DefaultDiscountPct);
        return new BillingSlipDraft(receivable.Id, receivable.Number, receivable.PartyId, party is null ? string.Empty : party.TradeName ?? party.LegalName, amount, receivable.DueDate,
            receivable.IsOverdue(Today), due, defaults.FinePct, defaults.InterestMonthPct, discount, discount > 0 ? due : null, defaults.AutoEmail && email is not null, email,
            defaults.OverdueFineFrom, BillingSettingsService.NonBusinessDayText, limits.DiscountApprovalPct, limits.DiscountApprovalAmount,
            await permissions.HasPermissionAsync("finance", "approve", cancellationToken), accounts, preconditions, receivable.BillingSlipId, receivable.Version);
    }

    /// <summary>Contas com integração Inter ativa e testada (a padrão primeiro).</summary>
    private async Task<List<BillingAccountOption>> AccountsAsync(CancellationToken ct)
    {
        var rows = await db.BankIntegrations.AsNoTracking().Where(i => i.Status == BankIntegrationStatus.Active && i.LastTestOutcome == BankTestOutcome.Ok)
            .Join(db.BankAccounts.AsNoTracking().Where(a => a.IsActive), i => i.BankAccountId, a => a.Id, (i, a) => new { a.Id, a.Name, a.IsDefault, i.Environment })
            .ToListAsync(ct);
        return rows.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Select((r, index) => new BillingAccountOption(r.Id, r.Name, r.Environment == BankEnvironment.Sandbox, index == 0)).ToList();
    }

    /// <summary>
    /// O que falta para emitir (CA 10 e 11): título elegível, sem boleto em aberto, conta do Inter conectada, cliente com CPF/CNPJ e
    /// endereço de cobrança completos; sem e-mail de cobrança é só aviso (o boleto sai sem envio).
    /// </summary>
    private async Task<(List<BillingPrecondition> Items, string? Email)> PreconditionsAsync(Receivable receivable, PartySummary? party, IReadOnlyList<BillingAccountOption> accounts, CancellationToken ct)
    {
        var items = new List<BillingPrecondition>();
        if (!BillingSlipRules.Eligible(receivable))
        {
            items.Add(new BillingPrecondition("receivable_not_eligible", "Só título em aberto, com saldo e vencimento, recebe boleto.", null, null, true));
        }

        if (receivable.BillingSlipId is { } current)
        {
            var status = await db.BillingSlips.AsNoTracking().Where(s => s.Id == current).Select(s => (BillingSlipStatus?)s.Status).FirstOrDefaultAsync(ct);
            items.Add(status == BillingSlipStatus.Failed
                ? new BillingPrecondition(FailedCode, "A última emissão deste título falhou. Use Tentar de novo ou cancele o boleto.", "Ver boleto", Link(receivable.Id), true)
                : new BillingPrecondition(ExistsCode, "Este título já tem um boleto em aberto. Cancele o atual para emitir outro.", "Ver boleto", Link(receivable.Id), true));
        }

        if (accounts.Count == 0)
        {
            items.Add(new BillingPrecondition(MissingIntegrationCode, NoIntegrationMessage, "Configurar o Banco Inter", "/app/configuracoes/integracoes/inter", true));
        }

        var fix = PartyLink(receivable.PartyId);
        if (party is null || string.IsNullOrWhiteSpace(party.Document))
        {
            items.Add(new BillingPrecondition("party_document", "O cliente está sem CPF/CNPJ.", "Corrigir cadastro do cliente", fix, true));
        }

        var address = await parties.DefaultAddressAsync(receivable.PartyId, AddressKind.Billing, ct);
        if (!AddressComplete(address))
        {
            items.Add(new BillingPrecondition("party_address", "O cliente está sem endereço de cobrança completo (CEP, rua, número, bairro, cidade e UF).", "Corrigir cadastro do cliente", fix, true));
        }

        var contact = await parties.BillingContactAsync(receivable.PartyId, ct);
        var email = contact?.Email is { Length: > 0 } e && EmailAddresses.IsPlain(e.Trim()) ? e.Trim() : null;
        if (email is null)
        {
            items.Add(new BillingPrecondition("party_email", "O cliente está sem e-mail de cobrança: o boleto pode ser emitido, mas não sai por e-mail.", "Corrigir cadastro do cliente", fix, false));
        }

        return (items, email);
    }

    internal static bool AddressComplete(PartyAddressInfo? address) =>
        address is not null && Digits(address.ZipCode).Length == 8 && !string.IsNullOrWhiteSpace(address.Street) && !string.IsNullOrWhiteSpace(address.Number)
        && !string.IsNullOrWhiteSpace(address.District) && !string.IsNullOrWhiteSpace(address.City) && address.State is { Length: 2 };

    internal static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    internal async Task<BillingSlipView> ViewAsync(BillingSlip slip, CancellationToken ct)
    {
        var account = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.Id == slip.BankAccountId).Select(a => a.Name).FirstOrDefaultAsync(ct);
        var sandbox = await db.BankIntegrations.AsNoTracking().Where(i => i.BankAccountId == slip.BankAccountId).Select(i => i.Environment == BankEnvironment.Sandbox).FirstOrDefaultAsync(ct);
        var emails = (await emailLogs.ForAsync(AttachmentOwner, slip.Id, ct)).Select(l => new BillingSlipEmailView(l.Id, EmailAddresses.Mask(l.To), l.Status, l.CreatedAt, l.SentAt, l.Error)).ToList();
        async Task<string?> Name(Guid? id) => id is { } u ? await users.DisplayNameAsync(u, ct) : null;
        return new BillingSlipView(slip.Id, slip.ReceivableId, slip.BankAccountId, account ?? string.Empty, sandbox, slip.Status, slip.OurReference, slip.OurNumber, slip.Amount, slip.DueDate,
            slip.ReceivableDueDate, slip.FineFrom, slip.FinePct, slip.InterestMonthPct, slip.DiscountAmount, slip.DiscountUntil, slip.Message, slip.Barcode, slip.DigitableLine, slip.PixCopyPaste,
            slip.QrCode, slip.PdfAttachmentId, slip.FailureReason, slip.FailureAction, slip.CancelReason, slip.CancelReason is { } r ? BillingSlipRules.ReasonText(r) : null, slip.CancelPending,
            slip.CancelFailureReason, slip.CancelledAt, slip.ExpiredAt, slip.PaidAt, slip.RequestedAt, await Name(slip.RequestedBy), slip.IssuedAt, await Name(slip.ApprovedBy),
            slip.SendOnIssue, slip.ScheduledEmailAt, slip.LastEmailAt, emails, slip.Version);
    }

    internal static Error NotFound() => Error.NotFound("billing_slip_not_found", "Boleto não encontrado neste título.");
}
