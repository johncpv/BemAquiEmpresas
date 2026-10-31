using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>
/// Pagador lido do Parties no envio ao banco (BNK-01b CA 7): nunca guardado no boleto nem nos argumentos do job, nunca no log
/// (<see cref="ToString"/> mascara).
/// </summary>
public sealed record BillingPayer(PartyKind Kind, string Name, string Document, string ZipCode, string Street, string Number, string? Complement, string District, string City, string State, string? Email)
{
    public override string ToString() => "BillingPayer(***)";
}

/// <summary>Pedido de emissão: "seu número", condições e pagador.</summary>
public sealed record BillingIssueRequest(string OurReference, BillingSlipTerms Terms, BillingPayer Payer)
{
    public override string ToString() => $"BillingIssueRequest({OurReference})";
}

/// <summary>Situação da cobrança no banco, já traduzida.</summary>
public enum BankSlipSituation
{
    /// <summary>O banco ainda está processando (emissão assíncrona).</summary>
    Processing,

    /// <summary>Emitida, a receber (inclusive vencida).</summary>
    Open,

    Paid,
    Cancelled,
    Expired,

    /// <summary>O banco não conseguiu emitir.</summary>
    Failed,
}

/// <summary>Cobrança consultada no banco. <c>Payload</c> é o retorno já sem os dados do pagador (só ids, situação, datas e valores).</summary>
public sealed record BankSlip(string RequestId, string? OurReference, BankSlipSituation Situation, BillingSlipBankData Data, string Payload, decimal? PaidAmount, DateOnly? PaidOn)
{
    /// <summary>Pronta para mostrar: emitida com a linha digitável.</summary>
    public bool Ready => Situation == BankSlipSituation.Open && !string.IsNullOrWhiteSpace(Data.DigitableLine);
}

public enum BillingFailureKind
{
    /// <summary>O banco recusou os dados (4xx de validação): nada foi criado.</summary>
    Rejected,

    /// <summary>O "seu número" já existe no banco: a cobrança pode ter sido criada numa tentativa anterior.</summary>
    Duplicate,

    /// <summary>Credencial, certificado ou permissão recusados.</summary>
    Credentials,

    /// <summary>Rede, tempo esgotado ou instabilidade do banco: a cobrança pode ter sido criada.</summary>
    Unavailable,
}

/// <summary>Ação sugerida na falha (CA 8).</summary>
public static class BillingFailureActions
{
    public const string FixParty = "fix_party";
    public const string Retry = "retry";
    public const string FixIntegration = "fix_integration";
}

/// <summary>Recusa traduzida (PT-BR, sem dado do pagador) e a ação sugerida.</summary>
public sealed record BillingFailure(BillingFailureKind Kind, string Message, string Action, int? HttpStatus = null);

public sealed record BillingResult<T>(T? Value, BillingFailure? Failure)
{
    public bool Ok => Failure is null;

    public static BillingResult<T> Success(T value) => new(value, null);

    public static BillingResult<T> Fail(BillingFailure failure) => new(default, failure);
}

/// <summary>
/// Cobrança pelo banco (<c>02-arquitetura</c> §6.2, ADR 0032 §6): emitir, consultar (por código ou pelo "seu número"), baixar o PDF e
/// cancelar. O Finance só conhece este contrato; o <c>InterBillingProvider</c> (Infrastructure) fala com o Inter pelo <c>InterApiClient</c>.
/// </summary>
public interface IBankBillingProvider
{
    /// <summary>Cria a cobrança e devolve o código da solicitação (a emissão no banco é assíncrona).</summary>
    Task<BillingResult<string>> IssueAsync(BankIntegration integration, BillingIssueRequest request, CancellationToken cancellationToken = default);

    Task<BillingResult<BankSlip>> GetAsync(BankIntegration integration, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Procura pelo "seu número" (CA 8: num tempo esgotado depois do envio, antes de criar outra cobrança). Nulo se não existe.</summary>
    Task<BillingResult<BankSlip?>> FindByReferenceAsync(BankIntegration integration, string ourReference, DateOnly requestedOn, CancellationToken cancellationToken = default);

    Task<BillingResult<byte[]>> PdfAsync(BankIntegration integration, string requestId, CancellationToken cancellationToken = default);

    Task<BillingResult<bool>> CancelAsync(BankIntegration integration, string requestId, BillingSlipCancelReason reason, CancellationToken cancellationToken = default);

    /// <summary>Cadastra (ou troca) o endereço do aviso de pagamento da cobrança no banco (BNK-02 CA 1 e 3).</summary>
    Task<BillingResult<bool>> RegisterWebhookAsync(BankIntegration integration, string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cobranças que mudaram de situação no período (BNK-02 CA 11, conferência diária): pagas, canceladas e expiradas, inclusive as que não
    /// foram emitidas pelo BemAqui (vão para a conciliação, CA 10).
    /// </summary>
    Task<BillingResult<IReadOnlyList<BankSlip>>> ListChangedAsync(BankIntegration integration, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
