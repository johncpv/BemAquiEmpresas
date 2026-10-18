namespace BemAqui.BuildingBlocks.Jobs;

/// <summary>
/// Job da plataforma (banco de catálogo), sem dado de negócio de tenant: provisionamento, por exemplo, que roda antes
/// de o tenant existir (FND-03b). Job que mexe em dados de um tenant continua obrigado a ser <see cref="TenantJob{TArgs}"/>.
/// </summary>
public abstract class PlatformJob<TArgs>
{
    public abstract Task ExecuteAsync(TArgs args, CancellationToken cancellationToken);
}
