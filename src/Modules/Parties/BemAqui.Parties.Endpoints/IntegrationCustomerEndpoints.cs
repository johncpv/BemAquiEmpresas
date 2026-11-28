using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Parties.Endpoints;

/// <summary>
/// Cliente visto pelo aplicativo conectado (INT-03 CA 7 e 8, LGPD): pelo telefone ou pelo id, id, tipo, nome, documento <b>mascarado</b>, cidade, UF e os
/// indicadores <c>canBill</c> (documento e endereço de cobrança completo) e <c>canInvoice</c> (também o código IBGE). Pelo documento, só id, primeiro nome
/// com as iniciais e os indicadores: os outros campos não vêm. Nunca endereço completo, outros contatos, crédito ou histórico financeiro.
/// </summary>
public sealed record IntegrationCustomer(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PartyKind? Kind,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Document,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? City,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? State,
    bool CanBill,
    bool CanInvoice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PhoneSetByApp = null);

/// <summary>Pedir o código de identidade (INT-04 CA 12): <c>email</c> ou <c>phone</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationIdentityChallengeInput(string Channel);

/// <summary>Desafio criado: o id, o canal e o destino mascarado (<c>m***@g***.com</c>, <c>(**) *****-4321</c>); nunca o destino completo nem o código.</summary>
public sealed record IntegrationIdentityChallenge(Guid ChallengeId, string Channel, string MaskedDestination);

/// <summary>Conferir o código (INT-04 CA 13).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationIdentityVerifyInput(string Code);

/// <summary>Resultado: conferiu; se não, quantas tentativas restam.</summary>
public sealed record IntegrationIdentityVerification(bool Verified, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AttemptsLeft = null);

/// <summary>Endereço de cobrança enviado pelo aplicativo (o código IBGE o ERP completa).</summary>
public sealed record IntegrationAddressInput(string ZipCode, string Street, string Number, string? Complement, string District, string City, string State);

/// <summary>Cliente novo pelo aplicativo (CA 10): só nome, tipo, documento, telefone, e-mail e endereço; o papel Cliente é dado pelo ERP.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationCustomerInput(PartyKind Kind, string Name, string? Document = null, string? Phone = null, string? Email = null, IntegrationAddressInput? Address = null);

/// <summary>Completar o contato (CA 11): e-mail, telefone e endereço só preenchem campo vazio. <c>document</c> e <c>name</c> são recusados.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationCustomerPatch(string? Email = null, string? Phone = null, IntegrationAddressInput? Address = null, string? Document = null, string? Name = null);

/// <summary>
/// Clientes pela API de integração (INT-03 CA 7 a 11): <c>GET /customers?phone=</c> ou <c>?document=</c> e <c>GET /customers/{id}</c> com
/// <c>parties.view</c>; <c>POST /customers</c> com <c>parties.create</c>; <c>PATCH /customers/{id}</c> com <c>parties.edit</c> (restrito). As buscas por
/// documento e telefone passam pelo <see cref="IntegrationLookupService"/> (limite, teto diário, anomalia e auditoria com o termo em hash).
/// </summary>
internal static class IntegrationCustomerEndpoints
{
    public static void MapIntegrationCustomers(this IEndpointRouteBuilder api)
    {
        var customers = api.MapIntegrationGroup().MapGroup("/customers");
        customers.MapGet(string.Empty, SearchAsync).RequirePermission("parties.view").WithName("IntegrationSearchCustomers").Produces<IReadOnlyList<IntegrationCustomer>>();
        customers.MapGet("/{id:guid}", async (Guid id, IParties parties, IOrderCharges charges, IAuditWriter audit, CancellationToken ct) =>
            {
                if (await CustomerAsync(id, parties, ct) is not { } party)
                {
                    return IntegrationApi.NotFound();
                }

                await audit.RecordReadAsync("Party", id.ToString(), ct);
                return Results.Ok(await FullAsync(party, parties, charges, ct));
            })
            .RequirePermission("parties.view").WithName("IntegrationGetCustomer").Produces<IntegrationCustomer>();
        customers.MapPost(string.Empty, CreateAsync).RequirePermission("parties.create").RequireIdempotencyKey().WithName("IntegrationCreateCustomer")
            .Produces<IntegrationCustomer>(StatusCodes.Status201Created);
        customers.MapPatch("/{id:guid}", PatchAsync).RequirePermission("parties.edit").WithName("IntegrationPatchCustomer").Produces<IntegrationCustomer>();
        customers.MapPost("/{id:guid}/identity-challenges", ChallengeAsync).RequirePermission("parties.view").RequireIdempotencyKey()
            .WithName("IntegrationCreateIdentityChallenge").Produces<IntegrationIdentityChallenge>(StatusCodes.Status201Created);
        customers.MapPost("/{id:guid}/identity-challenges/{challengeId:guid}/verify", VerifyAsync).RequirePermission("parties.view")
            .WithName("IntegrationVerifyIdentityChallenge").Produces<IntegrationIdentityVerification>();
    }

    /// <summary>
    /// Código de identidade (INT-04 CA 12): 6 dígitos ao e-mail ou telefone do cadastro que não veio de aplicativo; responde só o destino mascarado.
    /// Sem destino confirmado, 422 <c>no_verified_contact</c>; 3 por cliente e 20 por conexão por hora (429 com <c>Retry-After</c>).
    /// </summary>
    private static async Task<IResult> ChallengeAsync(Guid id, IntegrationIdentityChallengeInput input, IParties parties, IdentityChallengeService service, CancellationToken ct)
    {
        if (await CustomerAsync(id, parties, ct) is null)
        {
            return IntegrationApi.NotFound();
        }

        var (result, limit) = await service.StartAsync(id, input.Channel, ct);
        if (limit is not null)
        {
            return IntegrationApi.RateLimited(limit.RetryAfter, "Muitos códigos pedidos. Tente de novo mais tarde.");
        }

        if (result.IsFailure)
        {
            return result.Error!.Type == ErrorType.NotFound ? IntegrationApi.NotFound() : result.Error.ToProblem();
        }

        var started = result.Value;
        return Results.Created($"/api/v1/integration/customers/{id}/identity-challenges/{started.ChallengeId}",
            new IntegrationIdentityChallenge(started.ChallengeId, started.Channel, started.MaskedDestination));
    }

    /// <summary>Conferir (INT-04 CA 13): tempo constante, 5 tentativas; vencido, usado ou morto: 410 <c>identity_challenge_expired</c>.</summary>
    private static async Task<IResult> VerifyAsync(Guid id, Guid challengeId, IntegrationIdentityVerifyInput input, IParties parties, IdentityChallengeService service,
        CancellationToken ct)
    {
        if (await CustomerAsync(id, parties, ct) is null || await service.VerifyAsync(id, challengeId, input.Code, ct) is not { } outcome)
        {
            return IntegrationApi.NotFound();
        }

        if (outcome.Gone)
        {
            return Results.Problem(statusCode: StatusCodes.Status410Gone, title: "O código venceu ou já foi usado. Peça um código novo.",
                extensions: new Dictionary<string, object?> { ["code"] = IdentityChallengeService.ExpiredCode });
        }

        return Results.Ok(new IntegrationIdentityVerification(outcome.Verified, outcome.Verified ? null : outcome.AttemptsLeft));
    }

    private static async Task<IResult> SearchAsync(string? phone, string? document, IParties parties, IOrderCharges charges, IntegrationLookupService lookups, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone) == string.IsNullOrWhiteSpace(document))
        {
            return Invalid("phone", "Informe o telefone (E.164) ou o documento (CPF ou CNPJ), um dos dois.");
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var national = IntegrationData.NationalPhone(phone);
            if (national is null)
            {
                return Invalid("phone", "Telefone no formato E.164, com o DDD (ex.: +5511999998888).");
            }

            if (await lookups.BeginAsync("phone", national, ct) is { } limited)
            {
                return limited;
            }

            var found = (await parties.FindByPhoneAsync(national, ct)).Where(IsCustomer).ToList();
            var views = new List<IntegrationCustomer>();
            foreach (var party in found)
            {
                // INT-04 CA 11: telefone gravado por aplicativo e não confirmado no ERP não serve de prova para o Conecta.
                views.Add(await FullAsync(party, parties, charges, ct) with { PhoneSetByApp = await parties.PhoneSetByAppAsync(party.Id, national, ct) });
            }

            return Results.Ok(views);
        }

        var normalized = BrazilianDocuments.Normalize(document);
        if (!BrazilianDocuments.IsValid(normalized))
        {
            return Invalid("document", "CPF ou CNPJ inválido.");
        }

        if (await lookups.BeginAsync("document", normalized, ct) is { } refused)
        {
            return refused;
        }

        var byDocument = await parties.FindByDocumentAsync(normalized, ct);
        if (byDocument is null || !IsCustomer(byDocument))
        {
            return Results.Ok(Array.Empty<IntegrationCustomer>());
        }

        // CA 8: quem já tem o documento não ganha o nome completo nem o contato.
        var (canBill, canInvoice) = await FlagsAsync(byDocument.Id, parties, charges, ct);
        return Results.Ok(new[] { new IntegrationCustomer(byDocument.Id, null, IntegrationData.AbbreviateName(byDocument.LegalName), null, null, null, canBill, canInvoice) });
    }

    private static async Task<IResult> CreateAsync(IntegrationCustomerInput input, PartyService service, IParties parties, IOrderCharges charges, IntegrationLookupService lookups,
        CancellationToken ct)
    {
        var document = BrazilianDocuments.Normalize(input.Document);
        if (document.Length > 0 && lookups.Exhausted() is { } limited)
        {
            return limited;
        }

        var phone = input.Phone is null ? null : IntegrationData.NationalPhone(input.Phone) ?? input.Phone;
        var address = input.Address is { } a
            ? new AddressInput(AddressKind.Billing, new string((a.ZipCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray()), a.Street, a.Number, a.Complement, a.District, a.City, null, a.State, true)
            : null;
        var created = await service.CreateAsync(new CreatePartyRequest(input.Kind, document.Length == 0 ? null : document, input.Name ?? string.Empty, null, [PartyRoleKind.Customer],
            Email: input.Email, Phone: phone, BillingAddress: address), ct);
        if (created.IsFailure)
        {
            var error = created.Error!;
            if (error.Code is PartyService.ExistsCode or PartyService.DeletedCode)
            {
                // CA 9 e 10: o 409 conta no limite das buscas; só o id do cadastro ativo, nunca o nome.
                await lookups.RecordConflictAsync(document, ct);
                var data = error.Code == PartyService.ExistsCode && error.Data?.GetValueOrDefault("existingId") is Guid existing
                    ? new Dictionary<string, object?> { ["existingId"] = existing }
                    : null;
                return (Error.Conflict(PartyService.ExistsCode, "Este documento já está cadastrado.") with { Data = data }).ToProblem();
            }

            return error.ToProblem();
        }

        var party = (await parties.FindAsync(created.Value.Id, ct))!;
        return Results.Created($"/api/v1/integration/customers/{party.Id}", await FullAsync(party, parties, charges, ct));
    }

    private static async Task<IResult> PatchAsync(Guid id, IntegrationCustomerPatch input, IParties parties, IOrderCharges charges, CancellationToken ct)
    {
        if (await CustomerAsync(id, parties, ct) is null)
        {
            return IntegrationApi.NotFound();
        }

        var blocked = new Dictionary<string, string[]>();
        if (input.Document is not null)
        {
            blocked["document"] = ["O documento nunca muda pelo aplicativo."];
        }

        if (input.Name is not null)
        {
            blocked["name"] = ["O nome é alterado no ERP."];
        }

        if (blocked.Count > 0)
        {
            return Error.Validation(PartiesContract.BackofficeOnlyCode, "Este campo só muda no ERP, por uma pessoa.", blocked).ToProblem();
        }

        var phone = input.Phone is null ? null : IntegrationData.NationalPhone(input.Phone) ?? input.Phone;
        var address = input.Address is { } a ? new PartyAddressFill(a.ZipCode, a.Street, a.Number, a.Complement, a.District, a.City, a.State) : null;
        var filled = await parties.FillEmptyContactAsync(id, new PartyContactFill(input.Email, phone, address), ct);
        if (filled.IsFailure)
        {
            return filled.Error!.ToProblem();
        }

        return Results.Ok(await FullAsync((await parties.FindAsync(id, ct))!, parties, charges, ct));
    }

    /// <summary>Terceiro com o papel Cliente, ativo e não excluído; o resto responde 404 igual (CA 20).</summary>
    private static async Task<PartySummary?> CustomerAsync(Guid id, IParties parties, CancellationToken ct) =>
        await parties.FindAsync(id, ct) is { } party && IsCustomer(party) ? party : null;

    private static bool IsCustomer(PartySummary party) => party is { IsActive: true, IsDeleted: false } && party.Roles.Contains(PartyRoleKind.Customer);

    private static async Task<IntegrationCustomer> FullAsync(PartySummary party, IParties parties, IOrderCharges charges, CancellationToken ct)
    {
        var billing = await parties.DefaultAddressAsync(party.Id, AddressKind.Billing, ct);
        var (canBill, canInvoice) = await FlagsAsync(party.Id, parties, charges, ct);
        return new IntegrationCustomer(party.Id, party.Kind, party.TradeName ?? party.LegalName, IntegrationData.MaskDocument(party.Document), billing?.City, billing?.State,
            canBill, canInvoice);
    }

    /// <summary><c>canBill</c> é a mesma regra da cobrança do pedido (<see cref="IOrderCharges.ReadinessAsync"/>); <c>canInvoice</c> soma o código IBGE.</summary>
    private static async Task<(bool CanBill, bool CanInvoice)> FlagsAsync(Guid id, IParties parties, IOrderCharges charges, CancellationToken ct)
    {
        var canBill = (await charges.ReadinessAsync(id, ct)).CustomerBillable;
        var ibge = canBill && !string.IsNullOrWhiteSpace((await parties.DefaultAddressAsync(id, AddressKind.Billing, ct))?.IbgeCode);
        return (canBill, ibge);
    }

    private static IResult Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] }).ToProblem();
}
