using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>O aparelho como o <c>/fab</c> vê (CA 4, 11 e 12): registrado ou não, tema, e o que o usuário logado pode fazer nele.</summary>
public sealed record ShopfloorDeviceContext(
    bool Registered,
    Guid? DeviceId,
    string? Name,
    DeviceTheme Theme,
    DateTimeOffset? ExpiresAt,
    bool HasMyPin,
    bool CanCreatePin,
    string? PinUnavailableReason,
    bool CanRegister,
    string? RegisterUnavailableReason);

public sealed record RegisterDeviceInput(string Name);

public sealed record DeviceThemeInput(DeviceTheme Theme);

/// <summary>Linha de "Aparelhos da fábrica" (CA 9): situação Ativo, Revogado ou Vencido, e os dias sem uso para o aviso perto dos 30.</summary>
public sealed record ShopfloorDeviceRow(
    Guid Id,
    string Name,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedByName,
    string? RevokedReason,
    string Status,
    int DaysUnused,
    int Pins);

public sealed record ShopfloorDeviceList(IReadOnlyList<ShopfloorDeviceRow> Devices, int ActiveCount, int MaxActive);

/// <summary>Aparelho registrado agora: o segredo vai só para o cookie <c>__Host-bq_dev</c>, nunca para o corpo da resposta.</summary>
public sealed record DeviceRegistration(ShopfloorDeviceContext Device, string Secret, DateTimeOffset ExpiresAt);

/// <summary>
/// Aparelhos da fábrica (ADR 0026 §1, PRD-03b CA 4, 5, 8, 9 e 11): registrar com login completo, teto por tenant, tema por aparelho,
/// lista e revogação nas Configurações e o job diário que revoga o sem uso há 30 dias e o vencido.
/// </summary>
public sealed class ShopfloorDeviceService(
    IIdentityDb db,
    ShopfloorDeviceGate gate,
    ShopfloorRevocations revocations,
    IPermissionService permissions,
    IUserDirectory users,
    IEmployees employees,
    TimeProvider clock)
{
    public const string NotRegisteredMessage = "Este aparelho não está registrado. Entre com e-mail e senha.";

    public async Task<ShopfloorDeviceContext> ContextAsync(string? secret, Guid userId, bool fullSession, CancellationToken cancellationToken = default)
    {
        var device = await gate.ResolveAsync(secret, cancellationToken);
        var effective = await permissions.GetAsync(userId, cancellationToken);
        var registerBlocked = !fullSession ? "Registre o aparelho depois de entrar com e-mail e senha."
            : !(effective.Has("production.edit") && effective.Has("shopfloor.create")) ? "Registrar aparelho exige editar a produção e apontar no app do operador."
            : device is not null ? "Este aparelho já está registrado."
            : null;
        if (device is null)
        {
            return new ShopfloorDeviceContext(false, null, null, DeviceTheme.System, null, false, false, NotRegisteredMessage, registerBlocked is null, registerBlocked);
        }

        var hasPin = await db.UserPins.AnyAsync(p => p.DeviceId == device.Id && p.UserId == userId, cancellationToken);
        var pinBlocked = !fullSession ? "Para criar o PIN, entre com e-mail e senha."
            : await PinBlockReasonAsync(userId, effective, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new ShopfloorDeviceContext(true, device.Id, device.Name, device.Theme, device.ExpiresAt, hasPin, pinBlocked is null, pinBlocked, false, registerBlocked);
    }

    /// <summary>Motivo que impede o PIN (CA 17 e 20), ou null.</summary>
    internal async Task<string?> PinBlockReasonAsync(Guid userId, EffectivePermissions effective, CancellationToken cancellationToken)
    {
        if (!effective.Has("shopfloor.create"))
        {
            return "O PIN é para quem aponta no app do operador.";
        }

        if (PinProfile.SensitiveActions(effective).Count > 0)
        {
            return PinProfile.NotAllowedMessage;
        }

        return await EmployeeActiveAsync(userId, cancellationToken) ? null : PinService.NoEmployeeMessage;
    }

    internal async Task<bool> EmployeeActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employeeId = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == UserStatus.Active).Select(u => u.EmployeeId).FirstOrDefaultAsync(cancellationToken);
        if (employeeId is not { } id)
        {
            return false;
        }

        var found = await employees.FindAsync([id], cancellationToken);
        return found.Count == 1 && found[0].IsActive && found[0].Status == EmployeeSituation.Active;
    }

    /// <summary>
    /// Registra o aparelho (CA 4 e 5): nome até 40, teto de aparelhos ativos, segredo opaco de 32 bytes (só o hash no banco), 180 dias,
    /// auditoria e aviso a todos os membros do Proprietário.
    /// </summary>
    public async Task<Result<DeviceRegistration>> RegisterAsync(RegisterDeviceInput input, string? currentSecret, Guid userId, bool fullSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!fullSession)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Registre o aparelho depois de entrar com e-mail e senha.");
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > ShopfloorDevice.MaxNameLength)
        {
            const string message = "Dê um nome de até 40 letras ao aparelho, por exemplo Tablet Linha 1.";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["name"] = [message] });
        }

        if (await gate.ResolveAsync(currentSecret, cancellationToken) is { } existing)
        {
            return Error.Conflict("device_already_registered", $"Este aparelho já está registrado como {existing.Name}.");
        }

        var now = clock.GetUtcNow();
        var max = await MaxActiveAsync(cancellationToken);
        var active = await db.ShopfloorDevices.CountAsync(d => d.RevokedAt == null && d.ExpiresAt > now, cancellationToken);
        if (active >= max)
        {
            return Error.Conflict("device_limit", $"Limite de {max} aparelhos ativos. Revogue um antes de registrar outro.");
        }

        var secret = OpaqueToken.New();
        var device = ShopfloorDevice.Register(name, OpaqueToken.Hash(secret), userId, now);
        db.ShopfloorDevices.Add(device);
        revocations.Audit("ShopfloorDevice", device.Id.ToString(), "DeviceRegistered", userId, new { device = device.Id, device.ExpiresAt });
        var who = await users.DisplayNameAsync(userId, cancellationToken) ?? "um usuário";
        await revocations.NotifyOwnersAsync($"Aparelho {name} registrado por {who}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var effective = await permissions.GetAsync(userId, cancellationToken);
        var pinBlocked = await PinBlockReasonAsync(userId, effective, cancellationToken);
        var context = new ShopfloorDeviceContext(true, device.Id, device.Name, device.Theme, device.ExpiresAt, false, pinBlocked is null, pinBlocked, false, "Este aparelho já está registrado.");
        return new DeviceRegistration(context, secret, device.ExpiresAt);
    }

    /// <summary>Tema do <c>/fab</c> no aparelho (CA 11): vale para todos os operadores do tablet.</summary>
    public async Task<Result<ShopfloorDeviceContext>> SetThemeAsync(string? secret, Guid userId, bool fullSession, DeviceTheme theme, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(theme))
        {
            return Error.Validation(Error.Codes.Validation, "Escolha Automático, Claro ou Escuro.");
        }

        var device = await gate.ResolveAsync(secret, cancellationToken);
        if (device is null)
        {
            return Error.Conflict("device_not_registered", NotRegisteredMessage);
        }

        device.Theme = theme;
        await db.SaveChangesAsync(cancellationToken);
        return await ContextAsync(secret, userId, fullSession, cancellationToken);
    }

    public async Task<ShopfloorDeviceList> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var devices = await db.ShopfloorDevices.AsNoTracking().OrderBy(d => d.RevokedAt != null).ThenBy(d => d.Name).ToListAsync(cancellationToken);
        var ids = devices.Select(d => d.Id).ToList();
        var pins = await db.UserPins.AsNoTracking().Where(p => ids.Contains(p.DeviceId)).GroupBy(p => p.DeviceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var people = devices.Select(d => d.CreatedBy).Concat(devices.Where(d => d.RevokedBy != null).Select(d => d.RevokedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        var rows = devices.Select(d => new ShopfloorDeviceRow(
            d.Id, d.Name, names.GetValueOrDefault(d.CreatedBy, "Usuário removido"), d.CreatedAt, d.LastSeenAt, d.ExpiresAt, d.RevokedAt,
            d.RevokedBy is { } by ? names.GetValueOrDefault(by, "Usuário removido") : null, d.RevokedReason,
            d.RevokedAt is not null ? "Revoked" : d.ExpiresAt <= now ? "Expired" : "Active",
            Math.Max(0, (int)(now - d.LastSeenAt).TotalDays), pins.GetValueOrDefault(d.Id))).ToList();
        return new ShopfloorDeviceList(rows, rows.Count(r => r.Status == "Active"), await MaxActiveAsync(cancellationToken));
    }

    /// <summary>Revoga (CA 9): o PIN deixa de funcionar, as sessões por PIN do aparelho caem e o Proprietário é avisado.</summary>
    public async Task<Result<ShopfloorDeviceList>> RevokeAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        var device = await db.ShopfloorDevices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Aparelho não encontrado.");
        }

        if (device.RevokedAt is not null)
        {
            return Error.Conflict("device_already_revoked", "Este aparelho já foi revogado.");
        }

        await revocations.RevokeDeviceAsync(device, actorId, "manual", notifyOwners: true, cancellationToken);
        return await ListAsync(cancellationToken);
    }

    /// <summary>Job diário (CA 8): revoga o aparelho sem uso há 30 dias ou com os 180 dias vencidos, e as sessões por PIN dele.</summary>
    public async Task<int> RevokeStaleAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var limit = now - ShopfloorDevice.UnusedLimit;
        var stale = await db.ShopfloorDevices.Where(d => d.RevokedAt == null && (d.ExpiresAt <= now || d.LastSeenAt <= limit)).ToListAsync(cancellationToken);
        foreach (var device in stale)
        {
            await revocations.RevokeDeviceAsync(device, null, device.StaleReason(now) ?? "unused", notifyOwners: false, cancellationToken);
        }

        return stale.Count;
    }

    private async Task<int> MaxActiveAsync(CancellationToken cancellationToken)
    {
        var value = await db.TenantSettings.AsNoTracking().Where(s => s.Key == TenantSetting.MaxShopfloorDevices).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) && max is > 0 and <= 100 ? max : ShopfloorDevice.DefaultMaxActive;
    }
}