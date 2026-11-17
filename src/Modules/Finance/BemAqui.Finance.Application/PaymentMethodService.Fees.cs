using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class PaymentMethodService
{
    /// <summary>
    /// Formas sugeridas ("Usar as formas mais comuns" e, depois, o seed do perfil Comércio na PERF-04): Dinheiro, Pix, Débito, Crédito à
    /// vista e Crédito parcelado, todas "A confirmar", sem adquirente e sem taxa. Nunca rodam sozinhas num tenant (a Konebras não as vê).
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Name, PaymentMethodType Type)> Suggested =
    [
        ("dinheiro", "Dinheiro", PaymentMethodType.Cash),
        ("pix", "Pix", PaymentMethodType.Pix),
        ("debito", "Cartão de débito", PaymentMethodType.Debit),
        ("credito", "Cartão de crédito à vista", PaymentMethodType.Credit),
        ("credito-parcelado", "Cartão de crédito parcelado", PaymentMethodType.CreditInstallments),
    ];

    public const string CashAccountName = "Caixa da loja";

    /// <summary>
    /// Cria as formas sugeridas que faltam (idempotente pela <c>seed_key</c>; não recria a excluída). Dinheiro vai para o primeiro caixa ativo
    /// (sem caixa, cria "Caixa da loja"); as demais, para a conta bancária padrão, que precisa existir.
    /// </summary>
    public async Task<Result<IReadOnlyList<PaymentMethodDetail>>> ApplySuggestedAsync(CancellationToken cancellationToken = default)
    {
        var existing = (await db.PaymentMethods.IgnoreQueryFilters().Where(m => m.SeedKey != null).Select(m => m.SeedKey!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var missing = Suggested.Where(s => !existing.Contains(s.Key)).ToList();
        if (missing.Count == 0)
        {
            return Result<IReadOnlyList<PaymentMethodDetail>>.Success([]);
        }

        var bank = await db.BankAccounts.Where(a => a.IsActive && a.Kind == BankAccountKind.Bank).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Name)
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        if (bank is null && missing.Any(s => s.Type != PaymentMethodType.Cash))
        {
            return Error.Validation("bank_account_missing", "Cadastre a conta bancária principal antes: Pix e cartões caem nela.");
        }

        var now = clock.GetUtcNow();
        Guid? cash = null;
        if (missing.Any(s => s.Type == PaymentMethodType.Cash))
        {
            cash = await db.BankAccounts.Where(a => a.IsActive && a.Kind == BankAccountKind.Cash).OrderBy(a => a.Name).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
            if (cash is null)
            {
                var account = new BankAccount { Name = CashAccountName, Kind = BankAccountKind.Cash, CreatedAt = now };
                db.BankAccounts.Add(account);
                cash = account.Id;
            }
        }

        var created = new List<PaymentMethod>();
        foreach (var (key, name, type) in missing)
        {
            var method = new PaymentMethod
            {
                SeedKey = key, Name = name, Type = type, DestinationAccountId = type == PaymentMethodType.Cash ? cash!.Value : bank!.Value,
                NfePaymentCode = PaymentMethod.DefaultNfeCode(type), AllowsChange = type == PaymentMethodType.Cash, CreatedAt = now,
            };
            db.PaymentMethods.Add(method);
            events.Publish(new PaymentMethodChanged(method.Id));
            created.Add(method);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<PaymentMethodDetail>>.Success(await DetailsAsync(created, cancellationToken));
    }

    /// <summary>A tabela inteira da forma, vigentes e encerradas, com quem cadastrou e quando (histórico do CA 3).</summary>
    public async Task<Result<IReadOnlyList<AcquirerFeeView>>> FeesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.PaymentMethods.AnyAsync(m => m.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var fees = await db.AcquirerFees.AsNoTracking().Where(f => f.PaymentMethodId == id)
            .OrderBy(f => f.Brand).ThenBy(f => f.Installments).ThenByDescending(f => f.ValidFrom).ToListAsync(cancellationToken);
        var names = await NamesAsync(fees.Select(f => f.CreatedBy), cancellationToken);
        var today = BusinessTime.Today(clock);
        return Result<IReadOnlyList<AcquirerFeeView>>.Success(fees.Select(f => ToView(f, today, names)).ToList());
    }

    /// <summary>
    /// Nova taxa (CA 2 e 3). Já havendo taxa para a mesma adquirente, bandeira e parcelas, a nova começa depois da anterior e a partir de hoje,
    /// e a anterior é encerrada na véspera; a primeira pode começar em qualquer data.
    /// </summary>
    public async Task<Result<AcquirerFeeView>> AddFeeAsync(Guid id, AcquirerFeeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var method = await db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return NotFound();
        }

        if (!PaymentMethod.AcceptsAcquirer(method.Type) || method.AcquirerPartyId is not { } acquirer)
        {
            return Error.Validation("payment_method_acquirer_missing", "Taxa e prazo são da adquirente: informe a adquirente da forma antes de cadastrar as taxas.");
        }

        if (FeeError(method.Type, input) is { } invalid)
        {
            return invalid;
        }

        var current = await db.AcquirerFees.Where(f => f.PaymentMethodId == id && f.AcquirerPartyId == acquirer && f.Brand == input.Brand
            && f.Installments == input.Installments && f.ValidTo == null).OrderByDescending(f => f.ValidFrom).FirstOrDefaultAsync(cancellationToken);
        var today = BusinessTime.Today(clock);
        if (current is not null)
        {
            if (input.ValidFrom <= current.ValidFrom || input.ValidFrom < today)
            {
                var start = current.ValidFrom >= today ? current.ValidFrom.AddDays(1) : today;
                return Invalid("validFrom", $"A nova taxa começa a partir de {start:dd/MM/yyyy}: taxa passada não se altera.");
            }

            current.CloseAt(input.ValidFrom);
        }

        var fee = new AcquirerFee
        {
            PaymentMethodId = id, AcquirerPartyId = acquirer, Brand = input.Brand, Installments = input.Installments, FeePercent = input.FeePercent,
            SettlementDays = input.SettlementDays, ValidFrom = input.ValidFrom, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.AcquirerFees.Add(fee);
        events.Publish(new PaymentMethodChanged(id));
        await db.SaveChangesAsync(cancellationToken);
        return ToView(fee, today, await NamesAsync([fee.CreatedBy], cancellationToken));
    }

    private static Error? FeeError(PaymentMethodType type, AcquirerFeeInput input)
    {
        if (!Enum.IsDefined(input.Brand))
        {
            return Invalid("brand", "Escolha a bandeira.");
        }

        var (min, max) = PaymentMethod.InstallmentRange(type);
        if (input.Installments < min || input.Installments > max)
        {
            return Invalid("installments", min == max ? "Esta forma é à vista: a taxa vale para 1 parcela." : $"Informe a partir de quantas parcelas, de {min} a {max}.");
        }

        if (input.FeePercent < 0 || input.FeePercent > MaxFeePercent || decimal.Round(input.FeePercent, 4) != input.FeePercent)
        {
            return Invalid("feePercent", "Informe a taxa entre 0% e 100%, com até 4 casas.");
        }

        return input.SettlementDays is < 0 or > MaxSettlementDays
            ? Invalid("settlementDays", $"Informe o prazo em dias, de 0 a {MaxSettlementDays}.")
            : null;
    }
}
