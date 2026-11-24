using System.Text.Json;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// OFF-02 sem banco: a cadeia de hash (o mesmo vetor do Vitest), a normalização do número de cartão e o replay da venda no servidor (requisitos de
/// segurança da PDV-02 e da PDV-03).
/// </summary>
public class PosSyncUnitTests
{
    private static readonly Guid Operator = Guid.Parse("019a0000-0000-7000-8000-0000000000aa");
    private static readonly Guid Supervisor = Guid.Parse("019a0000-0000-7000-8000-0000000000bb");
    private static readonly Guid Product = Guid.Parse("019a0000-0000-7000-8000-0000000000f1");
    private static readonly DateTimeOffset At = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hash_do_evento_e_o_mesmo_do_aparelho()
    {
        const string payload = "{\"saleId\":\"019a0000-0000-7000-8000-000000000001\",\"número\":\"ação\"}";
        var eventId = Guid.Parse("019a0000-0000-7000-8000-0000000000e1");
        var device = Guid.Parse("019a0000-0000-7000-8000-0000000000d1");
        var register = Guid.Parse("019a0000-0000-7000-8000-0000000000c1");
        PosEventChain.Hash(eventId, device, register, 2, 7, 1, "pos.sale", "2026-11-19T12:00:00.000Z", null, payload)
            .Should().Be("4ec63d582faec531deef5e592e5e192a54d94d782b67145461ca46103215f207");
        PosEventChain.Hash(eventId, device, register, 2, 7, 1, "pos.sale", "2026-11-19T12:00:00.000Z", string.Concat(Enumerable.Repeat("ab", 32)), payload)
            .Should().Be("7234ba05a9aafa5d72938a7ee3bafe8231f8cd159d0864e5fcd59f407209aaa5");
        PosEventChain.IsSupported(1).Should().BeTrue();
        PosEventChain.IsSupported(0).Should().BeFalse();
        PosEventChain.IsSupported(2).Should().BeFalse();
    }

    [Theory]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("4111-1111-1111-1111", true)]
    [InlineData("4111​1111​1111​1111", true)] // \p{Cf} entre os dígitos
    [InlineData("４１１１１１１１１１１１１１１１", true)] // dígitos de largura total (NFKC)
    [InlineData("NSU 123456", false)]
    [InlineData("12345 abc 67890123", false)]
    public void Numero_de_cartao_com_a_mesma_normalizacao_do_aparelho(string text, bool expected) =>
        PosText.LooksLikeCardNumber(text).Should().Be(expected);

    [Fact]
    public void Texto_de_cartao_sai_do_conteudo_antes_de_gravar()
    {
        var (payload, redacted) = PosText.RedactCardNumbers("{\"events\":[{\"data\":{\"tender\":{\"nsu\":\"4111 1111 1111 1111\",\"amountCents\":100}}}]}");
        redacted.Should().BeTrue();
        payload.Should().NotContain("4111").And.Contain("\"nsu\":null");
    }

    [Fact]
    public void Ultimo_item_sai_sem_autorizacao_so_quando_e_o_ultimo_lancamento_e_a_loja_permite()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var removeLast = Run([Line(a, "2"), Line(b, "1"), Ev("LineRemoved", new { lineId = b, reason = "last-item", operatorId = Operator }), .. Pay(2000)]);
        removeLast.Violations.Should().BeEmpty();
        removeLast.Exceptions.Should().ContainSingle(e => e.Kind == OperatorIncidentKind.ItemRemoved && e.OperatorId == Operator);

        // "last-item" numa linha que não é o último lançamento: precisa de quem autorizou.
        Run([Line(a, "2"), Line(b, "1"), Ev("LineRemoved", new { lineId = a, reason = "last-item", operatorId = Operator }), .. Pay(1000)])
            .Violations.Should().ContainSingle(v => v.Contains("Sem quem autorizou", StringComparison.Ordinal));

        // A loja desligou a remoção livre (vale a do servidor, não a do aparelho).
        Run([Line(a, "2"), Line(b, "1"), Ev("LineRemoved", new { lineId = b, reason = "last-item", operatorId = Operator }), .. Pay(2000)], freeRemoveLast: false)
            .Violations.Should().ContainSingle(v => v.Contains("Sem quem autorizou", StringComparison.Ordinal));

        // PDV-04 (achado 2): outra pessoa que autoriza sem a aprovação do supervisor por PIN vai para a quarentena (antes era só divergência, OFF-02).
        Run([Line(a, "2"), Line(b, "1"), Ev("LineRemoved", new { lineId = a, reason = "authorized", operatorId = Operator, authorizedBy = Supervisor }), .. Pay(1000)])
            .Violations.Should().ContainSingle(v => v.Contains("sem a aprovação do supervisor", StringComparison.Ordinal));
    }

    [Fact]
    public void Desfazer_o_ultimo_lancamento_so_desfaz_o_que_entrou_por_ultimo()
    {
        var a = Guid.CreateVersion7();
        var ok = Run([Line(a, "2"), Ev("LineChanged", new { lineId = a, quantity = "3", reason = "same-item", operatorId = Operator }),
            Ev("LineChanged", new { lineId = a, quantity = "2", reason = "remove-last", operatorId = Operator }), .. Pay(2000)]);
        ok.Violations.Should().BeEmpty();
        ok.Exceptions.Should().ContainSingle(e => e.Kind == OperatorIncidentKind.QuantityReduced);

        // Desfazer mais do que o último lançamento ou reduzir outra linha: precisa de autorização.
        Run([Line(a, "2"), Ev("LineChanged", new { lineId = a, quantity = "3", reason = "same-item", operatorId = Operator }),
            Ev("LineChanged", new { lineId = a, quantity = "1", reason = "remove-last", operatorId = Operator }), .. Pay(1000)])
            .Violations.Should().ContainSingle(v => v.Contains("Sem quem autorizou", StringComparison.Ordinal));
        Run([Line(a, "3"), Line(Guid.CreateVersion7(), "1"), Ev("LineChanged", new { lineId = a, quantity = "1", reason = "quantity", operatorId = Operator }), .. Pay(2000)])
            .Violations.Should().ContainSingle(v => v.Contains("Sem quem autorizou", StringComparison.Ordinal));
    }

    [Fact]
    public void Pagamento_refeito_no_servidor_soma_troco_maquina_de_estados_e_integracao()
    {
        var a = Guid.CreateVersion7();
        Run([Line(a, "2"), .. Pay(2000)]).Violations.Should().BeEmpty();

        // Total do início do pagamento diferente das linhas, troco incoerente e valor acima do que faltava.
        var tender = Guid.CreateVersion7();
        var bad = Run([Line(a, "2"), Ev("PaymentStarted", new { operatorId = Operator, totalCents = 999 }),
            Ev("TenderCaptured", new { tender = Tender(tender, "Captured", 2500, received: 3000, change: 100) }),
            Ev("SaleCompleted", new { operatorId = Operator, totalCents = 999, paidCents = 999, changeCents = 0 })]);
        bad.Violations.Should().Contain(v => v.Contains("início do pagamento", StringComparison.Ordinal))
            .And.Contain(v => v.Contains("Troco incoerente", StringComparison.Ordinal))
            .And.Contain(v => v.Contains("passa do que faltava", StringComparison.Ordinal));

        // Capturado só sai por estorno com quem autorizou; manual não leva campo de integração; quem confirmou é obrigatório.
        var states = Run([Line(a, "2"), Ev("PaymentStarted", new { operatorId = Operator, totalCents = 2000 }),
            Ev("TenderCaptured", new { tender = Tender(tender, "Captured", 2000) }),
            Ev("TenderChanged", new { tender = Tender(tender, "Pending", 2000) }),
            Ev("TenderCaptured", new { tender = Tender(Guid.CreateVersion7(), "Captured", 1, txid: "abc", confirmedBy: null) }),
            Ev("SaleCompleted", new { operatorId = Operator, totalCents = 2000, paidCents = 2000, changeCents = 0 })]);
        states.Violations.Should().Contain(v => v.Contains("máquina de estados", StringComparison.Ordinal))
            .And.Contain(v => v.Contains("campos de integração", StringComparison.Ordinal))
            .And.Contain(v => v.Contains("Pagamento sem quem confirmou", StringComparison.Ordinal));

        // Unknown sem captura: a venda entra com a pendência humana (não abate da venda).
        var unknown = Run([Line(a, "2"), Ev("PaymentStarted", new { operatorId = Operator, totalCents = 2000 }),
            Ev("TenderChanged", new { tender = Tender(Guid.CreateVersion7(), "Unknown", 2000) }),
            Ev("TenderCaptured", new { tender = Tender(tender, "Captured", 2000) }),
            Ev("SaleCompleted", new { operatorId = Operator, totalCents = 2000, paidCents = 2000, changeCents = 0 })]);
        unknown.Violations.Should().BeEmpty();
        unknown.PaymentNotes.Should().ContainSingle(n => n.Contains("Unknown", StringComparison.Ordinal));
        unknown.CapturedCents.Should().Be(2000);
    }

    [Fact]
    public void Operador_nulo_e_recusado_e_todo_evento_com_dinheiro_entra_na_conferencia_da_sessao()
    {
        var a = Guid.CreateVersion7();
        var result = Run([Ev("SaleStarted", new { operatorId = (Guid?)null }), Line(a, "1", started: false), .. Pay(1000)]);
        result.Violations.Should().ContainSingle(v => v.Contains("sem o operador", StringComparison.Ordinal));
        Run([Line(a, "1"), .. Pay(1000)]).Checks.Where(c => c.NeedsPinSession).Select(c => c.UserId).Should().OnlyContain(id => id == Operator);
    }

    [Fact]
    public void Preco_do_aparelho_e_sempre_conferido_com_o_catalogo_da_loja()
    {
        var line = new ReplayLine(Guid.CreateVersion7(), Product, "Arroz", 1, 1000, 5, null);
        PosItemPayload Item(long? cents, long? version) => new(Product, null, "Arroz", "UN", false, null, [], cents, Guid.Empty, version);
        PosEventIngestion.PriceCheck(line, Item(1000, 5)).Should().BeNull();
        PosEventIngestion.PriceCheck(line, Item(1250, 6)).Should().Be(SyncDivergenceKind.PriceOutdated);
        PosEventIngestion.PriceCheck(line, Item(1250, 5)).Should().Be(SyncDivergenceKind.PriceMismatch, "preço diferente na versão vigente");
        PosEventIngestion.PriceCheck(line, Item(1000, 4)).Should().Be(SyncDivergenceKind.PriceMismatch, "versão do aparelho maior que a vigente");
        PosEventIngestion.PriceCheck(line with { PriceVersion = null }, Item(1000, 5)).Should().Be(SyncDivergenceKind.PriceMismatch);
        PosEventIngestion.PriceCheck(line, null).Should().Be(SyncDivergenceKind.PriceMismatch, "fora do catálogo da loja");
    }

    [Fact]
    public void Pagamento_repetido_no_mesmo_estado_com_outro_valor_e_recusado()
    {
        var a = Guid.CreateVersion7();
        var tender = Guid.CreateVersion7();
        Run([Line(a, "2"), Ev("PaymentStarted", new { operatorId = Operator, totalCents = 2000 }), Ev("TenderCaptured", new { tender = Tender(tender, "Captured", 2000) }),
            Ev("TenderCaptured", new { tender = Tender(tender, "Captured", 1500) }), Ev("SaleCompleted", new { operatorId = Operator, totalCents = 2000, paidCents = 2000, changeCents = 0 })])
            .Violations.Should().ContainSingle(v => v.Contains("repetido", StringComparison.Ordinal));
    }

    [Fact]
    public void Operador_com_a_permissao_remove_sem_pendencia_e_aprovacao_do_supervisor_vai_para_a_conferencia()
    {
        // PDV-04 CA 5: o próprio operador, com pos.cancel_item no instante, remove sem a pendência "Autorização sem supervisor" da OFF-02.
        var a = Guid.CreateVersion7();
        var self = Run([Line(a, "2"), Line(Guid.CreateVersion7(), "1"), Ev("LineRemoved", new { lineId = a, reason = "authorized", operatorId = Operator, authorizedBy = Operator }), .. Pay(1000)]);
        self.Violations.Should().BeEmpty();
        self.Checks.Should().Contain(c => c.UserId == Operator && c.Permission == "pos.cancel_item" && !c.NeedsPinSession);

        // CA 10: com a aprovação, a subida confere a trilha (ação, linha, operador e supervisor); sem conferência de sessão do supervisor.
        var approval = Guid.CreateVersion7();
        var approved = Run([Line(a, "2"), Line(Guid.CreateVersion7(), "1"),
            Ev("LineRemoved", new { lineId = a, reason = "authorized", operatorId = Operator, authorizedBy = Supervisor, approvalId = approval }), .. Pay(1000)]);
        approved.Violations.Should().BeEmpty();
        approved.Approvals.Should().ContainSingle(x => x.ApprovalId == approval && x.Action == SupervisorAction.CancelItem && x.SupervisorId == Supervisor && x.LineId == a
            && x.BeforeCents == 2000 && x.AfterCents == 0);
        approved.Checks.Should().NotContain(c => c.UserId == Supervisor);

        // CA 11: aprovação com o próprio operador como supervisor é recusada.
        Run([Line(a, "2"), Line(Guid.CreateVersion7(), "1"),
                Ev("LineRemoved", new { lineId = a, reason = "authorized", operatorId = Operator, authorizedBy = Operator, approvalId = approval }), .. Pay(1000)])
            .Violations.Should().ContainSingle(v => v.Contains("próprio operador", StringComparison.Ordinal));
    }

    [Fact]
    public void Desconto_ate_o_limite_passa_e_acima_pede_permissao_com_motivo_e_rateio_no_total()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        // 5% de R$ 20,00 = R$ 1,00 no item: dentro do limite de 5%, sem autorização; total 20,00 + 10,00 − 1,00.
        var inside = Run([Line(a, "2"), Line(b, "1"), Ev("DiscountApplied", new { lineId = a, discountCents = 100L, reason = "Cliente fiel", operatorId = Operator }), .. Pay(2900)],
            limit: 5m);
        inside.Violations.Should().BeEmpty();
        inside.Lines.Single(l => l.LineId == a).DiscountCents.Should().Be(100);
        inside.Checks.Should().NotContain(c => c.Permission == "pos.discount");

        // R$ 1,01 passa de 5%: sem quem autorizou é recusado; com o próprio operador, a permissão pos.discount dele entra na conferência.
        Run([Line(a, "2"), Ev("DiscountApplied", new { lineId = a, discountCents = 101L, reason = "Cliente fiel", operatorId = Operator }), .. Pay(1899)], limit: 5m)
            .Violations.Should().ContainSingle(v => v.Contains("Sem quem autorizou", StringComparison.Ordinal));
        Run([Line(a, "2"), Ev("DiscountApplied", new { lineId = a, discountCents = 101L, reason = "Cliente fiel", operatorId = Operator, authorizedBy = Operator }), .. Pay(1899)], limit: 5m)
            .Checks.Should().Contain(c => c.UserId == Operator && c.Permission == "pos.discount");

        // Sem motivo, ou maior que o item, não passa.
        Run([Line(a, "1"), Ev("DiscountApplied", new { lineId = a, discountCents = 10L, reason = "", operatorId = Operator }), .. Pay(990)], limit: 5m)
            .Violations.Should().ContainSingle(v => v.Contains("sem motivo", StringComparison.Ordinal));
        Run([Line(a, "1"), Ev("DiscountApplied", new { lineId = a, discountCents = 1001L, reason = "Outro: teste", operatorId = Operator, authorizedBy = Operator }), .. Pay(0)], limit: 100m)
            .Violations.Should().Contain(v => v.Contains("maior que o valor", StringComparison.Ordinal));

        // No total: R$ 1,00 sobre R$ 30,00; mudar os itens depois tira o desconto no total.
        var total = Run([Line(a, "2"), Line(b, "1"), Ev("DiscountApplied", new { lineId = (Guid?)null, discountCents = 100L, reason = "Negociação", operatorId = Operator }), .. Pay(2900)],
            limit: 5m);
        total.Violations.Should().BeEmpty();
        total.SaleDiscountCents.Should().Be(100);
        Run([Line(a, "2"), Ev("DiscountApplied", new { lineId = (Guid?)null, discountCents = 100L, reason = "Negociação", operatorId = Operator }), Line(b, "1"), .. Pay(3000)], limit: 5m)
            .SaleDiscountCents.Should().Be(0);
    }

    [Fact]
    public void Aprovacao_leva_os_valores_aplicados_de_cada_acao()
    {
        // Achado 1: o replay devolve, por ação, o antes e o depois que a subida compara com a aprovação, ao centavo.
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var (discount, total, price) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var result = Run([Line(a, "2"), Line(b, "1"),
            Ev("PriceOverridden", new { lineId = b, unitCents = 800L, reason = "Erro na etiqueta", operatorId = Operator, authorizedBy = Supervisor, approvalId = price }),
            Ev("DiscountApplied", new { lineId = a, discountCents = 500L, reason = "Cliente fiel", operatorId = Operator, authorizedBy = Supervisor, approvalId = discount }),
            Ev("DiscountApplied", new { lineId = (Guid?)null, discountCents = 300L, reason = "Negociação", operatorId = Operator, authorizedBy = Supervisor, approvalId = total }),
            .. Pay(2000)], limit: 5m);
        result.Violations.Should().BeEmpty();
        result.Approvals.Should().ContainSingle(x => x.ApprovalId == price && x.Action == SupervisorAction.ChangePrice && x.LineId == b && x.BeforeCents == 1000 && x.AfterCents == 800);
        result.Approvals.Should().ContainSingle(x => x.ApprovalId == discount && x.Action == SupervisorAction.Discount && x.LineId == a && x.BeforeCents == 2000 && x.AfterCents == 1500);
        result.Approvals.Should().ContainSingle(x => x.ApprovalId == total && x.Action == SupervisorAction.Discount && x.LineId == null && x.BeforeCents == 2300 && x.AfterCents == 2000);
    }

    [Fact]
    public void Desconto_no_total_nao_empilha_com_o_desconto_do_item()
    {
        // Achado 3: limite de 10%; R$ 20,00 + R$ 20,00. O item com R$ 2,00 (10%) e o total com R$ 3,80 (10% de 38,00) somam 14,5% da base bruta.
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        object[] Events(object? authorization) =>
        [
            Line(a, "2"), Line(b, "2"),
            Ev("DiscountApplied", new { lineId = a, discountCents = 200L, reason = "Cliente fiel", operatorId = Operator }),
            Ev("DiscountApplied", authorization ?? new { lineId = (Guid?)null, discountCents = 380L, reason = "Negociação", operatorId = Operator }),
            .. Pay(3420),
        ];
        Run(Events(null), limit: 10m).Violations.Should().ContainSingle(v => v.Contains("Sem quem autorizou: desconto no total que, somado aos dos itens", StringComparison.Ordinal));
        Run(Events(new { lineId = (Guid?)null, discountCents = 380L, reason = "Negociação", operatorId = Operator, authorizedBy = Operator }), limit: 10m)
            .Checks.Should().Contain(c => c.UserId == Operator && c.Permission == "pos.discount");

        // Somando até o limite (2,00 + 2,00 = 10% de 40,00), passa sem autorização.
        Run([Line(a, "2"), Line(b, "2"), Ev("DiscountApplied", new { lineId = a, discountCents = 200L, reason = "Cliente fiel", operatorId = Operator }),
                Ev("DiscountApplied", new { lineId = (Guid?)null, discountCents = 200L, reason = "Negociação", operatorId = Operator }), .. Pay(3600)], limit: 10m)
            .Violations.Should().BeEmpty();

        // O desconto do item já aprovado fica fora da conta: o item entra pelo líquido.
        var lines = new List<ReplayLine>
        {
            new(a, Product, "Arroz", 2, 1000, 1, null, DiscountCents: 1000, DiscountApproved: true),
            new(b, Product, "Arroz", 2, 1000, 1, null),
        };
        PosSaleReplay.StackedAboveLimit(lines, 300, 10m).Should().BeFalse();
        PosSaleReplay.StackedAboveLimit(lines, 301, 10m).Should().BeTrue();
    }

    [Theory]
    [InlineData(SupervisorAction.Discount, 10.0, 8.0, true)]
    [InlineData(SupervisorAction.Discount, 10.0, 10.5, false)]
    [InlineData(SupervisorAction.ChangePrice, 10.0, 12.0, true)]
    [InlineData(SupervisorAction.ChangePrice, 10.0, 10.0, false)]
    [InlineData(SupervisorAction.CancelItem, 10.0, 0.0, true)]
    [InlineData(SupervisorAction.CancelItem, 10.0, 11.0, false)]
    [InlineData(SupervisorAction.CancelSale, 10.0, 0.0, true)]
    [InlineData(SupervisorAction.CancelSale, 10.0, 1.0, false)]
    public void Valores_da_aprovacao_combinam_com_a_acao(SupervisorAction action, double before, double after, bool ok) =>
        (SupervisorApprovalService.DirectionProblem(action, (decimal)before, (decimal)after) is null).Should().Be(ok);

    [Fact]
    public void Autorizacao_de_outra_pessoa_sem_aprovacao_vai_para_a_quarentena_em_toda_acao()
    {
        // Achado 2: desconto acima do limite e troca de preço autorizados por outra pessoa sem approvalId não passam (nem como divergência).
        var a = Guid.CreateVersion7();
        Run([Line(a, "2"), Ev("DiscountApplied", new { lineId = a, discountCents = 500L, reason = "Cliente fiel", operatorId = Operator, authorizedBy = Supervisor }), .. Pay(1500)], limit: 5m)
            .Violations.Should().ContainSingle(v => v.Contains("sem a aprovação do supervisor", StringComparison.Ordinal));
        Run([Line(a, "2"), Ev("PriceOverridden", new { lineId = a, unitCents = 800L, reason = "Erro na etiqueta", operatorId = Operator, authorizedBy = Supervisor }), .. Pay(1600)])
            .Violations.Should().ContainSingle(v => v.Contains("sem a aprovação do supervisor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(100L, 1000L, 10.0, false)]
    [InlineData(101L, 1000L, 10.0, true)]
    [InlineData(1L, 0L, 50.0, true)]
    [InlineData(0L, 1000L, 0.0, false)]
    [InlineData(332L, 3333L, 9.99, false)]
    [InlineData(333L, 3333L, 9.99, true)]
    public void Limite_do_desconto_em_centavos_sem_arredondar_o_percentual(long discount, long baseCents, double limit, bool above) =>
        PosSaleReplay.AboveLimit(discount, baseCents, (decimal)limit).Should().Be(above);

    [Theory]
    [InlineData(1999L, 10.0, 200L)] // 199,9 centavos: arredonda para 2,00
    [InlineData(1005L, 1.5, 15L)] // 15,075: para baixo
    [InlineData(50L, 3.0, 2L)] // 1,5 centavo: meio centavo para cima
    [InlineData(12345L, 0.0, 0L)]
    public void Desconto_em_percentual_vira_centavos(long baseCents, double percent, long expected) =>
        PosSaleReplay.PercentToCents(baseCents, (decimal)percent).Should().Be(expected);

    [Fact]
    public void Rateio_do_desconto_no_total_fecha_o_centavo()
    {
        PosSaleReplay.Prorate([1000, 1000, 1000], 100).Should().Equal(34, 33, 33);
        PosSaleReplay.Prorate([2000, 1000], 1).Should().Equal(1, 0);
        PosSaleReplay.Prorate([1999, 0, 1], 1000).Sum().Should().Be(1000);
        PosSaleReplay.Prorate([500], 0).Should().Equal(0);
    }

    [Fact]
    public void Troca_de_preco_guarda_o_da_tabela_e_pede_a_permissao_com_motivo()
    {
        var a = Guid.CreateVersion7();
        var changed = Run([Line(a, "2"), Ev("PriceOverridden", new { lineId = a, unitCents = 800L, reason = "Erro na etiqueta", operatorId = Operator, authorizedBy = Operator }),
            .. Pay(1600)]);
        changed.Violations.Should().BeEmpty();
        var line = changed.Lines.Single();
        (line.UnitCents, line.ListUnitCents, line.TableCents, line.PriceReason).Should().Be((800L, 1000L, 1000L, "Erro na etiqueta"));
        changed.Checks.Should().Contain(c => c.UserId == Operator && c.Permission == "pos.change_price");

        Run([Line(a, "2"), Ev("PriceOverridden", new { lineId = a, unitCents = 800L, reason = "Erro na etiqueta", operatorId = Operator }), .. Pay(1600)])
            .Violations.Should().ContainSingle(v => v.Contains("Sem quem autorizou", StringComparison.Ordinal));
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Guid SaleId = Guid.Parse("019a0000-0000-7000-8000-000000000001");

    private static SaleReplayResult Run(IEnumerable<object> events, bool freeRemoveLast = true, decimal limit = 0m)
    {
        var list = events.ToList();
        if (list.Count == 0 || !JsonSerializer.Serialize(list[0]).Contains("SaleStarted", StringComparison.Ordinal))
        {
            list.Insert(0, Ev("SaleStarted", new { operatorId = Operator }));
        }

        var json = JsonSerializer.Serialize(new { saleId = SaleId, number = "LOJA-01.1-000001", status = "completed", events = list }, Web);
        using var document = JsonDocument.Parse(json);
        return PosSaleReplay.Run(document.RootElement.Clone(), freeRemoveLast, limit);
    }

    private static object Ev(string type, object data) => new { id = Guid.CreateVersion7(), saleId = SaleId, type, at = At, data };

    private static object Line(Guid lineId, string quantity, bool started = true)
    {
        _ = started;
        return Ev("LineAdded", new { line = new { lineId, itemId = Product, name = "Arroz", unit = "UN", fractional = false, quantity, unitCents = 1000L, priceTableVersion = 1L } });
    }

    private static object[] Pay(long total) =>
    [
        Ev("PaymentStarted", new { operatorId = Operator, totalCents = total }),
        Ev("TenderCaptured", new { tender = Tender(Guid.CreateVersion7(), "Captured", total) }),
        Ev("SaleCompleted", new { operatorId = Operator, totalCents = total, paidCents = total, changeCents = 0 }),
    ];

    private static object Tender(Guid id, string status, long amount, long? received = null, long change = 0, string? txid = null, Guid? confirmedBy = default, bool noConfirm = false)
    {
        _ = noConfirm;
        return new
        {
            tenderId = id, partId = "main", status, methodId = Guid.Parse("019a0000-0000-7000-8000-0000000000ca"), methodType = "Cash", amountCents = amount, receivedCents = received,
            changeCents = change, integration = "Manual", txid, confirmedBy = confirmedBy == default && txid is null ? Operator : confirmedBy, at = At,
        };
    }
}
