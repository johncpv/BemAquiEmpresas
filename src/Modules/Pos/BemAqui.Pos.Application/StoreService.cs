using BemAqui.BuildingBlocks.Identity;
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

/// <summary>Loja. CX-01: o aviso de sangria (nulo = mantém o atual) e o teto de dinheiro no caixa (nulo = sem teto), PP-22.</summary>
public sealed record StoreInput(
    string Code, string Name, Guid StockLocationId, Guid DefaultPriceListId, bool IsActive = true, decimal? CashDropAlertAmount = null, decimal? CashCeilingAmount = null,
    ReceiptPrintMode? ReceiptPrintMode = null, bool? FreeRemoveLast = null, int? SyncAlertHours = null);

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

/// <summary>
/// Loja na retaguarda. IMP-01: o modo fiscal (<c>NonFiscal</c> = "só cupom não fiscal"), a versão do texto aceito, quem e quando aceitou,
/// se o texto mudou desde o aceite (<c>NeedsNewAcceptance</c>, CA 13) e o que o caixa faz com o cupom depois da venda.
/// </summary>
public sealed record StoreView(
    Guid Id,
    string Code,
    string Name,
    Guid StockLocationId,
    string StockLocationName,
    Guid DefaultPriceListId,
    string DefaultPriceListName,
    bool IsActive,
    decimal CashDropAlertAmount,
    decimal? CashCeilingAmount,
    uint Version,
    IReadOnlyList<RegisterView> Registers,
    StoreFiscalMode FiscalMode = StoreFiscalMode.Nfce,
    int? NonFiscalTermsVersion = null,
    DateTimeOffset? NonFiscalAcceptedAt = null,
    string? NonFiscalAcceptedByName = null,
    bool NeedsNewAcceptance = false,
    ReceiptPrintMode ReceiptPrintMode = ReceiptPrintMode.Ask,
    bool FreeRemoveLast = true,
    int SyncAlertHours = Store.DefaultSyncAlertHours);

/// <summary>Ligar ou desligar o modo "só cupom não fiscal" (IMP-01 CA 10): ligar exige o aceite do texto na versão atual.</summary>
public sealed record NonFiscalModeInput(bool Enabled, bool Accepted, int? TermsVersion, uint Version);

/// <summary>O texto do aceite e a versão dele (IMP-01 CA 10 e 13).</summary>
public sealed record NonFiscalTermsView(int Version, string Title, string Text);

/// <summary>
/// Texto do aceite do modo "só cupom não fiscal" (PP-05). Versionado: mudou a redação, sobe <see cref="Version"/> e o Proprietário aceita de novo
/// (CA 13); o aceite anterior fica no histórico da auditoria da loja. A redação final passa pelo jurídico (QC-13 como orientação; RP-17).
/// </summary>
public static class NonFiscalTerms
{
    public const int Version = 1;

    /// <summary>Quando cada versão do texto passou a valer (OFF-02: a venda confere o aceite da versão vigente no instante dela).</summary>
    private static readonly (int Version, DateTimeOffset From)[] History = [(1, DateTimeOffset.MinValue)];

    public static int VersionAt(DateTimeOffset at) => History.Where(h => h.From <= at).Select(h => h.Version).DefaultIfEmpty(Version).Max();

    public const string Title = "Vender só com cupom não fiscal";

    public const string Text =
        "Ao ligar este modo, esta loja passa a vender entregando ao cliente um comprovante que diz \"Não é documento fiscal\", sem emitir NFC-e.\n\n"
        + "A obrigação de emitir documento fiscal é da empresa. O BemAqui não avalia se a sua empresa está dispensada dessa obrigação nem responde por essa escolha.\n\n"
        + "Antes de ligar, oriente-se com a contadora da sua empresa. Você pode desligar o modo a qualquer momento, e o relatório \"Vendas sem documento fiscal\" mostra tudo o que foi vendido assim.\n\n"
        + "Ao aceitar, você declara que leu este texto e que a decisão é da empresa.";
}

public sealed record NamedOption(Guid Id, string Name);

/// <summary>Opções dos formulários de loja e caixa: locais de estoque, tabelas de preço e contas do tipo Caixa ainda livres.</summary>
public sealed record StoreFormOptions(IReadOnlyList<NamedOption> Locations, IReadOnlyList<NamedOption> PriceLists, IReadOnlyList<NamedOption> CashAccounts, Guid? DefaultPriceListId);

/// <summary>
/// Lojas e caixas (PDV-01 CA 2 e 3), telas de retaguarda autorizadas com <c>sales.*</c>: loja com código curto único, local de estoque e tabela
/// de preço; caixa com número único na loja, conta do tipo Caixa que nenhum outro caixa usa e o modo de tela. O aparelho do caixa é do Identity
/// (<see cref="IRegisterDevices"/>); aqui só se mostra o último contato. Os outros módulos entram só pelos contratos.
/// </summary>
public sealed class StoreService(
    IPosDb db, ILocations locations, IPriceLists priceLists, ICashAccounts cashAccounts, IRegisterDevices devices, ICurrentUser currentUser, IPermissionService permissions,
    IUserDirectory users, TimeProvider clock)
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
        var acceptedBy = new Dictionary<Guid, string?>();
        foreach (var id in stores.Where(s => s.NonFiscalAcceptedBy is not null).Select(s => s.NonFiscalAcceptedBy!.Value).Distinct())
        {
            acceptedBy[id] = await users.DisplayNameAsync(id, cancellationToken);
        }

        return stores.Select(s => new StoreView(
            s.Id, s.Code, s.Name, s.StockLocationId, locationNames.GetValueOrDefault(s.StockLocationId, "Local removido"),
            s.DefaultPriceListId, listNames.GetValueOrDefault(s.DefaultPriceListId, "Tabela removida"), s.IsActive, s.CashDropAlertAmount, s.CashCeilingAmount, s.Version,
            registers.Where(r => r.StoreId == s.Id).Select(r => ToView(r, accountNames, deviceInfo, now)).ToList(),
            s.FiscalMode, s.NonFiscalTermsVersion, s.NonFiscalAcceptedAt, s.NonFiscalAcceptedBy is { } by ? acceptedBy.GetValueOrDefault(by) : null,
            s.NonFiscalAccepted && s.NonFiscalTermsVersion < NonFiscalTerms.Version, s.ReceiptPrintMode, s.FreeRemoveLast, s.SyncAlertHours)).ToList();
    }

    public static NonFiscalTermsView Terms() => new(NonFiscalTerms.Version, NonFiscalTerms.Title, NonFiscalTerms.Text);

    /// <summary>
    /// Liga ou desliga o modo "só cupom não fiscal" (IMP-01 CA 10, 11 e 13). Só o grupo Proprietário, mesmo para quem edita lojas (403). Ligar
    /// exige o aceite do texto na versão atual; aceitar de novo (texto novo) troca a versão. A loja é auditada: o histórico guarda quem, quando,
    /// a loja e a versão de cada aceite (o anterior fica lá).
    /// </summary>
    public async Task<Result<StoreView>> SetNonFiscalModeAsync(Guid storeId, NonFiscalModeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (currentUser.UserId is not { } me || !(await permissions.GetAsync(me, cancellationToken)).IsOwner)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só o Proprietário liga ou desliga o modo \"só cupom não fiscal\".");
        }

        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        if (store is null)
        {
            return StoreNotFound();
        }

        if (store.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A loja mudou desde que você abriu. Recarregue a tela.");
        }

        if (!input.Enabled)
        {
            if (store.FiscalMode == StoreFiscalMode.Nfce)
            {
                return (await ListAsync(cancellationToken)).First(s => s.Id == storeId);
            }

            store.DisableNonFiscal();
            return await SaveAsync(store.Id, cancellationToken);
        }

        if (!input.Accepted)
        {
            return Invalid("accepted", "Leia o texto e marque o aceite para ligar o modo \"só cupom não fiscal\".");
        }

        if (input.TermsVersion != NonFiscalTerms.Version)
        {
            return Error.Conflict("terms_changed", "O texto do aceite mudou. Leia a versão nova e aceite de novo.");
        }

        if (store.NonFiscalAccepted && store.NonFiscalTermsVersion == NonFiscalTerms.Version)
        {
            return (await ListAsync(cancellationToken)).First(s => s.Id == storeId);
        }

        store.AcceptNonFiscal(NonFiscalTerms.Version, me, clock.GetUtcNow());
        return await SaveAsync(store.Id, cancellationToken);
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
            accounts.Where(a => !used.Contains(a.Id) && !IsSafe(a)).Select(a => new NamedOption(a.Id, a.Name)).ToList(),
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

        if (CashLimitsError(input, current) is { } limits)
        {
            return limits;
        }

        if (input.SyncAlertHours is < 1 or > 72)
        {
            return Invalid("syncAlertHours", "Informe o aviso de caixa sem enviar vendas entre 1 e 72 horas.");
        }

        if (input.ReceiptPrintMode is { } printMode && !Enum.IsDefined(printMode))
        {
            return Invalid("receiptPrintMode", "Escolha o que o caixa faz com o cupom: perguntar, imprimir sempre ou não imprimir.");
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

        if (IsSafe(account))
        {
            return Invalid("cashAccountId", "O Cofre recebe as sangrias e dá o troco: escolha outra conta do tipo Caixa para o caixa.");
        }

        if (await db.Registers.AnyAsync(r => r.CashAccountId == input.CashAccountId && (current == null || r.Id != current.Id), cancellationToken))
        {
            return Invalid("cashAccountId", $"A conta {account.Name} já é de outro caixa. Cada caixa tem a sua conta.");
        }

        return null;
    }

    /// <summary>CX-01: o Cofre (do pacote do Comércio ou chamado "Cofre") é a contrapartida das sangrias; nunca é a conta de um caixa.</summary>
    private static bool IsSafe(CashAccountOption account) =>
        account.SeedKey == "cofre" || string.Equals(account.Name.Trim(), "Cofre", StringComparison.OrdinalIgnoreCase);

    private static void Apply(Store store, StoreInput input)
    {
        store.Code = PosRules.NormalizeCode(input.Code);
        store.Name = input.Name.Trim();
        store.StockLocationId = input.StockLocationId;
        store.DefaultPriceListId = input.DefaultPriceListId;
        store.IsActive = input.IsActive;
        store.CashDropAlertAmount = input.CashDropAlertAmount ?? store.CashDropAlertAmount;
        store.CashCeilingAmount = input.CashCeilingAmount;
        store.ReceiptPrintMode = input.ReceiptPrintMode ?? store.ReceiptPrintMode;
        store.FreeRemoveLast = input.FreeRemoveLast ?? store.FreeRemoveLast;
        store.SyncAlertHours = input.SyncAlertHours ?? store.SyncAlertHours;
    }

    /// <summary>CX-01 (PP-22): aviso de sangria de 0 a R$ 9.999.999,99 e teto (opcional) acima do aviso, em reais com até 2 casas.</summary>
    private static Error? CashLimitsError(StoreInput input, Store? current)
    {
        var alert = input.CashDropAlertAmount ?? current?.CashDropAlertAmount ?? 1000.00m;
        if (alert < 0 || alert > CashSessionIngestion.MaxAmount || decimal.Round(alert, 2) != alert)
        {
            return Invalid("cashDropAlertAmount", "Informe o aviso de sangria em reais, com até 2 casas.");
        }

        if (input.CashCeilingAmount is { } ceiling && (ceiling <= 0 || ceiling > CashSessionIngestion.MaxAmount || decimal.Round(ceiling, 2) != ceiling))
        {
            return Invalid("cashCeilingAmount", "Informe o teto de dinheiro em reais, com até 2 casas, ou deixe em branco para não ter teto.");
        }

        return input.CashCeilingAmount is { } top && top <= alert
            ? Invalid("cashCeilingAmount", "O teto de dinheiro no caixa tem de ser maior que o aviso de sangria.")
            : null;
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
