using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Application;

/// <summary>
/// Configurações → Integrações → Banco Inter (BNK-01a): uma integração por conta com banco 077 (ADR 0032 §1). Salvar, trocar e
/// desligar pedem <c>settings.edit</c> e o 2FA de novo (no endpoint); a credencial é conferida no Inter antes de gravar (escopos de
/// pagamento recusam, CA 8) e a troca testa a nova antes de substituir a atual (CA 14). Os segredos só saem cifrados e nunca voltam.
/// </summary>
public sealed partial class BankIntegrationService(
    IFinanceDb db,
    ISecretProtector secrets,
    IInterApiClient inter,
    TenantContextHolder tenant,
    ICompanyIdentity company,
    IUserDirectory users,
    ICurrentUser currentUser,
    INotificationService notifications,
    IOptions<InterOptions> options,
    TimeProvider clock)
{
    public const string InterBankCode = "077";
    public const string TestRateLimitPolicy = "bank-integration-test";
    public const string NotFoundMessage = "Conta do Inter não encontrada.";
    public const string ProductionNotAllowedMessage = "A cobrança real será ligada na virada do sistema.";
    public const string ProductionConfirmMessage = "Boletos emitidos em produção são cobranças reais ao cliente.";
    public const string NotificationType = "finance.bank_integration";

    public static string Link(Guid bankAccountId) => "/app/configuracoes/integracoes/inter/" + bankAccountId;

    /// <summary>Contas do Inter (banco 077) com a situação da integração (lista da tela).</summary>
    public async Task<BankIntegrationList> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var accounts = await db.BankAccounts.AsNoTracking()
            .Where(a => a.Kind == BankAccountKind.Bank && a.IsActive && a.BankCode == InterBankCode)
            .OrderBy(a => a.Name).ToListAsync(cancellationToken);
        var ids = accounts.Select(a => a.Id).ToList();
        var integrations = await db.BankIntegrations.AsNoTracking().Where(i => ids.Contains(i.BankAccountId)).ToDictionaryAsync(i => i.BankAccountId, cancellationToken);
        var rows = accounts.Select(a =>
        {
            var integration = integrations.GetValueOrDefault(a.Id);
            var active = integration is { IsActive: true };
            return new BankIntegrationRow(a.Id, a.Name, a.BankCode, a.Agency, a.Account, true, Situation(integration, now),
                active ? integration!.Environment.ToString() : null, active ? integration!.CertificateNotAfter : null);
        }).ToList();
        return new BankIntegrationList(rows, ProductionAllowed);
    }

    public async Task<Result<BankIntegrationDetail>> GetAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        var account = await InterAccountAsync(bankAccountId, tracking: false, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.BankAccountId == bankAccountId, cancellationToken);
        return await DetailAsync(account, integration, cancellationToken);
    }

    /// <summary>
    /// Ação com efeito no banco (emitir boleto, registrar webhook, importar extrato; BNK-01b, BNK-02, BNK-03a): integração ativa, produção
    /// só com a flag do catálogo (CA 12, 409 <c>bank_production_not_allowed</c>) e certificado no prazo (CA 15, 409 <c>bank_integration_expired</c>).
    /// "Testar conexão" não passa por aqui (leitura sem efeito, usada no D−1 da GOL-01b).
    /// </summary>
    public async Task<Result<BankIntegration>> EffectsAllowedAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.BankAccountId == bankAccountId, cancellationToken);
        if (integration is null)
        {
            return NotFound();
        }

        return BankEffectGuard.Check(integration, ProductionAllowed, clock.GetUtcNow()) is { } refused ? refused : integration;
    }

    private bool ProductionAllowed => tenant.Resolved?.BankProductionAllowed == true;

    private Guid TenantId => tenant.Tenant?.Id ?? throw new InvalidOperationException("Integração bancária fora do escopo de um tenant.");

    private static string Situation(BankIntegration? integration, DateTimeOffset now)
    {
        if (integration is not { IsActive: true })
        {
            return BankIntegrationSituations.NotConnected;
        }

        if (integration.IsExpired(now))
        {
            return BankIntegrationSituations.Expired;
        }

        if (integration.CertificateNotAfter is { } notAfter && notAfter - now <= TimeSpan.FromDays(BankIntegrationSituations.ExpiringSoonDays))
        {
            return BankIntegrationSituations.ExpiringSoon;
        }

        return integration.Environment == BankEnvironment.Production ? BankIntegrationSituations.Production : BankIntegrationSituations.Sandbox;
    }

    private Task<BankAccount?> InterAccountAsync(Guid bankAccountId, bool tracking, CancellationToken cancellationToken)
    {
        var accounts = tracking ? db.BankAccounts : db.BankAccounts.AsNoTracking();
        return accounts.FirstOrDefaultAsync(a => a.Id == bankAccountId && a.Kind == BankAccountKind.Bank && a.BankCode == InterBankCode, cancellationToken);
    }

    private async Task<BankIntegrationDetail> DetailAsync(BankAccount account, BankIntegration? integration, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = integration is { IsActive: true };
        var granted = active ? InterScopes.Parse(integration!.GrantedScopes) : [];
        var evaluation = InterScopes.Evaluate(granted, options.Value);
        BankCertificateView? certificate = active && integration!.CertificateNotAfter is { } notAfter
            ? new BankCertificateView(notAfter, integration.CertificateSubject ?? string.Empty, integration.CertificateThumbprint ?? string.Empty, integration.CertificateFormat?.ToString() ?? string.Empty,
                (int)Math.Floor((notAfter - now).TotalDays))
            : null;
        var test = active && integration!.LastTestAt is { } testedAt ? new BankTestView(testedAt, integration.LastTestOutcome?.ToString() ?? string.Empty, integration.LastTestMessage) : null;
        return new BankIntegrationDetail(
            account.Id,
            account.Name,
            account.Agency,
            account.Account,
            active,
            Situation(integration, now),
            active ? integration!.Environment.ToString() : nameof(BankEnvironment.Sandbox),
            active && integration!.ClientIdHint is { } hint ? "•••• " + hint : null,
            certificate,
            active ? integration!.AccountNumber : SuggestedAccountNumber(account),
            granted,
            active ? evaluation.Missing : [],
            options.Value.RequiredScopes,
            active ? integration!.ConfiguredAt : null,
            active && integration!.ConfiguredBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null,
            active ? integration!.RotatedAt : null,
            active && integration!.RotatedBy is { } rotatedBy ? await users.DisplayNameAsync(rotatedBy, cancellationToken) : null,
            test,
            ProductionAllowed,
            integration?.Id,
            integration?.Version);
    }

    /// <summary>Conta corrente sugerida na primeira configuração: a conta cadastrada, só os dígitos (CA da tela, propositivo).</summary>
    private static string? SuggestedAccountNumber(BankAccount account) =>
        account.Account is { Length: > 0 } number ? new string(number.Where(char.IsAsciiDigit).ToArray()) : null;

    private static Error NotFound() => Error.NotFound(Error.Codes.NotFound, NotFoundMessage);

    /// <summary>Abre a credencial gravada (decifrada só agora, para montar o handler, CA 5 e 9). Quem chama descarta.</summary>
    internal InterCredentials Open(BankIntegration integration)
    {
        var scope = SecretScope.ForTenant(TenantId);
        var clientId = secrets.Unprotect(scope, SecretKind.InterCredentials, integration.Id, integration.ClientIdCipher!).Reveal();
        var clientSecret = secrets.Unprotect(scope, SecretKind.InterCredentials, integration.Id, integration.ClientSecretCipher!);
        var format = integration.CertificateFormat ?? BankCertificateFormat.Pem;
        var stored = secrets.Unprotect(scope, SecretKind.InterCertificate, integration.Id, integration.CertificateCipher!);
        var key = integration.PrivateKeyCipher is { } k ? secrets.Unprotect(scope, SecretKind.InterCertificate, integration.Id, k) : null;
        var password = integration.CertificatePasswordCipher is { } p ? secrets.Unprotect(scope, SecretKind.InterCertificate, integration.Id, p) : null;
        return new InterCredentials(TenantId, integration.Id, integration.Environment, clientId, clientSecret,
            () => InterCertificateValidator.Load(format, stored.Reveal(), key?.Reveal(), password?.Reveal()), integration.CertificateThumbprint ?? string.Empty, integration.AccountNumber);
    }
}

/// <summary>Barreira das ações com efeito no banco (CA 12 e 15), usada pela BNK-01b, BNK-02 e BNK-03a.</summary>
public static class BankEffectGuard
{
    public const string ProductionNotAllowed = "bank_production_not_allowed";
    public const string Expired = "bank_integration_expired";
    public const string Inactive = "bank_integration_inactive";

    public static Error? Check(BankIntegration integration, bool productionAllowed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(integration);
        if (!integration.IsActive)
        {
            return Error.Conflict(Inactive, "A integração com o Inter está desligada nesta conta. Configure em Configurações → Integrações → Banco Inter.");
        }

        if (integration.Environment == BankEnvironment.Production && !productionAllowed)
        {
            return Error.Conflict(ProductionNotAllowed, BankIntegrationService.ProductionNotAllowedMessage);
        }

        return integration.IsExpired(now)
            ? Error.Conflict(Expired, $"O certificado do Inter venceu em {InterCertificateValidator.Date(integration.CertificateNotAfter!.Value)}. Troque as credenciais para voltar a emitir.")
            : null;
    }
}
