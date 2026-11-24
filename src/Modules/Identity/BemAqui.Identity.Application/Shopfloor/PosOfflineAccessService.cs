using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>Código de aprovação sem rede (OFF-03, PP-39): 6 números, duas vezes, diferente do PIN do caixa.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetOfflineCodeInput(string Code, string Confirmation)
{
    public override string ToString() => "SetOfflineCodeInput { }";
}

/// <summary>Os parâmetros do hash PBKDF2 guardado (formato V3 do <c>PasswordHasher</c>): o aparelho refaz a mesma derivação com o WebCrypto.</summary>
internal sealed record PinHashParts(string Algorithm, int Iterations, byte[] Salt, byte[] Subkey);

/// <summary>
/// Entrada e aprovação sem rede no caixa (OFF-03, adendo à ADR 0026; desenho no Change Log da story):
/// <list type="bullet">
/// <item><b>Descida (CA 1):</b> para cada operador (PIN) e supervisor (código de aprovação sem rede, PP-39) da loja, os parâmetros PBKDF2 do hash
/// guardado e o verificador <c>HMAC-SHA256(chave derivada, "…verifier|aparelho|pessoa|nonce")</c>. No aparelho, a lista fica cifrada com uma chave
/// AES-GCM não extraível própria, separada da chave ECDSA. O segredo e a chave derivada nunca descem.</item>
/// <item><b>Subida (CA 4, 7 e 15):</b> a entrada e a aprovação sem rede levam a prova <c>HMAC-SHA256(chave derivada, mensagem)</c>, que só quem
/// digitou o segredo calcula (o verificador não serve para forjá-la). O servidor refaz com o hash atual: segredo trocado ou pessoa desativada não confere.</item>
/// <item>O código do supervisor nunca vale com rede: quebrá-lo no aparelho rende só aprovações sem rede dentro dos limites da loja.</item>
/// </list>
/// </summary>
public sealed class PosOfflineAccessService(
    IIdentityDb db,
    IPasswordHasher<User> hasher,
    IPermissionService permissions,
    IEmployees employees,
    ShopfloorRevocations revocations,
    TimeProvider clock) : IPosOfflineAccess
{
    public const string OperatorKind = "operator";
    public const string SupervisorKind = "supervisor";

    /// <summary>Ações que o supervisor aprova sem rede: a troca de preço precisa de rede (rascunho A, item 11).</summary>
    public static readonly IReadOnlyList<string> OfflineSupervisorPermissions = ["pos.discount", "pos.cancel_item", "pos.cancel_sale"];

    public const string SameAsPinMessage = "O código sem internet tem de ser diferente do seu PIN do caixa.";

    public async Task<IReadOnlyList<PosOfflineVerifier>> VerifiersAsync(Guid deviceId, Guid storeId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from p in db.PosPins.AsNoTracking()
            join u in db.Users.AsNoTracking() on p.UserId equals u.Id
            where u.Status == UserStatus.Active && (!u.IsOperationUser || u.OperationStoreIds.Contains(storeId))
            select new { u.Id, u.Name, p.PinHash, p.MustChange, p.OfflineCodeHash }).ToListAsync(cancellationToken);
        var list = new List<PosOfflineVerifier>();
        var issued = new List<(Guid UserId, PinVerifierKind Kind, string Fingerprint)>();
        foreach (var row in rows)
        {
            var effective = await permissions.GetAsync(row.Id, cancellationToken);
            var name = PinNames.ShortName(row.Name);
            if (!row.MustChange && PinProfile.CanUsePosPin(effective) && Verifier(OperatorKind, deviceId, row.Id, name, row.PinHash, PosActions(effective)) is { } op)
            {
                list.Add(op);
                issued.Add((row.Id, PinVerifierKind.OperatorPin, Fingerprint(row.PinHash)));
            }

            var approves = PinProfile.SupervisorActions(effective).Where(OfflineSupervisorPermissions.Contains).Select(a => a[4..]).ToList();
            if (row.OfflineCodeHash is { } code && approves.Count > 0 && Verifier(SupervisorKind, deviceId, row.Id, name, code, approves) is { } sup)
            {
                list.Add(sup);
                issued.Add((row.Id, PinVerifierKind.SupervisorCode, Fingerprint(code)));
            }
        }

        await RecordIssuedAsync(deviceId, issued, cancellationToken);
        return list.OrderBy(v => v.DisplayName, StringComparer.CurrentCulture).ToList();
    }

    public async Task<PosOfflineCheck> CheckLoginAsync(Guid deviceId, Guid storeId, Guid userId, string message, string? proof, CancellationToken cancellationToken = default)
    {
        var (check, hash) = await CheckAsync(deviceId, storeId, userId, PinVerifierKind.OperatorPin, message, proof, cancellationToken);
        if (check == PosOfflineCheck.Valid && hash is not null && !PinProfile.CanUsePosPin(await permissions.GetAsync(userId, cancellationToken)))
        {
            check = PosOfflineCheck.UserInactive;
        }

        revocations.Audit("Auth", userId.ToString(), "PosPinOfflineLogin", userId, new { device = deviceId, result = check.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return check;
    }

    public async Task<PosOfflineCheck> CheckApprovalAsync(Guid deviceId, Guid storeId, Guid supervisorId, string message, string? proof, CancellationToken cancellationToken = default)
    {
        var (check, _) = await CheckAsync(deviceId, storeId, supervisorId, PinVerifierKind.SupervisorCode, message, proof, cancellationToken);
        revocations.Audit("Auth", supervisorId.ToString(), "PosOfflineApproval", supervisorId, new { device = deviceId, result = check.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return check;
    }

    public async Task RecordLockAsync(Guid deviceId, Guid userId, string kind, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        revocations.Audit("Auth", userId.ToString(), "PosPinOfflineLocked", userId, new { device = deviceId, kind = kind == SupervisorKind ? SupervisorKind : OperatorKind, at });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>"Código de aprovação sem internet" da própria pessoa (Meu perfil): quem aprova como supervisor no caixa; login completo.</summary>
    public async Task<Result> SetMineAsync(Guid userId, bool fullSession, SetOfflineCodeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!fullSession)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Para criar o código sem internet, entre com e-mail e senha.");
        }

        var effective = await permissions.GetAsync(userId, cancellationToken);
        var row = await db.PosPins.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active && !u.IsOperationUser, cancellationToken);
        if (user is null || !CanHaveOfflineCode(effective))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "O código sem internet é de quem aprova como supervisor no caixa.");
        }

        if (row is null)
        {
            return Error.Conflict("pos_pin_required", "Crie primeiro o PIN do caixa; o código sem internet vem depois.");
        }

        var birth = user.EmployeeId is { } employeeId ? await employees.BirthDateAsync(employeeId, cancellationToken) : null;
        var problem = PinRules.Problem(input.Code, input.Confirmation, birth)
            ?? (hasher.VerifyHashedPassword(user, row.PinHash, input.Code ?? string.Empty) != PasswordVerificationResult.Failed ? SameAsPinMessage : null);
        if (problem is not null)
        {
            return Error.Validation(Error.Codes.Validation, problem, new Dictionary<string, string[]> { ["code"] = [problem] });
        }

        row.SetOfflineCode(hasher.HashPassword(user, input.Code!), clock.GetUtcNow());
        revocations.Audit("Auth", userId.ToString(), "PosOfflineCodeSet", userId, null);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteMineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await db.PosPins.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (row?.OfflineCodeHash is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Você não tem código sem internet.");
        }

        row.ClearOfflineCode();
        revocations.Audit("Auth", userId.ToString(), "PosOfflineCodeErased", userId, null);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public static bool CanHaveOfflineCode(EffectivePermissions permissions) =>
        PinProfile.SupervisorActions(permissions).Any(OfflineSupervisorPermissions.Contains);

    /// <summary>Lê o formato V3 do <c>PasswordHasher</c>: 0x01, PRF, iterações, tamanho do sal, sal e a chave derivada (big-endian).</summary>
    internal static PinHashParts? Parse(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length < 13 || bytes[0] != 0x01)
        {
            return null;
        }

        var algorithm = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)) switch
        {
            1 => "PBKDF2-SHA256",
            2 => "PBKDF2-SHA512",
            _ => null,
        };
        var iterations = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5));
        var saltLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9));
        if (algorithm is null || iterations is < 10_000 or > 10_000_000 || saltLength is < 16 or > 64 || bytes.Length - 13 - (int)saltLength < 32)
        {
            return null;
        }

        return new PinHashParts(algorithm, (int)iterations, bytes[13..(13 + (int)saltLength)], bytes[(13 + (int)saltLength)..]);
    }

    internal static string Mac(byte[] key, string message) => Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message)));

    internal static string Fingerprint(string hash) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));

    private static PosOfflineVerifier? Verifier(string kind, Guid deviceId, Guid userId, string name, string hash, IReadOnlyList<string> actions)
    {
        if (Parse(hash) is not { } parts)
        {
            return null; // hash num formato antigo: a pessoa entra com rede uma vez (o rehash do login atualiza)
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var verifier = Mac(parts.Subkey, PosOfflineProof.VerifierMessage(kind, deviceId, userId, nonce));
        return new PosOfflineVerifier(userId, name, kind, parts.Algorithm, parts.Iterations, Convert.ToBase64String(parts.Salt), parts.Subkey.Length, nonce, verifier, actions);
    }

    private static List<string> PosActions(EffectivePermissions effective) =>
        effective.Permissions.Where(p => p.StartsWith("pos.", StringComparison.Ordinal)).Select(p => p[4..]).Order(StringComparer.Ordinal).ToList();

    private async Task<(PosOfflineCheck Check, string? Hash)> CheckAsync(Guid deviceId, Guid storeId, Guid userId, PinVerifierKind kind, string message, string? proof,
        CancellationToken cancellationToken)
    {
        var row = await db.PosPins.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active || (user.IsOperationUser && !user.OperationStoreIds.Contains(storeId)) || row is null)
        {
            return (PosOfflineCheck.UserInactive, null);
        }

        var hash = kind == PinVerifierKind.OperatorPin ? row.PinHash : row.OfflineCodeHash;
        if (Parse(hash) is not { } parts)
        {
            return (PosOfflineCheck.SecretChanged, null);
        }

        byte[] given;
        try
        {
            given = Convert.FromBase64String(proof ?? string.Empty);
        }
        catch (FormatException)
        {
            given = [];
        }

        var expected = HMACSHA256.HashData(parts.Subkey, Encoding.UTF8.GetBytes(message));
        if (given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected))
        {
            return (PosOfflineCheck.Valid, hash);
        }

        // A prova não confere: se o segredo mudou depois da última descida para este aparelho, é troca; senão, prova inválida.
        var fingerprint = Fingerprint(hash!);
        var changed = await db.DevicePinVerifiers.AsNoTracking()
            .AnyAsync(v => v.DeviceId == deviceId && v.UserId == userId && v.Kind == kind && v.Fingerprint != fingerprint, cancellationToken);
        return (changed ? PosOfflineCheck.SecretChanged : PosOfflineCheck.InvalidProof, hash);
    }

    private async Task RecordIssuedAsync(Guid deviceId, IReadOnlyList<(Guid UserId, PinVerifierKind Kind, string Fingerprint)> issued, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var current = await db.DevicePinVerifiers.Where(v => v.DeviceId == deviceId).ToListAsync(cancellationToken);
        foreach (var row in current.Where(r => !issued.Any(i => i.UserId == r.UserId && i.Kind == r.Kind)))
        {
            db.DevicePinVerifiers.Remove(row);
        }

        foreach (var item in issued)
        {
            var row = current.FirstOrDefault(r => r.UserId == item.UserId && r.Kind == item.Kind);
            if (row is null)
            {
                db.DevicePinVerifiers.Add(new DevicePinVerifier { DeviceId = deviceId, UserId = item.UserId, Kind = item.Kind, Fingerprint = item.Fingerprint, IssuedAt = now });
            }
            else if (row.Fingerprint != item.Fingerprint)
            {
                row.Fingerprint = item.Fingerprint;
                row.IssuedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
