using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Fiscal.Application;

/// <summary>
/// O <see cref="IFiscalEmitter"/> do tenant do escopo (CA 2, 7 e 9): a integração salva, ativa (teste aprovado no endereço da configuração) e com o ambiente dentro
/// do permitido no catálogo (<c>fiscal_environment_allowed</c>). A chave é decifrada aqui, no momento do uso, e só vive na memória.
/// </summary>
public sealed class FiscalEmitterAccess(IFiscalDb db, ISecretProtector secrets, ITenantContext tenant, ITenantProfile profile, IFiscalEmitterFactory factory, IOptions<FiscalIntegrationOptions> options)
{
    public const string NotConfiguredMessage = "Conecte o Emissor Fiscal para emitir notas.";
    public const string InactiveMessage = "A integração fiscal ainda não foi testada. Use \"Testar conexão\".";
    public const string EnvironmentMessage = "A emissão desta empresa só liga na virada para a produção.";

    /// <summary>Emissor pronto para uso; com <paramref name="requireActive"/> falso, vale também antes do primeiro teste aprovado.</summary>
    public async Task<Result<IFiscalEmitter>> CurrentAsync(bool requireActive = true, CancellationToken cancellationToken = default)
    {
        var settings = await db.FiscalIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings?.ApiKeyCipher is null)
        {
            return Error.Conflict(FiscalErrorCodes.NotConfigured, NotConfiguredMessage);
        }

        if (requireActive && !settings.Active)
        {
            return Error.Conflict(FiscalErrorCodes.Inactive, InactiveMessage);
        }

        var tenantId = TenantId();
        if (requireActive && !Allows(await profile.FiscalEnvironmentAllowedAsync(tenantId, cancellationToken), settings.Environment))
        {
            return Error.Forbidden(FiscalErrorCodes.EnvironmentNotAllowed, EnvironmentMessage);
        }

        // O endereço é sempre o da configuração (ADR 0027, adendo de 27/10/2026). Salvo com outro (o antigo da rede interna, ou a
        // configuração mudou), a integração só volta a emitir depois de um "Testar conexão" aprovado no endereço novo.
        var url = FiscalBaseUrl.Configured(options.Value);
        if (url.IsFailure)
        {
            return url.Error!;
        }

        if (requireActive && !SameUrl(settings.BaseUrl, url.Value))
        {
            return Error.Conflict(FiscalErrorCodes.Inactive, InactiveMessage);
        }

        return Result<IFiscalEmitter>.Success(Create(url.Value, Decrypt(settings.ApiKeyCipher)));
    }

    /// <summary>Emissor de uma chave ainda não salva (teste da chave nova antes da troca, CA 10).</summary>
    public IFiscalEmitter Create(Uri baseUrl, Secret apiKey) => factory.Create(new FiscalEmitterConnection(baseUrl, apiKey, TenantId()));

    /// <summary>O endereço salvo é o da configuração?</summary>
    public static bool SameUrl(string? saved, Uri configured) =>
        Uri.TryCreate(saved, UriKind.Absolute, out var uri) && uri == configured;

    public Secret Decrypt(string cipher) => secrets.Unprotect(SecretScope.ForTenant(TenantId()), SecretKind.FiscalApiKey, cipher);

    public string Protect(Secret apiKey) => secrets.Protect(SecretScope.ForTenant(TenantId()), SecretKind.FiscalApiKey, apiKey);

    /// <summary>O ambiente do emitente cabe no permitido? Produção inclui homologação; <c>None</c> não permite nada.</summary>
    public static bool Allows(FiscalEnvironmentAllowance allowance, FiscalEnvironment? environment) => (allowance, environment) switch
    {
        (FiscalEnvironmentAllowance.Production, _) => environment is not null,
        (FiscalEnvironmentAllowance.Homologation, FiscalEnvironment.Homologation) => true,
        _ => false,
    };

    private Guid TenantId() => tenant.Tenant?.Id ?? throw new InvalidOperationException("Integração fiscal fora do escopo de um tenant.");
}
