using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>
/// <see cref="IIncomingInvoiceReceipts"/> para o recebimento pela nota (FIS-04b): lê a nota conferida e faz as transições na transação do
/// Purchasing (o mesmo <c>DbContext</c> do request); a versão (<c>xmin</c>) da nota segura dois recebimentos ao mesmo tempo (CA 13).
/// </summary>
internal sealed class IncomingInvoicesContract(IFiscalDb db, TimeProvider clock) : IIncomingInvoiceReceipts
{
    public async Task<IncomingInvoiceForReceipt?> FindForReceiptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.IncomingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var items = await db.IncomingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var t = invoice.Totals;
        return new IncomingInvoiceForReceipt(
            invoice.Id, invoice.Number, invoice.Series, DateOnly.FromDateTime(BusinessTime.ToBusiness(invoice.IssuedAt).DateTime), invoice.IssuerTradeName ?? invoice.IssuerName,
            invoice.PartyId, invoice.PurchaseOrderId, invoice.WithoutOrder, Stage(invoice.Status), invoice.GoodsReceiptId, invoice.Version, t.Products, t.Freight, t.Insurance,
            t.OtherExpenses, t.Discount, t.Ipi, t.Total, invoice.Duplicates.Select(d => new IncomingReceiptDuplicate(d.Number, d.DueDate, d.Amount)).ToList(),
            items.Select(i => new IncomingReceiptItem(
                i.Id, i.Sequence, i.Description, i.ItemId, i.PoLineId, i.StockQuantity, i.StockUnitPrice, i.Amount, i.Discount, i.LotNumber, i.NoStockItem, i.ExpenseAccountId, i.Taxes.Icms,
                i.Taxes.Ipi, i.Taxes.Pis, i.Taxes.Cofins)).ToList(),
            await CreditRuleAsync(cancellationToken), IncomingReadingRules.MarkOf(invoice.Source));
    }

    public async Task<Result> MarkReceivedAsync(Guid id, uint version, Guid goodsReceiptId, string creditRule, decimal creditAmount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creditRule);
        var invoice = await db.IncomingInvoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return Error.NotFound(IncomingInvoiceService.NotFoundCode, "Nota de entrada não encontrada.");
        }

        if (invoice.Status == IncomingInvoiceStatus.Received)
        {
            return ReceivedConflict(invoice);
        }

        if (invoice.Status != IncomingInvoiceStatus.Checked)
        {
            return Error.Conflict(IncomingInvoiceReceiptCodes.NotChecked, "Só a nota conferida é recebida. Confira a nota antes.");
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = version;
        invoice.MarkReceived(goodsReceiptId, creditRule, creditAmount, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "A nota mudou desde que você abriu (outro recebimento ou alteração). Recarregue.");
        }
    }

    public async Task<Result> ReturnToCheckedAsync(Guid id, Guid goodsReceiptId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.IncomingInvoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null || invoice.Status != IncomingInvoiceStatus.Received || invoice.GoodsReceiptId != goodsReceiptId)
        {
            // A nota já voltou (ou nunca foi deste recebimento): nada a fazer.
            return Result.Success();
        }

        invoice.ReturnToChecked(clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "A nota mudou ao mesmo tempo. Recarregue e estorne de novo.");
        }
    }

    /// <summary>A regra de crédito em uso e se a contadora conferiu o valor (pelo hash da chave <c>inputTaxCredit</c>).</summary>
    private async Task<InputTaxCreditRule> CreditRuleAsync(CancellationToken ct)
    {
        var settings = await db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var taxes = settings?.InputTaxCredit ?? [];
        var key = FiscalSettingKeys.Find("inputTaxCredit")!;
        var hash = settings is null ? null : key.HashOf(settings);
        var confirmed = hash is not null && await db.SettingConfirmations.AsNoTracking().AnyAsync(c => c.SettingKey == key.Key && c.ValueHash == hash, ct);
        return new InputTaxCreditRule(taxes, confirmed, FiscalSettingsRules.InputTaxCreditLabel(taxes, confirmed));
    }

    private static Error ReceivedConflict(IncomingInvoice invoice) =>
        Error.Conflict(IncomingInvoiceReceiptCodes.Received, $"A NF-e {invoice.Number} já foi recebida.") with
        {
            Data = new Dictionary<string, object?>
            {
                ["goodsReceiptId"] = invoice.GoodsReceiptId,
                ["link"] = invoice.GoodsReceiptId is { } r ? "/app/compras/recebimentos/" + r : IncomingInvoiceReceiptCodes.ScreenLink + invoice.Id,
            },
        };

    private static IncomingInvoiceStage Stage(IncomingInvoiceStatus status) => status switch
    {
        IncomingInvoiceStatus.Checked => IncomingInvoiceStage.Checked,
        IncomingInvoiceStatus.Received => IncomingInvoiceStage.Received,
        IncomingInvoiceStatus.Rejected => IncomingInvoiceStage.Rejected,
        _ => IncomingInvoiceStage.Imported,
    };
}
