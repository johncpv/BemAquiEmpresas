using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BemAqui.Finance.Application;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Infrastructure.Inter;

/// <summary>
/// Cobrança pelo Inter (API de Cobrança v3, bolepix; BNK-01b, ADR 0032 §6) sobre o <see cref="IInterApiClient"/> (mTLS, token em cache,
/// URL base fixa). Os nomes dos campos e dos motivos são os da documentação pública e ficam conferidos no sandbox (runbook). O retorno
/// guardado (<c>provider_payload</c>) é montado só com ids, situação, datas e valores: o pagador nunca entra. Recusas viram mensagens em
/// PT-BR sem dado do pagador.
/// </summary>
public sealed class InterBillingProvider(IInterApiClient inter, BankIntegrationService integrations, IOptions<InterOptions> options) : IBankBillingProvider
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<BillingResult<string>> IssueAsync(BankIntegration integration, BillingIssueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = IssueBody(request, options.Value.BillingExpiryDays).ToJsonString();
        var response = await SendAsync(integration, new InterRequest("boleto.emitir", HttpMethod.Post, InterEnvironments.BillingPath, () => new StringContent(body, Encoding.UTF8, "application/json")), cancellationToken);
        if (!response.Ok)
        {
            return BillingResult<string>.Fail(response.Failure!);
        }

        if (response.Value!.Status is >= 200 and < 300 && Parse(response.Value.Body)?["codigoSolicitacao"]?.GetValue<string>() is { Length: > 0 } code)
        {
            return BillingResult<string>.Success(code);
        }

        return BillingResult<string>.Fail(Refusal(response.Value));
    }

    public async Task<BillingResult<BankSlip>> GetAsync(BankIntegration integration, string requestId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(integration, new InterRequest("boleto.consultar", HttpMethod.Get, $"{InterEnvironments.BillingPath}/{Uri.EscapeDataString(requestId)}"), cancellationToken);
        if (!response.Ok)
        {
            return BillingResult<BankSlip>.Fail(response.Failure!);
        }

        if (response.Value!.Status is >= 200 and < 300 && Parse(response.Value.Body) is { } node && Slip(node) is { } slip)
        {
            return BillingResult<BankSlip>.Success(slip);
        }

        return BillingResult<BankSlip>.Fail(Refusal(response.Value));
    }

    public async Task<BillingResult<BankSlip?>> FindByReferenceAsync(BankIntegration integration, string ourReference, DateOnly requestedOn, CancellationToken cancellationToken = default)
    {
        var from = requestedOn.AddDays(-1).ToString("yyyy-MM-dd", Invariant);
        var to = requestedOn.AddDays(2).ToString("yyyy-MM-dd", Invariant);
        var path = $"{InterEnvironments.BillingPath}?dataInicial={from}&dataFinal={to}&filtrarDataPor=EMISSAO&seuNumero={Uri.EscapeDataString(ourReference)}";
        var response = await SendAsync(integration, new InterRequest("boleto.procurar", HttpMethod.Get, path), cancellationToken);
        if (!response.Ok)
        {
            return BillingResult<BankSlip?>.Fail(response.Failure!);
        }

        if (response.Value!.Status is < 200 or >= 300 || Parse(response.Value.Body) is not { } node)
        {
            return BillingResult<BankSlip?>.Fail(Refusal(response.Value));
        }

        var found = (node["cobrancas"] as JsonArray ?? []).OfType<JsonNode>().Select(Slip).OfType<BankSlip>()
            .FirstOrDefault(s => string.Equals(s.OurReference, ourReference, StringComparison.OrdinalIgnoreCase));
        return BillingResult<BankSlip?>.Success(found);
    }

    public async Task<BillingResult<byte[]>> PdfAsync(BankIntegration integration, string requestId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(integration, new InterRequest("boleto.pdf", HttpMethod.Get, $"{InterEnvironments.BillingPath}/{Uri.EscapeDataString(requestId)}/pdf"), cancellationToken);
        if (!response.Ok)
        {
            return BillingResult<byte[]>.Fail(response.Failure!);
        }

        if (response.Value!.Status is >= 200 and < 300 && Parse(response.Value.Body)?["pdf"]?.GetValue<string>() is { Length: > 0 } base64)
        {
            try
            {
                return BillingResult<byte[]>.Success(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return BillingResult<byte[]>.Fail(new BillingFailure(BillingFailureKind.Unavailable, "O Inter devolveu o PDF do boleto num formato inesperado.", BillingFailureActions.Retry));
            }
        }

        return BillingResult<byte[]>.Fail(Refusal(response.Value));
    }

    public async Task<BillingResult<bool>> CancelAsync(BankIntegration integration, string requestId, BillingSlipCancelReason reason, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["motivoCancelamento"] = BillingSlipRules.BankReason(reason) }.ToJsonString();
        var response = await SendAsync(integration, new InterRequest("boleto.cancelar", HttpMethod.Post, $"{InterEnvironments.BillingPath}/{Uri.EscapeDataString(requestId)}/cancelar",
            () => new StringContent(body, Encoding.UTF8, "application/json")), cancellationToken);
        if (!response.Ok)
        {
            return BillingResult<bool>.Fail(response.Failure!);
        }

        return response.Value!.Status is >= 200 and < 300 ? BillingResult<bool>.Success(true) : BillingResult<bool>.Fail(Refusal(response.Value));
    }

    /// <summary>Webhook da cobrança v3 (<c>PUT cobrancas/webhook</c>, escopo <c>boleto-cobranca.write</c>; conferido no sandbox, runbook).</summary>
    public async Task<BillingResult<bool>> RegisterWebhookAsync(BankIntegration integration, string url, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["webhookUrl"] = url }.ToJsonString();
        var response = await SendAsync(integration, new InterRequest("boleto.webhook", HttpMethod.Put, InterEnvironments.BillingPath + "/webhook",
            () => new StringContent(body, Encoding.UTF8, "application/json")), cancellationToken);
        if (!response.Ok)
        {
            return BillingResult<bool>.Fail(response.Failure!);
        }

        return response.Value!.Status is >= 200 and < 300 ? BillingResult<bool>.Success(true) : BillingResult<bool>.Fail(Refusal(response.Value));
    }

    /// <summary>Cobranças por data da situação no período, todas as páginas (até <see cref="MaxPages"/>).</summary>
    public async Task<BillingResult<IReadOnlyList<BankSlip>>> ListChangedAsync(BankIntegration integration, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var found = new List<BankSlip>();
        for (var page = 0; page < MaxPages; page++)
        {
            var path = $"{InterEnvironments.BillingPath}?dataInicial={from.ToString("yyyy-MM-dd", Invariant)}&dataFinal={to.ToString("yyyy-MM-dd", Invariant)}&filtrarDataPor=SITUACAO"
                + $"&paginacao.paginaAtual={page.ToString(Invariant)}&paginacao.itensPorPagina={PageSize.ToString(Invariant)}";
            var response = await SendAsync(integration, new InterRequest("boleto.listar", HttpMethod.Get, path), cancellationToken);
            if (!response.Ok)
            {
                return BillingResult<IReadOnlyList<BankSlip>>.Fail(response.Failure!);
            }

            if (response.Value!.Status is < 200 or >= 300 || Parse(response.Value.Body) is not { } node)
            {
                return BillingResult<IReadOnlyList<BankSlip>>.Fail(Refusal(response.Value));
            }

            found.AddRange((node["cobrancas"] as JsonArray ?? []).OfType<JsonNode>().Select(Slip).OfType<BankSlip>()
                .Where(s => s.Situation is BankSlipSituation.Paid or BankSlipSituation.Cancelled or BankSlipSituation.Expired));
            var pages = node["totalPaginas"] is JsonValue total && total.TryGetValue<int>(out var t) ? t : 1;
            if (page + 1 >= pages)
            {
                break;
            }
        }

        return BillingResult<IReadOnlyList<BankSlip>>.Success(found);
    }

    public const int PageSize = 1000;
    public const int MaxPages = 50;

    private async Task<BillingResult<InterResponse>> SendAsync(BankIntegration integration, InterRequest request, CancellationToken ct)
    {
        using var credentials = integrations.Open(integration);
        var result = await inter.SendAsync(credentials, request, ct);
        if (result.Ok)
        {
            return BillingResult<InterResponse>.Success(result.Value!);
        }

        var failure = result.Failure!;
        var kind = failure.Kind is InterFailureKind.CredentialsRefused or InterFailureKind.Forbidden ? BillingFailureKind.Credentials : BillingFailureKind.Unavailable;
        var action = kind == BillingFailureKind.Credentials ? BillingFailureActions.FixIntegration : BillingFailureActions.Retry;
        return BillingResult<InterResponse>.Fail(new BillingFailure(kind, failure.Message, action, failure.HttpStatus));
    }

    /// <summary>Corpo da emissão (v3): valor, vencimento, prazo de baixa, pagador, multa, mora, desconto e mensagem.</summary>
    public static JsonObject IssueBody(BillingIssueRequest request, int expiryDays)
    {
        ArgumentNullException.ThrowIfNull(request);
        var terms = request.Terms;
        var payer = request.Payer;
        var body = new JsonObject
        {
            ["seuNumero"] = request.OurReference,
            ["valorNominal"] = terms.Amount,
            ["dataVencimento"] = terms.DueDate.ToString("yyyy-MM-dd", Invariant),
            ["numDiasAgenda"] = Math.Clamp(expiryDays, 0, 60),
            ["pagador"] = new JsonObject
            {
                ["cpfCnpj"] = payer.Document,
                ["tipoPessoa"] = payer.Kind == PartyKind.PF ? "FISICA" : "JURIDICA",
                ["nome"] = Cut(payer.Name, 100),
                ["endereco"] = Cut(payer.Street, 100),
                ["numero"] = Cut(payer.Number, 10),
                ["complemento"] = Cut(payer.Complement, 30),
                ["bairro"] = Cut(payer.District, 60),
                ["cidade"] = Cut(payer.City, 60),
                ["uf"] = payer.State.ToUpperInvariant(),
                ["cep"] = payer.ZipCode,
                ["email"] = Cut(payer.Email, 50),
            },
        };
        if (terms.FinePct > 0)
        {
            body["multa"] = new JsonObject { ["codigo"] = "PERCENTUAL", ["taxa"] = terms.FinePct };
        }

        if (terms.InterestMonthPct > 0)
        {
            body["mora"] = new JsonObject { ["codigo"] = "TAXAMENSAL", ["taxa"] = terms.InterestMonthPct };
        }

        if (terms.DiscountAmount > 0 && terms.DiscountUntil is { } until)
        {
            body["desconto"] = new JsonObject
            {
                ["codigo"] = "VALORFIXODATAINFORMADA", ["valor"] = terms.DiscountAmount, ["quantidadeDias"] = Math.Max(0, terms.DueDate.DayNumber - until.DayNumber),
            };
        }

        var lines = BillingSlipRules.MessageLines(terms.Message);
        if (lines.Count > 0)
        {
            var message = new JsonObject();
            for (var i = 0; i < lines.Count; i++)
            {
                message["linha" + (i + 1).ToString(Invariant)] = lines[i];
            }

            body["mensagem"] = message;
        }

        return body;
    }

    /// <summary>Lê a cobrança da consulta (v3: <c>cobranca</c>, <c>boleto</c>, <c>pix</c>) e monta o retorno guardado sem o pagador.</summary>
    public static BankSlip? Slip(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var charge = node["cobranca"];
        if (charge?["codigoSolicitacao"]?.GetValue<string>() is not { Length: > 0 } code)
        {
            return null;
        }

        var situation = Text(charge["situacao"]);
        var data = new BillingSlipBankData(Text(node["boleto"]?["nossoNumero"]), Text(node["boleto"]?["codigoBarras"]), Text(node["boleto"]?["linhaDigitavel"]), Text(node["pix"]?["pixCopiaECola"]));
        var payload = new JsonObject
        {
            ["codigoSolicitacao"] = code,
            ["seuNumero"] = Text(charge["seuNumero"]),
            ["situacao"] = situation,
            ["dataSituacao"] = Text(charge["dataSituacao"]),
            ["dataEmissao"] = Text(charge["dataEmissao"]),
            ["dataVencimento"] = Text(charge["dataVencimento"]),
            ["valorNominal"] = Number(charge["valorNominal"]),
            ["valorTotalRecebido"] = Number(charge["valorTotalRecebido"]),
            ["nossoNumero"] = data.OurNumber,
            ["txid"] = Text(node["pix"]?["txid"]),
        }.ToJsonString();
        var paidOn = DateOnly.TryParseExact(Text(charge["dataSituacao"]), "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
        return new BankSlip(code, Text(charge["seuNumero"]), Situation(situation), data, payload, Number(charge["valorTotalRecebido"]), paidOn);
    }

    /// <summary>Situação do Inter traduzida (conferida no sandbox).</summary>
    public static BankSlipSituation Situation(string? situation) => situation?.ToUpperInvariant() switch
    {
        "A_RECEBER" or "ATRASADO" or "PROTESTO" => BankSlipSituation.Open,
        "RECEBIDO" or "MARCADO_RECEBIDO" => BankSlipSituation.Paid,
        "CANCELADO" => BankSlipSituation.Cancelled,
        "EXPIRADO" => BankSlipSituation.Expired,
        "FALHA_EMISSAO" => BankSlipSituation.Failed,
        _ => BankSlipSituation.Processing,
    };

    /// <summary>
    /// Recusa do banco em PT-BR (CA 8), pelo campo recusado (<c>violacoes[].propriedade</c>), sem repetir o valor (que pode ser do
    /// pagador). Cadastro do cliente: ação "Corrigir cadastro do cliente"; o resto, "Tentar de novo".
    /// </summary>
    public static BillingFailure Refusal(InterResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Status is not (400 or 409 or 422))
        {
            var mapped = InterApiClient.FailureFor(response.Status, token: false);
            var kind = response.Status is 401 or 403 ? BillingFailureKind.Credentials : BillingFailureKind.Unavailable;
            return new BillingFailure(kind, mapped.Message, kind == BillingFailureKind.Credentials ? BillingFailureActions.FixIntegration : BillingFailureActions.Retry, response.Status);
        }

        var node = Parse(response.Body);
        var properties = (node?["violacoes"] as JsonArray ?? []).OfType<JsonNode>().Select(v => Text(v["propriedade"])?.ToLowerInvariant() ?? string.Empty).ToList();
        var text = (Text(node?["title"]) + " " + Text(node?["detail"])).ToLowerInvariant();
        if (properties.Any(p => p.Contains("seunumero", StringComparison.Ordinal)) || text.Contains("seunumero", StringComparison.Ordinal) || text.Contains("duplic", StringComparison.Ordinal))
        {
            return new BillingFailure(BillingFailureKind.Duplicate, "O Inter já tem uma cobrança com este número.", BillingFailureActions.Retry, response.Status);
        }

        string? Party(string field, string message) => properties.Any(p => p.Contains(field, StringComparison.Ordinal)) ? message : null;
        var partyMessage = Party("cep", "O CEP do cliente não foi aceito pelo banco.")
            ?? Party("cpfcnpj", "O CPF/CNPJ do cliente não foi aceito pelo banco.")
            ?? Party("email", "O e-mail de cobrança do cliente não foi aceito pelo banco.")
            ?? Party("nome", "O nome do cliente não foi aceito pelo banco.")
            ?? Party("uf", "A UF do endereço de cobrança do cliente não foi aceita pelo banco.")
            ?? (properties.Any(p => p.StartsWith("pagador", StringComparison.Ordinal)) ? "O endereço de cobrança do cliente não foi aceito pelo banco." : null);
        if (partyMessage is not null)
        {
            return new BillingFailure(BillingFailureKind.Rejected, partyMessage, BillingFailureActions.FixParty, response.Status);
        }

        var message = properties.Any(p => p.Contains("datavencimento", StringComparison.Ordinal)) ? "O vencimento do boleto não foi aceito pelo banco."
            : properties.Any(p => p.Contains("desconto", StringComparison.Ordinal)) ? "O desconto do boleto não foi aceito pelo banco."
            : properties.Any(p => p.Contains("multa", StringComparison.Ordinal) || p.Contains("mora", StringComparison.Ordinal)) ? "A multa ou os juros do boleto não foram aceitos pelo banco."
            : properties.Any(p => p.Contains("valor", StringComparison.Ordinal)) ? "O valor do boleto não foi aceito pelo banco."
            : "O Inter recusou os dados do boleto.";
        return new BillingFailure(BillingFailureKind.Rejected, message, BillingFailureActions.Retry, response.Status);
    }

    private static JsonNode? Parse(string body)
    {
        try
        {
            return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static decimal? Number(JsonNode? node) => node is JsonValue value
        ? value.TryGetValue<decimal>(out var d) ? d : value.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Number, Invariant, out var p) ? p : null
        : null;

    private static string? Cut(string? value, int max) => value is { Length: > 0 } v ? (v.Length > max ? v[..max] : v) : null;
}
