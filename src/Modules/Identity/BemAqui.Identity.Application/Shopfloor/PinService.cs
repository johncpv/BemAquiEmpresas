using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>Quem tem PIN no aparelho (CA 13): só primeiro nome e inicial; o último a entrar vem primeiro.</summary>
public sealed record PinUser(Guid UserId, string DisplayName);

public sealed record PinDevice(string Name, DeviceTheme Theme);

public sealed record PinUsers(PinDevice Device, IReadOnlyList<PinUser> Users);

public sealed record CreatePinInput(string Pin, string Confirmation);

/// <summary>Um PIN meu (Meu perfil → Segurança): o aparelho, quando criei e quando usei.</summary>
public sealed record MyPin(Guid DeviceId, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>Entrada por PIN aceita: a sessão e o segredo novo do aparelho (vai só para o cookie).</summary>
public sealed record PinLogin(LoginSession Session, string DeviceSecret, DateTimeOffset DeviceExpiresAt);

/// <summary>
/// PIN do aparelho da fábrica (ADR 0026 §2, PRD-03b CA 12 a 20): criar só depois do login completo num aparelho registrado, entrar
/// por PIN com o mesmo cuidado do login (tempo uniforme, bloqueio por par e por aparelho, auditoria), perfil reconferido a cada
/// entrada e segredo do aparelho trocado a cada entrada.
/// </summary>
public sealed class PinService(
    IIdentityDb db,
    IPasswordHasher<User> hasher,
    ShopfloorDeviceGate gate,
    ShopfloorRevocations revocations,
    ShopfloorDeviceService devices,
    IPermissionService permissions,
    IEmployees employees,
    AuthService auth,
    IAlertSink alerts,
    ITenantContext tenant,
    TimeProvider clock)
{
    public const string NoEmployeeMessage = "Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor.";
    public const string InvalidPin = "PIN incorreto. Confira e tente de novo.";
    public const string LockedPin = "PIN bloqueado por 15 minutos. Entre com e-mail e senha ou espere.";
    public const string ErasedPin = "PIN apagado depois de muitas tentativas erradas. Entre com e-mail e senha e crie um novo PIN.";
    public const string LockedDevice = "Entrada por PIN bloqueada neste aparelho por 15 minutos, por excesso de tentativas. Entre com e-mail e senha ou espere.";

    /// <summary>Lista do <c>/fab/entrar</c>; null quando o aparelho não é válido (a rota responde 404).</summary>
    public async Task<PinUsers?> UsersAsync(string? secret, CancellationToken cancellationToken = default)
    {
        var device = await gate.ResolveAsync(secret, cancellationToken);
        if (device is null)
        {
            return null;
        }

        var rows = await (
            from p in db.UserPins.AsNoTracking()
            join u in db.Users.AsNoTracking() on p.UserId equals u.Id
            where p.DeviceId == device.Id && u.Status == UserStatus.Active
            select new { u.Id, u.Name, p.LastUsedAt }).ToListAsync(cancellationToken);
        var users = rows
            .OrderByDescending(r => r.LastUsedAt.HasValue).ThenByDescending(r => r.LastUsedAt).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Select(r => new PinUser(r.Id, PinNames.ShortName(r.Name))).ToList();
        return new PinUsers(new PinDevice(device.Name, device.Theme), users);
    }

    /// <summary>
    /// Criar PIN (CA 12, 17 e 18): login completo, aparelho registrado, perfil sem ação reservada (exceto a da qualidade), funcionário
    /// ativo, PIN de 6 dígitos não óbvio. Substitui o PIN anterior do mesmo par.
    /// </summary>
    public async Task<Result<ShopfloorDeviceContext>> CreateAsync(string? secret, Guid userId, bool fullSession, CreatePinInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!fullSession)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Para criar o PIN, entre com e-mail e senha.");
        }

        var device = await gate.ResolveAsync(secret, cancellationToken);
        if (device is null)
        {
            return Error.Conflict("device_not_registered", ShopfloorDeviceService.NotRegisteredMessage);
        }

        var effective = await permissions.GetAsync(userId, cancellationToken);
        if (PinProfile.SensitiveActions(effective) is { Count: > 0 } sensitive)
        {
            revocations.Audit("Auth", userId.ToString(), "PinRefused", userId, new { device = device.Id, reason = "sensitive_profile", actions = sensitive });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Forbidden("auth.pin_profile_not_allowed", PinProfile.NotAllowedMessage);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken);
        if (user is null || !await devices.EmployeeActiveAsync(userId, cancellationToken))
        {
            return Error.Validation("auth.pin_employee_inactive", NoEmployeeMessage);
        }

        var birth = user.EmployeeId is { } employeeId ? await employees.BirthDateAsync(employeeId, cancellationToken) : null;
        if (PinRules.Problem(input.Pin, input.Confirmation, birth) is { } problem)
        {
            return Error.Validation(Error.Codes.Validation, problem, new Dictionary<string, string[]> { ["pin"] = [problem] });
        }

        var now = clock.GetUtcNow();
        var hash = hasher.HashPassword(user, input.Pin);
        var pin = await db.UserPins.FirstOrDefaultAsync(p => p.UserId == userId && p.DeviceId == device.Id, cancellationToken);
        if (pin is null)
        {
            db.UserPins.Add(UserPin.Create(userId, device.Id, hash, now));
        }
        else
        {
            pin.Replace(hash, now);
        }

        revocations.Audit("Auth", userId.ToString(), pin is null ? "PinCreated" : "PinReplaced", userId, new { device = device.Id });
        await db.SaveChangesAsync(cancellationToken);
        return await devices.ContextAsync(secret, userId, fullSession, cancellationToken);
    }

    /// <summary>"Apagar meu PIN deste aparelho" (CA 9): apaga o PIN e encerra as sessões por PIN do usuário neste aparelho.</summary>
    public async Task<Result> DeleteOnDeviceAsync(string? secret, Guid userId, CancellationToken cancellationToken = default)
    {
        var device = await gate.ResolveAsync(secret, cancellationToken);
        if (device is null)
        {
            return Error.Conflict("device_not_registered", ShopfloorDeviceService.NotRegisteredMessage);
        }

        return await DeleteAsync(userId, device.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<MyPin>> MineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return await (
            from p in db.UserPins.AsNoTracking()
            join d in db.ShopfloorDevices.AsNoTracking() on p.DeviceId equals d.Id
            where p.UserId == userId && d.RevokedAt == null && d.ExpiresAt > now
            orderby d.Name
            select new MyPin(d.Id, d.Name, p.CreatedAt, p.LastUsedAt)).ToListAsync(cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        var pin = await db.UserPins.FirstOrDefaultAsync(p => p.UserId == userId && p.DeviceId == deviceId, cancellationToken);
        if (pin is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Você não tem PIN neste aparelho.");
        }

        db.UserPins.Remove(pin);
        var now = clock.GetUtcNow();
        foreach (var token in await db.RefreshTokens.Where(t => t.UserId == userId && t.DeviceId == deviceId && t.RevokedAt == null).ToListAsync(cancellationToken))
        {
            token.Revoke(now);
        }

        revocations.Audit("Auth", userId.ToString(), "PinDeleted", userId, new { device = deviceId });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Entrar por PIN (CA 7, 13 a 18 e 20). O aparelho já foi validado pelo cookie (sem ele a rota é 404). Erro de usuário sem PIN e PIN
    /// errado têm a mesma resposta e o mesmo custo (uma verificação PBKDF2 sempre). Aceito: segredo novo do aparelho, sessão por PIN.
    /// </summary>
    public async Task<Result<PinLogin>> LoginAsync(ShopfloorDevice device, Guid userId, string? pin, string host, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var now = clock.GetUtcNow();
        var probe = User.Invite("ninguem@exemplo.com");
        if (device.IsPinLocked(now))
        {
            hasher.VerifyHashedPassword(probe, AuthService.DummyHash, pin ?? string.Empty);
            revocations.Audit("ShopfloorDevice", device.Id.ToString(), "PinLoginBlocked", null, new { device = device.Id, user = userId, reason = "device_locked" });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation("auth.device_pin_locked", LockedDevice);
        }

        var row = await db.UserPins.FirstOrDefaultAsync(p => p.DeviceId == device.Id && p.UserId == userId, cancellationToken);
        var user = row is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken);
        if (row is null || user is null || !PinRules.IsWellFormed(pin))
        {
            hasher.VerifyHashedPassword(user ?? probe, row?.PinHash ?? AuthService.DummyHash, pin ?? string.Empty);
            if (row is not null && user is not null)
            {
                return await FailAsync(device, row, userId, "bad_format", cancellationToken);
            }

            await DeviceFailureAsync(device, userId, "unknown_user", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation("auth.pin_invalid", InvalidPin);
        }

        if (row.IsLocked(now))
        {
            hasher.VerifyHashedPassword(user, row.PinHash, pin!);
            revocations.Audit("Auth", userId.ToString(), "PinLoginBlocked", userId, new { device = device.Id, reason = "pin_locked", row.LockedUntil });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation("auth.pin_locked", LockedPin);
        }

        var verified = hasher.VerifyHashedPassword(user, row.PinHash, pin!);
        if (verified == PasswordVerificationResult.Failed)
        {
            return await FailAsync(device, row, userId, "wrong_pin", cancellationToken);
        }

        // Perfil reconferido a cada entrada (CA 17, 18 e 20): ganhou ação reservada, o PIN é apagado.
        var effective = await permissions.GetAsync(userId, cancellationToken);
        if (PinProfile.SensitiveActions(effective) is { Count: > 0 } sensitive)
        {
            db.UserPins.Remove(row);
            revocations.Audit("Auth", userId.ToString(), "PinErased", userId, new { device = device.Id, reason = "sensitive_profile", actions = sensitive });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Forbidden("auth.pin_profile_changed", PinProfile.ChangedMessage);
        }

        if (!effective.Has("shopfloor.create"))
        {
            revocations.Audit("Auth", userId.ToString(), "PinLoginRefused", userId, new { device = device.Id, reason = "no_shopfloor_create" });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Forbidden(Error.Codes.Forbidden, "Seu perfil não usa o app do operador. Entre com e-mail e senha ou fale com o administrador.");
        }

        if (!await devices.EmployeeActiveAsync(userId, cancellationToken))
        {
            revocations.Audit("Auth", userId.ToString(), "PinLoginRefused", userId, new { device = device.Id, reason = "employee_not_active" });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Forbidden("auth.pin_employee_inactive", NoEmployeeMessage);
        }

        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            row.Replace(hasher.HashPassword(user, pin!), row.CreatedAt);
        }

        row.RegisterSuccess(now);
        var secret = OpaqueToken.New();
        device.Rotate(OpaqueToken.Hash(secret), now);
        var session = await auth.StartPinSessionAsync(user, device.Id, host, ip, cancellationToken);
        revocations.Audit("Auth", userId.ToString(), "PinLoginSucceeded", userId, new { device = device.Id });
        await db.SaveChangesAsync(cancellationToken);
        return new PinLogin(session, secret, device.ExpiresAt);
    }

    private async Task<Result<PinLogin>> FailAsync(ShopfloorDevice device, UserPin row, Guid userId, string reason, CancellationToken cancellationToken)
    {
        var outcome = row.RegisterFailure(clock.GetUtcNow());
        if (outcome == PinFailure.Erased)
        {
            db.UserPins.Remove(row);
        }

        var action = outcome switch { PinFailure.Erased => "PinErased", PinFailure.Locked => "PinLocked", _ => "PinLoginFailed" };
        revocations.Audit("Auth", userId.ToString(), action, userId, new { device = device.Id, reason });
        await DeviceFailureAsync(device, userId, reason, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return outcome switch
        {
            PinFailure.Erased => Error.Validation("auth.pin_erased", ErasedPin),
            PinFailure.Locked => Error.Validation("auth.pin_locked", LockedPin),
            _ => Error.Validation("auth.pin_invalid", InvalidPin),
        };
    }

    /// <summary>Limite de erros por aparelho (CA 14): 20 em 15 min, somando todos os usuários, bloqueiam e avisam o Proprietário e a plataforma.</summary>
    private async Task DeviceFailureAsync(ShopfloorDevice device, Guid userId, string reason, CancellationToken cancellationToken)
    {
        if (!device.RegisterPinFailure(clock.GetUtcNow()))
        {
            return;
        }

        revocations.Audit("ShopfloorDevice", device.Id.ToString(), "DevicePinLocked", null, new { device = device.Id, user = userId, reason });
        await revocations.NotifyOwnersAsync($"Entrada por PIN bloqueada no aparelho {device.Name} por 15 minutos: muitos PINs errados.", cancellationToken);
        await alerts.RaiseAsync(new Alert(AlertKinds.AuthAbuse, tenant.Tenant?.Key, $"Entrada por PIN bloqueada no aparelho {device.Name}: 20 PINs errados em 15 minutos."), cancellationToken);
    }
}