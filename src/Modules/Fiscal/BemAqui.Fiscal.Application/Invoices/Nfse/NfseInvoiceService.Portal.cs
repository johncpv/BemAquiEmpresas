using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>"Registrar NFS-e emitida na prefeitura" (CA 6): os valores como saíram na nota da prefeitura e o PDF ou o XML dela (obrigatório).</summary>
public sealed record RegisterNfseInput(string? Number, string? VerificationCode, DateOnly? IssuedOn, decimal ServicesAmount, decimal IssAmount, uint Version, string? FileName, byte[]? Content);

public sealed partial class NfseInvoiceService
{
    public const string RegisteredMessage = "NFS-e registrada";
    public const string StepUpMessage = "Confirme com o código do aplicativo autenticador para registrar o cancelamento.";

    // ---------- CA 5 e 6: processo provisório pelo portal da prefeitura ----------

    /// <summary>
    /// Registra a NFS-e emitida no portal da prefeitura (CA 6, D5): só no processo provisório (a NFS-e do município ainda não habilitada no
    /// Emissor), com o arquivo da nota triado pelo antivírus e guardado com dono <c>OutgoingInvoice</c>. A nota fica <c>Authorized</c> com a origem
    /// "Registrada (portal da prefeitura)", o ISS digitado vale para o imposto do projeto (comparado ao calculado, sem bloquear) e o
    /// <c>InvoiceAuthorized</c> (<c>type = NFSe</c>) vai ao outbox como na emissão. Número repetido no tenant: 409. Duas gravações juntas: só uma
    /// (<c>xmin</c> e os índices únicos).
    /// </summary>
    public async Task<Result<NfseRegisterResultView>> RegisterAsync(Guid id, RegisterNfseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (mode, _) = await ModeAsync(cancellationToken);
        if (mode != NfseMode.Portal)
        {
            return Error.Conflict(FiscalErrorCodes.Conflict, "O Emissor Fiscal já emite a NFS-e deste município. Use \"Emitir NFS-e\".");
        }

        var loaded = await LoadDraftAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (invoice, items) = loaded.Value;
        var acceptedOn = await db.InstallationBillings.AsNoTracking().Where(b => b.InstallationOrderId == invoice.InstallationOrderId).Select(b => (DateOnly?)b.AcceptedOn)
            .FirstOrDefaultAsync(cancellationToken);
        var number = NfseRules.ParseNumber(input.Number);
        var code = NfseRules.CleanCode(input.VerificationCode);
        var errors = NfseRules.RegistrationErrors(number, code, input.IssuedOn, input.ServicesAmount, input.IssAmount, BusinessTime.Today(clock), acceptedOn);
        if (input.Content is not { Length: > 0 })
        {
            errors["file"] = ["Anexe o PDF ou o XML da NFS-e emitida na prefeitura."];
        }

        if (errors.Count > 0)
        {
            Clear();
            return Error.Validation(FiscalErrorCodes.Validation, errors.First().Value[0], errors);
        }

        if (await db.OutgoingInvoices.AsNoTracking().AnyAsync(i => i.Type == InvoiceType.NFSe && i.Number == number && i.Id != id, cancellationToken))
        {
            Clear();
            return NumberExists(number!.Value);
        }

        var screened = await attachments.ScreenAsync(OutgoingInvoiceReconciler.AttachmentOwner, input.FileName ?? "nfse", input.Content!, cancellationToken);
        if (screened.IsFailure)
        {
            Clear();
            return screened.Error!.Fields is { Count: > 0 } ? screened.Error : Error.Validation(screened.Error.Code, screened.Error.Message,
                new Dictionary<string, string[]> { ["file"] = [screened.Error.Message] });
        }

        // O ISS da nota da prefeitura vale para o imposto do projeto: rateado nas linhas de ISS dos itens pela base, o resto refeito pela soma.
        var calculated = items.SelectMany(i => i.Taxes).Where(t => t.Code == TaxCodes.Iss).Sum(t => t.Amount);
        ApplyTypedIss(items, input.IssAmount);
        invoice.ApplyTotals(OutgoingInvoiceCalculator.Totals(items), null);
        var warnings = new[] { NfseRules.IssDifference(input.IssAmount, calculated), NfseRules.AmountDifference(input.ServicesAmount, invoice.TotalAmount) }
            .Where(w => w is not null).Select(w => w!).ToList();
        var now = clock.GetUtcNow();
        if (invoice.RegisterFromPortal(number!.Value, code!, input.ServicesAmount, input.IssAmount, currentUser.UserId, now) is { } refused)
        {
            Clear();
            return refused;
        }

        if (invoice.Service is { } s)
        {
            invoice.Service = s with { IssAmount = invoice.IssTyped!.Value, NetAmount = NfseRules.NetAmount(invoice.TotalAmount, invoice.IssTyped.Value, s.IssWithheld) };
        }

        invoice.Environment = (await settings.CurrentAsync(cancellationToken)).Environment;
        var stored = await attachments.StoreScreenedAsync(OutgoingInvoiceReconciler.AttachmentOwner, invoice.Id, screened.Value, TenantId().ToString("N"), AttachmentOrigin.User, cancellationToken);
        if (stored.ContentType == "application/xml")
        {
            invoice.AttachAuthorizedXml(stored.Id, stored.Sha256);
        }
        else
        {
            invoice.AttachDanfe(stored.Id);
        }

        await effects.PublishAuthorizedAsync(invoice, input.IssuedOn!.Value, cancellationToken);
        await effects.SendAsync(invoice, resend: false, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A NFS-e já foi registrada ou mudou desde que você abriu. Recarregue.");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_outgoing_invoices_nfse_number", StringComparison.Ordinal) == true)
        {
            Clear();
            return NumberExists(number.Value);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_outgoing_invoices_active_installation", StringComparison.Ordinal) == true)
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.InvoiceExists, "A instalação já tem NFS-e registrada. Recarregue.");
        }

        return new NfseRegisterResultView((await GetAsync(id, cancellationToken)).Value, warnings);
    }

    private static Error NumberExists(long number) =>
        Error.Conflict(FiscalErrorCodes.NfseNumberExists, $"A NFS-e {number.ToString(System.Globalization.CultureInfo.InvariantCulture)} já está registrada nesta empresa. Confira o número da nota.");

    /// <summary>Troca o ISS calculado pelo da nota da prefeitura, rateado pela base das linhas de ISS (a soma bate com o digitado).</summary>
    public static void ApplyTypedIss(IReadOnlyList<OutgoingInvoiceItem> items, decimal typed)
    {
        ArgumentNullException.ThrowIfNull(items);
        var ordered = items.OrderBy(i => i.Sequence).ToList();
        var bases = ordered.Select(i => i.Taxes.Where(t => t.Code == TaxCodes.Iss).Sum(t => t.Base)).ToList();
        var shares = OutgoingInvoiceRules.Apportion(bases, TaxCalculator.Round(typed));
        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            if (!item.Taxes.Any(t => t.Code == TaxCodes.Iss))
            {
                continue;
            }

            item.Taxes = item.Taxes.Select(t => t.Code == TaxCodes.Iss ? t with { Amount = shares[i] } : t).ToList();
            item.CostTaxAmount = item.Taxes.Where(t => t.InProjectCost).Sum(t => t.Amount);
        }
    }

    // ---------- CA 9: cancelamento registrado ----------

    /// <summary>
    /// "Registrar cancelamento feito na prefeitura" (CA 9, D6): com <c>fiscal.approve</c> no endpoint e o código do autenticador (2FA de novo,
    /// conferido antes de tocar a nota), motivo e protocolo. Enquanto o Emissor não cancela NFS-e (lacuna L-m), é o caminho de toda NFS-e,
    /// emitida ou registrada. A nota fica <c>Cancelled</c> (origem manual), o <c>InvoiceCancelled</c> vai ao outbox (o Commercial lança o reverso
    /// <c>InvoiceTaxReversal</c>; a receita sai pelo <c>IOutgoingInvoices</c>) e nada é apagado.
    /// </summary>
    public async Task<Result<NfseView>> RegisterCancellationAsync(Guid id, RegisterNfseCancellationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = currentUser.UserId;
        var verified = actor is { } user && await stepUp.VerifyAsync(user, request.Code, cancellationToken);
        // O 2FA grava no mesmo contexto (o passo usado e a auditoria): fica gravado antes de qualquer mudança na nota (anti-replay).
        await db.SaveChangesAsync(cancellationToken);
        if (!verified)
        {
            return Error.Forbidden(string.IsNullOrWhiteSpace(request.Code) ? FiscalErrorCodes.TwoFactorRequired : FiscalErrorCodes.TwoFactorInvalid, StepUpMessage);
        }

        var errors = NfseRules.CancellationErrors(request.Reason, request.Protocol);
        if (errors.Count > 0)
        {
            return Error.Validation(FiscalErrorCodes.Validation, errors.First().Value[0], errors);
        }

        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.Type == InvoiceType.NFSe && i.InstallationOrderId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Version != request.Version)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A NFS-e mudou desde que você abriu. Recarregue.");
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version;
        var now = clock.GetUtcNow();
        var today = BusinessTime.Today(clock);
        if (invoice.RegisterPortalCancellation(NfeTextRules.Clean(request.Reason)!, NfseRules.CleanCode(request.Protocol)!, actor, now, today) is { } refused)
        {
            Clear();
            return refused;
        }

        events.Publish(new InvoiceCancelled(invoice.Id, invoice.Type, invoice.ProjectId, null, today, invoice.CostTaxAmount, invoice.TotalAmount,
            await effects.CountsInProjectAsync(invoice, cancellationToken)));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "O cancelamento já foi registrado por outra pessoa. Recarregue.");
        }

        return await ViewAsync(invoice, cancellationToken);
    }
}
