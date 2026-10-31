using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Host;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// CMP-01 e CMP-03a (ADR 0006, ADR 0023 §6, ADR 0024): o Purchasing só conversa com o Estoque, o Comercial, a Produção, o Financeiro e
/// os Terceiros pelos Contracts e nunca lê tabela do Finance; a ação <c>purchasing.receive</c> está no catálogo e é a do recebimento
/// (CMP-04); o aviso <see cref="StockShortageChanged"/> tem o consumidor do Purchasing; a SC e o pedido exigem <c>Idempotency-Key</c>;
/// os eventos do pedido são só aviso (nenhum consumidor cria título).
/// </summary>
public class PurchasingRulesTests
{
    private static string EndpointsDir => Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing", "BemAqui.Purchasing.Endpoints");

    private static string EndpointsText => string.Join("\n", Directory.GetFiles(EndpointsDir, "PurchasingEndpoints*.cs").Select(File.ReadAllText));

    private static IEnumerable<(string File, string Text)> PurchasingSources()
    {
        var root = Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing");
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(root, f), File.ReadAllText(f)));
    }

    [Fact]
    public void Purchasing_so_referencia_os_Contracts_dos_outros_modulos()
    {
        var others = Solution.Projects.Where(p => p.Module == "Purchasing")
            .SelectMany(p => p.ProjectReferences.Select(Solution.Classify))
            .Where(t => t.Module is not null && t.Module != "Purchasing")
            .Select(t => $"BemAqui.{t.Module}.{t.Layer}")
            .Distinct()
            .ToList();
        others.Should().BeEquivalentTo([
            "BemAqui.Inventory.Contracts", "BemAqui.Commercial.Contracts", "BemAqui.Production.Contracts", "BemAqui.Finance.Contracts", "BemAqui.Parties.Contracts",
            "BemAqui.Identity.Contracts", // CMP-03b: dados da empresa para o PDF (ICompanyIdentity) e o limite por usuário (ITenantParameters)
            "BemAqui.Fiscal.Contracts", // FIS-04b: o recebimento pela nota lê a nota conferida e faz a transição dela (IIncomingInvoices)
        ]);
    }

    /// <summary>CMP-03b (ADR 0022): só o layout <c>PurchaseOrderPdfDocument</c> conhece o QuestPDF no Purchasing.</summary>
    [Fact]
    public void So_o_layout_do_pedido_referencia_QuestPDF()
    {
        PurchasingSources().Where(s => s.Text.Contains("QuestPDF", StringComparison.Ordinal)).Select(s => Path.GetFileName(s.File))
            .Should().Equal("PurchaseOrderPdfDocument.cs");
    }

    /// <summary>
    /// CMP-03b CA 1, 2, 4 e 10: baixar o PDF, ver os documentos e um envio com <c>purchasing.view</c>; o diálogo e o envio com
    /// <c>purchasing.edit</c>; o envio com <c>Idempotency-Key</c>; o envio é lido pelo pedido da URL (IDOR); o e-mail é o do limite somado.
    /// </summary>
    [Fact]
    public void Envio_ao_fornecedor_usa_as_acoes_certas_e_o_limite_somado()
    {
        var statements = EndpointsText.Split(';').Where(s => s.Contains(".WithName(\"", StringComparison.Ordinal)).ToList();
        string Of(string name) => statements.Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal));
        Of("DownloadPurchaseOrderPdf").Should().Contain("\"purchasing.view\"");
        Of("GetPurchaseOrderDocuments").Should().Contain("\"purchasing.view\"");
        Of("GetPurchaseOrderDispatch").Should().Contain("\"purchasing.view\"").And.Contain("/{id:guid}/dispatches/{dispatchId:guid}");
        Of("GetPurchaseOrderDispatchDraft").Should().Contain("\"purchasing.edit\"");
        Of("DispatchPurchaseOrder").Should().Contain("\"purchasing.edit\"").And.Contain(".RequireIdempotencyKey()");
        var service = string.Join("\n", Directory.GetFiles(Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing", "BemAqui.Purchasing.Application", "Documents"),
            "PurchaseOrderDispatchService*.cs").Select(File.ReadAllText));
        service.Should().Contain("limiter.CheckAsync(").And.Contain("BeginTransactionAsync").And.Contain("attachments.StoreGeneratedAsync(PurchaseOrderService.AttachmentOwner")
            .And.Contain("EmailDispatchLimiter.PurchaseOrderTemplate").And.Contain("d.Id == dispatchId && d.OrderId == id");
        var quotes = File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Modules", "Commercial", "BemAqui.Commercial.Application", "Documents", "QuoteDocumentService.Send.cs"));
        quotes.Should().Contain("limiter.CheckAsync(").And.NotContain("QuoteDispatches.CountAsync", "a COM-03 conta pela mesma consulta somada (CMP-03b)");
    }

    /// <summary>CMP-03a (ADR 0024 §4): o Purchasing guarda os próprios níveis e decisões; nada lê <c>finance.*</c> nem o banco do Finance.</summary>
    [Fact]
    public void Nada_do_Purchasing_le_tabela_do_Finance()
    {
        var offending = PurchasingSources()
            .Where(s => s.Text.Contains("IFinanceDb", StringComparison.Ordinal) || s.Text.Contains("\"finance.", StringComparison.Ordinal) && s.Text.Contains("ToTable", StringComparison.Ordinal)
                || s.Text.Contains("finance.payables", StringComparison.Ordinal) || s.Text.Contains("finance.approval_rules", StringComparison.Ordinal))
            .Select(s => s.File)
            .ToList();
        offending.Should().BeEmpty();
    }

    [Fact]
    public void Contracts_do_Purchasing_nao_depende_de_outro_modulo() =>
        Solution.Projects.Single(p => p.Name == "BemAqui.Purchasing.Contracts").ProjectReferences
            .Select(Solution.Classify).Where(t => t.Module is not null).Should().BeEmpty();

    /// <summary>CA 1: a ação específica com o texto da ADR 0003 (adendo de 21/10/2026).</summary>
    [Fact]
    public void Catalogo_declara_purchasing_receive()
    {
        var purchasing = ModuleCatalog.Find("purchasing")!;
        purchasing.HasAction("receive").Should().BeTrue();
        purchasing.Specific.Single(a => a.Code == "receive").Description
            .Should().Be("Registrar recebimento: dar entrada no estoque do que chegou de um pedido de compra aprovado.");
        ModuleCatalog.IsKnownPermission("purchasing.receive").Should().BeTrue();
    }

    /// <summary>CMP-03a: as permissões do pedido vêm do catálogo (view, create, edit, approve).</summary>
    [Fact]
    public void Catalogo_declara_as_acoes_do_pedido()
    {
        foreach (var permission in new[] { "purchasing.view", "purchasing.create", "purchasing.edit", "purchasing.approve" })
        {
            ModuleCatalog.IsKnownPermission(permission).Should().BeTrue(permission);
        }
    }

    /// <summary>
    /// CMP-04 CA 16 (D8, ADR 0024 §5): registrar o recebimento exige <c>purchasing.receive</c> (nunca <c>purchasing.create</c>) e
    /// <c>Idempotency-Key</c>; estornar e corrigir preço exigem <c>purchasing.approve</c>; encerrar o saldo, <c>purchasing.edit</c>; os grupos
    /// do recebimento exigem também o módulo <c>inventory</c>.
    /// </summary>
    [Fact]
    public void Recebimento_usa_purchasing_receive_e_as_acoes_certas()
    {
        var statements = EndpointsText.Split(';').Where(s => s.Contains(".WithName(\"", StringComparison.Ordinal)).ToList();
        string Of(string name) => statements.Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal));
        Of("RegisterGoodsReceipt").Should().Contain("\"purchasing.receive\"").And.Contain(".RequireIdempotencyKey()").And.NotContain("purchasing.create");
        Of("PreviewGoodsReceipt").Should().Contain("\"purchasing.receive\"");
        Of("GetGoodsReceiptDefaults").Should().Contain("\"purchasing.receive\"");
        Of("ReverseGoodsReceipt").Should().Contain("\"purchasing.approve\"");
        Of("CorrectPurchaseOrderPrice").Should().Contain("\"purchasing.approve\"");
        Of("ClosePurchaseOrderBalance").Should().Contain("\"purchasing.edit\"");
        var receipts = File.ReadAllText(Path.Combine(EndpointsDir, "PurchasingEndpoints.Receipts.cs"));
        receipts.Should().Contain("purchasing.MapGroup(\"/purchase-orders\").RequireModule(\"inventory\")").And.Contain("purchasing.MapGroup(\"/goods-receipts\").RequireModule(\"inventory\")");
    }

    /// <summary>
    /// CMP-04 CA 5, 8, 9 e 12 (ADR 0023 §1, ADR 0024 §3): a entrada no estoque e os títulos são chamadas síncronas na transação do
    /// recebimento, pelo motor e pelo <c>IPayables</c>; o estorno cancela os títulos antes de mexer no estoque.
    /// </summary>
    [Fact]
    public void Recebimento_chama_o_motor_e_o_IPayables_na_transacao()
    {
        var source = File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing", "BemAqui.Purchasing.Application", "GoodsReceiptService.cs"));
        source.Should().Contain("BeginTransactionAsync").And.Contain("stock.PostManyAsync(").And.Contain("payables.CreateOpenInstallmentsAsync(DocumentOrigin.GoodsReceipt")
            .And.Contain("orders.ReplanAsync(").And.Contain("stock.ReverseAsync(");
        var reverse = source[source.IndexOf("public async Task<Result<GoodsReceiptDetail>> ReverseAsync", StringComparison.Ordinal)..];
        reverse.IndexOf("payables.CancelAllFromOriginAsync(DocumentOrigin.GoodsReceipt", StringComparison.Ordinal).Should()
            .BeLessThan(reverse.IndexOf("stock.ReverseAsync(", StringComparison.Ordinal), "os títulos são cancelados antes do estoque (CA 12)");
    }

    [Fact]
    public void Criar_exige_Idempotency_Key_e_todo_endpoint_tem_permissao()
    {
        var text = EndpointsText;
        text.Should().Contain(".RequireModule(\"purchasing\")");
        var statements = text.Split(';').Where(s => s.Contains(".WithName(\"", StringComparison.Ordinal)).ToList();
        statements.Should().NotBeEmpty();
        foreach (var statement in statements)
        {
            statement.Should().MatchRegex(@"\.Require(Any)?Permission(Of)?\(", "todo endpoint de Compras tem perm:");
        }

        statements.Single(s => s.Contains(".WithName(\"CreatePurchaseRequest\")", StringComparison.Ordinal)).Should().Contain(".RequireIdempotencyKey()");
        statements.Single(s => s.Contains(".WithName(\"CreatePurchaseOrder\")", StringComparison.Ordinal)).Should().Contain(".RequireIdempotencyKey()");
        statements.Single(s => s.Contains(".WithName(\"ApprovePurchaseOrder\")", StringComparison.Ordinal)).Should().Contain("\"purchasing.approve\"");
        statements.Single(s => s.Contains(".WithName(\"RejectPurchaseOrder\")", StringComparison.Ordinal)).Should().Contain("\"purchasing.approve\"");
        statements.Single(s => s.Contains(".WithName(\"CancelPurchaseOrder\")", StringComparison.Ordinal)).Should().Contain("\"purchasing.edit\"");
    }

    /// <summary>
    /// CMP-02 CA 1, 9, 10 e 13: criar a cotação e gerar os pedidos exigem <c>purchasing.create</c> e <c>Idempotency-Key</c>; respostas,
    /// fornecedores, concluir e cancelar exigem <c>purchasing.edit</c>; "Enviar todos" usa a mesma permissão do envio do pedido (CMP-03a).
    /// </summary>
    [Fact]
    public void Cotacao_usa_as_acoes_certas_e_a_Idempotency_Key()
    {
        var statements = EndpointsText.Split(';').Where(s => s.Contains(".WithName(\"", StringComparison.Ordinal)).ToList();
        string Of(string name) => statements.Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal));
        Of("CreateRfq").Should().Contain("\"purchasing.create\"").And.Contain(".RequireIdempotencyKey()");
        Of("GenerateRfqPurchaseOrders").Should().Contain("\"purchasing.create\"").And.Contain(".RequireIdempotencyKey()");
        foreach (var name in new[] { "AddRfqSupplier", "RemoveRfqSupplier", "SaveRfqResponse", "DeclineRfqSupplier", "CloseRfq", "CancelRfq" })
        {
            Of(name).Should().Contain("\"purchasing.edit\"", name);
        }

        Of("SubmitRfqPurchaseOrders").Should().Contain(".RequireAnyPermissionOf(\"purchasing.create\", \"purchasing.edit\")");
        Of("SubmitPurchaseOrder").Should().Contain(".RequireAnyPermissionOf(\"purchasing.create\", \"purchasing.edit\")", "a mesma do envio da CMP-03a");
        Of("GetRfq").Should().Contain("\"purchasing.view\"");
        Of("SaveRfqResponse").Should().Contain("/{id:guid}/suppliers/{supplierId:guid}/", "fornecedor lido pela cotação da URL (IDOR)");
    }

    /// <summary>
    /// CMP-02 (regra da story): a cotação não grava em <c>purchase_orders</c> por conta própria; o pedido nasce pelo serviço de criação da
    /// CMP-03a e o envio pelo envio da CMP-03a.
    /// </summary>
    [Fact]
    public void Cotacao_cria_e_envia_o_pedido_pelo_servico_da_CMP_03a()
    {
        var dir = Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing", "BemAqui.Purchasing.Application");
        var sources = string.Join("\n", Directory.GetFiles(dir, "RfqService*.cs").Select(File.ReadAllText));
        sources.Should().NotContain("db.PurchaseOrders.Add").And.NotContain("db.PurchaseOrderLines.Add").And.NotContain("new PurchaseOrder {").And.NotContain("new PurchaseOrder\n");
        sources.Should().Contain("orders.AddFromRfqAsync(").And.Contain("orders.SubmitAsync(");
    }

    /// <summary>CMP-02 CA 4 (ADR 0019 §7, adendo de 21/10/2026): o dono de anexo <c>Rfq</c> com os tipos, o tamanho, o <c>OwnerExists</c> e a escrita com edit.</summary>
    [Fact]
    public void Dono_de_anexo_Rfq_esta_inscrito_com_as_regras_da_ADR()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        BemAqui.Purchasing.Infrastructure.PurchasingInfrastructure.AddPurchasingInfrastructure(services);
        var rfq = services.Select(d => d.ImplementationInstance).OfType<BemAqui.BuildingBlocks.Attachments.AttachmentOwnerRegistration>().Single(r => r.OwnerType == "Rfq").Rules;
        rfq.Module.Should().Be("purchasing");
        rfq.ReadAction.Should().Be("view");
        rfq.WriteAction.Should().Be("edit");
        rfq.MaxSize.Should().Be(10 * 1024 * 1024);
        rfq.ContentTypes.Should().BeEquivalentTo(["application/pdf", "image/png", "image/jpeg", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]);
        rfq.OwnerExists.Should().NotBeNull();
        rfq.CanAttach.Should().NotBeNull("a cotação concluída não recebe anexo (CA 14)");
    }

    /// <summary>CA 5 (ADR 0023 §6): o aviso de falta tem o consumidor do Purchasing, que recompõe pela fonte.</summary>
    [Fact]
    public void StockShortageChanged_tem_o_consumidor_do_Purchasing()
    {
        var registry = new IntegrationEventRegistry();
        HostEvents.Register(registry);
        registry.TryGet(IntegrationEventRegistry.NameOf(typeof(StockShortageChanged)), out var registered).Should().BeTrue();
        registered!.Handlers.Select(h => h.HandlerType).Should().Contain(typeof(BemAqui.Purchasing.Application.StockShortageChangedHandler));
    }

    /// <summary>CMP-03a CA 12 (ADR 0024 §1): o previsto nasce na transação da aprovação; os eventos do pedido não têm consumidor que crie título.</summary>
    [Theory]
    [InlineData(typeof(PurchaseOrderSubmitted))]
    [InlineData(typeof(PurchaseOrderLevelReleased))]
    [InlineData(typeof(PurchaseOrderApproved))]
    [InlineData(typeof(PurchaseOrderRejected))]
    [InlineData(typeof(PurchaseOrderChanged))]
    [InlineData(typeof(PurchaseOrderCancelled))]
    [InlineData(typeof(GoodsReceived))]
    [InlineData(typeof(GoodsReceiptReversed))]
    [InlineData(typeof(PurchaseOrderClosed))]
    [InlineData(typeof(PurchaseOrderPriceCorrected))]
    public void Eventos_do_pedido_sao_so_aviso(Type eventType)
    {
        var registry = new IntegrationEventRegistry();
        HostEvents.Register(registry);
        registry.TryGet(IntegrationEventRegistry.NameOf(eventType), out var registered).Should().BeTrue();
        registered!.Handlers.Where(h => h.HandlerType.Namespace?.StartsWith("BemAqui.Finance", StringComparison.Ordinal) == true).Should().BeEmpty();
    }

    /// <summary>CMP-03a (ADR 0024 §4): o <c>IApprovalRouting</c> usa o núcleo do <c>ApprovalResolver</c>, sem repetir a regra de alçada.</summary>
    [Fact]
    public void IApprovalRouting_nao_duplica_a_regra_do_ApprovalResolver()
    {
        var routing = typeof(BemAqui.Finance.Application.ApprovalRoutingContract);
        routing.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).Should().Contain(typeof(BemAqui.Finance.Application.ApprovalResolver));
        var source = File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Modules", "Finance", "BemAqui.Finance.Application", "ApprovalRoutingContract.cs"));
        source.Should().NotContain("ApprovalRules.Resolve(").And.NotContain("UsersWithPermissionAsync").And.Contain("resolver.ResolveRulesAsync(");
    }
}
