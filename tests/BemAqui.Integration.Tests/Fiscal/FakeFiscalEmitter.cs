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

/// <summary>O que o fake faz no próximo evento (FIS-03b).</summary>
public enum FakeEvent
{
    Accept,

    /// <summary>A SEFAZ recusa (422 com <c>aceito = false</c>).</summary>
    Reject,

    /// <summary>O Emissor recusa antes da SEFAZ (400).</summary>
    Refuse,

    /// <summary>Rede caiu: não se sabe se saiu (nada registrado).</summary>
    Down,

    /// <summary>Registrou na SEFAZ e caiu antes de responder: o webhook e a reconsulta confirmam.</summary>
    DownAfterRegister,
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

    /// <summary>O que o fake faz no próximo evento (cancelamento, carta de correção, inutilização; FIS-03b).</summary>
    public FakeEvent NextEvent { get; set; } = FakeEvent.Accept;

    /// <summary>Sem a rota do XML do evento (lacuna L-n): 404.</summary>
    public bool NoEventXml { get; set; }

    /// <summary>Conversor de PDF fora do ar: a DANFE cai na reserva do BemAqui.</summary>
    public bool NoDanfe { get; set; }

    public ConcurrentQueue<string> Justifications { get; } = new();

    public ConcurrentQueue<NumberRangeInvalidation> Invalidations { get; } = new();

    private readonly ConcurrentDictionary<string, ConcurrentQueue<NfeEventResult>> _events = new(StringComparer.Ordinal);

    private NfeEventResult AddEvent(string accessKey, NfeEventType type, bool accepted)
    {
        var result = accepted
            ? new NfeEventResult(Guid.CreateVersion7(), type, true, "13526" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture), 135, "Evento registrado e vinculado a NF-e", null, DateTimeOffset.UtcNow)
            : new NfeEventResult(Guid.CreateVersion7(), type, false, null, 501, "Rejeicao: Prazo de cancelamento superior ao previsto na Legislacao", "Emita a nota de devolução.", DateTimeOffset.UtcNow);
        if (accepted)
        {
            _events.GetOrAdd(accessKey, _ => new ConcurrentQueue<NfeEventResult>()).Enqueue(result);
        }

        return result;
    }

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
            Certificate ?? new EmitterCertificate(true, "AB12CD", "WSP EQUIPAMENTOS", DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddDays(140), 140, false))], NfseCities);

    // ---------- NFS-e (FIS-06) ----------

    private readonly ConcurrentDictionary<string, Guid> _nfseByIdempotency = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, NfseDocument> _nfse = new();
    private long _nfseNumber;

    /// <summary>Municípios com a NFS-e habilitada no <c>GET /me</c> (lacuna L-m); nulo, como o Emissor de hoje (processo provisório).</summary>
    public string[]? NfseCities { get; set; }

    public ConcurrentQueue<NfseIssueRequest> NfseRequests { get; } = new();

    public NfseDocument? Nfse(Guid documentId) => _nfse.TryGetValue(documentId, out var doc) ? doc : null;

    private NfseDocument AuthorizedNfse(Guid id)
    {
        var n = Interlocked.Increment(ref _nfseNumber) + 9_000_000;
        return new NfseDocument(id, FiscalDocumentStatus.Authorized, n.ToString(CultureInfo.InvariantCulture), "VER" + n.ToString(CultureInfo.InvariantCulture), "https://nfe.etransparencia.com.br/x", null);
    }

    /// <summary>Certificado no cofre do fake (FIS-02a): o enviado por último ou o trocado "fora do BemAqui" pelo teste; nulo, o padrão.</summary>
    public EmitterCertificate? Certificate { get; set; }

    /// <summary>Quantos bytes chegaram no último envio e para qual emitente (nunca o arquivo nem a senha).</summary>
    public (int Bytes, string Issuer)? LastUpload { get; set; }

    /// <summary>Recusa o próximo envio como o Emissor faria (CNPJ do certificado diferente do emitente, lacuna L-h).</summary>
    public bool RejectCertificate { get; set; }

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

        public Task<Result<FiscalFile>> GetDanfePdfAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            Track("danfe");
            if (fake.NoDanfe)
            {
                return Task.FromResult<Result<FiscalFile>>(Error.Unavailable(FiscalErrorCodes.Unavailable, "O conversor de PDF do Emissor está fora do ar."));
            }

            var cancelled = fake._byKey.TryGetValue(accessKey, out var doc) && doc.Status == FiscalDocumentStatus.Cancelled;
            return Task.FromResult(Result<FiscalFile>.Success(new FiscalFile(cancelled ? "%PDF-1.7 CANCELADA"u8.ToArray() : "%PDF-1.7"u8.ToArray(), "application/pdf", accessKey + "-danfe.pdf")));
        }

        public Task<Result<NfeEventResult>> CancelNfeAsync(string accessKey, string justification, CancellationToken cancellationToken = default)
        {
            Track("cancelar");
            fake.Justifications.Enqueue(justification);
            switch (fake.NextEvent)
            {
                case FakeEvent.Down:
                    return Task.FromResult<Result<NfeEventResult>>(Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal não respondeu a tempo."));
                case FakeEvent.Refuse:
                    return Task.FromResult<Result<NfeEventResult>>(Error.Validation(FiscalErrorCodes.Validation, "Prazo de cancelamento encerrado (24 h)."));
                case FakeEvent.Reject:
                    return Task.FromResult(Result<NfeEventResult>.Success(fake.AddEvent(accessKey, NfeEventType.Cancellation, false)));
                case FakeEvent.DownAfterRegister:
                    fake.AddEvent(accessKey, NfeEventType.Cancellation, true);
                    fake.Settle(accessKey, FiscalDocumentStatus.Cancelled);
                    return Task.FromResult<Result<NfeEventResult>>(Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal não respondeu a tempo."));
            }

            var registered = fake.AddEvent(accessKey, NfeEventType.Cancellation, true);
            fake.Settle(accessKey, FiscalDocumentStatus.Cancelled);
            return Task.FromResult(Result<NfeEventResult>.Success(registered));
        }

        public Task<Result<NfeEventResult>> CorrectNfeAsync(string accessKey, string correction, CancellationToken cancellationToken = default)
        {
            Track("carta_correcao");
            return Task.FromResult(fake.NextEvent switch
            {
                FakeEvent.Down => Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal não respondeu a tempo."),
                FakeEvent.Reject => Result<NfeEventResult>.Success(fake.AddEvent(accessKey, NfeEventType.CorrectionLetter, false)),
                _ => Result<NfeEventResult>.Success(fake.AddEvent(accessKey, NfeEventType.CorrectionLetter, true)),
            });
        }

        public Task<Result<NumberRangeInvalidationResult>> InvalidateRangeAsync(NumberRangeInvalidation request, CancellationToken cancellationToken = default)
        {
            Track("inutilizar");
            fake.Invalidations.Enqueue(request);
            return Task.FromResult(fake.NextEvent switch
            {
                FakeEvent.Reject => Result<NumberRangeInvalidationResult>.Success(new NumberRangeInvalidationResult(false, request.Series, request.FirstNumber, request.LastNumber, null, 241,
                    "Rejeicao: Um numero da faixa ja foi utilizado", "Confira a faixa.")),
                FakeEvent.Down => Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal não respondeu a tempo."),
                _ => Result<NumberRangeInvalidationResult>.Success(new NumberRangeInvalidationResult(true, request.Series, request.FirstNumber, request.LastNumber, "102", 102,
                    "Inutilização homologada", null)),
            });
        }

        public Task<Result<IReadOnlyList<NfeEventResult>>> GetEventsAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            Track("eventos");
            return Task.FromResult(Result<IReadOnlyList<NfeEventResult>>.Success(fake._events.TryGetValue(accessKey, out var list) ? list.ToList() : []));
        }

        public Task<Result<FiscalFile>> GetEventXmlAsync(string accessKey, Guid eventId, CancellationToken cancellationToken = default)
        {
            Track("evento_xml");
            if (fake.NoEventXml || !fake._events.TryGetValue(accessKey, out var list) || list.All(e => e.Id != eventId))
            {
                return Task.FromResult<Result<FiscalFile>>(Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o evento."));
            }

            var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><procEventoNFe versao=\"1.00\"><evento><infEvento><chNFe>" + accessKey + "</chNFe></infEvento></evento>"
                + "<retEvento><infEvento><nProt>135" + eventId.ToString("N")[..8] + "</nProt></infEvento></retEvento></procEventoNFe>";
            return Task.FromResult(Result<FiscalFile>.Success(new FiscalFile(Encoding.UTF8.GetBytes(xml), "application/xml", accessKey + "-evento.xml")));
        }

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

        public Task<Result<CertificateUploadResult>> UploadCertificateAsync(string issuerDocument, ReadOnlyMemory<byte> pfx, Secret password, CancellationToken cancellationToken = default)
        {
            Track("certificado");
            if (fake.Unavailable<CertificateUploadResult>() is { } down)
            {
                return Task.FromResult(down);
            }

            if (fake.RejectCertificate)
            {
                return Task.FromResult<Result<CertificateUploadResult>>(Error.Validation(FiscalErrorCodes.Validation, "O CNPJ do certificado não é o do emitente."));
            }

            using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(pfx.Span, password.Reveal(),
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            fake.LastUpload = (pfx.Length, issuerDocument);
            fake.Certificate = new EmitterCertificate(true, certificate.Thumbprint, certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false),
                certificate.NotBefore, notAfter, (int)(notAfter - DateTimeOffset.UtcNow).TotalDays, false);
            return Task.FromResult(Result<CertificateUploadResult>.Success(new CertificateUploadResult(Guid.CreateVersion7(), certificate.Thumbprint, notAfter)));
        }

        // ---------- NFS-e (FIS-06) ----------

        public Task<Result<NfseDocument>> IssueNfseAsync(NfseIssueRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            Track("nfse_emitir", idempotencyKey);
            fake.NfseRequests.Enqueue(request);
            if (fake.Unavailable<NfseDocument>() is { } down)
            {
                return Task.FromResult(down);
            }

            if (fake._nfseByIdempotency.TryGetValue(idempotencyKey, out var existing))
            {
                return Task.FromResult(Result<NfseDocument>.Success(fake._nfse[existing]));
            }

            if (fake.Next == FakeIssue.Invalid)
            {
                return Task.FromResult<Result<NfseDocument>>(Error.Validation(FiscalErrorCodes.Validation, "O Emissor Fiscal recusou o pedido: servico.nbs: informe o NBS.",
                    new Dictionary<string, string[]> { ["servico.nbs"] = ["informe o NBS"] }));
            }

            var id = Guid.CreateVersion7();
            var doc = fake.Next switch
            {
                FakeIssue.Reject => new NfseDocument(id, FiscalDocumentStatus.Rejected, null, null, null, new FiscalRejection(4, "Código do serviço não declarado pelo prestador", "Confira o CodSrv com a contadora.")),
                FakeIssue.Delay => new NfseDocument(id, FiscalDocumentStatus.Processing, null, null, null, null),
                _ => fake.AuthorizedNfse(id),
            };
            fake._nfse[id] = doc;
            fake._nfseByIdempotency[idempotencyKey] = id;
            return Task.FromResult(fake.Next == FakeIssue.CrashAfterCreate
                ? Error.Unavailable(FiscalErrorCodes.Unavailable, "O Emissor Fiscal não respondeu a tempo.")
                : Result<NfseDocument>.Success(doc));
        }

        public Task<Result<NfseDocument>> GetNfseAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            Track("nfse_consultar");
            return Task.FromResult(fake.Unavailable<NfseDocument>() ?? (fake._nfse.TryGetValue(documentId, out var doc)
                ? Result<NfseDocument>.Success(doc)
                : Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o documento.")));
        }

        /// <summary>A prefeitura processou o lote: a NFS-e em processamento sai autorizada (ou rejeitada, se o teste pediu).</summary>
        public Task<Result<NfseDocument>> ForceConsultNfseAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            Track("nfse_consulta_forcada");
            if (fake.Unavailable<NfseDocument>() is { } down)
            {
                return Task.FromResult(down);
            }

            if (fake.NoForceConsult || !fake._nfse.TryGetValue(documentId, out var doc))
            {
                return Task.FromResult<Result<NfseDocument>>(Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o documento."));
            }

            if (doc.Status == FiscalDocumentStatus.Processing)
            {
                doc = fake.AuthorizedNfse(documentId);
                fake._nfse[documentId] = doc;
            }

            return Task.FromResult(Result<NfseDocument>.Success(doc));
        }

        public Task<Result<FiscalFile>> GetNfseXmlAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            Track("nfse_xml");
            if (!fake._nfse.TryGetValue(documentId, out var doc) || doc.Status != FiscalDocumentStatus.Authorized)
            {
                return Task.FromResult<Result<FiscalFile>>(Error.NotFound(FiscalErrorCodes.NotFound, "O Emissor Fiscal não encontrou o documento."));
            }

            var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Nfse><Numero>" + doc.Number + "</Numero><CodigoVerificacao>" + doc.VerificationCode + "</CodigoVerificacao></Nfse>";
            return Task.FromResult(Result<FiscalFile>.Success(new FiscalFile(Encoding.UTF8.GetBytes(xml), "application/xml", documentId + "-nfse.xml")));
        }

        public Task<Result<FiscalFile>> GetNfsePdfAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            Track("nfse_pdf");
            return Task.FromResult(fake._nfse.TryGetValue(documentId, out var doc) && doc.Status == FiscalDocumentStatus.Authorized
                ? Result<FiscalFile>.Success(new FiscalFile("%PDF-1.7 nfse"u8.ToArray(), "application/pdf", documentId + "-nfse.pdf"))
                : Error.NotFound(FiscalErrorCodes.NotFound, "Esta NFS-e não tem link de impressão da prefeitura."));
        }
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
