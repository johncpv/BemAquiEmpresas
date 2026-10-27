using System.Text.Json;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Domain;
using BemAqui.Fiscal.Infrastructure.Emissor;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// FIS-01 (ADR 0027): integração por serviço HTTP sem código do Emissor (CA 1), chave e tentativa, <c>payload_hash</c> e reenvio (CA 5 e
/// 6), mapeamento do pedido e da resposta (CA 4), conferência de escopos, CNPJ e ambiente (CA 8 e 9) e a URL só da lista (CA 7).
/// </summary>
public class FiscalRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private const string Key = "35261047225005000186550020000000421234567890";
    private static readonly Guid Doc = Guid.Parse("019fca35-b0f8-7f92-aca0-b13de0f8088d");

    public static NfeIssueRequest Sample(decimal quantity = 1m, decimal total = 1000m, string nature = "VENDA DE MERCADORIA") => new(
        "47225005000186", 2, nature, NfeOperationType.Outbound, NfePurpose.Normal, false, NfePresence.InPerson, NfeFreightMode.NoTransport,
        new NfeRecipient("11222333000181", null, "CLIENTE DE TESTE LTDA", NfeStateRegistration.Taxpayer, "111111111111", "fiscal@exemplo.com.br",
            new NfeAddress("RUA DO DESTINATARIO", "200", null, "CENTRO", "3550308", "SAO PAULO", "SP", "01001000")),
        [new NfeItem("ELEV-001", "ELEVADOR DE CARGA", "84281000", null, "5101", "UN", quantity, total / quantity, total,
            new NfeItemTaxes(0, new NfeIcms("00", total, 18m, total * 0.18m), new NfePisCofins("01", total, 0.65m, total * 0.0065m), new NfePisCofins("01", total, 3m, total * 0.03m),
                IbsCbs: new NfeIbsCbs("000", "000001", total, new NfeTaxRate(0.10m, total * 0.001m), new NfeTaxRate(0m, 0m), new NfeTaxRate(0.90m, total * 0.009m))))],
        [new NfePayment(NfePaymentMethod.BankDeposit, total)]);

    private static NfeDocument Remote(FiscalDocumentStatus status, Guid? doc = null, int? rejection = null) =>
        new(doc ?? Doc, status, Key, 2, 42, status == FiscalDocumentStatus.Authorized ? "135260000000001" : null, status == FiscalDocumentStatus.Authorized ? Now : null,
            rejection is { } code ? new FiscalRejection(code, "Rejeicao: teste", "Corrija e emita de novo.") : null, Now);

    // ---------- CA 1 ----------

    [Fact]
    public void Nenhum_assembly_do_EmissorFiscal_da_Unimake_ou_da_DANFE_e_referenciado()
    {
        var forbidden = new[] { "EmissorFiscal", "Unimake", "Zeus", "NFe.Danfe" };
        var offenders = Solution.Assemblies
            .SelectMany(a => a.GetReferencedAssemblies().Select(r => (Assembly: a.GetName().Name, Reference: r.Name ?? string.Empty)))
            .Where(x => forbidden.Any(f => x.Reference.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.Assembly + " → " + x.Reference)
            .ToList();
        offenders.Should().BeEmpty("a integração é por serviço HTTP (ADR 0027 §1)");
        Solution.Projects.SelectMany(p => p.PackageReferences).Should().NotContain(p => p.StartsWith("Unimake", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Zeus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Provedor_emissor_fiscal_e_do_modulo_fiscal_com_timestamp_fila_fiscal_e_cabecalhos_do_Emissor()
    {
        var provider = EmissorFiscalWebhook.Provider;

        WebhookProviderRegistry.Problems(provider).Should().BeEmpty();
        provider.Validator.Should().BeOfType<TimestampedSignatureValidator>().Which.Window.Should().Be(TimeSpan.FromMinutes(5));
        provider.Queue.Should().Be(JobQueues.Fiscal);
        provider.DeliveryIdHeader.Should().Be("X-BemAqui-Delivery-Id");
        provider.EventTypeHeader.Should().Be("X-BemAqui-Event");
        provider.Validator.Should().BeOfType<TimestampedSignatureValidator>().Which.SignatureHeader.Should().Be("X-BemAqui-Signature");
        provider.RotationInSettings.Should().BeFalse("o segredo é trocado na tela do Emissor fiscal, com o teste da chave");
    }

    // ---------- CA 5 e 6 ----------

    [Fact]
    public void Chave_de_idempotencia_e_nfe_id_tentativa()
    {
        var invoice = OutgoingInvoice.Create(1000m, Now);

        invoice.IdempotencyKey.Should().Be("nfe-" + invoice.Id.ToString("D") + "-1");
    }

    [Fact]
    public void Entrar_em_Processing_grava_o_hash_e_o_reenvio_so_aceita_o_mesmo_payload()
    {
        var invoice = OutgoingInvoice.Create(1000m, Now);
        var hash = NfePayload.Hash(Sample());

        invoice.BeginSubmission(hash, Now).Should().BeNull();
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Processing);
        invoice.PayloadHash.Should().Be(hash).And.HaveLength(64);
        invoice.ProcessingSince.Should().Be(Now);

        invoice.BeginSubmission(hash, Now.AddMinutes(1)).Should().BeNull("rede caiu: a mesma chave e o mesmo payload");
        invoice.IdempotencyKey.Should().EndWith("-1");
        var changed = invoice.BeginSubmission(NfePayload.Hash(Sample(total: 1001m)), Now);
        changed.Should().NotBeNull();
        changed!.Code.Should().Be(FiscalErrorCodes.PayloadChanged);
        changed.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void Tentativa_so_sobe_depois_de_rejeicao()
    {
        var invoice = OutgoingInvoice.Create(1000m, Now);
        invoice.BeginSubmission(NfePayload.Hash(Sample()), Now);
        invoice.Apply(Remote(FiscalDocumentStatus.Rejected, rejection: 225).ToState(), Now).Should().BeTrue();
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Rejected);
        invoice.RejectionCode.Should().Be(225);

        invoice.BeginSubmission(NfePayload.Hash(Sample(total: 1001m)), Now).Should().BeNull("a correção é nova emissão");

        invoice.Attempt.Should().Be(2);
        invoice.IdempotencyKey.Should().EndWith("-2");
        invoice.EmitterDocumentId.Should().BeNull();
        invoice.RejectionCode.Should().BeNull();
    }

    [Fact]
    public void Contingencia_nunca_reemite_e_nota_com_desfecho_nao_volta()
    {
        var invoice = OutgoingInvoice.Create(1000m, Now);
        var hash = NfePayload.Hash(Sample());
        invoice.BeginSubmission(hash, Now);
        invoice.Apply(Remote(FiscalDocumentStatus.ContingencyPending).ToState(), Now);

        invoice.Status.Should().Be(OutgoingInvoiceStatus.ContingencyPending);
        invoice.ContingencySince.Should().Be(Now);
        invoice.BeginSubmission(hash, Now)!.Code.Should().Be(FiscalErrorCodes.ContingencyPending);

        invoice.Apply(Remote(FiscalDocumentStatus.Authorized).ToState(), Now.AddHours(1)).Should().BeTrue();
        invoice.ContingencySince.Should().BeNull();
        invoice.BeginSubmission(hash, Now)!.Code.Should().Be(FiscalErrorCodes.InvoiceClosed);
        invoice.Apply(Remote(FiscalDocumentStatus.Processing).ToState(), Now).Should().BeFalse("nunca regride");
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Authorized);
        invoice.Apply(Remote(FiscalDocumentStatus.Authorized).ToState(), Now).Should().BeFalse("o mesmo estado duas vezes não muda nada");
        invoice.Apply(Remote(FiscalDocumentStatus.Cancelled).ToState(), Now).Should().BeTrue();
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Cancelled);
    }

    [Fact]
    public void Documento_de_outra_tentativa_nao_muda_a_nota()
    {
        var invoice = OutgoingInvoice.Create(1000m, Now);
        invoice.BeginSubmission(NfePayload.Hash(Sample()), Now);
        invoice.Apply(Remote(FiscalDocumentStatus.Processing).ToState(), Now);

        invoice.Apply(Remote(FiscalDocumentStatus.Authorized, doc: Guid.CreateVersion7()).ToState(), Now).Should().BeFalse();

        invoice.Status.Should().Be(OutgoingInvoiceStatus.Processing);
    }

    [Fact]
    public void Recusa_antes_de_criar_volta_para_rascunho_com_a_mesma_tentativa()
    {
        var invoice = OutgoingInvoice.Create(1000m, Now);
        invoice.BeginSubmission(NfePayload.Hash(Sample()), Now);

        invoice.SubmissionNotCreated();

        invoice.Status.Should().Be(OutgoingInvoiceStatus.Draft);
        invoice.PayloadHash.Should().BeNull();
        invoice.Attempt.Should().Be(1);
    }

    [Theory]
    [InlineData(FiscalErrorCodes.Validation, SubmissionOutcome.NotCreated)]
    [InlineData(FiscalErrorCodes.IntegrationMisconfigured, SubmissionOutcome.NotCreated)]
    [InlineData(FiscalErrorCodes.RateLimited, SubmissionOutcome.NotCreated)]
    [InlineData(FiscalErrorCodes.Unavailable, SubmissionOutcome.Uncertain)]
    [InlineData(FiscalErrorCodes.Conflict, SubmissionOutcome.Uncertain)]
    public void Falha_depois_do_POST_nunca_gera_chave_nova(string code, SubmissionOutcome expected)
    {
        NfeSubmissionService.Classify(new Error(code, "x", ErrorType.Unavailable)).Should().Be(expected);
    }

    // ---------- CA 4 ----------

    [Fact]
    public void Hash_do_payload_e_estavel_com_1_ou_1_00_e_muda_com_o_conteudo()
    {
        var a = NfePayload.Hash(Sample(quantity: 1m, total: 1000m));
        var b = NfePayload.Hash(Sample(quantity: 1.0000m, total: 1000.00m));

        a.Should().Be(b);
        NfePayload.Hash(Sample(total: 1000.01m)).Should().NotBe(a);
        NfePayload.Hash(Sample() with { Recipient = Sample().Recipient with { Name = "OUTRO CLIENTE" } }).Should().NotBe(a);
    }

    [Fact]
    public void Valores_com_2_casas_quantidades_com_4_e_enums_com_os_codigos_da_SEFAZ()
    {
        var request = Sample(quantity: 3m, total: 100m) with { Items = [Sample().Items[0] with { Quantity = 1.23456m, UnitPrice = 81.000049m, Total = 100.005m }] };

        var wire = EmissorFiscalMapping.ToEmissor(NfePayload.Normalize(request));
        var json = JsonDocument.Parse(JsonSerializer.Serialize(wire, EmissorFiscalHttpEmitter.Json)).RootElement;

        json.GetProperty("tipoOperacao").GetInt32().Should().Be(1, "saída é 1 (L-l)");
        EmissorFiscalMapping.ToEmissor(NfePayload.Normalize(request with { OperationType = NfeOperationType.Inbound })).TipoOperacao.Should().Be(0);
        json.GetProperty("modalidadeFrete").GetInt32().Should().Be(9);
        json.GetProperty("indicadorPresenca").GetInt32().Should().Be(1);
        json.GetProperty("finalidade").GetInt32().Should().Be(1);
        json.GetProperty("pagamentos")[0].GetProperty("meio").GetInt32().Should().Be(16);
        var item = json.GetProperty("itens")[0];
        item.GetProperty("quantidade").GetDecimal().Should().Be(1.2346m);
        item.GetProperty("valorUnitario").GetDecimal().Should().Be(81.0000m);
        item.GetProperty("valorTotal").GetDecimal().Should().Be(100.01m);
        item.GetProperty("impostos").GetProperty("ibsCbs").GetProperty("classificacaoTributaria").GetString().Should().Be("000001");
        json.GetProperty("destinatario").GetProperty("indicadorInscricaoEstadual").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData(0, FiscalDocumentStatus.Received)]
    [InlineData(1, FiscalDocumentStatus.Processing)]
    [InlineData(2, FiscalDocumentStatus.Authorized)]
    [InlineData(3, FiscalDocumentStatus.Rejected)]
    [InlineData(4, FiscalDocumentStatus.Cancelled)]
    [InlineData(5, FiscalDocumentStatus.Denied)]
    [InlineData(6, FiscalDocumentStatus.ContingencyPending)]
    [InlineData(99, FiscalDocumentStatus.Processing)]
    public void Situacao_numerica_do_Emissor_vira_a_do_BemAqui(int situacao, FiscalDocumentStatus expected) =>
        EmissorFiscalMapping.Status(situacao).Should().Be(expected);

    [Fact]
    public void Detalhe_do_400_vira_erro_campo_a_campo()
    {
        var fields = EmissorFiscalHttpEmitter.Fields("emitente: CNPJ inválido. naturezaOperacao: obrigatória. itens: informe ao menos um item.");

        fields.Should().NotBeNull();
        fields!["emitente"].Should().Equal("CNPJ inválido");
        fields["naturezaOperacao"].Should().Equal("obrigatória");
        fields["itens"].Should().Equal("informe ao menos um item");
        EmissorFiscalHttpEmitter.Fields("Requisição inválida").Should().BeNull();
    }

    // ---------- CA 8 e 9 ----------

    private static EmitterAccount Account(string[] scopes, string document = "47225005000186", FiscalEnvironment environment = FiscalEnvironment.Homologation) =>
        new(Guid.CreateVersion7(), "BemAqui Empresas — tenant demo", Guid.CreateVersion7(), scopes, false, 120,
            [new EmitterIssuer(document, "WSP Equipamentos", "SP", "3508504", environment, true, new EmitterCertificate(true, "AB12", "WSP", Now.AddYears(-1), Now.AddMonths(5), 140, false))]);

    [Fact]
    public void Chave_com_admin_e_recusada()
    {
        var verdict = FiscalAccountCheck.Evaluate(Account(["admin", "nfe:emitir", "nfe:eventos", "consultar"]), "47.225.005/0001-86", FiscalEnvironmentAllowance.Homologation);

        verdict.Ok.Should().BeFalse();
        verdict.Result.Should().Be(FiscalTestResults.AdminScope);
        verdict.Message.Should().Be("Use uma chave sem o escopo admin.");
    }

    [Fact]
    public void Escopo_faltando_e_listado()
    {
        var verdict = FiscalAccountCheck.Evaluate(Account(["nfe:emitir"]), "47225005000186", FiscalEnvironmentAllowance.Homologation);

        verdict.Result.Should().Be(FiscalTestResults.MissingScopes);
        verdict.Message.Should().Be("Faltam os escopos nfe:eventos, consultar nesta chave.");
    }

    [Fact]
    public void Emitente_com_outro_CNPJ_e_recusado()
    {
        var verdict = FiscalAccountCheck.Evaluate(Account(FiscalScopes.Required, document: "11222333000181"), "47225005000186", FiscalEnvironmentAllowance.Homologation);

        verdict.Result.Should().Be(FiscalTestResults.IssuerMismatch);
        verdict.Message.Should().Be("O emitente do Emissor Fiscal não é o CNPJ desta empresa.");
    }

    [Theory]
    [InlineData(FiscalEnvironmentAllowance.None, FiscalEnvironment.Homologation, false)]
    [InlineData(FiscalEnvironmentAllowance.Homologation, FiscalEnvironment.Homologation, true)]
    [InlineData(FiscalEnvironmentAllowance.Homologation, FiscalEnvironment.Production, false)]
    [InlineData(FiscalEnvironmentAllowance.Production, FiscalEnvironment.Production, true)]
    [InlineData(FiscalEnvironmentAllowance.Production, FiscalEnvironment.Homologation, true)]
    public void Ambiente_acima_do_permitido_e_recusado(FiscalEnvironmentAllowance allowance, FiscalEnvironment environment, bool ok)
    {
        var verdict = FiscalAccountCheck.Evaluate(Account(FiscalScopes.Required, environment: environment), "47225005000186", allowance);

        verdict.Ok.Should().Be(ok);
        if (!ok)
        {
            verdict.Message.Should().Be("A emissão desta empresa só liga na virada para a produção.");
        }
        else
        {
            verdict.Snapshot!.IssuerDocument.Should().Be("47225005000186");
            verdict.Message.Should().Be("Conexão testada");
        }
    }

    // ---------- CA 7 ----------

    [Theory]
    [InlineData("https://api.fiscal.bemaquiapp.com.br", true)]
    [InlineData("https://api.fiscal.bemaquiapp.com.br/", true)]
    [InlineData("https://API.FISCAL.BEMAQUIAPP.COM.BR", true)]
    [InlineData("https://api.fiscal.bemaquiapp.com.br:443/", true)]
    [InlineData("http://api.fiscal.bemaquiapp.com.br", false)]
    [InlineData("https://api.fiscal.bemaquiapp.com.br:8443", false)]
    [InlineData("http://emissor-fiscal:8080", false)]
    [InlineData("https://emissor-fiscal", false)]
    [InlineData("https://169.254.169.254/latest", false)]
    [InlineData("https://localhost", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("https://[::1]", false)]
    [InlineData("https://api.fiscal.bemaquiapp.com.br.evil.com", false)]
    [InlineData("https://user:pass.fiscal.bemaquiapp.com.br", false)]
    [InlineData("https://api.fiscal.bemaquiapp.com.br/api/v1", false)]
    [InlineData("https://api.fiscal.bemaquiapp.com.br/?x=1", false)]
    [InlineData("ftp://api.fiscal.bemaquiapp.com.br", false)]
    [InlineData("api.fiscal.bemaquiapp.com.br", false)]
    [InlineData("", false)]
    public void URL_so_aceita_a_API_publica_do_Emissor_em_HTTPS(string url, bool ok)
    {
        var result = FiscalBaseUrl.Validate(url, new FiscalIntegrationOptions());

        result.IsSuccess.Should().Be(ok);
        if (ok)
        {
            result.Value.Should().Be(new Uri("https://api.fiscal.bemaquiapp.com.br/"));
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://api.fiscal.bemaquiapp.com.br", true)]
    [InlineData("http://emissor-fiscal:8080", false)]
    public void Endereco_vem_da_configuracao_e_a_tela_nao_troca(string? requested, bool ok)
    {
        var result = FiscalBaseUrl.Resolve(requested, new FiscalIntegrationOptions());

        result.IsSuccess.Should().Be(ok);
        if (ok)
        {
            result.Value.Should().Be(new Uri("https://api.fiscal.bemaquiapp.com.br/"));
        }
    }

    [Fact]
    public void Configuracao_fora_da_regra_nao_e_usada()
    {
        FiscalBaseUrl.Configured(new FiscalIntegrationOptions { BaseUrl = "http://emissor-fiscal:8080" }).IsFailure.Should().BeTrue("sem HTTPS");
        FiscalBaseUrl.Configured(new FiscalIntegrationOptions { BaseUrl = "https://outro.exemplo.com.br" }).IsFailure.Should().BeTrue("fora da lista");
        FiscalBaseUrl.Configured(new FiscalIntegrationOptions()).Value.Should().Be(new Uri("https://api.fiscal.bemaquiapp.com.br/"));
        new FiscalIntegrationOptions().AllowedHosts.Should().Equal("api.fiscal.bemaquiapp.com.br");
    }
}
