using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Formas de pagamento e taxas da adquirente (PAG-01): tipo, conta de destino (Dinheiro só em conta Caixa), adquirente com o papel Adquirente,
/// tabela de taxas por bandeira e parcelas com vigência (a nova encerra a anterior; nenhuma taxa passada é editada) e o selo "A confirmar"
/// das formas sugeridas, conferido por quem tem <c>finance.edit</c>. Em uso numa venda, a forma não é excluída, só desativada.
/// </summary>
public sealed partial class PaymentMethodService(
    IFinanceDb db, IParties parties, IEnumerable<IPaymentMethodUsageProvider> usages, ICurrentUser currentUser, IUserDirectory users, IEventPublisher events, TimeProvider clock)
{
    public const decimal MaxFeePercent = 100m;

    public const int MaxSettlementDays = 400;

    public async Task<PagedList<PaymentMethodDetail>> ListAsync(bool includeInactive, PageRequest page, CancellationToken cancellationToken = default)
    {
        var result = await db.PaymentMethods.AsNoTracking().Where(m => includeInactive || m.IsActive)
            .OrderBy(m => m.Name).ToPagedListAsync(page, cancellationToken);
        return new PagedList<PaymentMethodDetail>(await DetailsAsync(result.Items, cancellationToken), result.Page, result.PageSize, result.Total);
    }

    public async Task<Result<PaymentMethodDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var method = await db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return method is null ? NotFound() : (await DetailsAsync([method], cancellationToken))[0];
    }

    public async Task<Result<PaymentMethodDetail>> CreateAsync(PaymentMethodRequest input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ValidateAsync(input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        // Quem digita a forma já a conferiu: o selo "A confirmar" é só das formas sugeridas pelo sistema (ADR 0042 §1).
        var method = new PaymentMethod { CreatedAt = now, ConfirmedAt = now, ConfirmedBy = currentUser.UserId };
        Apply(method, input);
        db.PaymentMethods.Add(method);
        events.Publish(new PaymentMethodChanged(method.Id));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(method.Id, cancellationToken);
    }

    public async Task<Result<PaymentMethodDetail>> UpdateAsync(Guid id, PaymentMethodRequest input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return NotFound();
        }

        if (await ValidateAsync(input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        Apply(method, input);
        events.Publish(new PaymentMethodChanged(method.Id));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<PaymentMethodDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return NotFound();
        }

        method.IsActive = active;
        events.Publish(new PaymentMethodChanged(method.Id));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Usada em alguma venda (CA 6): 409 <c>payment_method_in_use</c> e o diálogo oferece "Desativar".</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return NotFound();
        }

        foreach (var usage in usages)
        {
            if (await usage.UsageAsync(id, cancellationToken) is { } where)
            {
                return Error.Conflict("payment_method_in_use", "A forma de pagamento já foi usada. Desative-a.") with { Data = new Dictionary<string, object?> { ["usage"] = where } };
            }
        }

        method.MarkDeleted(clock.GetUtcNow());
        events.Publish(new PaymentMethodChanged(method.Id));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>"Marcar como conferido" (CA 4): grava quem e quando (auditado). Cartão sem adquirente não é conferido.</summary>
    public async Task<Result<PaymentMethodDetail>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return NotFound();
        }

        if (PaymentMethod.RequiresAcquirer(method.Type) && method.AcquirerPartyId is null)
        {
            return Error.Validation("payment_method_acquirer_missing", "Informe a adquirente (a empresa da maquininha) antes de marcar como conferido.",
                new Dictionary<string, string[]> { ["acquirerPartyId"] = ["Informe a adquirente."] });
        }

        if (method.ConfirmedAt is null)
        {
            method.ConfirmedAt = clock.GetUtcNow();
            method.ConfirmedBy = currentUser.UserId;
            events.Publish(new PaymentMethodChanged(method.Id));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Adquirentes para o seletor da forma: terceiros ativos com o papel Adquirente.</summary>
    public async Task<IReadOnlyList<AcquirerChoice>> AcquirersAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Acquirer, 20, cancellationToken)).Select(p => new AcquirerChoice(p.Id, p.TradeName ?? p.LegalName, p.Document)).ToList();

    private static void Apply(PaymentMethod method, PaymentMethodRequest input)
    {
        method.Name = input.Name.Trim();
        method.Type = input.Type;
        method.DestinationAccountId = input.DestinationAccountId;
        method.AcquirerPartyId = PaymentMethod.AcceptsAcquirer(input.Type) ? input.AcquirerPartyId : null;
        method.NfePaymentCode = string.IsNullOrWhiteSpace(input.NfePaymentCode) ? PaymentMethod.DefaultNfeCode(input.Type) : input.NfePaymentCode.Trim();
        method.AllowsChange = input.Type == PaymentMethodType.Cash;
    }

    private async Task<Error?> ValidateAsync(PaymentMethodRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            return Invalid("name", "Informe o nome da forma de pagamento, com até 100 caracteres.");
        }

        if (!Enum.IsDefined(input.Type))
        {
            return Invalid("type", "Escolha o tipo da forma de pagamento.");
        }

        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.DestinationAccountId && a.IsActive, ct);
        if (account is null)
        {
            return Invalid("destinationAccountId", "Escolha a conta onde o dinheiro cai.");
        }

        if (input.Type == PaymentMethodType.Cash && account.Kind != BankAccountKind.Cash)
        {
            return Invalid("destinationAccountId", "Dinheiro cai numa conta do tipo Caixa. Escolha um caixa.");
        }

        if (PaymentMethod.RequiresAcquirer(input.Type) && input.AcquirerPartyId is null)
        {
            return Invalid("acquirerPartyId", "Informe a adquirente (a empresa da maquininha).");
        }

        if (PaymentMethod.AcceptsAcquirer(input.Type) && input.AcquirerPartyId is { } acquirer
            && !(await parties.FindAsync(acquirer, ct) is { IsActive: true, IsDeleted: false } party && party.Roles.Contains(PartyRoleKind.Acquirer)))
        {
            return Invalid("acquirerPartyId", "A adquirente precisa ser um terceiro ativo com o papel Adquirente.");
        }

        if (!string.IsNullOrWhiteSpace(input.NfePaymentCode) && !PaymentMethod.NfeCodes.Contains(input.NfePaymentCode.Trim()))
        {
            return Invalid("nfePaymentCode", "Escolha o meio de pagamento da nota fiscal na lista.");
        }

        return null;
    }

    private async Task<IReadOnlyList<PaymentMethodDetail>> DetailsAsync(IReadOnlyList<PaymentMethod> methods, CancellationToken ct)
    {
        var ids = methods.Select(m => m.Id).ToList();
        var accountIds = methods.Select(m => m.DestinationAccountId).Distinct().ToList();
        var accounts = await db.BankAccounts.AsNoTracking().IgnoreQueryFilters().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var acquirerIds = methods.Where(m => m.AcquirerPartyId is not null).Select(m => m.AcquirerPartyId!.Value).Distinct().ToList();
        var acquirers = acquirerIds.Count == 0 ? new Dictionary<Guid, string>()
            : (await parties.FindManyAsync(acquirerIds, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var today = BusinessTime.Today(clock);
        var fees = await db.AcquirerFees.AsNoTracking().Where(f => ids.Contains(f.PaymentMethodId) && f.ValidFrom <= today && (f.ValidTo == null || f.ValidTo > today)).ToListAsync(ct);
        var names = await NamesAsync(fees.Select(f => f.CreatedBy).Concat(methods.Select(m => m.ConfirmedBy)), ct);
        return methods.Select(m => new PaymentMethodDetail(
            m.Id, m.Name, m.Type, m.DestinationAccountId, accounts.GetValueOrDefault(m.DestinationAccountId), m.AcquirerPartyId,
            m.AcquirerPartyId is { } a ? acquirers.GetValueOrDefault(a) : null, m.NfePaymentCode, m.AllowsChange, m.IsActive, m.SeedKey,
            m.ConfirmedAt is not null, m.ConfirmedAt, m.ConfirmedBy is { } c ? names.GetValueOrDefault(c) : null,
            fees.Where(f => f.PaymentMethodId == m.Id && f.AcquirerPartyId == m.AcquirerPartyId).OrderBy(f => f.Brand).ThenBy(f => f.Installments).Select(f => ToView(f, today, names)).ToList(),
            m.CreatedAt)).ToList();
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static AcquirerFeeView ToView(AcquirerFee f, DateOnly today, Dictionary<Guid, string?> names) =>
        new(f.Id, f.Brand, f.Installments, f.FeePercent, f.SettlementDays, f.ValidFrom, f.ValidTo, f.InForce(today), f.CreatedAt, f.CreatedBy,
            f.CreatedBy is { } u ? names.GetValueOrDefault(u) : null);

    private static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    private static Error NotFound() => Error.NotFound("payment_method_not_found", "Forma de pagamento não encontrada.");
}
