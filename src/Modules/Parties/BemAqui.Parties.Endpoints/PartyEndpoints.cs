using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Parties.Endpoints;

/// <summary>
/// Terceiros (CAD-01a). Módulo <c>parties</c>, com as três camadas de acesso (CA 28); toda resposta sai com
/// <c>Cache-Control: no-store</c> porque traz dado pessoal (CA 27).
/// </summary>
internal static class PartyEndpoints
{
    public static IEndpointRouteBuilder MapPartyEndpoints(this IEndpointRouteBuilder api)
    {
        var parties = api.MapGroup("/parties").WithTags("Terceiros").RequireModule("parties")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        parties.MapGet(string.Empty, async (string? q, PartyRoleKind? role, bool? includeInactive, int? page, int? pageSize, string? sort, PartyService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new PartyListQuery(q, role, includeInactive == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("parties.view").WithName("ListParties").Produces<PagedList<PartyListItem>>();
        parties.MapGet("/search", async (string? q, PartyService service, CancellationToken ct) => Results.Ok(await service.SearchHitsAsync(q, ct)))
            .RequirePermission("parties.view").WithName("SearchParties").Produces<IReadOnlyList<PartySearchHit>>();
        // Condições ativas para o seletor da aba Dados (CAD-05b CA 16): o Finance responde pelo contrato, sem exigir finance.view.
        parties.MapGet("/payment-terms", async (PartyService service, CancellationToken ct) => Results.Ok(await service.PaymentTermOptionsAsync(ct)))
            .RequirePermission("parties.view").WithName("ListPartyPaymentTerms").Produces<IReadOnlyList<PaymentTermOption>>();
        // Consultas públicas (CAD-01b): quem cria ou edita terceiros; limite por usuário e circuit breaker no provedor.
        parties.MapGet("/cnpj/{cnpj}", async (string cnpj, Guid? partyId, CnpjLookupService service, CancellationToken ct) => (await service.LookupAsync(cnpj, partyId, ct)).ToHttpResult())
            .RequireAnyPermission("parties.create", "parties.edit").AddEndpointFilter<LookupRateLimitFilter>().WithName("LookupCnpj").Produces<CnpjLookupResult>();
        parties.MapGet("/cep/{cep}", async (string cep, BemAqui.BuildingBlocks.Lookups.ICepLookup lookup, CancellationToken ct) => (await lookup.LookupAsync(cep, ct)).ToHttpResult())
            .RequireAnyPermission("parties.create", "parties.edit").AddEndpointFilter<LookupRateLimitFilter>().WithName("LookupCep").Produces<BemAqui.BuildingBlocks.Lookups.AddressData>();
        parties.MapGet("/document-check", async (string? document, PartyService service, CancellationToken ct) => Results.Ok(await service.CheckDocumentAsync(document, ct)))
            .RequirePermission("parties.view").WithName("CheckPartyDocument").Produces<DocumentCheck>();
        parties.MapGet("/{id:guid}", async (Guid id, PartyService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("parties.view").WithName("GetParty").Produces<PartyDetail>();

        parties.MapPost(string.Empty, async (CreatePartyRequest request, PartyService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToHttpResult(saved => Results.Created("/api/v1/parties/" + saved.Id, saved)))
            .RequirePermission("parties.create").WithName("CreateParty").Produces<PartyDetail>(StatusCodes.Status201Created);
        parties.MapPut("/{id:guid}", async (Guid id, UpdatePartyRequest request, PartyService service, CancellationToken ct) => (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("UpdateParty").Produces<PartyDetail>();
        parties.MapPost("/{id:guid}/inactivate", async (Guid id, PartyService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("InactivateParty").Produces<PartyDetail>();
        parties.MapPost("/{id:guid}/reactivate", async (Guid id, PartyService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("ReactivateParty").Produces<PartyDetail>();
        parties.MapDelete("/{id:guid}", async (Guid id, PartyService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("parties.delete").WithName("DeleteParty");
        // Restaurar desfaz uma exclusão: exige a mesma permissão de excluir.
        parties.MapPost("/{id:guid}/restore", async (Guid id, PartyService service, CancellationToken ct) => (await service.RestoreAsync(id, ct)).ToHttpResult())
            .RequirePermission("parties.delete").WithName("RestoreParty").Produces<PartyDetail>();

        parties.MapPost("/{id:guid}/addresses", async (Guid id, AddressInput request, PartyService service, CancellationToken ct) => (await service.AddAddressAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("AddPartyAddress").Produces<PartyDetail>();
        parties.MapPut("/{id:guid}/addresses/{addressId:guid}", async (Guid id, Guid addressId, AddressInput request, PartyService service, CancellationToken ct) => (await service.UpdateAddressAsync(id, addressId, request, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("UpdatePartyAddress").Produces<PartyDetail>();
        parties.MapDelete("/{id:guid}/addresses/{addressId:guid}", async (Guid id, Guid addressId, PartyService service, CancellationToken ct) => (await service.RemoveAddressAsync(id, addressId, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("RemovePartyAddress").Produces<PartyDetail>();

        parties.MapPost("/{id:guid}/contacts", async (Guid id, ContactInput request, PartyService service, CancellationToken ct) => (await service.AddContactAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("AddPartyContact").Produces<PartyDetail>();
        parties.MapPut("/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, ContactInput request, PartyService service, CancellationToken ct) => (await service.UpdateContactAsync(id, contactId, request, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("UpdatePartyContact").Produces<PartyDetail>();
        parties.MapDelete("/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, PartyService service, CancellationToken ct) => (await service.RemoveContactAsync(id, contactId, ct)).ToHttpResult())
            .RequirePermission("parties.edit").WithName("RemovePartyContact").Produces<PartyDetail>();
        return api;
    }
}
