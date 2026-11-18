using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Catalog.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

public sealed record StoreInput(string Code, string Name, Guid StockLocationId, Guid DefaultPriceListId, bool IsActive = true);

public sealed record RegisterInput(int Number, Guid CashAccountId, RegisterUiMode UiMode, bool IsActive = true);

/// <summary>Aparelho ativo do caixa na tela das lojas: o último contato (sincronização ou uso) e as horas sem contato, para o chip.</summary>
public sealed record RegisterDeviceView(Guid Id, string Name, DateTimeOffset LastContactAt, DateTimeOffset? LastSyncAt, int HoursWithoutContact);

public sealed record RegisterView(
    Guid Id,
    Guid StoreId,
    int Number,
    Guid CashAccountId,
    string CashAccountName,
    RegisterUiMode UiMode,
    bool IsActive,
    int DeviceEpoch,
    RegisterDeviceView? Device);

public sealed record StoreView(
    Guid Id,
    string Code,
    string Name,
    Guid StockLocationId,
    string StockLocationName,
    Guid DefaultPriceListId,
    string DefaultPriceListName,
    bool IsActive,
    uint Version,
    IReadOnlyList<RegisterView> Registers);

public sealed record NamedOption(Guid Id, string Name);

/// <summary>Opções dos formulários de loja e caixa: locais de estoque, tabelas de preço e contas do tipo Caixa ainda livres.</summary>
public sealed record StoreFormOptions(IReadOnlyList<NamedOption> Locations, IReadOnlyList<NamedOption> PriceLists, IReadOnlyList<NamedOption> CashAccounts, Guid? DefaultPriceListId);

/// <summary>
/// Lojas e caixas (PDV-01 CA 2 e 3), telas de retaguarda autorizadas com <c>sales.*</c>: loja com código curto único, local de estoque e tabela
/// de preço; caixa com número único na loja, conta do tipo Caixa que nenhum outro caixa usa e o modo de tela. O aparelho do caixa é do Identity
/// (<see cref="IRegisterDevices"/>); aqui só se mostra o último contato. Os outros módulos entram só pelos contratos.
/// </summary>
public sealed class StoreService(IPosDb db, ILocations locations, IPriceLists priceLists, ICashAccounts cashAccounts, IRegisterDevices devices, TimeProvider clock)
{
    public const string Module = "pos";
    public const string StoreCodeIndex = "ix_stores_code";
    public const string RegisterNumberIndex = "ix_registers_store_number";
    public const string RegisterCashAccountIndex = "ix_registers_cash_account";

    public async Task<IReadOnlyList<StoreView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stores = await db.Stores.AsNoTracking().OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        var registers = await db.Registers.AsNoTracking().OrderBy(r => r.Number).ToListAsync(cancellationToken);
        var locationNames = (await locations.FindAsync(stores.Select(s => s.StockLocationId).Distinct().ToList(), cancellationToken)).ToDictionary(l => l.Id, l => l.Name);
        var listNames = (await priceLists.FindAsync(stores.Select(s => s.DefaultPriceListId).Distinct().ToList(), cancellationToken)).ToDictionary(l => l.Id, l => l.Name);
        var accountNames = (await cashAccounts.FindAsync(registers.Select(r => r.CashAccountId).Distinct().ToList(), cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        var deviceInfo = await devices.FindAsync(registers.Where(r => r.ActiveDeviceId != null).Select(r => r.ActiveDeviceId!.Value).ToList(), cancellationToken);
        var now = clock.GetUtcNow();
        return stores.Select(s => new StoreView(
            s.Id, s.Code, s.Name, s.StockLocationId, locationNames.GetValueOrDefault(s.StockLocationId, "Local removido"),
            s.DefaultPriceListId, listNames.GetValueOrDefault(s.DefaultPriceListId, "Tabela removida"), s.IsActive, s.Version,
            registers.Where(r => r.StoreId == s.Id).Select(r => ToView(r, accountNames, deviceInfo, now)).ToList())).ToList();
    }

    public async Task<StoreFormOptions> OptionsAsync(CancellationToken cancellationToken = default)
    {
        var active = await locations.ActiveAsync(cancellationToken);
        var lists = await priceLists.ActiveAsync(cancellationToken);
        var used = await db.Registers.AsNoTracking().Select(r => r.CashAccountId).ToListAsync(cancellationToken);
        var accounts = await cashAccounts.ActiveAsync(cancellationToken);
        return new StoreFormOptions(
            active.Select(l => new NamedOption(l.Id, l.Name)).ToList(),
            lists.Select(l => new NamedOption(l.Id, l.Name)).ToList(),
            accounts.Where(a => !used.Contains(a.Id)).Select(a => new NamedOption(a.Id, a.Name)).ToList(),
            lists.FirstOrDefault(l => l.IsDefault)?.Id);
    }

    public async Task<Result<StoreView>> CreateStoreAsync(StoreInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ValidateStoreAsync(input, null, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var store = new Store { CreatedAt = clock.GetUtcNow() };
        Apply(store, input);
        db.Stores.Add(store);
        return await SaveAsync(store.Id, cancellationToken);
    }

    public async Task<Result<StoreView>> UpdateStoreAsync(Guid id, StoreInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (store is null)
        {
            return StoreNotFound();
        }

        if (await ValidateStoreAsync(input, store, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        Apply(store, input);
        return await SaveAsync(store.Id, cancellationToken);
    }

    public async Task<Result<StoreView>> CreateRegisterAsync(Guid storeId, RegisterInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Stores.AnyAsync(s => s.Id == storeId, cancellationToken))
        {
            return StoreNotFound();
        }

        if (await ValidateRegisterAsync(storeId, input, null, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var register = new Register { StoreId = storeId, CreatedAt = clock.GetUtcNow() };
        Apply(register, input);
        db.Registers.Add(register);
        return await SaveAsync(storeId, cancellationToken);
    }

    public async Task<Result<StoreView>> UpdateRegisterAsync(Guid storeId, Guid registerId, RegisterInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var register = await db.Registers.FirstOrDefaultAsync(r => r.Id == registerId && r.StoreId == storeId, cancellationToken);
        if (register is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Caixa não encontrado.");
        }

        if (await ValidateRegisterAsync(storeId, input, register, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (!input.IsActive && register.ActiveDeviceId is not null)
        {
            return Error.Conflict("register_has_device", "Bloqueie o aparelho do caixa antes de desativá-lo.");
        }

        Apply(register, input);
        return await SaveAsync(storeId, cancellationToken);
    }

    private async Task<Error?> ValidateStoreAsync(StoreInput input, Store? current, CancellationToken cancellationToken)
    {
        var code = PosRules.NormalizeCode(input.Code);
        if (!PosRules.IsValidStoreCode(code))
        {
            return Invalid("code", "Informe o código da loja com 2 a 6 letras ou números, sem espaço nem acento (ex.: LOJA, CENTRO).");
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > Store.MaxNameLength)
        {
            return Invalid("name", "Informe o nome da loja, com até 80 caracteres.");
        }

        if (await db.Stores.AnyAsync(s => s.Code == code && (current == null || s.Id != current.Id), cancellationToken))
        {
            return Invalid("code", "Já existe uma loja com este código. Use outro.");
        }

        var location = (await locations.FindAsync([input.StockLocationId], cancellationToken)) is [var foundLocation, ..] ? foundLocation : null;
        if (location is null || (!location.IsActive && current?.StockLocationId != input.StockLocationId))
        {
            return Invalid("stockLocationId", "Escolha um local de estoque ativo.");
        }

        var list = (await priceLists.FindAsync([input.DefaultPriceListId], cancellationToken)) is [var foundList, ..] ? foundList : null;
        if (list is null || (!list.IsActive && current?.DefaultPriceListId != input.DefaultPriceListId))
        {
            return Invalid("defaultPriceListId", "Escolha uma tabela de preço ativa.");
        }

        if (!input.IsActive && current is not null && await db.Registers.AnyAsync(r => r.StoreId == current.Id && r.ActiveDeviceId != null, cancellationToken))
        {
            return Error.Conflict("store_has_devices", "A loja tem caixas com aparelho. Bloqueie os aparelhos antes de desativar a loja.");
        }

        return null;
    }

    private async Task<Error?> ValidateRegisterAsync(Guid storeId, RegisterInput input, Register? current, CancellationToken cancellationToken)
    {
        if (!PosRules.IsValidRegisterNumber(input.Number))
        {
            return Invalid("number", "Informe o número do caixa, de 1 a 99.");
        }

        if (!Enum.IsDefined(input.UiMode))
        {
            return Invalid("uiMode", "Escolha o modo de tela: Toque ou Teclado e leitor.");
        }

        if (await db.Registers.AnyAsync(r => r.StoreId == storeId && r.Number == input.Number && (current == null || r.Id != current.Id), cancellationToken))
        {
            return Invalid("number", $"Esta loja já tem o caixa {input.Number:00}. Use outro número.");
        }

        var account = (await cashAccounts.FindAsync([input.CashAccountId], cancellationToken)) is [var foundAccount, ..] ? foundAccount : null;
        if (account is null || (!account.IsActive && current?.CashAccountId != input.CashAccountId))
        {
            return Invalid("cashAccountId", "Escolha uma conta ativa do tipo Caixa.");
        }

        if (await db.Registers.AnyAsync(r => r.CashAccountId == input.CashAccountId && (current == null || r.Id != current.Id), cancellationToken))
        {
            return Invalid("cashAccountId", $"A conta {account.Name} já é de outro caixa. Cada caixa tem a sua conta.");
        }

        return null;
    }

    private static void Apply(Store store, StoreInput input)
    {
        store.Code = PosRules.NormalizeCode(input.Code);
        store.Name = input.Name.Trim();
        store.StockLocationId = input.StockLocationId;
        store.DefaultPriceListId = input.DefaultPriceListId;
        store.IsActive = input.IsActive;
    }

    private static void Apply(Register register, RegisterInput input)
    {
        register.Number = input.Number;
        register.CashAccountId = input.CashAccountId;
        register.UiMode = input.UiMode;
        register.IsActive = input.IsActive;
    }

    /// <summary>Grava e devolve a loja; a corrida pelo mesmo código, número ou conta cai no índice único e vira a mesma mensagem da validação.</summary>
    private async Task<Result<StoreView>> SaveAsync(Guid storeId, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, StoreCodeIndex))
        {
            return Invalid("code", "Já existe uma loja com este código. Use outro.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, RegisterNumberIndex))
        {
            return Invalid("number", "Esta loja já tem um caixa com este número. Use outro.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, RegisterCashAccountIndex))
        {
            return Invalid("cashAccountId", "Esta conta já é de outro caixa. Cada caixa tem a sua conta.");
        }

        return (await ListAsync(cancellationToken)).First(s => s.Id == storeId);
    }

    private static RegisterView ToView(Register r, Dictionary<Guid, string> accounts, IReadOnlyDictionary<Guid, RegisterDeviceInfo> devices, DateTimeOffset now)
    {
        RegisterDeviceView? device = null;
        if (r.ActiveDeviceId is { } id && devices.TryGetValue(id, out var info) && info.RevokedAt is null)
        {
            var last = info.LastSyncAt is { } sync && sync > info.LastSeenAt ? sync : info.LastSeenAt;
            device = new RegisterDeviceView(info.Id, info.Name, last, info.LastSyncAt, Math.Max(0, (int)(now - last).TotalHours));
        }

        return new RegisterView(r.Id, r.StoreId, r.Number, r.CashAccountId, accounts.GetValueOrDefault(r.CashAccountId, "Conta removida"), r.UiMode, r.IsActive, r.DeviceEpoch, device);
    }

    private static Error StoreNotFound() => Error.NotFound(Error.Codes.NotFound, "Loja não encontrada.");

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
