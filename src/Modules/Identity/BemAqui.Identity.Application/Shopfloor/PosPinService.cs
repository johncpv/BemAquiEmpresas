using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Pos.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>Quem pode entrar por PIN neste caixa: primeiro nome e inicial; o último a entrar vem primeiro.</summary>
public sealed record PosOperator(Guid UserId, string DisplayName);

/// <summary>Lista do caixa (PDV-01 CA 11): o aparelho, a loja, o caixa e os operadores.</summary>
public sealed record PosOperators(string DeviceName, string StoreName, int RegisterNumber, IReadOnlyList<PosOperator> Operators);

/// <summary>
/// Entrada por PIN no caixa. <c>NewPin</c> e <c>NewPinConfirmation</c> só quando o servidor pede a troca (<c>pin_change_required</c>): o PIN inicial
/// que o gerente deu ao usuário de operação vale só para a primeira entrada (ADR 0049).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PosPinLoginInput(Guid UserId, string Pin, string? NewPin = null, string? NewPinConfirmation = null);

/// <summary>Quem está no caixa (PDV-01 CA 11): o operador da sessão por PIN, a loja, o caixa e as ações do caixa que ele tem.</summary>
public sealed record PosOperatorSession(
    Guid UserId,
    string Name,
    bool IsOperationUser,
    Guid StoreId,
    string StoreName,
    Guid RegisterId,
    int RegisterNumber,
    string DeviceName,
    DateTimeOffset SessionExpiresAt,
    IReadOnlyList<string> Actions);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetPosPinInput(string Pin, string Confirmation);

/// <summary>"PIN do caixa" da própria pessoa (Meu perfil): se tem, quando criou e usou, e se o perfil pode usar.</summary>
public sealed record MyPosPin(bool HasPin, DateTimeOffset? CreatedAt, DateTimeOffset? LastUsedAt, bool CanUse);

/// <summary>
/// Operadores com PIN que podem entrar nos caixas de uma loja (PDV-01 CA 11; OFF-01 CA 10 pelo <see cref="IPosStoreOperators"/>): ativos, com perfil
/// que usa o caixa (<c>pos.create</c>, sem ação reservada) e, se de operação, ligados à loja. O último a entrar vem primeiro.
/// </summary>
public sealed class PosStoreOperators(IIdentityDb db, IPermissionService permissions) : IPosStoreOperators
{
    public async Task<IReadOnlyList<PosStoreOperator>> ForStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from p in db.PosPins.AsNoTracking()
            join u in db.Users.AsNoTracking() on p.UserId equals u.Id
            where u.Status == UserStatus.Active && (!u.IsOperationUser || u.OperationStoreIds.Contains(storeId))
            select new { u.Id, u.Name, p.LastUsedAt }).ToListAsync(cancellationToken);
        var operators = new List<(Guid Id, string Name, DateTimeOffset? LastUsedAt)>();
        foreach (var row in rows)
        {
            if (PinProfile.CanUsePosPin(await permissions.GetAsync(row.Id, cancellationToken)))
            {
                operators.Add((row.Id, row.Name, row.LastUsedAt));
            }
        }

        return operators
            .OrderByDescending(r => r.LastUsedAt.HasValue).ThenByDescending(r => r.LastUsedAt).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Select(r => new PosStoreOperator(r.Id, PinNames.ShortName(r.Name))).ToList();
    }
}

/// <summary>
/// Aparelho de caixa pelo cookie <c>__Host-bq_pos</c>, sem a assinatura (ADR 0048 §2): serve à sessão por PIN do caixa, conferida a cada request, e à
/// renovação dela. Só o caixa ativo; o segredo anterior, como no <c>/fab</c> (<see cref="ShopfloorDeviceGate"/>), só dentro da tolerância da rotação
/// e enquanto não foi aposentado (revisão de segurança da PDV-01, B2). A aposentadoria e a revogação por reuso são do esquema <c>Device</c>, que tem a
/// prova da chave; aqui o segredo fora da tolerância só é recusado.
/// </summary>
public sealed class PosDeviceCookieGate(IIdentityDb db, TokenSigning signing, TimeProvider clock)
{
    public async Task<ShopfloorDevice?> ResolveAsync(string? secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 128)
        {
            return null;
        }

        var hash = OpaqueToken.Hash(secret);
        var device = await db.ShopfloorDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Kind == DeviceKind.Register && (d.SecretHash == hash || d.PreviousSecretHash == hash), cancellationToken);
        var now = clock.GetUtcNow();
        if (device is null || !device.IsActive(now) || device.StoreId is null || device.RegisterId is null)
        {
            return null;
        }

        return device.Match(hash, now, signing.Options.RefreshReuseTolerance) switch
        {
            DeviceSecretMatch.Current => device,
            DeviceSecretMatch.PreviousInTolerance when device.PreviousSecretStillValid => device,
            _ => null,
        };
    }
}

/// <summary>
/// PIN no aparelho de caixa do PDV (PDV-01 CA 11, ADR 0048 e 0049). O operador escolhe o nome e digita o PIN; a entrada chega pelo esquema
/// <c>Device</c> (cookie e assinatura da chave do aparelho), então não há rota anônima nova. Mesmo cuidado do PIN da fábrica (ADR 0026 §2): hash PBKDF2,
/// tempo uniforme (sempre uma verificação), 5 erros seguidos bloqueiam 15 min, o 10º em 24 h apaga, 20 erros em 15 min no aparelho bloqueiam o
/// aparelho e avisam o Proprietário, perfil reconferido a cada entrada, tudo auditado e o PIN nunca em log. A sessão é de 12 h, presa ao aparelho, um
/// operador por caixa, e só alcança a lista <c>[PosPinSessionAllowed]</c>.
/// </summary>
public sealed class PosPinService(
    IIdentityDb db,
    IPasswordHasher<User> hasher,
    PosDeviceCookieGate gate,
    ShopfloorRevocations revocations,
    IPermissionService permissions,
    IEmployees employees,
    IPosRegisters registers,
    AuthService auth,
    IAlertSink alerts,
    ITenantContext tenant,
    PosStoreOperators storeOperators,
    TimeProvider clock)
{
    public const string InvalidPin = "PIN incorreto. Confira e tente de novo.";
    public const string LockedPin = "PIN bloqueado por 15 minutos. Espere ou peça ao gerente um PIN novo.";
    public const string ErasedPin = "PIN apagado depois de muitas tentativas erradas. Peça ao gerente um PIN novo.";
    public const string LockedDevice = "Entrada por PIN bloqueada neste caixa por 15 minutos, por excesso de tentativas.";
    public const string ChangeRequiredCode = "pin_change_required";
    public const string ChangeRequired = "Crie o seu PIN: o PIN que o gerente deu vale só para a primeira entrada.";
    public const string NotAllowedMessage = "Seu perfil não usa o caixa. Fale com o gerente.";
    public const string NotRegisteredMessage = "Este aparelho não é mais um caixa registrado. Registre o aparelho de novo.";

    /// <summary>Operadores com PIN que podem entrar neste caixa: ativos, com <c>pos.create</c>, sem ação reservada e, se de operação, da loja.</summary>
    public async Task<Result<PosOperators>> OperatorsAsync(DeviceIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var device = await RegisterDeviceAsync(identity, tracked: false, cancellationToken);
        if (device is null || await registers.FindAsync(device.RegisterId!.Value, cancellationToken) is not { } place)
        {
            return Error.Conflict("device_not_registered", NotRegisteredMessage);
        }

        var list = (await storeOperators.ForStoreAsync(device.StoreId!.Value, cancellationToken))
            .Select(r => new PosOperator(r.UserId, r.DisplayName)).ToList();
        return new PosOperators(device.Name, place.StoreName, place.RegisterNumber, list);
    }

    /// <summary>
    /// Entrar por PIN no caixa (CA 11). Usuário sem PIN, de outra loja ou PIN errado têm a mesma resposta e o mesmo custo (uma verificação PBKDF2
    /// sempre). Aceito: sessão por PIN do caixa (<c>amr = ["pin", "hwk"]</c>); o segredo do aparelho não gira aqui (gira na sincronização).
    /// </summary>
    /// <remarks>
    /// Tentativas em paralelo (revisão de segurança da PDV-01, M2): a tentativa roda numa transação com <c>pg_advisory_xact_lock</c> do aparelho e
    /// do usuário, nessa ordem, antes de ler os contadores e verificar o PIN. Sem isso, N tentativas simultâneas liam o mesmo contador e somavam um
    /// erro só. As travas valem até o commit; o erro gravado sai no commit, inclusive quando a resposta é recusa.
    /// </remarks>
    public async Task<Result<LoginSession>> LoginAsync(DeviceIdentity identity, PosPinLoginInput input, string host, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("pos.pin.device:" + identity.DeviceId.ToString("N"), cancellationToken);
        await LockAsync("pos.pin.user:" + input.UserId.ToString("N"), cancellationToken);
        var result = await LoginLockedAsync(identity, input, host, ip, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task LockAsync(string key, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(cancellationToken);

    private async Task<Result<LoginSession>> LoginLockedAsync(DeviceIdentity identity, PosPinLoginInput input, string host, string? ip, CancellationToken cancellationToken)
    {
        var device = await RegisterDeviceAsync(identity, tracked: true, cancellationToken);
        if (device is null)
        {
            return Error.Conflict("device_not_registered", NotRegisteredMessage);
        }

        var now = clock.GetUtcNow();
        var probe = User.Invite("ninguem@exemplo.com");
        var pin = input.Pin ?? string.Empty;
        if (device.IsPinLocked(now))
        {
            hasher.VerifyHashedPassword(probe, AuthService.DummyHash, pin);
            revocations.Audit("ShopfloorDevice", device.Id.ToString(), "PosPinLoginBlocked", null, new { device = device.Id, user = input.UserId, reason = "device_locked" });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation("auth.device_pin_locked", LockedDevice);
        }

        var row = await db.PosPins.FirstOrDefaultAsync(p => p.UserId == input.UserId, cancellationToken);
        var user = row is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == input.UserId && u.Status == UserStatus.Active, cancellationToken);
        var store = device.StoreId!.Value;
        if (user is not null && user.IsOperationUser && !user.OperationStoreIds.Contains(store))
        {
            user = null; // usuário de operação de outra loja: igual a quem não tem PIN aqui
        }

        if (row is null || user is null || !PinRules.IsWellFormed(pin))
        {
            hasher.VerifyHashedPassword(user ?? probe, row is not null && user is not null ? row.PinHash : AuthService.DummyHash, pin);
            if (row is not null && user is not null)
            {
                return await FailAsync(device, row, user.Id, "bad_format", cancellationToken);
            }

            await DeviceFailureAsync(device, input.UserId, "unknown_user", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation("auth.pin_invalid", InvalidPin);
        }

        if (row.IsLocked(now))
        {
            hasher.VerifyHashedPassword(user, row.PinHash, pin);
            revocations.Audit("Auth", user.Id.ToString(), "PosPinLoginBlocked", user.Id, new { device = device.Id, reason = "pin_locked", row.LockedUntil });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation("auth.pin_locked", LockedPin);
        }

        var verified = hasher.VerifyHashedPassword(user, row.PinHash, pin);
        if (verified == PasswordVerificationResult.Failed)
        {
            return await FailAsync(device, row, user.Id, "wrong_pin", cancellationToken);
        }

        // Perfil reconferido a cada entrada: ganhou ação reservada, o PIN do caixa é apagado (ADR 0026 §2).
        var effective = await permissions.GetAsync(user.Id, cancellationToken);
        if (PinProfile.SensitiveActions(effective) is { Count: > 0 } sensitive)
        {
            db.PosPins.Remove(row);
            revocations.Audit("Auth", user.Id.ToString(), "PosPinErased", user.Id, new { device = device.Id, reason = "sensitive_profile", actions = sensitive });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Forbidden("auth.pin_profile_changed", "Seu perfil mudou e não pode usar PIN no caixa. Fale com o gerente.");
        }

        if (!effective.Has(PinProfile.PosAction) || !await EmployeeOkAsync(user, cancellationToken))
        {
            revocations.Audit("Auth", user.Id.ToString(), "PosPinLoginRefused", user.Id, new { device = device.Id, reason = effective.Has(PinProfile.PosAction) ? "employee_not_active" : "no_pos_create" });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Forbidden(Error.Codes.Forbidden, NotAllowedMessage);
        }

        if (row.MustChange)
        {
            if (input.NewPin is null)
            {
                row.RegisterSuccess(now);
                revocations.Audit("Auth", user.Id.ToString(), "PosPinChangeRequired", user.Id, new { device = device.Id });
                await db.SaveChangesAsync(cancellationToken);
                return Error.Conflict(ChangeRequiredCode, ChangeRequired);
            }

            var birth = user.EmployeeId is { } employeeId ? await employees.BirthDateAsync(employeeId, cancellationToken) : null;
            var problem = PinRules.Problem(input.NewPin, input.NewPinConfirmation, birth)
                ?? (string.Equals(input.NewPin, pin, StringComparison.Ordinal) ? "O PIN novo precisa ser diferente do que o gerente deu." : null);
            if (problem is not null)
            {
                return Error.Validation(Error.Codes.Validation, problem, new Dictionary<string, string[]> { ["newPin"] = [problem] });
            }

            row.Replace(hasher.HashPassword(user, input.NewPin), mustChange: false, user.Id, now);
            revocations.Audit("Auth", user.Id.ToString(), "PosPinChanged", user.Id, new { device = device.Id, reason = "first_use" });
        }
        else if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            row.Rehash(hasher.HashPassword(user, pin));
        }

        row.RegisterSuccess(now);
        device.Touch(now);
        var session = await auth.StartPosPinSessionAsync(user, device.Id, host, ip, cancellationToken);
        revocations.Audit("Auth", user.Id.ToString(), "PosPinLoginSucceeded", user.Id, new { device = device.Id, store, register = device.RegisterId });
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    /// <summary>Quem está no caixa (CA 11): a sessão por PIN viva deste usuário neste aparelho.</summary>
    public async Task<Result<PosOperatorSession>> CurrentAsync(Guid userId, string? posSecret, CancellationToken cancellationToken = default)
    {
        var device = await gate.ResolveAsync(posSecret, cancellationToken);
        var now = clock.GetUtcNow();
        var expires = device is null ? null : await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.DeviceId == device.Id && t.SessionKind == SessionKind.PosPin && t.RevokedAt == null && t.AbsoluteExpiresAt > now)
            .MaxAsync(t => t.AbsoluteExpiresAt, cancellationToken);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (device is null || expires is null || user is null || await registers.FindAsync(device.RegisterId!.Value, cancellationToken) is not { } place)
        {
            return Error.NotFound(Error.Codes.NotFound, "Ninguém entrou neste caixa.");
        }

        var actions = (await permissions.GetAsync(userId, cancellationToken)).Permissions
            .Where(p => p.StartsWith("pos.", StringComparison.Ordinal)).Select(p => p[4..]).Order(StringComparer.Ordinal).ToList();
        return new PosOperatorSession(user.Id, user.Name, user.IsOperationUser, place.StoreId, place.StoreName, place.RegisterId, place.RegisterNumber, device.Name, expires.Value, actions);
    }

    /// <summary>"Sair" do caixa: encerra a sessão por PIN deste usuário neste aparelho; o PIN continua.</summary>
    public async Task<int> EndAsync(Guid userId, string? posSecret, CancellationToken cancellationToken = default)
    {
        var device = await gate.ResolveAsync(posSecret, cancellationToken);
        var now = clock.GetUtcNow();
        var sessions = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.SessionKind == SessionKind.PosPin && t.RevokedAt == null && (device == null || t.DeviceId == device.Id))
            .ToListAsync(cancellationToken);
        foreach (var token in sessions)
        {
            token.Revoke(now);
        }

        revocations.Audit("Auth", userId.ToString(), "PosPinSessionEnded", userId, new { device = device?.Id, sessions = sessions.Count });
        await db.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    public async Task<MyPosPin> MineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await db.PosPins.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return new MyPosPin(row is not null, row?.CreatedAt, row?.LastUsedAt, PinProfile.CanUsePosPin(await permissions.GetAsync(userId, cancellationToken)));
    }

    /// <summary>
    /// "PIN do caixa" da própria pessoa (Meu perfil): só com login completo, com <c>pos.create</c> e sem ação reservada; 6 números, nada óbvio. Vale em
    /// todo caixa registrado da empresa. O usuário de operação nunca chega aqui (não tem login completo); o PIN dele é do gerente.
    /// </summary>
    public async Task<Result<MyPosPin>> SetMineAsync(Guid userId, bool fullSession, SetPosPinInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!fullSession)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Para criar o PIN do caixa, entre com e-mail e senha.");
        }

        var effective = await permissions.GetAsync(userId, cancellationToken);
        if (PinProfile.SensitiveActions(effective) is { Count: > 0 } sensitive)
        {
            revocations.Audit("Auth", userId.ToString(), "PosPinRefused", userId, new { reason = "sensitive_profile", actions = sensitive });
            await db.SaveChangesAsync(cancellationToken);
            return Error.Forbidden("auth.pin_profile_not_allowed", PinProfile.NotAllowedMessage);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active && !u.IsOperationUser, cancellationToken);
        if (user is null || !effective.Has(PinProfile.PosAction))
        {
            return Error.Forbidden(Error.Codes.Forbidden, NotAllowedMessage);
        }

        if (await PinProblemAsync(user, input.Pin, input.Confirmation, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await UpsertAsync(user, input.Pin, mustChange: false, userId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MineAsync(userId, cancellationToken);
    }

    /// <summary>Apaga o PIN do caixa da própria pessoa e encerra as sessões por PIN dela nos caixas.</summary>
    public async Task<Result> DeleteMineAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await EraseAsync(userId, "self", cancellationToken) ? Result.Success() : Error.NotFound(Error.Codes.NotFound, "Você não tem PIN do caixa.");

    /// <summary>Apaga o PIN do caixa e derruba as sessões por PIN do caixa do usuário (desativação, vínculo trocado, a própria pessoa). Grava.</summary>
    public async Task<bool> EraseAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        var row = await db.PosPins.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var now = clock.GetUtcNow();
        var sessions = await db.RefreshTokens.Where(t => t.UserId == userId && t.SessionKind == SessionKind.PosPin && t.RevokedAt == null).ToListAsync(cancellationToken);
        if (row is null && sessions.Count == 0)
        {
            return false;
        }

        if (row is not null)
        {
            db.PosPins.Remove(row);
        }

        foreach (var token in sessions)
        {
            token.Revoke(now);
        }

        revocations.Audit("Auth", userId.ToString(), "PosPinErased", userId, new { reason, sessions = sessions.Count });
        await db.SaveChangesAsync(cancellationToken);
        return row is not null;
    }

    /// <summary>PIN que o gerente dá ao usuário de operação (criação e "Novo PIN"): trocado no primeiro uso. Não grava; o valor nunca vai à auditoria.</summary>
    internal async Task<Error?> PinProblemAsync(User user, string? pin, string? confirmation, CancellationToken cancellationToken)
    {
        var birth = user.EmployeeId is { } employeeId ? await employees.BirthDateAsync(employeeId, cancellationToken) : null;
        return PinRules.Problem(pin, confirmation, birth) is { } problem
            ? Error.Validation(Error.Codes.Validation, problem, new Dictionary<string, string[]> { ["pin"] = [problem] })
            : null;
    }

    internal async Task UpsertAsync(User user, string pin, bool mustChange, Guid by, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hash = hasher.HashPassword(user, pin);
        var row = await db.PosPins.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        if (row is null)
        {
            db.PosPins.Add(PosPin.Create(user.Id, hash, mustChange, by, now));
        }
        else
        {
            row.Replace(hash, mustChange, by, now);
        }

        revocations.Audit("Auth", user.Id.ToString(), row is null ? "PosPinCreated" : "PosPinReplaced", by, new { mustChange });
    }

    private async Task<ShopfloorDevice?> RegisterDeviceAsync(DeviceIdentity identity, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? db.ShopfloorDevices : db.ShopfloorDevices.AsNoTracking();
        var device = await query.FirstOrDefaultAsync(d => d.Id == identity.DeviceId && d.Kind == DeviceKind.Register, cancellationToken);
        return device is not null && device.IsActive(clock.GetUtcNow()) && device.StoreId is not null && device.RegisterId is not null ? device : null;
    }

    /// <summary>Funcionário ligado (opcional no caixa) precisa estar ativo; o desligamento já desativa o usuário (USR-01).</summary>
    private async Task<bool> EmployeeOkAsync(User user, CancellationToken cancellationToken)
    {
        if (user.EmployeeId is not { } employeeId)
        {
            return true;
        }

        var found = await employees.FindAsync([employeeId], cancellationToken);
        return found.Count == 1 && found[0].IsActive && found[0].Status == EmployeeSituation.Active;
    }

    private async Task<Result<LoginSession>> FailAsync(ShopfloorDevice device, PosPin row, Guid userId, string reason, CancellationToken cancellationToken)
    {
        var outcome = row.RegisterFailure(clock.GetUtcNow());
        if (outcome == PinFailure.Erased)
        {
            db.PosPins.Remove(row);
        }

        var action = outcome switch { PinFailure.Erased => "PosPinErased", PinFailure.Locked => "PosPinLocked", _ => "PosPinLoginFailed" };
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

    /// <summary>Limite por aparelho: 20 erros em 15 min, somando todos os operadores, bloqueiam o caixa e avisam o Proprietário e a plataforma.</summary>
    private async Task DeviceFailureAsync(ShopfloorDevice device, Guid userId, string reason, CancellationToken cancellationToken)
    {
        if (!device.RegisterPinFailure(clock.GetUtcNow()))
        {
            return;
        }

        revocations.Audit("ShopfloorDevice", device.Id.ToString(), "DevicePinLocked", null, new { device = device.Id, user = userId, reason });
        await revocations.NotifyOwnersAsync($"Entrada por PIN bloqueada no caixa {device.Name} por 15 minutos: muitos PINs errados.", cancellationToken);
        await alerts.RaiseAsync(new Alert(AlertKinds.AuthAbuse, tenant.Tenant?.Key, $"Entrada por PIN bloqueada no caixa {device.Name}: 20 PINs errados em 15 minutos."), cancellationToken);
    }
}
