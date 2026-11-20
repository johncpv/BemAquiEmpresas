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

        // Com quem autorizou: a regra passa e a permissão dele no instante entra na conferência.
        var authorized = Run([Line(a, "2"), Line(b, "1"), Ev("LineRemoved", new { lineId = a, reason = "authorized", operatorId = Operator, authorizedBy = Supervisor }), .. Pay(1000)]);
        authorized.Violations.Should().BeEmpty();
        authorized.Checks.Should().Contain(c => c.UserId == Supervisor && c.Permission == "pos.cancel_item" && !c.NeedsPinSession);
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

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Guid SaleId = Guid.Parse("019a0000-0000-7000-8000-000000000001");

    private static SaleReplayResult Run(IEnumerable<object> events, bool freeRemoveLast = true)
    {
        var list = events.ToList();
        if (list.Count == 0 || !JsonSerializer.Serialize(list[0]).Contains("SaleStarted", StringComparison.Ordinal))
        {
            list.Insert(0, Ev("SaleStarted", new { operatorId = Operator }));
        }

        var json = JsonSerializer.Serialize(new { saleId = SaleId, number = "LOJA-01.1-000001", status = "completed", events = list }, Web);
        using var document = JsonDocument.Parse(json);
        return PosSaleReplay.Run(document.RootElement.Clone(), freeRemoveLast);
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
