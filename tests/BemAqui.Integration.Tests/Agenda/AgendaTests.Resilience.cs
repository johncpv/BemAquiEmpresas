using System.Diagnostics;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Agenda;

public partial class AgendaTests
{
    /// <summary>Fonte que sempre falha (banco fora, bug): a pauta responde com as outras categorias.</summary>
    private sealed class BrokenSource : IAgendaSource
    {
        public string Module => "finance";

        public IReadOnlyList<AgendaCategory> Categories { get; } = [new("finance.test_broken", "finance", "finance.view", AgendaTier.Unblocks, "a categoria quebrada")];

        public Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("fonte de teste fora do ar");
    }

    /// <summary>Fonte que passa do tempo limite de 300 ms.</summary>
    private sealed class SlowSource : IAgendaSource
    {
        public string Module => "hr";

        public IReadOnlyList<AgendaCategory> Categories { get; } = [new("hr.test_slow", "hr", "hr.view", AgendaTier.Overdue, "a categoria lenta")];

        public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            return [];
        }
    }

    /// <summary>CA 10: uma fonte que falha e outra que demora viram "Não foi possível carregar {categoria}."; as demais aparecem.</summary>
    [Fact]
    public async Task Fonte_com_falha_ou_atraso_vira_falha_parcial_e_as_outras_aparecem()
    {
        var (space, _, owner) = await SpaceAsync();
        await OverdueReceivablesAsync(space, owner);
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("Agenda:SourceTimeoutMs", AgendaLimits.DefaultSourceTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureTestServices(s =>
        {
            s.AddScoped<IAgendaSource, BrokenSource>();
            s.AddScoped<IAgendaSource, SlowSource>();
        }));
        var (_, client) = await ActorAsync(space, BemAqui.Identity.Domain.Group.OwnerSeedKey, factory);
        await AgendaAsync(client); // aquece o host novo: a medida é do corte da fonte lenta, não da primeira carga das fontes

        var watch = Stopwatch.StartNew();
        var view = await AgendaAsync(client);
        watch.Stop();

        Category(view, "finance.receivables_overdue").Should().NotBeNull("as outras categorias aparecem");
        var failures = view.GetProperty("failures").EnumerateArray().ToDictionary(f => f.GetProperty("category").GetString()!, f => f.GetProperty("message").GetString());
        failures.Should().Contain("finance.test_broken", "Não foi possível carregar a categoria quebrada.");
        failures.Should().Contain("hr.test_slow", "Não foi possível carregar a categoria lenta.");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.5), "a fonte lenta é cortada no tempo limite, não espera os 3 s");
    }

    /// <summary>
    /// CA 12: 50 mil títulos a receber, 50 mil a pagar, 5 mil OS e 50 mil orçamentos; o Proprietário abre a pauta em menos de 1 s, sem falha
    /// de tempo limite. Folga: aquece uma vez e vale a melhor de três medições (absorve o ruído da máquina de CI).
    /// </summary>
    [Fact]
    public async Task Pauta_do_Proprietario_com_volume_chega_em_menos_de_1_s()
    {
        var (space, ownerId, owner) = await SpaceAsync();
        var customer = await PartyAsync(owner, "Customer");
        var supplier = await PartyAsync(owner, "Supplier");
        await WithDbAsync(space, async db =>
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var expense = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
            for (var batch = 0; batch < 10; batch++)
            {
                for (var i = 0; i < 5_000; i++)
                {
                    var n = (batch * 5_000) + i;
                    db.Receivables.Add(new Receivable
                    {
                        Number = $"V-{n:D6}", PartyId = customer, AccountId = revenue, Description = "Volume", Amount = 100m + (n % 50), IssueDate = Today.AddDays(-400),
                        DueDate = Today.AddDays((n % 360) - 300), Status = n % 3 == 0 ? ReceivableStatus.Paid : ReceivableStatus.Open, Origin = DocumentOrigin.Manual, CreatedAt = DateTimeOffset.UtcNow,
                    });
                    db.Payables.Add(new Payable
                    {
                        Number = $"VP-{n:D6}", PartyId = supplier, AccountId = expense, Description = "Volume", Amount = 200m, IssueDate = Today.AddDays(-400),
                        DueDate = Today.AddDays((n % 400) - 200), Status = n % 4 == 0 ? PayableStatus.Paid : PayableStatus.Open, Origin = DocumentOrigin.Manual, CreatedAt = DateTimeOffset.UtcNow,
                    });
                    db.Set<Quote>().Add(new Quote
                    {
                        Number = $"ORC-V-{n:D6}", Revision = 0, IsCurrent = true, Status = n % 5 == 0 ? QuoteStatus.Sent : QuoteStatus.Draft, PartyId = customer, TotalPrice = 1_000m,
                        ValidUntil = Today.AddDays((n % 60) - 20), CreatedAt = DateTimeOffset.UtcNow, CreatedBy = ownerId,
                    });
                    if (n < 5_000)
                    {
                        db.Set<ProductionOrder>().Add(new ProductionOrder
                        {
                            Number = $"OS-V-{n:D5}", Type = ProductionOrderType.Fabrication, ProjectId = Guid.CreateVersion7(), ContractId = Guid.CreateVersion7(), ProjectItemId = Guid.CreateVersion7(),
                            Quantity = 1m, Status = ProductionOrderStatus.Open,
                        });
                    }
                }

                db.ChangeTracker.DetectChanges();
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }

            return 0;
        });

        var warm = await AgendaAsync(owner);
        warm.GetProperty("failures").GetArrayLength().Should().Be(0, "nenhuma fonte passa do tempo limite com o volume");
        Category(warm, "finance.receivables_overdue")!.Value.GetProperty("entries").GetArrayLength().Should().Be(AgendaLimits.EntriesPerCategory);
        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            var view = await AgendaAsync(owner);
            watch.Stop();
            view.GetProperty("failures").GetArrayLength().Should().Be(0);
            best = watch.Elapsed < best ? watch.Elapsed : best;
        }

        best.Should().BeLessThan(TimeSpan.FromSeconds(1), "CA 12: a pauta abre em menos de 1 s");
    }
}
