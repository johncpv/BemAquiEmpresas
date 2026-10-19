using System.Text.Json;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

/// <summary>
/// Consulta de CNPJ do cadastro (CAD-01b CA 1 a 9): valida antes de qualquer chamada (CA 7), não consulta CNPJ que já é de
/// outro terceiro do tenant (CAD-01a CA 4) e usa o cache de 30 dias do tenant (CA 4).
/// </summary>
public sealed class CnpjLookupService(IPartiesDb db, ICnpjLookup provider, TimeProvider clock)
{
    public const int CacheDays = 30;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="partyId">No "Atualizar pela Receita" (CA 8), o próprio terceiro não conta como duplicado.</param>
    public async Task<Result<CnpjLookupResult>> LookupAsync(string cnpj, Guid? partyId, CancellationToken cancellationToken = default)
    {
        var document = BrazilianDocuments.Normalize(cnpj);
        if (BrazilianDocuments.KindOf(document) != DocumentKind.Cnpj || !BrazilianDocuments.IsValidCnpj(document))
        {
            return Error.Validation("validation", "CNPJ inválido.", new Dictionary<string, string[]> { ["cnpj"] = ["CNPJ inválido."] });
        }

        var other = await db.Parties.IgnoreQueryFilters().AsNoTracking().Where(p => p.Document == document && p.Id != partyId)
            .Select(p => new { p.Id, p.LegalName }).FirstOrDefaultAsync(cancellationToken);
        if (other is not null)
        {
            return Error.Conflict(PartyService.ExistsCode, $"Este CNPJ já está cadastrado como {other.LegalName}.") with { Data = new Dictionary<string, object?> { ["existingId"] = other.Id } };
        }

        var now = clock.GetUtcNow();
        var cached = await db.CnpjLookups.FirstOrDefaultAsync(c => c.Document == document, cancellationToken);
        if (cached is not null && cached.LookedUpAt > now.AddDays(-CacheDays) && JsonSerializer.Deserialize<CnpjData>(cached.Payload, Json) is { } data)
        {
            return new CnpjLookupResult(data, cached.LookedUpAt, true);
        }

        var fresh = await provider.LookupAsync(document, cancellationToken);
        if (fresh.IsFailure)
        {
            return fresh.Error!;
        }

        if (cached is null)
        {
            db.CnpjLookups.Add(new CnpjLookupCache { Document = document, Payload = JsonSerializer.Serialize(fresh.Value, Json), LookedUpAt = now });
        }
        else
        {
            cached.Payload = JsonSerializer.Serialize(fresh.Value, Json);
            cached.LookedUpAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new CnpjLookupResult(fresh.Value, now, false);
    }

    /// <summary>Expurgo diário (CA 14): apaga o que passou de 30 dias.</summary>
    public Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var limit = clock.GetUtcNow().AddDays(-CacheDays);
        return db.CnpjLookups.Where(c => c.LookedUpAt <= limit).ExecuteDeleteAsync(cancellationToken);
    }
}
