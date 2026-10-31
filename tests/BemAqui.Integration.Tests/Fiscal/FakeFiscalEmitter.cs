using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>O que o fake faz na próxima emissão (FIS-01 CA 3).</summary>
public enum FakeIssue
{
    Authorize,
    Reject,
    Deny,
    Contingency,

    /// <summary>Demora: fica em processamento até a consulta forçada ou o <see cref="FakeFiscalEmitter.Resolve"/>.</summary>
    Delay,

    /// <summary>Cria o documento e cai antes de responder (o adaptador vê <c>fiscal_unavailable</c>).</summary>
    CrashAfterCreate,

    /// <summary>Recusa antes de criar (400 campo a campo).</summary>
    Invalid,

    /// <summary>Chave recusada (401/403).</summary>
    KeyRejected,
}

/// <summary>
/// EmissorFiscal em memória para os testes dos módulos (CA 3): idempotência por chave (a mesma chave devolve o documento original),
/// autorização, rejeição, denegação, contingência, demora, queda depois de criar, a consulta forçada e o corpo do webhook. Nenhum teste
/// de módulo chama a rede.
/// </summary>
public sealed class FakeFiscalEmitter
{
    private readonly ConcurrentDictionary<string, NfeDocument> _byIdempotency = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, NfeDocument> _byKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FiscalDocumentStatus> _resolveOnConsult = new(StringComparer.Ordinal);
    private long _number;

    public Guid TenantId { get; set; } = Guid.CreateVersion7();

    public string IssuerDocument { get; set; } = "47225005000186";

    public FakeIssue Next { get; set; } = FakeIssue.Authorize;

    /// <summary>Contas por chave de API (rotação, escopos): sem a chave aqui, a conta padrão.</summary>
    public ConcurrentDictionary<string, EmitterAccount> Accounts { get; } = new(StringComparer.Ordinal);

    public string[] Scopes { get; set; } = ["consultar", "nfe:emitir", "nfe:eventos"];

    public FiscalEnvironment Environment { get; set; } = FiscalEnvironment.Homologation;

    /// <summary>Simula o Emissor fora do ar em todas as chamadas.</summary>
    public bool Down { get; set; }

    /// <summary>Sem a consulta forçada (lacuna L-a ainda aberta): 404.</summary>
    public bool NoForceConsult { get; set; }

    public bool SefazOnline { get; set; } = true;

    public ConcurrentQueue<(string Operation, string? ApiKey, string? IdempotencyKey)> Calls { get; } = new();

    public ConcurrentQueue<NfeIssueRequest> Requests { get; } = new();

    public EmitterAccount DefaultAccount() => new(Guid.Parse("8a1c0000-0000-7000-8000-000000000001"), "BemAqui Empresas — tenant de teste", TenantId, Scopes, false, 120,
        [new EmitterIssuer(IssuerDocument, "WSP Equipamentos por Elevação LTDA", "SP", "3508504", Environment, true,
            new EmitterCertificate(true, "AB12CD", "WSP EQUIPAMENTOS", DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddDays(140), 140, false))]);

    public IFiscalEmitter For(string apiKey) => new Bound(this, apiKey);

    public int Count(string operation) => Calls.Count(c => c.Operation == operation);

    /// <summary>Muda o desfecho que a consulta forçada vai achar na SEFAZ (L-a).</summary>
    public void Resolve(string accessKey, FiscalDocumentStatus status) => _resolveOnConsult[accessKey] = status;

    public NfeDocument? Document(string accessKey) => _byKey.TryGetValue(accessKey, out var doc) ? doc : null;

    /// <summary>Corpo do webhook do Emissor (o essencial, guia §6).</summary>
    public string WebhookBody(NfeDocument doc, string type, Guid? tenantId = null) => JsonSerializer.Serialize(new
    {
        tipo = type,
        tenantId = tenantId ?? TenantId,
        documentoId = doc.DocumentId,
        serie = doc.Series,
        numero = doc.Number,
        chaveDeAcesso = doc.AccessKey,
        situacao = doc.Status.ToString(),
        protocolo = doc.Protocol,
        codigoDeRejeicao = doc.Rejection?.Code,
        motivoDaRejeicao = doc.Rejection?.Reason,
        ocorridoEm = DateTimeOffset.UtcNow,
    });

    public void Settle(string accessKey, FiscalDocumentStatus status)
    {
        if (_byKey.TryGetValue(accessKey, out var doc))
        {
            var settled = With(doc, status);
            _byKey[accessKey] = settled;
            foreach (var pair in _byIdempotency.Where(p => p.Value.AccessKey == accessKey).ToList())
            {
                _byIdempotency[pair.Key] = settled;
            }
        }
    }

    private static NfeDocument With(NfeDocument doc, FiscalDocumentStatus status) => doc with
    {
        Status = status,
        Protocol = status == FiscalDocumentStatus.Authorized ? "1352600000" + doc.Number.ToString("00000", CultureInfo.InvariantCulture) : doc.Protocol,
        AuthorizedAt = status == FiscalDocumentStatus.Authorized ? DateTimeOffset.UtcNow : doc.AuthorizedAt,
        Rejection = status == FiscalDocumentStatus.Rejected ? new FiscalRejection(225, "Rejeicao: Falha no Schema XML", "Reporte com o número da nota.")
            : status == FiscalDocumentStatus.Denied ? new FiscalRejection(301, "Uso Denegado: Irregularidade fiscal do emitente", null) : null,
    };

    private string NewKey()
    {
        var n = Interlocked.Increment(ref _number);
        return "3526104722500518655002000" + n.ToString("D19", CultureInfo.InvariantCulture);
    }

    private Result<T>? Unavailable<T>() => Down ? Result<T>.Failure(Error.Unavailable(FiscalErrorCodes.Unavailable, "Não foi possível falar com o Emissor Fiscal.")) : null;

    private sealed class Bound(FakeFiscalEmitter fake, string apiKey) : IFiscalEmitter
    {
        private void Track(string operation, string? idempotency = null) => fake.Calls.Enqueue((operation, apiKey, idempotency));

        public Task<Result<NfePreview>> PreviewAsync(NfeIssueRequest request, CancellationToken cancellationToken = default)
        {
            Track("previa");
            return Task.FromResult(fake.Unavailable<NfePreview>() ?? Result<NfePreview>.Success(new NfePreview(fake.NewKey(), fake._number + 1, request.Series, [])));
        }

        public Task<Result<NfeDocument>> IssueNfeAsync(NfeIssueRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            Track("emitir", idempotencyKey);
            fake.Requests.Enqueue(request);
            if (fake.Unavailable<NfeDocument>() is { } down)
            {
                return Task.FromResult(down);
            }

            if (fake._byIdempotency.TryGetValue(idempotencyKey, out var existing))
            {
                return Task.FromResult(Result<NfeDocument>.Success(existing)); // a mesma chave devolve o documento original
            }

            switch (fake.Next)
            {
                case FakeIssue.Invalid:
                    return Task.FromResult<Result<NfeDocument>>(Error.Validation(FiscalErrorCodes.Validation, "O Emissor Fiscal recusou o pedido: itens: informe ao menos um item.",
                        new Dictionary<string, string[]> { ["itens"] = ["informe ao menos um item"] }));
                case FakeIssue.KeyRejected:
                    return Task.FromResult<Result<NfeDocument>>(Error.Unavailable(FiscalErrorCodes.IntegrationMisconfigured, "O Emissor Fiscal recusou a chave desta empresa."));
            }

            var key = fake.NewKey();
            var status = fake.Next switch
            {
                FakeIssue.Authorize or FakeIssue.CrashAfterCreate => FiscalDocumentStatus.Authorized,
                FakeIssue.Reject => FiscalDocumentStatus.Rejected,
                FakeIssue.Deny => FiscalDocumentStatus.Denied,
                FakeIssue.Contingency => FiscalDocumentStatus.ContingencyPending,
                _ => FiscalDocumentStatus.Processing,
            };
            var doc = With(new NfeDocument(Guid.CreateVersion7(), FiscalDocumentStatus.Processing, key, request.Series, fake._number, null, null, null, DateTimeOffset.UtcNow), status);
            fake._byIdempotency[idempotencyKey] = doc;
            fake._byKey[key] = doc;
            return Task.FromResult(fake.Next == FakeIssue.CrashAfterCreate
                ? Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal não respondeu a tempo.")
                : Result<NfeDocument>.Success(doc));
        }

        public Task<Result<NfeDocument>> GetNfeAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            Track("consultar");
            if (fake.Unavailable<NfeDocument>() is { } down)
            {
                return Task.FromResult(down);
            }

            return Task.FromResult(fake._byKey.TryGetValue(accessKey, out var doc)
                ? Result<NfeDocument>.Success(doc)
                : Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o documento."));
        }

        public Task<Result<NfeDocument>> ForceConsultAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            Track("consulta_forcada");
            if (fake.Unavailable<NfeDocument>() is { } down)
            {
                return Task.FromResult(down);
            }

            if (fake.NoForceConsult || !fake._byKey.TryGetValue(accessKey, out _))
            {
                return Task.FromResult<Result<NfeDocument>>(Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o documento."));
            }

            if (fake._resolveOnConsult.TryRemove(accessKey, out var status))
            {
                fake.Settle(accessKey, status);
            }

            return Task.FromResult(fake._byKey.TryGetValue(accessKey, out var current) ? Result<NfeDocument>.Success(current) : Error.NotFound(FiscalErrorCodes.NotFound, "x"));
        }

        public Task<Result<FiscalFile>> GetXmlAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            Track("xml");
            if (!fake._byKey.TryGetValue(accessKey, out var doc) || doc.Status != FiscalDocumentStatus.Authorized)
            {
                return Task.FromResult<Result<FiscalFile>>(Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o documento."));
            }

            var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><nfeProc versao=\"4.00\"><NFe><infNFe Id=\"NFe" + accessKey + "\"><dest><xNome>CLIENTE CANARIO XML</xNome></dest></infNFe></NFe>"
                + "<protNFe><infProt><chNFe>" + accessKey + "</chNFe><nProt>" + doc.Protocol + "</nProt></infProt></protNFe></nfeProc>";
            return Task.FromResult(Result<FiscalFile>.Success(new FiscalFile(Encoding.UTF8.GetBytes(xml), "application/xml", accessKey + "-nfe.xml")));
        }

        public Task<Result<FiscalFile>> GetDanfePdfAsync(string accessKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<FiscalFile>.Success(new FiscalFile("%PDF-1.7"u8.ToArray(), "application/pdf", accessKey + "-danfe.pdf")));

        public Task<Result<NfeEventResult>> CancelNfeAsync(string accessKey, string justification, CancellationToken cancellationToken = default)
        {
            Track("cancelar");
            fake.Settle(accessKey, FiscalDocumentStatus.Cancelled);
            return Task.FromResult(Result<NfeEventResult>.Success(new NfeEventResult(Guid.CreateVersion7(), NfeEventType.Cancellation, true, "135", 135, "Evento registrado", null, DateTimeOffset.UtcNow)));
        }

        public Task<Result<NfeEventResult>> CorrectNfeAsync(string accessKey, string correction, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<NfeEventResult>.Success(new NfeEventResult(Guid.CreateVersion7(), NfeEventType.CorrectionLetter, true, "135", 135, "Evento registrado", null, DateTimeOffset.UtcNow)));

        public Task<Result<NumberRangeInvalidationResult>> InvalidateRangeAsync(NumberRangeInvalidation request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<NumberRangeInvalidationResult>.Success(new NumberRangeInvalidationResult(true, request.Series, request.FirstNumber, request.LastNumber, "102", 102, "Inutilização homologada", null)));

        public Task<Result<IReadOnlyList<NfeEventResult>>> GetEventsAsync(string accessKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<NfeEventResult>>.Success([]));

        public Task<Result<SefazStatus>> SefazStatusAsync(string? issuerDocument, CancellationToken cancellationToken = default)
        {
            Track("sefaz");
            return Task.FromResult(fake.Unavailable<SefazStatus>() ?? Result<SefazStatus>.Success(new SefazStatus(fake.SefazOnline, "SP", "homologacao", fake.SefazOnline ? null : "Fora do ar", 300, fake.SefazOnline ? null : "SVC-AN", DateTimeOffset.UtcNow)));
        }

        public Task<Result<EmitterAccount>> GetAccountAsync(CancellationToken cancellationToken = default)
        {
            Track("me");
            if (fake.Unavailable<EmitterAccount>() is { } down)
            {
                return Task.FromResult(down);
            }

            if (apiKey.StartsWith("recusada", StringComparison.Ordinal))
            {
                return Task.FromResult<Result<EmitterAccount>>(Error.Unavailable(FiscalErrorCodes.IntegrationMisconfigured, "O Emissor Fiscal recusou a chave desta empresa."));
            }

            return Task.FromResult(Result<EmitterAccount>.Success(fake.Accounts.TryGetValue(apiKey, out var account) ? account : fake.DefaultAccount()));
        }

        public Task<Result<CertificateUploadResult>> UploadCertificateAsync(string issuerDocument, ReadOnlyMemory<byte> pfx, Secret password, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<CertificateUploadResult>.Success(new CertificateUploadResult(Guid.CreateVersion7(), "AB12CD", DateTimeOffset.UtcNow.AddYears(1))));

        public Task<Result<NfseDocument>> IssueNfseAsync(NfseIssueRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<NfseDocument>>(Error.Unavailable(FiscalErrorCodes.Unavailable, "NFS-e entra na FIS-06."));

        public Task<Result<NfseDocument>> GetNfseAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<NfseDocument>>(Error.NotFound(FiscalErrorCodes.NotFound, "NFS-e entra na FIS-06."));
    }
}

/// <summary>Fábrica dos testes: um fake por tenant; a chave de cada conexão chega ao fake (rotação, CA 10).</summary>
public sealed class FakeFiscalEmitterFactory : IFiscalEmitterFactory
{
    public ConcurrentDictionary<Guid, FakeFiscalEmitter> ByTenant { get; } = new();

    /// <summary>Endereços com que o adaptador foi criado (o da configuração, ADR 0027 adendo de 27/10/2026).</summary>
    public ConcurrentQueue<Uri> BaseUrls { get; } = new();

    public FakeFiscalEmitter Of(Guid tenantId) => ByTenant.GetOrAdd(tenantId, _ => new FakeFiscalEmitter());

    public IFiscalEmitter Create(FiscalEmitterConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        BaseUrls.Enqueue(connection.BaseUrl);
        return Of(connection.TenantId).For(connection.ApiKey.Reveal());
    }
}
