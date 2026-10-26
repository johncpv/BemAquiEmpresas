using BemAqui.BuildingBlocks.Security;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class BankIntegrationService
{
    public const string AlreadyConfigured = "A integração já está configurada nesta conta. Use \"Trocar credenciais\".";
    public const string NotConfigured = "Configure a integração desta conta antes.";
    public const string NothingToRotate = "Envie o client secret novo, o client id novo ou o certificado novo.";

    /// <summary>
    /// "Salvar integração bancária" (CA 3 a 5, 8 e 12): confere o certificado (CA 4), pede o token ao Inter com a credencial nova
    /// (recusada se puder pagar, CA 8) e só então cifra e grava; a conta passa a <c>provider = Inter</c>. Nada é gravado na recusa.
    /// </summary>
    public async Task<Result<BankIntegrationDetail>> SaveAsync(Guid bankAccountId, BankIntegrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var account = await InterAccountAsync(bankAccountId, tracking: true, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (!account.IsActive)
        {
            return Error.Validation("bank_account_inactive", "Reative a conta bancária antes de ligar o Inter.");
        }

        var integration = await db.BankIntegrations.FirstOrDefaultAsync(i => i.BankAccountId == bankAccountId, cancellationToken);
        if (integration is { IsActive: true })
        {
            return Error.Conflict("bank_integration_configured", AlreadyConfigured);
        }

        if (integration is not null && command.Version is { } version)
        {
            db.Entry(integration).Property(i => i.Version).OriginalValue = version;
        }

        var fields = ValidateFields(command, requireAll: true);
        if (fields is not null)
        {
            return fields;
        }

        var environment = Enum.Parse<BankEnvironment>(command.Environment!, ignoreCase: true);
        if (ProductionGate(environment, command.ConfirmProduction) is { } gate)
        {
            return gate;
        }

        var validated = InterCertificateValidator.Validate(command.Certificate, (await company.GetAsync(cancellationToken)).Document, clock.GetUtcNow());
        if (!validated.IsSuccess)
        {
            return validated.Error!;
        }

        using var certificate = validated.Value;
        var isNew = integration is null;
        integration ??= BankIntegration.For(account.Id);
        var probe = await ProbeAsync(integration.Id, environment, command.ClientId!.Trim(), new Secret(command.ClientSecret!.Trim()), certificate, AccountNumber(command, account), cancellationToken);
        if (!probe.IsSuccess)
        {
            return probe.Error!;
        }

        var cipher = Protect(integration.Id, command.ClientId!.Trim(), command.ClientSecret!.Trim(), certificate);
        var now = clock.GetUtcNow();
        integration.Environment = environment;
        integration.AccountNumber = AccountNumber(command, account);
        integration.Configure(cipher, certificate.Info, probe.Value, currentUser.UserId, now);
        account.Provider = BankProvider.Inter;
        if (isNew)
        {
            db.BankIntegrations.Add(integration);
        }

        await NotifyOwnersAsync($"Integração com o Inter da conta {account.Name} ligada por {await WhoAsync(cancellationToken)}.", account.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        inter.Forget(TenantId, integration.Id);
        return await DetailAsync(account, integration, cancellationToken);
    }

    /// <summary>
    /// "Trocar credenciais" (CA 14): a combinação nova (o que veio, mais o que ficou) passa no teste antes de trocar; se passar, troca
    /// na mesma transação, descarta o handler e o token em cache e avisa o Proprietário; se falhar, as atuais continuam.
    /// </summary>
    public async Task<Result<BankIntegrationDetail>> RotateAsync(Guid bankAccountId, BankIntegrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var account = await InterAccountAsync(bankAccountId, tracking: true, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var integration = await db.BankIntegrations.FirstOrDefaultAsync(i => i.BankAccountId == bankAccountId, cancellationToken);
        if (integration is not { IsActive: true })
        {
            return Error.Conflict("bank_integration_not_configured", NotConfigured);
        }

        if (command.Version is { } version)
        {
            db.Entry(integration).Property(i => i.Version).OriginalValue = version;
        }

        if (string.IsNullOrWhiteSpace(command.ClientId) && string.IsNullOrWhiteSpace(command.ClientSecret) && !command.Certificate.HasAny)
        {
            return Error.Validation("bank_rotation_empty", NothingToRotate, new Dictionary<string, string[]> { ["clientSecret"] = [NothingToRotate] });
        }

        var fields = ValidateFields(command, requireAll: false);
        if (fields is not null)
        {
            return fields;
        }

        var environment = string.IsNullOrWhiteSpace(command.Environment) ? integration.Environment : Enum.Parse<BankEnvironment>(command.Environment, ignoreCase: true);
        if (ProductionGate(environment, command.ConfirmProduction, integration.Environment) is { } gate)
        {
            return gate;
        }

        // O que não veio continua o atual (decifrado só agora, para testar a combinação nova).
        using var current = Open(integration);
        var clientId = string.IsNullOrWhiteSpace(command.ClientId) ? current.ClientId : command.ClientId.Trim();
        var clientSecret = string.IsNullOrWhiteSpace(command.ClientSecret) ? current.ClientSecret.Reveal() : command.ClientSecret.Trim();
        ValidatedInterCertificate certificate;
        if (command.Certificate.HasAny)
        {
            var validated = InterCertificateValidator.Validate(command.Certificate, (await company.GetAsync(cancellationToken)).Document, clock.GetUtcNow());
            if (!validated.IsSuccess)
            {
                return validated.Error!;
            }

            certificate = validated.Value;
        }
        else
        {
            certificate = CurrentCertificate(integration);
        }

        using (certificate)
        {
            var accountNumber = string.IsNullOrWhiteSpace(command.AccountNumber) ? integration.AccountNumber : Digits(command.AccountNumber);
            var probe = await ProbeAsync(integration.Id, environment, clientId, new Secret(clientSecret), certificate, accountNumber, cancellationToken);
            if (!probe.IsSuccess)
            {
                return Error.Validation(probe.Error!.Code, "As credenciais novas não passaram no teste e as atuais continuam valendo. " + probe.Error.Message, probe.Error.Fields)
                    with { Data = probe.Error.Data };
            }

            var cipher = Protect(integration.Id, clientId, clientSecret, certificate);
            integration.Environment = environment;
            integration.AccountNumber = accountNumber;
            integration.Rotate(cipher, certificate.Info, probe.Value, currentUser.UserId, clock.GetUtcNow());
        }

        await NotifyOwnersAsync($"Credenciais do Inter trocadas por {await WhoAsync(cancellationToken)}.", account.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        inter.Forget(TenantId, integration.Id);
        return await DetailAsync(account, integration, cancellationToken);
    }

    /// <summary>
    /// "Testar conexão" (CA 7, 8 e 12): token com mTLS e a leitura do saldo, sem efeito; vale também em produção sem a flag (D−1 da
    /// GOL-01b). Grava só o resultado do teste; credencial que pode pagar responde 422.
    /// </summary>
    public async Task<Result<BankConnectionTest>> TestAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        var account = await InterAccountAsync(bankAccountId, tracking: false, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var integration = await db.BankIntegrations.FirstOrDefaultAsync(i => i.BankAccountId == bankAccountId, cancellationToken);
        if (integration is not { IsActive: true })
        {
            return Error.Conflict("bank_integration_not_configured", NotConfigured);
        }

        InterResult<InterConnectionCheck> result;
        using (var credentials = Open(integration))
        {
            result = await inter.TestConnectionAsync(credentials, cancellationToken);
        }

        var now = clock.GetUtcNow();
        if (!result.Ok)
        {
            integration.RecordTest(now, BankTestOutcome.Refused, result.Failure!.Message, null);
            await db.SaveChangesAsync(cancellationToken);
            return new BankConnectionTest(false, nameof(BankTestOutcome.Refused), result.Failure.Message, result.Failure.Action, [], [], now);
        }

        var evaluation = InterScopes.Evaluate(result.Value!.GrantedScopes, options.Value);
        var granted = string.Join(' ', evaluation.Granted);
        if (evaluation.CanPay)
        {
            integration.RecordTest(now, BankTestOutcome.PaymentScopes, InterScopes.PaymentScopesMessage, granted);
            await db.SaveChangesAsync(cancellationToken);
            return CanPay();
        }

        if (evaluation.Missing.Count > 0)
        {
            var message = InterScopes.MissingMessage(evaluation.Missing);
            integration.RecordTest(now, BankTestOutcome.MissingScopes, message, granted);
            await db.SaveChangesAsync(cancellationToken);
            return new BankConnectionTest(false, nameof(BankTestOutcome.MissingScopes), message, InterScopes.MissingAction, evaluation.Granted, evaluation.Missing, now);
        }

        integration.RecordTest(now, BankTestOutcome.Ok, null, granted);
        await db.SaveChangesAsync(cancellationToken);
        return new BankConnectionTest(true, nameof(BankTestOutcome.Ok), "Conexão com o Inter funcionando", null, evaluation.Granted, [], now);
    }

    /// <summary>
    /// "Desligar integração" (CA 16): os segredos são sobrescritos (nulos), o handler sai do cache, a conta volta a <c>provider = None</c>;
    /// os boletos já emitidos ficam no banco e são conferidos pelo extrato ou pela baixa manual.
    /// </summary>
    public async Task<Result> DisableAsync(Guid bankAccountId, uint? version, CancellationToken cancellationToken = default)
    {
        var account = await InterAccountAsync(bankAccountId, tracking: true, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var integration = await db.BankIntegrations.FirstOrDefaultAsync(i => i.BankAccountId == bankAccountId, cancellationToken);
        if (integration is not { IsActive: true })
        {
            return Error.Conflict("bank_integration_not_configured", NotConfigured);
        }

        if (version is { } v)
        {
            db.Entry(integration).Property(i => i.Version).OriginalValue = v;
        }

        integration.Disable(currentUser.UserId, clock.GetUtcNow());
        account.Provider = BankProvider.None;
        await NotifyOwnersAsync($"Integração com o Inter da conta {account.Name} desligada por {await WhoAsync(cancellationToken)}.", account.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        inter.Forget(TenantId, integration.Id);
        return Result.Success();
    }

    private static Error CanPay() =>
        Error.Validation("bank_credential_can_pay", InterScopes.PaymentScopesMessage, new Dictionary<string, string[]> { ["clientId"] = [InterScopes.PaymentScopesMessage] });

    /// <summary>Token de prova com a credencial nova (sem cache): recusa do Inter ou escopo de pagamento param tudo (CA 8 e 14).</summary>
    private async Task<Result<string>> ProbeAsync(Guid integrationId, BankEnvironment environment, string clientId, Secret clientSecret, ValidatedInterCertificate certificate, string? accountNumber, CancellationToken cancellationToken)
    {
        // A prova abre a própria cópia do certificado; o validado continua com quem chamou.
        using var credentials = new InterCredentials(TenantId, integrationId, environment, clientId, clientSecret,
            () => InterCertificateValidator.Load(certificate.Info.Format, certificate.StoredCertificate, certificate.StoredPrivateKey, certificate.Password), certificate.Info.Thumbprint, accountNumber);
        var token = await inter.ProbeAsync(credentials, cancellationToken);
        if (!token.Ok)
        {
            var failure = token.Failure!;
            return Error.Validation("bank_connection_refused", failure.Message + " " + failure.Action, new Dictionary<string, string[]> { ["clientSecret"] = [failure.Message] })
                with { Data = new Dictionary<string, object?> { ["action"] = failure.Action } };
        }

        var evaluation = InterScopes.Evaluate(token.Value!.GrantedScopes, options.Value);
        return evaluation.CanPay ? CanPay() : string.Join(' ', evaluation.Granted);
    }

    private BankIntegrationSecrets Protect(Guid integrationId, string clientId, string clientSecret, ValidatedInterCertificate certificate)
    {
        var scope = SecretScope.ForTenant(TenantId);
        return new BankIntegrationSecrets(
            secrets.Protect(scope, SecretKind.InterCredentials, integrationId, new Secret(clientId)),
            clientId.Length <= 4 ? clientId : clientId[^4..],
            secrets.Protect(scope, SecretKind.InterCredentials, integrationId, new Secret(clientSecret)),
            secrets.Protect(scope, SecretKind.InterCertificate, integrationId, new Secret(certificate.StoredCertificate)),
            certificate.StoredPrivateKey is { } key ? secrets.Protect(scope, SecretKind.InterCertificate, integrationId, new Secret(key)) : null,
            certificate.Password is { Length: > 0 } password ? secrets.Protect(scope, SecretKind.InterCertificate, integrationId, new Secret(password)) : null);
    }

    /// <summary>O certificado atual, para trocar só o client secret (a combinação nova é testada com ele).</summary>
    private ValidatedInterCertificate CurrentCertificate(BankIntegration integration)
    {
        var scope = SecretScope.ForTenant(TenantId);
        var stored = secrets.Unprotect(scope, SecretKind.InterCertificate, integration.Id, integration.CertificateCipher!).Reveal();
        var key = integration.PrivateKeyCipher is { } k ? secrets.Unprotect(scope, SecretKind.InterCertificate, integration.Id, k).Reveal() : null;
        var password = integration.CertificatePasswordCipher is { } p ? secrets.Unprotect(scope, SecretKind.InterCertificate, integration.Id, p).Reveal() : null;
        var format = integration.CertificateFormat ?? BankCertificateFormat.Pem;
        var certificate = InterCertificateValidator.Load(format, stored, key, password);
        return new ValidatedInterCertificate(
            new BankCertificateInfo(format, InterCertificateValidator.NotAfter(certificate), InterCertificateValidator.Thumbprint(certificate), certificate.Subject), certificate, stored, key, password);
    }

    /// <summary>Produção (CA 12): sem a flag, salva e testa; com a flag, ligar ou trocar para Produção pede "Ligar a cobrança real".</summary>
    private Error? ProductionGate(BankEnvironment environment, bool confirmed, BankEnvironment? current = null) =>
        environment == BankEnvironment.Production && ProductionAllowed && !confirmed && current != BankEnvironment.Production
            ? Error.Conflict("bank_production_confirmation_required", ProductionConfirmMessage)
            : null;

    private static Error? ValidateFields(BankIntegrationCommand command, bool requireAll)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requireAll || !string.IsNullOrWhiteSpace(command.Environment))
        {
            if (!Enum.TryParse<BankEnvironment>(command.Environment, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                errors["environment"] = ["Escolha o ambiente: Sandbox ou Produção."];
            }
        }

        if (requireAll && string.IsNullOrWhiteSpace(command.ClientId))
        {
            errors["clientId"] = ["Informe o client id gerado no Inter."];
        }

        if (command.ClientId is { Length: > 100 })
        {
            errors["clientId"] = ["O client id tem no máximo 100 caracteres."];
        }

        if (requireAll && string.IsNullOrWhiteSpace(command.ClientSecret))
        {
            errors["clientSecret"] = ["Informe o client secret gerado no Inter."];
        }

        if (command.ClientSecret is { Length: > 200 })
        {
            errors["clientSecret"] = ["O client secret tem no máximo 200 caracteres."];
        }

        if (requireAll && !command.Certificate.HasAny)
        {
            errors["certificate"] = [InterCertificateValidator.MissingFiles];
        }

        if (command.AccountNumber is { Length: > 0 } number && (Digits(number).Length is 0 or > 20))
        {
            errors["accountNumber"] = ["Informe a conta corrente do Inter só com números (até 20)."];
        }

        return errors.Count == 0 ? null : Error.Validation(Error.Codes.Validation, "Confira os campos da integração.", errors);
    }

    private static string? AccountNumber(BankIntegrationCommand command, BankAccount account) =>
        string.IsNullOrWhiteSpace(command.AccountNumber) ? SuggestedAccountNumber(account) : Digits(command.AccountNumber);

    private static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());

    private async Task<string> WhoAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "um usuário" : "o sistema";

    private async Task NotifyOwnersAsync(string title, Guid bankAccountId, CancellationToken cancellationToken)
    {
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, title, Link(bankAccountId));
        }
    }
}
