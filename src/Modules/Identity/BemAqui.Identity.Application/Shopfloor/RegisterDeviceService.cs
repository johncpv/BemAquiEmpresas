using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Pos.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>
/// Pedido de registro do aparelho (PDV-01 CA 4 e 6): tipo (só "Caixa" nesta versão), a loja e o caixa escolhidos, a chave pública ECDSA P-256 que o
/// aparelho gerou (SubjectPublicKeyInfo em base64) e a confirmação de troca quando o caixa já tem aparelho.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterPosDeviceInput(string Name, DeviceKind Kind, Guid StoreId, Guid RegisterId, string PublicKey, bool ReplaceActive = false);

/// <summary>Aparelho de caixa como a tela vê: tipo, loja e caixa, época, situação e o último contato.</summary>
public sealed record PosDeviceView(
    Guid Id,
    string Name,
    DeviceKind Kind,
    Guid? StoreId,
    Guid? RegisterId,
    string? RegisterLabel,
    int Epoch,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastSyncAt,
    DateTimeOffset? RevokedAt,
    string? RevokedReason);

/// <summary>Aparelho registrado agora: o segredo vai só para o cookie <c>__Host-bq_pos</c>.</summary>
public sealed record PosDeviceRegistration(PosDeviceView Device, string Secret, DateTimeOffset ExpiresAt);

/// <summary>
/// Aparelhos de caixa do PDV (PDV-01 CA 4 a 8, ADR 0048): registrar com login completo, <c>pos.manage_devices</c> e TOTP recente (o endpoint
/// confere), escolhendo a loja e o caixa; a chave pública fica no aparelho; o registro é auditado e o Proprietário é avisado. Trocar o aparelho de um
/// caixa pede confirmação, revoga o anterior (motivo <c>replaced</c>) e abre uma época nova do caixa. Implementa também o
/// <see cref="IRegisterDevices"/> (o <c>Pos</c> lê o último contato e gira o segredo na sincronização) e o <see cref="IDeviceAuthenticator"/> do
/// esquema <c>Device</c>.
/// </summary>
public sealed class RegisterDeviceService(
    IIdentityDb db,
    ShopfloorRevocations revocations,
    IPosRegisters registers,
    IUserDirectory users,
    TimeProvider clock) : IRegisterDevices, IDeviceAuthenticator
{
    public const int MaxPublicKeyLength = 512;

    /// <summary>OID da curva nistP256 (secp256r1).</summary>
    public const string P256Oid = "1.2.840.10045.3.1.7";

    public async Task<IReadOnlyList<PosDeviceView>> ListAsync(Guid? storeId, CancellationToken cancellationToken = default)
    {
        var devices = await db.ShopfloorDevices.AsNoTracking().Where(d => d.Kind != DeviceKind.Factory && (storeId == null || d.StoreId == storeId))
            .OrderBy(d => d.RevokedAt != null).ThenBy(d => d.Name).ToListAsync(cancellationToken);
        var places = await registers.FindManyAsync(devices.Where(d => d.RegisterId != null).Select(d => d.RegisterId!.Value).Distinct().ToList(), cancellationToken);
        var now = clock.GetUtcNow();
        return devices.Select(d => ToView(d, places, now)).ToList();
    }

    /// <summary>Registra o aparelho de caixa (CA 4, 6 e 7). A sessão por PIN nunca registra; o TOTP recente é conferido no endpoint.</summary>
    public async Task<Result<PosDeviceRegistration>> RegisterAsync(RegisterPosDeviceInput input, string? currentSecret, Guid userId, bool fullSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!fullSession)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Registre o aparelho depois de entrar com e-mail e senha.");
        }

        if (input.Kind != DeviceKind.Register)
        {
            return Invalid("kind", "Por enquanto só dá para registrar aparelhos do tipo Caixa.");
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > ShopfloorDevice.MaxNameLength)
        {
            return Invalid("name", "Dê um nome de até 40 letras ao aparelho, por exemplo Caixa da frente.");
        }

        if (!IsValidPublicKey(input.PublicKey))
        {
            return Invalid("publicKey", "A chave do aparelho não veio certa. Atualize a página e tente de novo.");
        }

        var now = clock.GetUtcNow();
        if (await FindBySecretAsync(currentSecret, cancellationToken) is { } existing && existing.IsActive(now))
        {
            return Error.Conflict("device_already_registered", $"Este aparelho já está registrado como {existing.Name}.");
        }

        var register = await registers.FindAsync(input.RegisterId, cancellationToken);
        if (register is null || register.StoreId != input.StoreId)
        {
            return Invalid("registerId", "Escolha um caixa da loja.");
        }

        if (!register.IsActive)
        {
            return Invalid("registerId", "Este caixa ou a loja está desativado. Escolha outro caixa.");
        }

        // CA 6: um aparelho ativo por caixa; trocar pede confirmação.
        ShopfloorDevice? previous = null;
        if (register.ActiveDeviceId is { } activeId)
        {
            previous = await db.ShopfloorDevices.FirstOrDefaultAsync(d => d.Id == activeId, cancellationToken);
            if (previous is not null && previous.IsActive(now) && !input.ReplaceActive)
            {
                return Error.Conflict("register_has_device", $"O caixa {register.RegisterNumber:00} já tem um aparelho ({previous.Name}). Trocar?") with
                {
                    Data = new Dictionary<string, object?> { ["deviceName"] = previous.Name },
                };
            }
        }

        var secret = OpaqueToken.New();
        var deviceId = Guid.CreateVersion7();
        var assignment = await registers.AssignDeviceAsync(register.RegisterId, deviceId, cancellationToken);
        if (assignment is null)
        {
            return Invalid("registerId", "Este caixa ou a loja está desativado. Escolha outro caixa.");
        }

        var device = ShopfloorDevice.RegisterPos(deviceId, name, OpaqueToken.Hash(secret), userId, register.StoreId, register.RegisterId, assignment.Epoch, input.PublicKey.Trim(), now);
        db.ShopfloorDevices.Add(device);
        revocations.Audit("ShopfloorDevice", deviceId.ToString(), "DeviceRegistered", userId,
            new { device = deviceId, kind = device.Kind.ToString(), store = register.StoreId, register = register.RegisterId, epoch = assignment.Epoch, replaced = previous?.Id, device.ExpiresAt });
        var who = await users.DisplayNameAsync(userId, cancellationToken) ?? "um usuário";
        await revocations.NotifyOwnersAsync($"Novo caixa registrado na loja {register.StoreName} por {who}.", cancellationToken);
        if (previous is not null && previous.RevokedAt is null)
        {
            // Grava tudo de uma vez (o aparelho novo, a época do caixa e o anterior revogado).
            await revocations.RevokeDeviceAsync(previous, userId, "replaced", notifyOwners: false, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var places = new Dictionary<Guid, PosRegisterRef> { [register.RegisterId] = assignment.Register };
        return new PosDeviceRegistration(ToView(device, places, now), secret, device.ExpiresAt);
    }

    /// <summary>Bloqueia o aparelho de caixa (CA 8): o caixa fica sem aparelho e o Proprietário é avisado.</summary>
    public async Task<Result> RevokeAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        var device = await db.ShopfloorDevices.FirstOrDefaultAsync(d => d.Id == id && d.Kind != DeviceKind.Factory, cancellationToken);
        if (device is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Aparelho não encontrado.");
        }

        if (device.RevokedAt is not null)
        {
            return Error.Conflict("device_already_revoked", "Este aparelho já foi bloqueado.");
        }

        await revocations.RevokeDeviceAsync(device, actorId, "manual", notifyOwners: true, cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyDictionary<Guid, RegisterDeviceInfo>> FindAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        if (deviceIds.Count == 0)
        {
            return new Dictionary<Guid, RegisterDeviceInfo>();
        }

        return await db.ShopfloorDevices.AsNoTracking().Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new RegisterDeviceInfo(d.Id, d.Name, d.LastSeenAt, d.LastSyncAt, d.RevokedAt), cancellationToken);
    }

    public async Task<DeviceSyncResult> CompleteSyncAsync(Guid deviceId, bool usedPreviousSecret, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var read = await db.ShopfloorDevices.AsNoTracking().Where(d => d.Id == deviceId && d.Kind != DeviceKind.Factory)
            .Select(d => new { d.SecretHash, d.RevokedAt, d.ExpiresAt }).FirstOrDefaultAsync(cancellationToken);
        if (read is null || read.RevokedAt is not null || read.ExpiresAt <= now)
        {
            return new DeviceSyncResult(DeviceSyncStatus.Revoked, null);
        }

        // B1 (revisão do PR 227): só gira se o segredo ainda é o que foi lido; a outra sincronização simultânea recebe Conflict. Pedida com o segredo
        // anterior (resposta perdida), a rotação já aposenta o anterior novo: um clone que guardou o atual e o apresente depois é reuso (M1).
        var secret = OpaqueToken.New();
        var hash = OpaqueToken.Hash(secret);
        DateTimeOffset? retired = usedPreviousSecret ? now : null;
        var rows = await db.ShopfloorDevices
            .Where(d => d.Id == deviceId && d.SecretHash == read.SecretHash && d.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.PreviousSecretHash, read.SecretHash)
                .SetProperty(d => d.SecretHash, hash)
                .SetProperty(d => d.SecretRotatedAt, now)
                .SetProperty(d => d.PreviousSecretRetiredAt, retired)
                .SetProperty(d => d.LastSyncAt, now)
                .SetProperty(d => d.LastSeenAt, now), cancellationToken);
        return rows == 1
            ? new DeviceSyncResult(DeviceSyncStatus.Rotated, new DeviceSecretRotation(secret, read.ExpiresAt))
            : new DeviceSyncResult(DeviceSyncStatus.Conflict, null);
    }

    /// <summary>
    /// Esquema <c>Device</c> (ADR 0048 §2): acha o aparelho pelo segredo do cookie antes de ler o corpo; depois confere a assinatura ECDSA do texto
    /// da requisição com a chave do aparelho. O segredo atual aposenta o anterior. O anterior só vale enquanto não foi aposentado (resposta perdida
    /// na rede); aposentado e apresentado com a assinatura certa, é reuso de um clone: o aparelho é revogado (<c>secret_reused</c>) e o Proprietário
    /// avisado. Aparelho revogado ou vencido, com segredo e assinatura certos, volta como <see cref="DeviceAuthStatus.Revoked"/>.
    /// </summary>
    public async Task<DeviceAuthResult> AuthenticateAsync(string? secret, Func<CancellationToken, Task<string>> signedPayload, string? signature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signedPayload);
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 128)
        {
            return DeviceAuthResult.Invalid;
        }

        var hash = OpaqueToken.Hash(secret);
        var device = await db.ShopfloorDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Kind != DeviceKind.Factory && (d.SecretHash == hash || d.PreviousSecretHash == hash), cancellationToken);
        if (device?.PublicKey is null || device.RegisterId is null || !VerifySignature(device.PublicKey, await signedPayload(cancellationToken), signature))
        {
            return DeviceAuthResult.Invalid;
        }

        var now = clock.GetUtcNow();
        var revoked = !device.IsActive(now);
        var usedPrevious = !string.Equals(device.SecretHash, hash, StringComparison.Ordinal);
        if (!revoked && usedPrevious && !device.PreviousSecretStillValid)
        {
            // M1: o anterior já foi aposentado; quem o apresenta com a chave do aparelho é um clone revezando com o original.
            var tracked = await db.ShopfloorDevices.FirstAsync(d => d.Id == device.Id, cancellationToken);
            await revocations.RevokeDeviceAsync(tracked, null, "secret_reused", notifyOwners: true, cancellationToken);
            return DeviceAuthResult.Invalid;
        }

        if (!revoked && !usedPrevious && device.PreviousSecretStillValid)
        {
            // O aparelho provou que recebeu o segredo novo: o anterior deixa de valer (UPDATE condicional, sem linha de auditoria).
            await db.ShopfloorDevices.Where(d => d.Id == device.Id && d.SecretHash == hash && d.PreviousSecretRetiredAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.PreviousSecretRetiredAt, now), cancellationToken);
        }

        var identity = new DeviceIdentity(device.Id, device.Kind.ToString(), device.StoreId, device.RegisterId, device.RegisterEpoch, revoked, usedPrevious);
        return new DeviceAuthResult(revoked ? DeviceAuthStatus.Revoked : DeviceAuthStatus.Active, identity);
    }

    /// <summary>Chave pública ECDSA P-256 (SubjectPublicKeyInfo em base64), como o WebCrypto exporta em <c>spki</c>.</summary>
    public static bool IsValidPublicKey(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey.Length > MaxPublicKeyLength)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey.Trim()), out _);
            return key.ExportParameters(false).Curve.Oid.Value == P256Oid; // B10: a curva, não só o tamanho
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Assinatura do WebCrypto (r||s, IEEE P1363) em base64, sobre o texto em UTF-8 com SHA-256.</summary>
    public static bool VerifySignature(string publicKey, string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || signature.Length > 200)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private async Task<ShopfloorDevice?> FindBySecretAsync(string? secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 128)
        {
            return null;
        }

        var hash = OpaqueToken.Hash(secret);
        return await db.ShopfloorDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Kind != DeviceKind.Factory && (d.SecretHash == hash || d.PreviousSecretHash == hash), cancellationToken);
    }

    private static PosDeviceView ToView(ShopfloorDevice d, IReadOnlyDictionary<Guid, PosRegisterRef> places, DateTimeOffset now) => new(
        d.Id, d.Name, d.Kind, d.StoreId, d.RegisterId,
        d.RegisterId is { } register && places.TryGetValue(register, out var place) ? place.Label : null,
        d.RegisterEpoch,
        d.RevokedAt is not null ? "Revoked" : d.ExpiresAt <= now ? "Expired" : "Active",
        d.CreatedAt, d.LastSeenAt, d.LastSyncAt, d.RevokedAt, d.RevokedReason);

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
