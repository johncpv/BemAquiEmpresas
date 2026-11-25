using System.Security.Cryptography;
using System.Text;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Sales.Application;

/// <summary>
/// Documento do consumidor da venda do caixa (OFF-02 CA 17, PP-26). Cifra com o key ring do tenant (propósito com o id da linha), calcula o índice
/// cego com a chave do tenant (criada no primeiro uso, guardada cifrada) e grava em <c>sales.sale_customer_documents</c>. O documento nunca vai
/// para log, exceção nem evento.
/// </summary>
public sealed class SaleCustomerDocumentService(ISalesDb db, ISecretProtector secrets, ITenantContext tenant, TimeProvider clock) : ISaleCustomerDocuments
{
    public async Task<Result> AttachAsync(SaleCustomerDocumentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var value = BrazilianDocuments.Normalize(input.Value);
        var kind = input.Kind?.Trim().ToLowerInvariant();
        var valid = kind switch
        {
            "cpf" => BrazilianDocuments.KindOf(value) == DocumentKind.Cpf && BrazilianDocuments.IsValidCpf(value),
            "cnpj" => BrazilianDocuments.KindOf(value) == DocumentKind.Cnpj && BrazilianDocuments.IsValidCnpj(value),
            _ => false,
        };
        if (input.SaleId == Guid.Empty || !valid)
        {
            return Error.Validation("customer_document_invalid", "CPF ou CNPJ do consumidor inválido.");
        }

        if (await db.SaleCustomerDocuments.AnyAsync(d => d.SaleId == input.SaleId, cancellationToken))
        {
            return Result.Success();
        }

        var scope = Scope();
        var id = Guid.CreateVersion7();
        db.SaleCustomerDocuments.Add(new SaleCustomerDocument
        {
            Id = id,
            SaleId = input.SaleId,
            Kind = kind!,
            DocumentCipher = secrets.Protect(scope, SecretKind.CustomerDocument, id, new Secret(value)),
            BlindIndex = Index(await KeyAsync(scope, cancellationToken), value),
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<Guid>> FindSalesAsync(string document, CancellationToken cancellationToken = default)
    {
        var value = BrazilianDocuments.Normalize(document);
        if (!BrazilianDocuments.IsValid(value))
        {
            return [];
        }

        var index = Index(await KeyAsync(Scope(), cancellationToken), value);
        return await db.SaleCustomerDocuments.AsNoTracking().Where(d => d.BlindIndex == index).Select(d => d.SaleId).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// PDV-07 CA 6: o documento das vendas, mascarado, para a lista e a exportação (CPF <c>***.456.789-**</c>, CNPJ <c>**.***.678/0001-**</c>).
    /// Decifra só para mascarar; o documento inteiro nunca sai daqui.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, string>> MaskedAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saleIds);
        if (saleIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = saleIds.Distinct().ToList();
        var rows = await db.SaleCustomerDocuments.AsNoTracking().Where(d => ids.Contains(d.SaleId)).Select(d => new { d.Id, d.SaleId, d.DocumentCipher }).ToListAsync(cancellationToken);
        var scope = Scope();
        return rows.ToDictionary(r => r.SaleId, r => Mask(secrets.Unprotect(scope, SecretKind.CustomerDocument, r.Id, r.DocumentCipher).Reveal()));
    }

    /// <summary>PDV-07 CA 6: o documento inteiro da venda (a leitura é auditada por quem chama); null se a venda não tem documento.</summary>
    public async Task<string?> RevealAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var row = await db.SaleCustomerDocuments.AsNoTracking().Where(d => d.SaleId == saleId).Select(d => new { d.Id, d.DocumentCipher }).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Format(secrets.Unprotect(Scope(), SecretKind.CustomerDocument, row.Id, row.DocumentCipher).Reveal());
    }

    /// <summary>Máscara da lista: CPF com os 6 dígitos do meio; CNPJ com a parte do meio e a filial.</summary>
    public static string Mask(string? document)
    {
        var doc = BrazilianDocuments.Normalize(document);
        return doc.Length == 14 ? $"**.***.{doc[5..8]}/{doc[8..12]}-**" : BrazilianDocuments.MaskCpf(doc);
    }

    private static string Format(string document)
    {
        var doc = BrazilianDocuments.Normalize(document);
        return doc.Length switch
        {
            11 => $"{doc[..3]}.{doc[3..6]}.{doc[6..9]}-{doc[9..]}",
            14 => $"{doc[..2]}.{doc[2..5]}.{doc[5..8]}/{doc[8..12]}-{doc[12..]}",
            _ => doc,
        };
    }

    private SecretScope Scope() => SecretScope.ForTenant(tenant.Tenant?.Id ?? throw new InvalidOperationException("Documento do consumidor fora de um tenant."));

    /// <summary>A chave do índice cego do tenant: criada no primeiro uso (duas criações ao mesmo tempo: fica a primeira gravada).</summary>
    private async Task<byte[]> KeyAsync(SecretScope scope, CancellationToken cancellationToken)
    {
        var row = await db.BlindIndexKeys.AsNoTracking().OrderBy(k => k.CreatedAt).ThenBy(k => k.Id).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            var fresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var cipher = secrets.Protect(scope, SecretKind.BlindIndexKey, new Secret(fresh));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO sales.blind_index_keys (id, key_cipher, created_at) SELECT {Guid.CreateVersion7()}, {cipher}, {clock.GetUtcNow()} WHERE NOT EXISTS (SELECT 1 FROM sales.blind_index_keys)",
                cancellationToken);
            row = await db.BlindIndexKeys.AsNoTracking().OrderBy(k => k.CreatedAt).ThenBy(k => k.Id).FirstAsync(cancellationToken);
        }

        return Convert.FromBase64String(secrets.Unprotect(scope, SecretKind.BlindIndexKey, row.KeyCipher).Reveal());
    }

    private static string Index(byte[] key, string value) => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)));
}

public sealed record SaleCustomerDocumentPurgeArgs;

/// <summary>
/// Retenção do documento do consumidor (PP-26, ADR 0018 e 0037): apagado 5 anos depois da venda (o prazo do documento fiscal). Apagar não mexe
/// no registro de eventos do caixa, que nunca teve o documento.
/// </summary>
[PersonalDataPurge(typeof(SaleCustomerDocument), 1825, "CPF/CNPJ do consumidor apagado 5 anos depois da venda (PP-26)")]
public sealed partial class SaleCustomerDocumentPurgeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<SaleCustomerDocumentPurgeJob> logger)
    : TenantJob<SaleCustomerDocumentPurgeArgs>(directory, scopes, logger)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1825);

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, SaleCustomerDocumentPurgeArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        var cutoff = services.GetRequiredService<TimeProvider>().GetUtcNow() - Retention;
        var deleted = await services.GetRequiredService<ISalesDb>().SaleCustomerDocuments.Where(d => d.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        LogPurged(logger, deleted, tenant.Key);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expurgo do documento do consumidor: {Deleted} registros apagados no tenant {Tenant}.")]
    private static partial void LogPurged(ILogger logger, int deleted, string tenant);
}

/// <summary>Recorrente global diário (03:40 de São Paulo) que enfileira o expurgo por tenant ativo.</summary>
public sealed class SaleCustomerDocumentPurgeRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<SaleCustomerDocumentPurgeJob, SaleCustomerDocumentPurgeArgs>(directory, jobs)
{
    public const string Id = "sales-customer-documents-purge";
    public const string Cron = "40 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override SaleCustomerDocumentPurgeArgs ArgsFor(TenantInfo tenant) => new();
}
