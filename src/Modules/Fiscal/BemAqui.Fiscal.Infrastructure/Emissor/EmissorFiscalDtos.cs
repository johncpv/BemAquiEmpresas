using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;

namespace BemAqui.Fiscal.Infrastructure.Emissor;

// Contrato HTTP do EmissorFiscal (docs/integracao/README.md de lá, lido em 26/10/2026), escrito do lado do BemAqui: nenhum tipo de lá é
// referenciado nem copiado (ADR 0027 §1). JSON em camelCase, enums numéricos com os códigos da SEFAZ.

internal sealed record EfNfeRequest(
    string Emitente,
    int Serie,
    string NaturezaOperacao,
    int TipoOperacao,
    int Finalidade,
    bool ConsumidorFinal,
    int IndicadorPresenca,
    int ModalidadeFrete,
    EfDestinatario Destinatario,
    IReadOnlyList<EfItem> Itens,
    IReadOnlyList<EfPagamento> Pagamentos,
    string? InformacoesComplementares,
    string? InformacoesFisco,
    IReadOnlyList<string> NotasReferenciadas,
    DateTimeOffset? DataEmissao);

internal sealed record EfDestinatario(string? Cnpj, string? Cpf, string RazaoSocial, int IndicadorInscricaoEstadual, string? InscricaoEstadual, string? Email, EfEndereco Endereco);

internal sealed record EfEndereco(string Logradouro, string Numero, string? Complemento, string Bairro, string CodigoMunicipio, string Municipio, string Uf, string Cep, string? Telefone);

internal sealed record EfItem(
    string Codigo,
    string Descricao,
    string Ncm,
    string? Cest,
    string Cfop,
    string UnidadeComercial,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal,
    decimal ValorDesconto,
    decimal ValorFrete,
    decimal ValorSeguro,
    decimal ValorOutrasDespesas,
    string? CodigoBarras,
    bool CompoeTotal,
    string? NumeroPedido,
    EfImpostos Impostos);

internal sealed record EfImpostos(int Origem, EfIcms Icms, EfPisCofins Pis, EfPisCofins Cofins, EfIpi? Ipi, EfIbsCbs? IbsCbs);

internal sealed record EfIcms(
    string Cst,
    int ModalidadeBaseCalculo,
    decimal BaseCalculo,
    decimal Aliquota,
    decimal Valor,
    decimal? PercentualReducaoBase,
    decimal? BaseCalculoSt,
    decimal? AliquotaSt,
    decimal? ValorSt,
    decimal? MargemValorAgregadoSt,
    decimal? BaseCalculoFcp,
    decimal? AliquotaFcp,
    decimal? ValorFcp);

internal sealed record EfPisCofins(string Cst, decimal BaseCalculo, decimal Aliquota, decimal Valor);

internal sealed record EfIpi(string Cst, string CodigoEnquadramento, decimal BaseCalculo, decimal Aliquota, decimal Valor);

internal sealed record EfIbsCbs(string Cst, string ClassificacaoTributaria, decimal? BaseCalculo, EfAliquota? IbsUf, EfAliquota? IbsMunicipio, EfAliquota? Cbs);

internal sealed record EfAliquota(decimal Aliquota, decimal Valor);

internal sealed record EfPagamento(int Meio, decimal Valor);

internal sealed record EfRejeicao(int Codigo, string? Motivo, string? AcaoSugerida);

internal sealed record EfNfeResponse(Guid Id, int Situacao, string? Chave, int Serie, long Numero, string? Protocolo, DateTimeOffset? AutorizadaEm, EfRejeicao? Rejeicao, DateTimeOffset CriadaEm);

internal sealed record EfPrevia(string? ChaveDeAcesso, long Numero, int Serie, IReadOnlyList<string>? Avisos);

internal sealed record EfCertificado(string? Cnpj, bool Possui, string? Thumbprint, string? Titular, DateTimeOffset? ValidoDesde, DateTimeOffset? ValidoAte, int? DiasRestantes, bool Vencido);

internal sealed record EfEmitente(string Cnpj, string? RazaoSocial, string? Uf, string? CodigoMunicipio, string? Ambiente, bool Ativo, EfCertificado? Certificado);

internal sealed record EfMe(Guid ClienteId, string? Nome, Guid TenantId, IReadOnlyList<string>? Escopos, bool LicencaBloqueada, int? LimitePorMinuto, IReadOnlyList<EfEmitente>? Emitentes);

internal sealed record EfEvento(Guid Id, int Tipo, bool Aceito, string? Protocolo, int? Codigo, string? Motivo, string? AcaoSugerida, DateTimeOffset RegistradoEm);

internal sealed record EfInutilizacaoRequest(string Emitente, int Serie, long NumeroInicial, long NumeroFinal, string Justificativa);

internal sealed record EfInutilizacao(bool Aceito, int Serie, long NumeroInicial, long NumeroFinal, string? Protocolo, int? Codigo, string? Motivo, string? AcaoSugerida);

internal sealed record EfSefazStatus(bool Online, string? Uf, string? Ambiente, string? Mensagem, int? TempoMedioResposta, string? ContingenciaSugerida, DateTimeOffset ConsultadoEm);

internal sealed record EfCertificadoEnviado(Guid Id, string? Thumbprint, DateTimeOffset NotAfter);

internal sealed record EfNfseRequest(string Emitente, string Serie, EfDestinatario Tomador, EfServico Servico, string? Informacoes, string? NfeVinculada);

internal sealed record EfServico(
    string ItemListaServico,
    string CodigoTributacaoMunicipio,
    string CodigoTributacaoNacional,
    string Nbs,
    string Discriminacao,
    string CodigoMunicipioPrestacao,
    decimal ValorServicos,
    decimal BaseCalculo,
    decimal AliquotaIss,
    decimal ValorIss,
    bool IssRetido,
    decimal? ValorInss,
    decimal ValorLiquido,
    EfNfseIbsCbs? IbsCbs);

internal sealed record EfNfseIbsCbs(string Cst, string ClassificacaoTributaria, string CodigoOperacao);

internal sealed record EfNfseResponse(Guid Id, int Situacao, string? Numero, string? CodigoVerificacao, string? LinkImpressao, EfRejeicao? Rejeicao);

internal sealed record EfProblem(string? Title, int? Status, string? Detail);

/// <summary>Tradução BemAqui ↔ Emissor (CA 4): o pedido já normalizado (<see cref="NfePayload"/>) e as situações numéricas.</summary>
internal static class EmissorFiscalMapping
{
    public static EfNfeRequest ToEmissor(NfeIssueRequest r) => new(
        r.IssuerDocument,
        r.Series,
        r.OperationNature,
        (int)r.OperationType,
        (int)r.Purpose,
        r.FinalConsumer,
        (int)r.Presence,
        (int)r.FreightMode,
        ToEmissor(r.Recipient),
        r.Items.Select(ToEmissor).ToList(),
        r.Payments.Select(p => new EfPagamento((int)p.Method, p.Amount)).ToList(),
        r.AdditionalInformation,
        r.TaxAuthorityInformation,
        r.ReferencedAccessKeys ?? [],
        r.IssuedAt);

    public static EfDestinatario ToEmissor(NfeRecipient d) => new(d.Cnpj, d.Cpf, d.Name, (int)d.StateRegistrationIndicator, d.StateRegistration, d.Email,
        new EfEndereco(d.Address.Street, d.Address.Number, d.Address.Complement, d.Address.District, d.Address.CityCode, d.Address.CityName, d.Address.State, d.Address.PostalCode, d.Address.Phone));

    private static EfItem ToEmissor(NfeItem i) => new(
        i.Code, i.Description, i.Ncm, i.Cest, i.Cfop, i.Unit, i.Quantity, i.UnitPrice, i.Total, i.Discount, i.Freight, i.Insurance, i.OtherCosts, i.Gtin, i.ComposesTotal, i.CustomerOrderNumber,
        new EfImpostos(
            i.Taxes.Origin,
            new EfIcms(i.Taxes.Icms.Cst, i.Taxes.Icms.BaseMode, i.Taxes.Icms.Base, i.Taxes.Icms.Rate, i.Taxes.Icms.Amount, i.Taxes.Icms.BaseReduction, i.Taxes.Icms.StBase, i.Taxes.Icms.StRate,
                i.Taxes.Icms.StAmount, i.Taxes.Icms.StMargin, i.Taxes.Icms.FcpBase, i.Taxes.Icms.FcpRate, i.Taxes.Icms.FcpAmount),
            new EfPisCofins(i.Taxes.Pis.Cst, i.Taxes.Pis.Base, i.Taxes.Pis.Rate, i.Taxes.Pis.Amount),
            new EfPisCofins(i.Taxes.Cofins.Cst, i.Taxes.Cofins.Base, i.Taxes.Cofins.Rate, i.Taxes.Cofins.Amount),
            i.Taxes.Ipi is { } ipi ? new EfIpi(ipi.Cst, ipi.LegalFramework, ipi.Base, ipi.Rate, ipi.Amount) : null,
            i.Taxes.IbsCbs is { } ibs ? new EfIbsCbs(ibs.Cst, ibs.Classification, ibs.Base, Rate(ibs.StateIbs), Rate(ibs.CityIbs), Rate(ibs.Cbs)) : null));

    private static EfAliquota? Rate(NfeTaxRate? rate) => rate is null ? null : new EfAliquota(rate.Rate, rate.Amount);

    /// <summary>Situação numérica do Emissor (0 a 6); desconhecida fica como "em processamento" (nunca vira desfecho por engano).</summary>
    public static FiscalDocumentStatus Status(int situacao) => situacao switch
    {
        0 => FiscalDocumentStatus.Received,
        1 => FiscalDocumentStatus.Processing,
        2 => FiscalDocumentStatus.Authorized,
        3 => FiscalDocumentStatus.Rejected,
        4 => FiscalDocumentStatus.Cancelled,
        5 => FiscalDocumentStatus.Denied,
        6 => FiscalDocumentStatus.ContingencyPending,
        _ => FiscalDocumentStatus.Processing,
    };

    public static NfeDocument ToDocument(EfNfeResponse r) => new(r.Id, Status(r.Situacao), r.Chave, r.Serie, r.Numero, r.Protocolo, r.AutorizadaEm,
        r.Rejeicao is { } rj ? new FiscalRejection(rj.Codigo, rj.Motivo ?? string.Empty, rj.AcaoSugerida) : null, r.CriadaEm);

    public static EmitterAccount ToAccount(EfMe me) => new(me.ClienteId, me.Nome ?? string.Empty, me.TenantId, me.Escopos ?? [], me.LicencaBloqueada, me.LimitePorMinuto,
        (me.Emitentes ?? []).Select(e => new EmitterIssuer(e.Cnpj, e.RazaoSocial ?? string.Empty, e.Uf ?? string.Empty, e.CodigoMunicipio ?? string.Empty,
            string.Equals(e.Ambiente, "producao", StringComparison.OrdinalIgnoreCase) ? FiscalEnvironment.Production : FiscalEnvironment.Homologation,
            e.Ativo,
            e.Certificado is { } c ? new EmitterCertificate(c.Possui, c.Thumbprint, c.Titular, c.ValidoDesde, c.ValidoAte, c.DiasRestantes, c.Vencido) : new EmitterCertificate(false, null, null, null, null, null, false))).ToList());

    public static NfeEventResult ToEvent(EfEvento e) => new(e.Id, (NfeEventType)e.Tipo, e.Aceito, e.Protocolo, e.Codigo, e.Motivo, e.AcaoSugerida, e.RegistradoEm);

    public static EfNfseRequest ToEmissor(NfseIssueRequest r) => new(r.IssuerDocument, r.Series, ToEmissor(r.Taker),
        new EfServico(r.Service.ServiceListItem, r.Service.MunicipalTaxCode, r.Service.NationalTaxCode, r.Service.Nbs, r.Service.Description, r.Service.ServiceCityCode,
            NfePayload.Money(r.Service.ServicesAmount), NfePayload.Money(r.Service.Base), NfePayload.Rate(r.Service.IssRate), NfePayload.Money(r.Service.IssAmount), r.Service.IssWithheld,
            NfePayload.Money(r.Service.InssAmount), NfePayload.Money(r.Service.NetAmount),
            r.Service.IbsCbs is { } ibs ? new EfNfseIbsCbs(ibs.Cst, ibs.Classification, ibs.OperationCode) : null),
        r.Notes, r.LinkedNfeAccessKey);

    public static NfseDocument ToDocument(EfNfseResponse r) => new(r.Id, Status(r.Situacao), r.Numero, r.CodigoVerificacao, r.LinkImpressao,
        r.Rejeicao is { } rj ? new FiscalRejection(rj.Codigo, rj.Motivo ?? string.Empty, rj.AcaoSugerida) : null);
}
