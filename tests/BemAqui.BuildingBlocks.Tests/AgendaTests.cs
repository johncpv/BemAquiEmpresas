using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>Minha pauta (DSH-04): ordem (CA 4), textos do porquê (CA 5) e o agregador com as camadas e a falha parcial (CA 1, 10, 13).</summary>
public class AgendaTests
{
    private static readonly DateOnly Today = new(2026, 10, 15); // quinta-feira
    private static readonly Guid Me = Guid.CreateVersion7();

    private static AgendaItem Item(string category, AgendaTier tier, DateOnly? due = null, decimal? amount = null) =>
        new(category, category, string.Empty, null, due, amount, tier, 1, "Abrir", []);

    [Fact]
    public void Ordena_por_nivel_vencimento_sem_data_no_fim_e_maior_valor_no_empate()
    {
        var sorted = AgendaOrdering.Sort(
        [
            Item("vence-breve", AgendaTier.DueSoon, Today.AddDays(1)),
            Item("atrasada-sem-data", AgendaTier.Overdue),
            Item("atrasada", AgendaTier.Overdue, Today.AddDays(-5)),
            Item("destrava-sem-data", AgendaTier.Unblocks, null, 1_000_000m),
            Item("destrava-depois", AgendaTier.Unblocks, Today.AddDays(3), 10m),
            Item("destrava-menor", AgendaTier.Unblocks, Today.AddDays(1), 100m),
            Item("destrava-maior", AgendaTier.Unblocks, Today.AddDays(1), 900m),
        ]);

        sorted.Select(i => i.Category).Should().Equal(
            "destrava-maior", "destrava-menor", "destrava-depois", "destrava-sem-data", "atrasada", "atrasada-sem-data", "vence-breve");
    }

    [Fact]
    public void Monta_titulo_com_contagem_e_porque_numa_linha()
    {
        AgendaText.Count(3, "ordem de pagamento aguarda você", "ordens de pagamento aguardam você").Should().Be("3 ordens de pagamento aguardam você");
        AgendaText.Count(1, "ordem de pagamento aguarda você", "ordens de pagamento aguardam você").Should().Be("1 ordem de pagamento aguarda você");
        AgendaText.Sentence(AgendaText.Total(38420m), AgendaText.Due("a primeira", new DateOnly(2026, 10, 19), Today))
            .Should().Be("R$ 38.420,00 no total; a primeira vence segunda-feira, 19/10.");
        AgendaText.Due("o primeiro", Today, Today).Should().Be("o primeiro vence hoje");
        AgendaText.Due("o primeiro", Today.AddDays(1), Today).Should().Be("o primeiro vence amanhã");
        AgendaText.Due("o mais antigo", Today.AddDays(-1), Today).Should().Be("o mais antigo venceu ontem");
        AgendaText.Due("o mais antigo", new DateOnly(2026, 10, 2), Today).Should().Be("o mais antigo venceu em 02/10");
        AgendaText.DayMonth(new DateOnly(2025, 12, 30), Today).Should().Be("30/12/2025");
        AgendaText.Sentence(null, " ", "só isto").Should().Be("Só isto.");
        AgendaText.Sentence().Should().BeEmpty();
    }

    [Theory]
    [InlineData("/app/financeiro/ordens-de-pagamento/1?acao=aprovar", true)]
    [InlineData("/app", true)]
    [InlineData("//evil.example", false)]
    [InlineData("https://evil.example/app", false)]
    [InlineData("/app//evil", false)]
    [InlineData("/app\\evil", false)]
    [InlineData("/entrar", false)]
    [InlineData("", false)]
    public void Link_so_do_app(string link, bool valid) => AgendaText.IsAppLink(link).Should().Be(valid);

    [Fact]
    public async Task So_chama_fonte_de_modulo_liberado_e_so_pede_categoria_com_permissao()
    {
        var finance = new FakeSource("finance", ("finance.a", "finance.approve"), ("finance.b", "finance.pay"));
        var hr = new FakeSource("hr", ("hr.a", "hr.edit"));
        var service = Service([finance, hr], released: ["finance"], permissions: ["finance.approve", "hr.edit"]);

        var view = await service.ForUserAsync(cancellationToken: TestContext.Current.CancellationToken);

        hr.Calls.Should().Be(0, "módulo não liberado: a fonte nem é chamada (camada 1)");
        finance.LastRequest!.Categories.Should().BeEquivalentTo(["finance.a"], "sem finance.pay a categoria não é pedida (camada 3)");
        view.Items.Select(i => i.Category).Should().BeEquivalentTo(["finance.a"]);
        view.Total.Should().Be(2);
        view.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Filtro_de_categorias_e_registro()
    {
        var finance = new FakeSource("finance", ("finance.a", "finance.approve"), ("finance.b", "finance.pay"));
        var service = Service([finance], released: ["finance"], permissions: ["finance.approve", "finance.pay"]);

        var view = await service.ForUserAsync(["finance.b"], TestContext.Current.CancellationToken);

        view.Items.Select(i => i.Category).Should().BeEquivalentTo(["finance.b"]);
        service.Registered.Select(c => c.Code).Should().BeEquivalentTo(["finance.a", "finance.b"]);
    }

    [Fact]
    public async Task Fonte_que_falha_ou_demora_vira_falha_parcial_e_as_outras_aparecem()
    {
        var broken = new FakeSource("finance", ("finance.a", "finance.approve")) { Throw = true };
        var slow = new FakeSource("production", ("production.a", "production.approve")) { Delay = TimeSpan.FromSeconds(5) };
        var ok = new FakeSource("hr", ("hr.a", "hr.edit"));
        var service = Service([broken, slow, ok], released: ["finance", "production", "hr"], permissions: ["finance.approve", "production.approve", "hr.edit"], timeoutMs: 100);

        var view = await service.ForUserAsync(cancellationToken: TestContext.Current.CancellationToken);

        view.Items.Select(i => i.Category).Should().BeEquivalentTo(["hr.a"]);
        view.Failures.Select(f => f.Category).Should().BeEquivalentTo(["finance.a", "production.a"]);
        view.Failures.Should().Contain(f => f.Message == "Não foi possível carregar o rótulo de finance.a.");
    }

    [Fact]
    public async Task Item_com_link_fora_do_app_nao_sai()
    {
        var bad = new FakeSource("finance", ("finance.a", "finance.approve")) { EntryLink = "https://evil.example" };
        var service = Service([bad], released: ["finance"], permissions: ["finance.approve"]);

        var view = await service.ForUserAsync(cancellationToken: TestContext.Current.CancellationToken);

        view.Items.Should().BeEmpty();
        view.Failures.Should().ContainSingle(f => f.Category == "finance.a");
    }

    [Fact]
    public async Task Sem_usuario_a_pauta_e_vazia()
    {
        var finance = new FakeSource("finance", ("finance.a", "finance.approve"));
        var service = Service([finance], released: ["finance"], permissions: ["finance.approve"], anonymous: true);

        var view = await service.ForUserAsync(cancellationToken: TestContext.Current.CancellationToken);

        view.Items.Should().BeEmpty();
        finance.Calls.Should().Be(0);
    }

    private static AgendaService Service(IAgendaSource[] sources, string[] released, string[] permissions, int timeoutMs = 2000, bool anonymous = false)
    {
        var holder = new TenantContextHolder();
        holder.Set(new ResolvedTenant(new TenantInfo(Guid.CreateVersion7(), "t", TenantStatus.Active), "T", null, "t.local", "x")
        {
            ReleasedModules = released.ToHashSet(StringComparer.Ordinal),
        });
        return new AgendaService(
            sources, new FixedUser(anonymous ? null : Me), new FixedPermissions(permissions), holder, TimeProvider.System,
            Options.Create(new AgendaOptions { SourceTimeoutMs = timeoutMs }), NullLogger<AgendaService>.Instance);
    }

    private sealed class FixedUser(Guid? id) : ICurrentUser
    {
        public Guid? UserId => id;

        public string? IpAddress => null;
    }

    private sealed class FixedPermissions(string[] permissions) : IPermissionService
    {
        public Task<EffectivePermissions> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectivePermissions(permissions.ToHashSet(StringComparer.Ordinal), false, "1"));

        public Task BumpAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSource(string module, params (string Code, string Permission)[] categories) : IAgendaSource
    {
        public string Module => module;

        public IReadOnlyList<AgendaCategory> Categories { get; } =
            [.. categories.Select(c => new AgendaCategory(c.Code, module, c.Permission, AgendaTier.Unblocks, "o rótulo de " + c.Code))];

        public bool Throw { get; init; }

        public TimeSpan Delay { get; init; }

        public string EntryLink { get; init; } = "/app/x";

        public int Calls { get; private set; }

        public AgendaRequest? LastRequest { get; private set; }

        public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            if (Throw)
            {
                throw new InvalidOperationException("banco fora");
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return [.. request.Categories.Select(c => new AgendaItem(c, c, "porquê", "/app/lista", null, null, AgendaTier.Unblocks, 2, "Abrir",
                [new AgendaEntry("doc", null, EntryLink, null, null)]))];
        }
    }
}
