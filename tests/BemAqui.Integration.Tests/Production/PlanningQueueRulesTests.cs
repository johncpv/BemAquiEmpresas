using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-02 sem banco: a manutenção da fila (entrada pela data prometida, saída e posições contíguas, CA 2), a carga inicial pela ordem
/// padrão (CA 3), a validação da permutação (CA 7), as posições antes → depois do histórico (CA 8) e o "passa a atrasar" da prévia (CA 5).
/// </summary>
public class PlanningQueueRulesTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static QueueCandidate C(string number, DateOnly? promised, DateOnly? signed = null) => new(Guid.NewGuid(), number, promised, signed);

    private static List<string> Numbers(QueueSync sync, params QueueCandidate[] all)
    {
        var byId = all.ToDictionary(c => c.ProjectId, c => c.Number);
        return [.. sync.Order.Select(id => byId[id])];
    }

    [Fact]
    public void Carga_inicial_pela_data_prometida_depois_assinatura_e_numero()
    {
        var p1 = C("PRJ-3", D(12, 1), D(9, 1));
        var p2 = C("PRJ-1", D(11, 20), D(9, 5));
        var p3 = C("PRJ-2", D(12, 1), D(8, 1));
        var p4 = C("PRJ-4", null, D(7, 1));
        var p5 = C("PRJ-0", D(12, 1), D(8, 1));

        var sync = PlanningQueueRules.Sync([], [p1, p2, p3, p4, p5]);

        Numbers(sync, p1, p2, p3, p4, p5).Should().Equal("PRJ-1", "PRJ-0", "PRJ-2", "PRJ-3", "PRJ-4");
        sync.Initial.Should().BeTrue("CA 3: a fila vazia é montada inteira");
        sync.Entered.Should().HaveCount(5);
        sync.Left.Should().BeEmpty();
    }

    [Fact]
    public void Projeto_novo_entra_antes_do_primeiro_com_data_prometida_posterior()
    {
        var a = C("A", D(11, 10));
        var b = C("B", D(12, 20));
        var c = C("C", D(11, 5));
        var novo = C("N", D(11, 30));

        // A fila manual pôs C depois de B: o novo entra antes do primeiro com data posterior à dele (B), não pela ordem padrão inteira.
        var sync = PlanningQueueRules.Sync([a.ProjectId, b.ProjectId, c.ProjectId], [a, b, c, novo]);

        Numbers(sync, a, b, c, novo).Should().Equal("A", "N", "B", "C");
        sync.Entered.Should().Equal(novo.ProjectId);
        sync.Initial.Should().BeFalse();
        PlanningQueueRules.Positions(sync.Order)[novo.ProjectId].Should().Be(2);
    }

    [Fact]
    public void No_empate_de_data_entra_depois_dos_que_ja_estavam_e_sem_data_no_fim()
    {
        var a = C("A", D(11, 10));
        var b = C("B", D(11, 10));
        var semData = C("S", null);
        var empate = C("E", D(11, 10));
        var novoSemData = C("Z", null);

        var sync = PlanningQueueRules.Sync([a.ProjectId, semData.ProjectId, b.ProjectId], [a, b, semData, empate, novoSemData]);

        // O projeto sem data prometida no meio da fila não conta como "posterior": o empate vai depois de B; o novo sem data vai para o fim.
        Numbers(sync, a, b, semData, empate, novoSemData).Should().Equal("A", "S", "B", "E", "Z");
    }

    [Fact]
    public void Varios_novos_entram_um_a_um_pela_ordem_padrao()
    {
        var a = C("A", D(11, 15));
        var n1 = C("N1", D(11, 20));
        var n2 = C("N2", D(11, 1));
        var n3 = C("N3", D(11, 20), D(9, 1));

        var sync = PlanningQueueRules.Sync([a.ProjectId], [a, n1, n2, n3]);

        Numbers(sync, a, n1, n2, n3).Should().Equal("N2", "A", "N3", "N1");
        sync.Entered.Should().Equal(n2.ProjectId, n3.ProjectId, n1.ProjectId);
    }

    [Fact]
    public void Quem_nao_tem_mais_OS_aberta_sai_e_as_posicoes_ficam_contiguas()
    {
        var a = C("A", D(11, 1));
        var b = C("B", D(11, 2));
        var c = C("C", D(11, 3));
        var gone = Guid.NewGuid();

        var sync = PlanningQueueRules.Sync([a.ProjectId, gone, b.ProjectId, c.ProjectId], [a, b, c]);

        sync.Left.Should().Equal(gone);
        sync.Order.Should().Equal(a.ProjectId, b.ProjectId, c.ProjectId);
        PlanningQueueRules.Positions(sync.Order).Values.Should().Equal(1, 2, 3);
        sync.Changed.Should().BeTrue();
    }

    [Fact]
    public void Sem_mudanca_a_fila_fica_como_estava()
    {
        var a = C("A", D(12, 1));
        var b = C("B", D(11, 1));

        var sync = PlanningQueueRules.Sync([a.ProjectId, b.ProjectId], [a, b]);

        sync.Order.Should().Equal([a.ProjectId, b.ProjectId], "a ordem manual é mantida mesmo contra a data prometida");
        sync.Changed.Should().BeFalse();
    }

    [Fact]
    public void Permutacao_exata_da_fila()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        List<Guid> current = [a, b, c];

        PlanningQueueRules.IsPermutation(current, [c, a, b]).Should().BeTrue();
        PlanningQueueRules.IsPermutation(current, [a, b, c]).Should().BeTrue();
        PlanningQueueRules.IsPermutation(current, [a, b]).Should().BeFalse("projeto a menos");
        PlanningQueueRules.IsPermutation(current, [a, b, c, Guid.NewGuid()]).Should().BeFalse("projeto a mais");
        PlanningQueueRules.IsPermutation(current, [a, a, b]).Should().BeFalse("repetido");
        PlanningQueueRules.IsPermutation(current, [a, b, Guid.NewGuid()]).Should().BeFalse("projeto de fora da fila (ou de outro tenant)");
        PlanningQueueRules.IsPermutation(current, null).Should().BeFalse();
        PlanningQueueRules.IsPermutation([], []).Should().BeTrue();
    }

    [Fact]
    public void Movimentos_trazem_so_quem_mudou_de_posicao()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();

        var moves = PlanningQueueRules.Moves([a, b, c], [c, b, a]);
        moves.Should().BeEquivalentTo([new QueueMove(c, 3, 1), new QueueMove(a, 1, 3)]);

        PlanningQueueRules.Moves([a, b], [a, d]).Should().BeEquivalentTo([new QueueMove(d, null, 2), new QueueMove(b, 2, null)]);
        PlanningQueueRules.Moves([a, b], [a, b]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("2026-12-01", "2026-11-28", "2026-12-04", true)]
    [InlineData("2026-12-01", "2026-12-03", "2026-12-05", true)]
    [InlineData("2026-12-01", "2026-12-05", "2026-12-03", false)]
    [InlineData("2026-12-01", "2026-11-20", "2026-11-30", false)]
    [InlineData("2026-12-01", "2026-12-01", "2026-12-01", false)]
    [InlineData("2026-12-01", null, "2026-12-02", true)]
    [InlineData(null, "2026-11-20", "2026-12-30", false)]
    [InlineData("2026-12-01", "2026-11-20", null, false)]
    public void Passa_a_atrasar_ou_piora(string? promised, string? before, string? after, bool worsened)
    {
        static DateOnly? P(string? s) => s is null ? null : DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        PlanningQueueService.Worsened(P(promised), P(before), P(after)).Should().Be(worsened);
    }

    [Fact]
    public void Historico_guarda_as_posicoes_em_JSON_PascalCase()
    {
        var id = Guid.NewGuid();
        var change = PlanningQueueStore.Change(DateTimeOffset.UnixEpoch, PlanningQueueChangeKind.Manual, null, "Ordem alterada", [new QueueMove(id, 3, 1)], Guid.NewGuid());

        change.Moves.Should().Contain("\"ProjectId\"").And.Contain("\"From\":3").And.Contain("\"To\":1");
        PlanningQueueStore.ReadMoves(change.Moves).Should().Equal(new QueueMove(id, 3, 1));
        PlanningQueueStore.ReadMoves(string.Empty).Should().BeEmpty();
    }
}
