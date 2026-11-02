using BemAqui.BuildingBlocks.Security;

namespace BemAqui.Host.Cli.Reencryption;

/// <summary>Banco onde a coluna cifrada mora.</summary>
internal enum SecretDatabase
{
    Catalog,
    Tenant,
}

/// <summary>Como o propósito do segredo é montado (ADR 0007): escopo e, quando houver, o id da linha depois do tipo.</summary>
internal enum SecretPurpose
{
    /// <summary><c>bemaqui → platform → {tipo} → v1</c>.</summary>
    Platform,

    /// <summary><c>bemaqui → tenant:{id do tenant} → {tipo} → v1</c>.</summary>
    Tenant,

    /// <summary>Linha de <c>catalog.tenants</c>: o id da linha é o próprio tenant.</summary>
    TenantRow,

    /// <summary><c>bemaqui → tenant:{id} → {tipo} → {id da linha} → v1</c> (Inter por integração de conta, ADR 0007 adendo).</summary>
    TenantItem,
}

/// <summary>Uma coluna cifrada pelo Data Protection, com o tipo do segredo e o jeito de montar o propósito.</summary>
internal sealed record SecretColumn(SecretDatabase Database, string Schema, string Table, string Column, SecretKind Kind, SecretPurpose Purpose)
{
    public string Name => Schema + "." + Table + "." + Column;
}

/// <summary>
/// Todas as colunas cifradas com o key ring (GOL-01a CA 4, ADR 0034 §2), achadas no código: <c>SecretProtector.Protect</c> só é chamado para
/// estas. A convenção é o sufixo <c>_cipher</c> (mais a <c>catalog.tenants.connection_secret</c>, anterior a ela); o comando confere o schema
/// real e recusa rodar se aparecer coluna cifrada fora desta lista. <c>SmtpPassword</c> não tem coluna (o SMTP vem de arquivo de secret) e o
/// certificado A1 não é guardado no BemAqui (ADR 0007 adendo, fica no cofre do EmissorFiscal).
/// </summary>
internal static class SecretColumns
{
    public static readonly SecretColumn TenantConnection =
        new(SecretDatabase.Catalog, "catalog", "tenants", "connection_secret", SecretKind.TenantConnection, SecretPurpose.TenantRow);

    public static readonly IReadOnlyList<SecretColumn> All =
    [
        TenantConnection,
        new(SecretDatabase.Catalog, "catalog", "platform_admins", "totp_secret_cipher", SecretKind.TotpSecret, SecretPurpose.Platform),
        new(SecretDatabase.Tenant, "identity", "users", "totp_secret_cipher", SecretKind.TotpSecret, SecretPurpose.Tenant),
        new(SecretDatabase.Tenant, "identity", "ai_settings", "api_key_cipher", SecretKind.AiApiKey, SecretPurpose.Tenant),
        new(SecretDatabase.Tenant, "fiscal", "integration_settings", "api_key_cipher", SecretKind.FiscalApiKey, SecretPurpose.Tenant),
        new(SecretDatabase.Tenant, "shared", "webhook_tokens", "secret_cipher", SecretKind.WebhookSecret, SecretPurpose.Tenant),
        new(SecretDatabase.Tenant, "shared", "webhook_tokens", "previous_secret_cipher", SecretKind.WebhookSecret, SecretPurpose.Tenant),
        new(SecretDatabase.Tenant, "finance", "bank_integrations", "client_id_cipher", SecretKind.InterCredentials, SecretPurpose.TenantItem),
        new(SecretDatabase.Tenant, "finance", "bank_integrations", "client_secret_cipher", SecretKind.InterCredentials, SecretPurpose.TenantItem),
        new(SecretDatabase.Tenant, "finance", "bank_integrations", "certificate_cipher", SecretKind.InterCertificate, SecretPurpose.TenantItem),
        new(SecretDatabase.Tenant, "finance", "bank_integrations", "private_key_cipher", SecretKind.InterCertificate, SecretPurpose.TenantItem),
        new(SecretDatabase.Tenant, "finance", "bank_integrations", "certificate_password_cipher", SecretKind.InterCertificate, SecretPurpose.TenantItem),
    ];

    public static IEnumerable<SecretColumn> In(SecretDatabase database) => All.Where(c => c.Database == database);

    /// <summary>Nome fixo do tipo no propósito (o mesmo do teste golden), para o relatório por tipo.</summary>
    public static string KindName(SecretKind kind) => SecretProtector.Purposes(SecretScope.Platform, kind)[2];

    /// <summary>Colunas cifradas do schema real que não estão na lista: o comando recusa rodar com qualquer uma.</summary>
    public static IReadOnlyList<string> Unknown(SecretDatabase database, IEnumerable<string> schemaColumns)
    {
        var known = In(database).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        return schemaColumns.Where(c => !known.Contains(c)).Order(StringComparer.Ordinal).ToList();
    }
}
