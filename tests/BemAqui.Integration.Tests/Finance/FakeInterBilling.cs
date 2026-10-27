using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BemAqui.Finance.Infrastructure.Inter;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>Uma cobrança guardada pelo fake (com o corpo recebido, para conferir o que foi ao banco).</summary>
public sealed class FakeCharge
{
    public required string Code { get; init; }

    public required string OurReference { get; init; }

    public required JsonObject Body { get; init; }

    public string Situation { get; set; } = "EM_PROCESSAMENTO";

    /// <summary>Consultas que ainda respondem "em processamento" (emissão assíncrona).</summary>
    public int PendingPolls { get; set; }

    public string? CancelReason { get; set; }
}

/// <summary>
/// API de Cobrança v3 do Inter simulada (BNK-01b): emissão assíncrona (código da solicitação, dados por consulta), procura pelo "seu
/// número", PDF em base64 e cancelamento. Modos de falha: recusa de validação, 5xx, tempo esgotado depois de criar (CA 8) e situação
/// forçada (expirado, pago).
/// </summary>
public sealed class FakeInterBilling
{
    public const string PdfText = "%PDF-1.4 boleto de teste";

    public ConcurrentDictionary<string, FakeCharge> Charges { get; } = new(StringComparer.Ordinal);

    /// <summary>Próxima emissão recusada com 400 neste campo (ex.: <c>pagador.cep</c>).</summary>
    public string? RejectProperty { get; set; }

    /// <summary>Próximas emissões respondem 503.</summary>
    public int ServerErrors { get; set; }

    /// <summary>Próxima emissão cria a cobrança e depois "cai" (tempo esgotado sem resposta).</summary>
    public bool TimeoutAfterCreate { get; set; }

    /// <summary>Consultas "em processamento" de cada cobrança nova.</summary>
    public int PollsUntilReady { get; set; }

    /// <summary>Próximo cancelamento recusado com 400.</summary>
    public bool RejectCancel { get; set; }

    public int IssueCalls => _issueCalls;

    private int _issueCalls;

    public FakeCharge? ByReference(string ourReference) => Charges.Values.FirstOrDefault(c => c.OurReference == ourReference);

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/')[InterEnvironments.BillingPath.Length..].Trim('/');
        var segments = path.Length == 0 ? [] : path.Split('/');
        if (request.Method == HttpMethod.Post && segments.Length == 0)
        {
            return await IssueAsync(request, cancellationToken);
        }

        if (request.Method == HttpMethod.Get && segments.Length == 0)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var found = Charges.Values.Where(c => c.OurReference == query["seuNumero"]).Select(View).ToArray();
            return Json(HttpStatusCode.OK, new JsonObject { ["totalPaginas"] = 1, ["totalElementos"] = found.Length, ["cobrancas"] = new JsonArray(found) }.ToJsonString());
        }

        if (!Charges.TryGetValue(segments[0], out var charge))
        {
            return Json(HttpStatusCode.NotFound, "{\"title\":\"Cobrança não encontrada\"}");
        }

        if (request.Method == HttpMethod.Get && segments.Length == 1)
        {
            if (charge.PendingPolls > 0)
            {
                charge.PendingPolls--;
            }
            else if (charge.Situation == "EM_PROCESSAMENTO")
            {
                charge.Situation = "A_RECEBER";
            }

            return Json(HttpStatusCode.OK, View(charge).ToJsonString());
        }

        if (request.Method == HttpMethod.Get && segments is [_, "pdf"])
        {
            return Json(HttpStatusCode.OK, new JsonObject { ["pdf"] = Convert.ToBase64String(Encoding.ASCII.GetBytes(PdfText)) }.ToJsonString());
        }

        if (request.Method == HttpMethod.Post && segments is [_, "cancelar"])
        {
            if (RejectCancel)
            {
                RejectCancel = false;
                return Json(HttpStatusCode.BadRequest, "{\"title\":\"Cobrança não pode ser cancelada\",\"violacoes\":[{\"razao\":\"situação\",\"propriedade\":\"situacao\"}]}");
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            charge.CancelReason = body["motivoCancelamento"]?.GetValue<string>();
            charge.Situation = "CANCELADO";
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private async Task<HttpResponseMessage> IssueAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _issueCalls);
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        if (RejectProperty is { } property)
        {
            RejectProperty = null;
            return Json(HttpStatusCode.BadRequest, new JsonObject
            {
                ["title"] = "Requisição inválida", ["detail"] = "Dados inválidos",
                ["violacoes"] = new JsonArray(new JsonObject { ["razao"] = "inválido", ["propriedade"] = property, ["valor"] = "x" }),
            }.ToJsonString());
        }

        if (ServerErrors > 0)
        {
            ServerErrors--;
            return Json(HttpStatusCode.ServiceUnavailable, "{\"title\":\"indisponível\"}");
        }

        var reference = body["seuNumero"]!.GetValue<string>();
        if (ByReference(reference) is not null)
        {
            return Json(HttpStatusCode.BadRequest, "{\"title\":\"seuNumero já utilizado\",\"violacoes\":[{\"razao\":\"duplicado\",\"propriedade\":\"seuNumero\"}]}");
        }

        var charge = new FakeCharge { Code = Guid.NewGuid().ToString(), OurReference = reference, Body = body, PendingPolls = PollsUntilReady };
        Charges[charge.Code] = charge;
        if (TimeoutAfterCreate)
        {
            TimeoutAfterCreate = false;
            throw new TaskCanceledException("O Inter caiu depois de criar a cobrança.");
        }

        return Json(HttpStatusCode.OK, new JsonObject { ["codigoSolicitacao"] = charge.Code }.ToJsonString());
    }

    private static JsonObject View(FakeCharge charge)
    {
        var ready = charge.Situation != "EM_PROCESSAMENTO";
        var view = new JsonObject
        {
            ["cobranca"] = new JsonObject
            {
                ["codigoSolicitacao"] = charge.Code, ["seuNumero"] = charge.OurReference, ["situacao"] = charge.Situation, ["dataSituacao"] = "2026-10-27",
                ["dataEmissao"] = "2026-10-27", ["dataVencimento"] = charge.Body["dataVencimento"]!.GetValue<string>(), ["valorNominal"] = charge.Body["valorNominal"]!.GetValue<decimal>(),
                ["pagador"] = charge.Body["pagador"]!.DeepClone(),
            },
        };
        if (ready)
        {
            view["boleto"] = new JsonObject { ["nossoNumero"] = "0001234567", ["codigoBarras"] = "07791000000000100001112345678901234567890123", ["linhaDigitavel"] = "07790001161234567890123456789012345678901234567" };
            view["pix"] = new JsonObject { ["txid"] = "tx" + charge.Code[..8], ["pixCopiaECola"] = "00020101021226930014br.gov.bcb.pix2571pix.inter.co/qr/" + charge.Code[..8] };
        }

        return view;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
