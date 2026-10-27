using BemAqui.SharedKernel;

namespace BemAqui.Platform.Domain;

public enum TenantLifecycle
{
    Provisioning,
    Active,
    Suspended,
}

/// <summary>Cliente do SaaS (<c>catalog.tenants</c>). A conexão do banco dele fica cifrada (FND-11).</summary>
public sealed class Tenant : Entity, IAuditable
{
    public required string Key { get; init; }

    public required string LegalName { get; set; }

    public string? TradeName { get; set; }

    public string? Document { get; set; }

    public TenantLifecycle Status { get; private set; } = TenantLifecycle.Provisioning;

    public Guid? PlanId { get; set; }

    public required string DbName { get; init; }

    [ProtectedSecret]
    public required string ConnectionSecret { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Última migration aplicada no banco do tenant (o migrator grava; a API compara com a do build).</summary>
    public string? SchemaVersion { get; set; }

    public DateTimeOffset? MigratedAt { get; set; }

    /// <summary>
    /// Chave opaca da URL dos webhooks (FND-16 CA 2, ADR 0031 §1): 128 bits aleatórios em base64url, criada no primeiro provedor
    /// configurado, sem relação com nome, slug ou id. Na auditoria do catálogo aparece só como "alterado".
    /// </summary>
    [ProtectedSecret]
    public string? WebhookKey { get; private set; }

    /// <summary>Grava a chave só se ainda não existe (a URL cadastrada nos provedores não muda).</summary>
    public bool AssignWebhookKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (WebhookKey is not null)
        {
            return false;
        }

        WebhookKey = key;
        return true;
    }

    /// <summary>
    /// Cobrança real pelo banco liberada (BNK-01a CA 12, ADR 0034): padrão falso; só o super admin liga, na virada (GOL-01b). Antes
    /// disso a integração bancária de produção pode ser salva e testada, mas emitir, registrar webhook e importar extrato respondem 409.
    /// </summary>
    public bool BankProductionAllowed { get; set; }

    /// <summary>
    /// Ambiente fiscal permitido (FIS-01 CA 9, ADR 0027 §6): <c>None</c> por padrão; o <c>demo</c> em homologação; produção só ligada
    /// no checklist da GOL-01b (ADR 0034). A integração com o EmissorFiscal num ambiente acima deste é recusada.
    /// </summary>
    public BemAqui.Platform.Contracts.FiscalEnvironmentAllowance FiscalEnvironmentAllowed { get; set; } = BemAqui.Platform.Contracts.FiscalEnvironmentAllowance.None;

    public List<TenantDomain> Domains { get; } = [];

    /// <summary>Tenant novo, em Provisioning, com o id já definido (o id entra no propósito do segredo).</summary>
    public static Tenant Create(Guid id, string key, string legalName, string dbName, string connectionSecret, DateTimeOffset createdAt) =>
        new() { Id = id, Key = key, LegalName = legalName, DbName = dbName, ConnectionSecret = connectionSecret, CreatedAt = createdAt };

    public void Activate() => Status = TenantLifecycle.Active;

    public void Suspend() => Status = TenantLifecycle.Suspended;
}

/// <summary>Host que resolve o tenant (<c>catalog.tenant_domains</c>), ex.: <c>app.konebras.com.br</c> (ADR 0004).</summary>
public sealed class TenantDomain : Entity
{
    public Guid TenantId { get; init; }

    /// <summary>Host normalizado: minúsculas, sem porta.</summary>
    public required string Host { get; init; }

    public bool IsPrimary { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public static string Normalize(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var value = host.Trim().TrimEnd('.').ToLowerInvariant();
        var colon = value.LastIndexOf(':');
        return colon > 0 && !value.EndsWith(']') ? value[..colon] : value;
    }
}

/// <summary>Plano/versão contratada (<c>catalog.plans</c>); os módulos liberados chegam na FND-06.</summary>
public sealed class Plan : Entity
{
    public required string Name { get; set; }

    /// <summary>JSONB: limites (usuários, armazenamento).</summary>
    public string Limits { get; set; } = "{}";

    public decimal Price { get; set; }
}

public enum ProvisioningStatus
{
    Started,
    Succeeded,
    Failed,
}

/// <summary>Uma linha por etapa e tentativa do provisionamento (<c>catalog.provisioning_logs</c>). Nunca contém segredo.</summary>
public sealed class ProvisioningLog : Entity
{
    public Guid TenantId { get; init; }

    public required string Step { get; init; }

    public ProvisioningStatus Status { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>Deduplicação de alertas (<c>catalog.platform_alerts</c>): um envio por chave (tipo + tenant) por janela.</summary>
public sealed class PlatformAlert : Entity
{
    public required string Key { get; init; }

    public DateTimeOffset LastSentAt { get; set; }

    /// <summary>Ocorrências desde o último envio (inclusive as suprimidas pela janela).</summary>
    public int Occurrences { get; set; }

    public string? LastSummary { get; set; }
}
