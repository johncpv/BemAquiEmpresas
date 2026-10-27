using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application;

/// <summary>Resultado da conferência da chave: o código gravado, a mensagem da tela e, se aprovada, o que fica guardado.</summary>
public sealed record FiscalAccountVerdict(string Result, string Message, FiscalAccountSnapshot? Snapshot)
{
    public bool Ok => Snapshot is not null;
}

/// <summary>
/// Conferência do <c>GET /me</c> no "Testar conexão" e antes da troca de chave (FIS-01 CA 8 a 10, ADR 0027 §2): chave sem <c>admin</c>,
/// com <c>nfe:emitir</c>, <c>nfe:eventos</c> e <c>consultar</c>, emitente com o CNPJ da empresa e ambiente dentro do permitido.
/// </summary>
public static class FiscalAccountCheck
{
    public const string ConnectionTested = "Conexão testada";
    public const string AdminMessage = "Use uma chave sem o escopo admin.";
    public const string IssuerMessage = "O emitente do Emissor Fiscal não é o CNPJ desta empresa.";
    public const string NoDocumentMessage = "Cadastre o CNPJ da empresa antes de conectar o Emissor Fiscal.";

    public static string MissingMessage(IEnumerable<string> missing) => "Faltam os escopos " + string.Join(", ", missing) + " nesta chave.";

    public static FiscalAccountVerdict Evaluate(EmitterAccount account, string? tenantDocument, FiscalEnvironmentAllowance allowance)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (FiscalScopes.HasAdmin(account.Scopes))
        {
            return new FiscalAccountVerdict(FiscalTestResults.AdminScope, AdminMessage, null);
        }

        var missing = FiscalScopes.Missing(account.Scopes);
        if (missing.Count > 0)
        {
            return new FiscalAccountVerdict(FiscalTestResults.MissingScopes, MissingMessage(missing), null);
        }

        if (string.IsNullOrEmpty(tenantDocument))
        {
            return new FiscalAccountVerdict(FiscalTestResults.IssuerMismatch, NoDocumentMessage, null);
        }

        var document = BrazilianDocuments.Normalize(tenantDocument);
        var issuer = account.Issuers.FirstOrDefault(i => i.Active && string.Equals(BrazilianDocuments.Normalize(i.Document), document, StringComparison.Ordinal));
        if (issuer is null)
        {
            return new FiscalAccountVerdict(FiscalTestResults.IssuerMismatch, IssuerMessage, null);
        }

        if (!FiscalEmitterAccess.Allows(allowance, issuer.Environment))
        {
            return new FiscalAccountVerdict(FiscalTestResults.EnvironmentNotAllowed, FiscalEmitterAccess.EnvironmentMessage, null);
        }

        return new FiscalAccountVerdict(FiscalTestResults.Ok, ConnectionTested,
            new FiscalAccountSnapshot(account.ClientId, account.TenantId, document, issuer.Name, issuer.Environment, account.Scopes, issuer.Certificate.ValidUntil));
    }
}
