using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// O que o aparelho recebe no aperto de mão (PDV-01 CA 10): tudo vem do principal do aparelho e do banco, nunca do corpo. <c>SalePrefix</c> é o
/// prefixo da época (<c>LOJA-02.3-</c>), que o aparelho completa com a sequência a partir de 1.
/// </summary>
public sealed record PosHandshake(
    Guid DeviceId,
    Guid StoreId,
    string StoreCode,
    string StoreName,
    Guid RegisterId,
    int RegisterNumber,
    int Epoch,
    RegisterUiMode UiMode,
    string SalePrefix,
    DateTimeOffset ServerTime);

/// <summary>Aperto de mão e o segredo novo (só para o cookie).</summary>
public sealed record PosHandshakeResult(PosHandshake Handshake, DeviceSecretRotation Rotation);

/// <summary>
/// Sincronização do aparelho de caixa (PDV-01 CA 8 a 10, ADR 0048). Nesta story, só o aperto de mão: confirma o caixa do aparelho e gira o segredo.
/// A subida de eventos (que aceita o aparelho revogado) e a descida do catálogo são da OFF-01 e da OFF-02.
/// </summary>
public sealed class PosSyncService(IPosDb db, IRegisterDevices devices, TimeProvider clock)
{
    public const string ReplacedMessage = "Este caixa recebeu outro aparelho. Registre este aparelho de novo.";

    public async Task<Result<PosHandshakeResult>> HandshakeAsync(DeviceIdentity device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.RegisterId is not { } registerId || device.StoreId is not { } storeId)
        {
            return Error.Forbidden("device_kind", "Este aparelho não é um caixa.");
        }

        // A loja e o caixa vêm do principal (CA 10); o caixa precisa ainda ter este aparelho como o ativo da mesma época.
        var register = await db.Registers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registerId && r.StoreId == storeId, cancellationToken);
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        if (register is null || store is null || register.ActiveDeviceId != device.DeviceId || register.DeviceEpoch != device.Epoch)
        {
            return Error.Forbidden("device_replaced", ReplacedMessage);
        }

        if (!register.IsActive || !store.IsActive)
        {
            return Error.Forbidden("register_inactive", "Este caixa está desativado. Fale com o gerente da loja.");
        }

        var rotation = await devices.CompleteSyncAsync(device.DeviceId, cancellationToken);
        if (rotation is null)
        {
            return Error.Forbidden("device_revoked", "Este aparelho foi bloqueado. Registre o aparelho de novo.");
        }

        var handshake = new PosHandshake(
            device.DeviceId, store.Id, store.Code, store.Name, register.Id, register.Number, register.DeviceEpoch, register.UiMode,
            PosSaleNumber.Prefix(store.Code, register.Number, register.DeviceEpoch), clock.GetUtcNow());
        return new PosHandshakeResult(handshake, rotation);
    }
}
