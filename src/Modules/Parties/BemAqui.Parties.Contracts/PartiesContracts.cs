using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Parties.Contracts;

/// <summary>Pessoa física ou jurídica.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyKind>))]
public enum PartyKind
{
    PF,
    PJ,
}

/// <summary>Papel do terceiro (R-TER-1): um mesmo documento pode ter vários.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyRoleKind>))]
public enum PartyRoleKind
{
    Customer,
    Supplier,
    Installer,
    Carrier,

    /// <summary>Empresa da maquininha de cartão (PAG-01): a forma de pagamento aponta para ela; o CNPJ serve ao grupo de cartão da NFC-e.</summary>
    Acquirer,
}

[JsonConverter(typeof(JsonStringEnumConverter<AddressKind>))]
public enum AddressKind
{
    Billing,
    Delivery,
    Site,
}

[JsonConverter(typeof(JsonStringEnumConverter<TaxRegime>))]
public enum TaxRegime
{
    NotInformed,
    SimplesNacional,
    Mei,
    LucroPresumido,
    LucroReal,
    Exempt,
}

/// <summary>Resumo para outros módulos (CAD-01a CA 21). <c>Document</c> é o normalizado, para o documento fiscal.</summary>
/// <param name="DefaultPaymentTermId">Condição de pagamento padrão (CAD-05b CA 16), para o documento vir preenchido (R-PRP-1).</param>
public sealed record PartySummary(Guid Id, PartyKind Kind, string LegalName, string? TradeName, string? Document, IReadOnlyList<PartyRoleKind> Roles, bool IsActive, bool IsDeleted, Guid? DefaultPaymentTermId = null, DateTimeOffset? CreatedAt = null);

public sealed record PartyAddressInfo(Guid Id, AddressKind Kind, string ZipCode, string Street, string Number, string? Complement, string District, string City, string? IbgeCode, string State);

public sealed record PartyContactInfo(Guid Id, string Name, string? Role, string? Email, string? Phone, bool ReceivesBilling);

/// <summary>
/// Terceiros para os outros módulos (Commercial, Purchasing, Finance, Inventory): a única porta de entrada, nunca a
/// tabela. A busca devolve só ativos; o resumo por id devolve até excluído (documento antigo continua mostrando o nome).
/// </summary>
public interface IParties
{
    Task<PartySummary?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Vários por id numa consulta só, inclusive excluídos (nomes nas listas por página, ADR 0019 §13).</summary>
    Task<IReadOnlyList<PartySummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartySummary>> SearchAsync(string? term, PartyRoleKind? role, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// O terceiro do documento (FIS-04a CA 6: o emitente da nota de entrada pelo CNPJ), ativo ou inativo; excluído não volta. Documento
    /// inválido ou sem cadastro: null.
    /// </summary>
    Task<PartySummary?> FindByDocumentAsync(string document, CancellationToken cancellationToken = default);

    /// <summary>
    /// O terceiro de um pacote de dados iniciais do perfil (PERF-04: a adquirente <c>maquininha</c>), se ainda existe (excluído: null).
    /// </summary>
    Task<PartySummary?> FindBySeedKeyAsync(string seedKey, CancellationToken cancellationToken = default) => Task.FromResult<PartySummary?>(null);

    Task<bool> HasRoleAsync(Guid id, PartyRoleKind role, CancellationToken cancellationToken = default);

    Task<PartyAddressInfo?> DefaultAddressAsync(Guid id, AddressKind kind, CancellationToken cancellationToken = default);

    Task<PartyContactInfo?> BillingContactAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Contatos do terceiro (seletor do orçamento, COM-01a CA 4 e 5).</summary>
    Task<IReadOnlyList<PartyContactInfo>> ContactsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Endereços do terceiro (endereço da obra do orçamento).</summary>
    Task<IReadOnlyList<PartyAddressInfo>> AddressesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Endereços do terceiro com município e UF e sem o código IBGE recebem o código pelo nome do município e ficam gravados no cadastro
    /// (rascunho da NF-e). Sem resposta do provedor, nada muda. Devolve quantos foram preenchidos.
    /// </summary>
    Task<int> FillIbgeCodesAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(0);

    /// <summary>
    /// Dados fiscais do terceiro para o destinatário da nota (FIS-03a CA 1): tipo, documento, razão social, IE, e-mail e telefone do
    /// cadastro. Dado pessoal: só para montar o documento fiscal, nunca para evento ou log.
    /// </summary>
    async Task<PartyFiscalInfo?> FiscalInfoAsync(Guid id, CancellationToken cancellationToken = default) =>
        await FindAsync(id, cancellationToken) is { } p ? new PartyFiscalInfo(p.Id, p.Kind, p.LegalName, p.Document, null, null, null) : null;

    /// <summary>
    /// Terceiros ativos pelo telefone (INT-03 CA 7): os 10 ou 11 dígitos com DDD, no telefone do cadastro ou no de um contato; até 10, nome em
    /// ordem. Telefone inválido: lista vazia.
    /// </summary>
    Task<IReadOnlyList<PartySummary>> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PartySummary>>([]);

    /// <summary>
    /// O telefone que achou o terceiro é o do cadastro gravado por aplicativo e ainda não confirmado no ERP (INT-04 CA 11): o Conecta não o aceita como
    /// prova de identidade. Telefone de um contato do terceiro (cadastrado no ERP) não conta como de aplicativo.
    /// </summary>
    Task<bool> PhoneSetByAppAsync(Guid id, string phone, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// Completa o contato pelo aplicativo conectado (INT-03 CA 11): e-mail, telefone e endereço de cobrança <b>só preenchem campo vazio</b>; valor igual
    /// ao gravado não muda nada; trocar um valor existente é só no ERP, por pessoa (422 <c>field_change_requires_backoffice</c>). O documento nunca
    /// muda por aqui. Não existe ou excluído: 404.
    /// </summary>
    Task<Result> FillEmptyContactAsync(Guid id, PartyContactFill fill, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(Error.NotFound(Error.Codes.NotFound, "Terceiro não encontrado.")));
}

/// <summary>O que o aplicativo pode completar no cadastro (INT-03 CA 11): e-mail, telefone e o endereço de cobrança.</summary>
public sealed record PartyContactFill(string? Email, string? Phone, PartyAddressFill? BillingAddress);

/// <summary>Endereço de cobrança vindo do aplicativo: CEP, rua, número, complemento, bairro, cidade e UF (o código IBGE o ERP completa).</summary>
public sealed record PartyAddressFill(string ZipCode, string Street, string Number, string? Complement, string District, string City, string State);

/// <summary>Destinatário da nota fiscal (FIS-03a): tipo, razão social, documento normalizado, IE, e-mail e telefone do cadastro.</summary>
public sealed record PartyFiscalInfo(Guid Id, PartyKind Kind, string LegalName, string? Document, string? StateRegistration, string? Email, string? Phone);

/// <summary>
/// Quem usa um terceiro (CAD-01a CA 20): cada módulo que guarda o id de um terceiro implementa e registra no DI.
/// Em uso, o terceiro não é excluído, só inativado.
/// </summary>
public interface IPartyUsageProvider
{
    /// <summary>Descrição curta do uso ("código de fornecedor", "orçamentos") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid partyId, CancellationToken cancellationToken = default);
}
