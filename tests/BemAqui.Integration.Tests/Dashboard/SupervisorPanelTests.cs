using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Host.Persistence;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Contracts;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-03: painel do supervisor com um dataset fixo gravado direto (cada teste numa linha própria, para não somar com os outros): três OS
/// (atrasada na qualidade, em risco no corte, no prazo), duas NC abertas e uma encerrada, faltas com e sem compra, pausas e decisões de
/// qualidade. Banco desta classe.
/// </summary>
public partial class SupervisorPanelTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/dashboard/supervisor";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? Code(JsonElement problem) => problem.TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string Tag() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Usuário num grupo do seed (seed_key), num grupo só com as ações dadas ("production.view", ...) ou sem grupo.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey, params string[] permissions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "s" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null || permissions.Length > 0)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey is not null)
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }
            else
            {
                var group = new Group { Name = "Supervisor " + Guid.NewGuid().ToString("N")[..8], Description = "Teste do painel do supervisor" };
                db.Groups.Add(group);
                foreach (var module in permissions.Select(p => p.Split('.')[0]).Distinct())
                {
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                }

                foreach (var permission in permissions)
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = permission.Split('.')[0], Action = permission.Split('.')[1] });
                }

                groupId = group.Id;
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static async Task<JsonElement> OkAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, url + ": " + body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task SetReleasedAsync(string module, bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private sealed record Dataset(
        string Line, Guid Cut, Guid Inspection, Guid OrderLate, Guid OrderRisk, Guid OrderOnTime, string NumberLate, string NumberRisk, string NumberOnTime, Guid QualityStepLate,
        Guid CutStepLate, Guid CutStepRisk, Guid NcOld, Guid NcNew, Guid ItemLate, Guid ItemRisk, Guid ItemOnTime, string RequestNumber, string PurchaseOrderNumber,
        DateOnly RiskStart, DateOnly PurchaseExpected);

    /// <summary>Projeto direto no banco (orçamento, contrato e projeto), com a data prometida.</summary>
    private static async Task<Guid> ProjectAsync(TenantDbContext db, string number, DateOnly? promised)
    {
        var party = Guid.CreateVersion7();
        var quote = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO commercial.quotes (id, number, revision, is_current, status, party_id, pricing_mode, total_cost, total_price, valid_until, created_at, is_deleted)
            VALUES ({quote}, {"Q" + number}, 0, true, 'Approved', {party}, 'Markup', 0, 1000, {Today}, now(), false)
            """);
        var account = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
        var contract = new Contract { Number = "CT" + number, QuoteId = quote, PartyId = party, SignedAt = Today.AddDays(-30), TotalValue = 1000m, RevenueAccountId = account, CreatedAt = DateTimeOffset.UtcNow };
        var project = new Project { Number = "PR" + number, ContractId = contract.Id, PartyId = party, Name = "Projeto " + number, Status = ProjectStatus.InProgress, PromisedDate = promised, CreatedAt = DateTimeOffset.UtcNow };
        db.Contracts.Add(contract);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static ProductionOrder Order(string number, Guid projectId, ProductionOrderStatus status) => new()
    {
        Number = number, Type = ProductionOrderType.Fabrication, ProjectId = projectId, ContractId = Guid.CreateVersion7(), ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, Status = status,
        CreatedAt = DateTimeOffset.UtcNow, QualityAttempt = 1, QualityAttemptStartedAt = DateTimeOffset.UtcNow.AddDays(-3),
    };

    private static ProductionOrderStep Step(Guid order, int sequence, string code, string name, Guid center, ProductionStepStatus status, decimal hours, bool quality = false) => new()
    {
        OrderId = order, Sequence = sequence, Code = code, Name = name, WorkCenterId = center, Status = status, StandardHours = hours, IsQualityStep = quality,
    };

    private static async Task<Guid> EmployeeAsync(TenantDbContext db, Guid? userId)
    {
        var position = new JobPosition { Name = "Montador " + Tag(), MonthlyHours = 220m, CreatedAt = DateTimeOffset.UtcNow };
        db.JobPositions.Add(position);
        var employee = new Employee
        {
            Name = "Diego " + Tag(), PositionId = position.Id, AdmissionDate = Today.AddYears(-1), Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            BirthDate = new DateOnly(1990, 1, 1), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        if (userId is { } user)
        {
            await db.Users.Where(u => u.Id == user).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmployeeId, employee.Id));
        }

        return employee.Id;
    }

    /// <summary>
    /// Dataset fixo numa linha nova: Corte e Inspeção; OS atrasada (prometido há 3 dias) na qualidade, OS em risco (prevista depois da prometida) no
    /// corte e OS no prazo no corte; NC aberta há 5 dias, em ação há 1 dia e uma encerrada; faltas: a atrasada sem compra (início hoje), a em risco
    /// com SC aberta (início no 3º dia útil) e a no prazo com pedido (início em 40 dias).
    /// </summary>
    private async Task<Dataset> SeedAsync()
    {
        var tag = Tag();
        var line = "Linha " + tag;
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var calendar = await scope.ServiceProvider.GetRequiredService<CalendarService>().DefaultEntityAsync();
        var cut = new WorkCenter { Name = "Corte " + tag, Line = line, CalendarId = calendar.Id, CreatedAt = DateTimeOffset.UtcNow.AddYears(-1) };
        cut.SetShifts([new Shift("07:00", "16:00")]);
        var inspection = new WorkCenter { Name = "Inspeção " + tag, Line = line, CalendarId = calendar.Id, CreatedAt = DateTimeOffset.UtcNow.AddYears(-1) };
        inspection.SetShifts([new Shift("07:00", "16:00")]);
        db.WorkCenters.AddRange(cut, inspection);
        await db.SaveChangesAsync();

        var late = await ProjectAsync(db, "L" + tag, Today.AddDays(-3));
        var risk = await ProjectAsync(db, "R" + tag, Today.AddDays(10));
        var onTime = await ProjectAsync(db, "T" + tag, Today.AddDays(60));
        db.ScheduleProjectForecasts.Add(new ScheduleProjectForecast { ProjectId = risk, ForecastDate = Today.AddDays(15), FabricationEnd = Today.AddDays(12), ComputedAt = DateTimeOffset.UtcNow, RunId = Guid.CreateVersion7() });
        db.ScheduleProjectForecasts.Add(new ScheduleProjectForecast { ProjectId = onTime, ForecastDate = Today.AddDays(50), FabricationEnd = Today.AddDays(45), ComputedAt = DateTimeOffset.UtcNow, RunId = Guid.CreateVersion7() });

        var osLate = Order("OS-L" + tag, late, ProductionOrderStatus.Quality);
        var osRisk = Order("OS-R" + tag, risk, ProductionOrderStatus.InProgress);
        var osOnTime = Order("OS-T" + tag, onTime, ProductionOrderStatus.Open);
        db.ProductionOrders.AddRange(osLate, osRisk, osOnTime);
        var cutLate = Step(osLate.Id, 1, "C", "Corte", cut.Id, ProductionStepStatus.Done, 2m);
        var qLate = Step(osLate.Id, 2, "Q", "Qualidade", inspection.Id, ProductionStepStatus.Ready, 0.5m, quality: true);
        var cutRisk = Step(osRisk.Id, 1, "C", "Corte", cut.Id, ProductionStepStatus.InProgress, 4m);
        var qRisk = Step(osRisk.Id, 2, "Q", "Qualidade", inspection.Id, ProductionStepStatus.Waiting, 0.5m, quality: true);
        var cutOnTime = Step(osOnTime.Id, 1, "C", "Corte", cut.Id, ProductionStepStatus.Ready, 3m);
        var qOnTime = Step(osOnTime.Id, 2, "Q", "Qualidade", inspection.Id, ProductionStepStatus.Waiting, 0.5m, quality: true);
        db.ProductionOrderSteps.AddRange(cutLate, qLate, cutRisk, qRisk, cutOnTime, qOnTime);

        // Início previsto (o plano): a atrasada hoje, a em risco no 3º dia útil, a no prazo em 40 dias.
        var working = await scope.ServiceProvider.GetRequiredService<IBusinessCalendar>().WorkingDatesAsync(Today.AddDays(1), Today.AddDays(20));
        var riskStart = working[2];
        db.ScheduleStepForecasts.AddRange(
            new ScheduleStepForecast { StepId = qLate.Id, ProductionOrderId = osLate.Id, Sequence = 2, Hours = 0.5m, PlannedStart = Today, PlannedEnd = Today, RunId = Guid.CreateVersion7() },
            new ScheduleStepForecast { StepId = cutRisk.Id, ProductionOrderId = osRisk.Id, Sequence = 1, Hours = 4m, PlannedStart = riskStart, PlannedEnd = riskStart, RunId = Guid.CreateVersion7() },
            new ScheduleStepForecast { StepId = cutOnTime.Id, ProductionOrderId = osOnTime.Id, Sequence = 1, Hours = 3m, PlannedStart = Today.AddDays(40), PlannedEnd = Today.AddDays(40), RunId = Guid.CreateVersion7() });
        await db.SaveChangesAsync();

        var ncOld = new NonConformity
        {
            Number = "NC-O" + tag, OrderId = osLate.Id, OrderStepId = qLate.Id, Attempt = 1, Description = "Solda fora do padrão", ResponsibleUserId = Guid.CreateVersion7(), ReturnToStepId = cutLate.Id,
            OpenedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        var ncNew = new NonConformity
        {
            Number = "NC-N" + tag, OrderId = osLate.Id, OrderStepId = qLate.Id, Attempt = 2, Description = "Risco na pintura", ResponsibleUserId = Guid.CreateVersion7(), ReturnToStepId = cutLate.Id,
            Status = NonConformityStatus.InAction, OpenedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var ncClosed = new NonConformity
        {
            Number = "NC-C" + tag, OrderId = osLate.Id, OrderStepId = qLate.Id, Attempt = 3, Description = "Encerrada", ResponsibleUserId = Guid.CreateVersion7(), ReturnToStepId = cutLate.Id,
            Status = NonConformityStatus.Closed, OpenedAt = DateTimeOffset.UtcNow.AddDays(-9), ClosedAt = DateTimeOffset.UtcNow.AddDays(-8),
        };
        db.NonConformities.AddRange(ncOld, ncNew, ncClosed);

        // Faltas (EST-02): 6 da atrasada, 3 da em risco, 2 da no prazo.
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var location = await db.Locations.Select(l => l.Id).FirstAsync();
        BemAqui.Inventory.Domain.Item Material(string name) => new() { Type = ItemType.RawMaterial, Code = "MP-" + Tag(), Name = name + " " + tag, UnitId = unit, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var (iLate, iRisk, iOnTime) = (Material("Chapa"), Material("Cabo"), Material("Motor"));
        db.Items.AddRange(iLate, iRisk, iOnTime);
        BemAqui.Inventory.Domain.Reservation Short(ProductionOrder o, BemAqui.Inventory.Domain.Item i, decimal requested, decimal reserved) => new()
        {
            ItemId = i.Id, LocationId = location, ProductionOrderId = o.Id, ProjectId = o.ProjectId, BomRevision = 1, RequestedQuantity = requested, Quantity = reserved, Origin = "BomReleased",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Reservations.AddRange(Short(osLate, iLate, 10m, 4m), Short(osRisk, iRisk, 3m, 0m), Short(osOnTime, iOnTime, 2m, 0m));

        // Compra (CMP-01/CMP-03a): SC aberta para a em risco; SC pedida com pedido enviado para a no prazo.
        var request = new PurchaseRequest { Number = "SC-" + tag, Origin = PurchaseRequestOrigin.BomShortage, ProductionOrderId = osRisk.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var ordered = new PurchaseRequest { Number = "SC-P" + tag, Origin = PurchaseRequestOrigin.BomShortage, ProductionOrderId = osOnTime.Id, Status = PurchaseRequestStatus.Ordered, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.PurchaseRequests.AddRange(request, ordered);
        db.PurchaseRequestLines.Add(new PurchaseRequestLine { RequestId = request.Id, Origin = PurchaseRequestOrigin.BomShortage, Sequence = 1, ItemId = iRisk.Id, Quantity = 3m, NeededBy = riskStart, ProductionOrderId = osRisk.Id, ProjectId = risk });
        var orderedLine = new PurchaseRequestLine
        {
            RequestId = ordered.Id, Origin = PurchaseRequestOrigin.BomShortage, Sequence = 1, ItemId = iOnTime.Id, Quantity = 2m, NeededBy = Today.AddDays(40), ProductionOrderId = osOnTime.Id, ProjectId = onTime,
            Status = PurchaseRequestStatus.Ordered,
        };
        db.PurchaseRequestLines.Add(orderedLine);
        var expected = Today.AddDays(7);
        var po = new PurchaseOrder
        {
            Number = "PC-" + tag, PartyId = Guid.CreateVersion7(), Status = PurchaseOrderStatus.Sent, PaymentTermId = Guid.CreateVersion7(), ExpectedDate = expected, Subtotal = 100m, Total = 100m,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.PurchaseOrders.Add(po);
        db.PurchaseOrderLines.Add(new PurchaseOrderLine { OrderId = po.Id, Sequence = 1, ItemId = iOnTime.Id, Quantity = 2m, UnitPrice = 50m, LineTotal = 100m, RequestLineIds = [orderedLine.Id] });
        await db.SaveChangesAsync();

        return new Dataset(line, cut.Id, inspection.Id, osLate.Id, osRisk.Id, osOnTime.Id, osLate.Number, osRisk.Number, osOnTime.Number, qLate.Id, cutLate.Id, cutRisk.Id, ncOld.Id, ncNew.Id,
            iLate.Id, iRisk.Id, iOnTime.Id, request.Number, po.Number, riskStart, expected);
    }

    private static string Q(Dataset d) => "?line=" + Uri.EscapeDataString(d.Line);

    [Fact]
    public async Task Supervisao_abre_o_painel_sem_o_modulo_dashboard_e_sem_dashboard_view()
    {
        var d = await SeedAsync();
        var (_, supervisor) = await ActorAsync("production_supervision");
        await SetReleasedAsync("dashboard", false);
        try
        {
            var overview = await OkAsync(supervisor, Base);
            overview.GetProperty("blocks").EnumerateArray().Select(b => b.GetString()).Should().Equal("quality", "shortages", "board", "capacity", "indicators");
            overview.GetProperty("lines").EnumerateArray().Select(l => l.GetString()).Should().Contain(d.Line);
            overview.GetProperty("canDecideQuality").GetBoolean().Should().BeTrue("o seed patch da PRD-05 dá production.approve_quality à Supervisão");
            overview.GetProperty("canCreatePurchaseRequest").GetBoolean().Should().BeFalse("a Supervisão do seed não tem purchasing");
            foreach (var block in new[] { "/board", "/capacity", "/quality", "/indicators", "/shortages" })
            {
                (await supervisor.GetAsync(Base + block + Q(d))).StatusCode.Should().Be(HttpStatusCode.OK, block);
            }

            (await supervisor.GetAsync("/api/v1/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "o painel do dono continua exigindo o módulo dashboard");
        }
        finally
        {
            await SetReleasedAsync("dashboard", true);
        }
    }

    [Fact]
    public async Task Quadro_compacto_mostra_as_colunas_e_so_as_atrasadas_e_em_risco_como_o_Kanban()
    {
        var d = await SeedAsync();
        var (_, supervisor) = await ActorAsync("production_supervision");
        var block = await OkAsync(supervisor, Base + "/board" + Q(d));
        block.GetProperty("openCount").GetInt32().Should().Be(3);
        block.GetProperty("lateCount").GetInt32().Should().Be(1);
        block.GetProperty("atRiskCount").GetInt32().Should().Be(1);
        var cards = block.GetProperty("cards").EnumerateArray().ToList();
        cards.Select(c => c.GetProperty("number").GetString()).Should().Equal(d.NumberLate, d.NumberRisk);
        cards[0].GetProperty("risk").GetString().Should().Be("Late");
        cards[0].GetProperty("delayDays").GetInt32().Should().Be(3);
        cards[1].GetProperty("risk").GetString().Should().Be("AtRisk");
        var columns = block.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("key").GetString()!);
        columns["C"].GetProperty("count").GetInt32().Should().Be(2);
        columns["C"].GetProperty("atRiskCount").GetInt32().Should().Be(1);
        columns["Q"].GetProperty("count").GetInt32().Should().Be(1);
        columns["Q"].GetProperty("lateCount").GetInt32().Should().Be(1);

        // Os mesmos números do Kanban (PRD-02) na mesma linha.
        var kanban = await OkAsync(supervisor, "/api/v1/production/board" + Q(d));
        kanban.GetProperty("lateCount").GetInt32().Should().Be(block.GetProperty("lateCount").GetInt32());
        kanban.GetProperty("atRiskCount").GetInt32().Should().Be(block.GetProperty("atRiskCount").GetInt32());
        foreach (var column in kanban.GetProperty("columns").EnumerateArray())
        {
            var mine = columns[column.GetProperty("key").GetString()!];
            mine.GetProperty("count").GetInt32().Should().Be(column.GetProperty("count").GetInt32());
            mine.GetProperty("remainingHours").GetDecimal().Should().Be(column.GetProperty("remainingHours").GetDecimal());
        }

        var otherLine = await OkAsync(supervisor, Base + "/board?line=" + Uri.EscapeDataString("Outra " + Tag()));
        otherLine.GetProperty("openCount").GetInt32().Should().Be(0, "o filtro de linha vale para o bloco");
    }

    [Fact]
    public async Task Capacidade_da_semana_atual_e_das_proximas_4_dos_centros_da_linha()
    {
        var d = await SeedAsync();
        var (_, supervisor) = await ActorAsync("production_supervision");
        var block = await OkAsync(supervisor, Base + "/capacity" + Q(d));
        block.GetProperty("weeks").GetArrayLength().Should().Be(5);
        block.GetProperty("centers").EnumerateArray().Select(c => c.GetProperty("workCenterId").GetGuid()).Should().BeEquivalentTo([d.Cut, d.Inspection]);
        var load = await OkAsync(supervisor, "/api/v1/production/planning/load?weeks=5&line=" + Uri.EscapeDataString(d.Line));
        block.GetProperty("factory").GetProperty("cells").EnumerateArray().Select(c => c.GetProperty("capacityHours").GetDecimal())
            .Should().Equal(load.GetProperty("load").GetProperty("factory").GetProperty("cells").EnumerateArray().Select(c => c.GetProperty("capacityHours").GetDecimal()), "os mesmos números da Ocupação");
        block.GetProperty("factory").GetProperty("cells")[0].GetProperty("band").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Qualidade_segue_a_segregacao_e_sem_approve_quality_vira_contagem()
    {
        var d = await SeedAsync();
        var (involvedId, involved) = await ActorAsync("production_supervision");
        var (_, other) = await ActorAsync("production_supervision");
        await WithDbAsync(async db =>
        {
            var employee = await EmployeeAsync(db, involvedId);
            db.LaborEntries.Add(new LaborEntry { OrderId = d.OrderLate, OrderStepId = d.CutStepLate, EmployeeId = employee, StartedAt = DateTimeOffset.UtcNow.AddDays(-2), EndedAt = DateTimeOffset.UtcNow.AddDays(-2).AddHours(2), CreatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });

        var free = await OkAsync(other, Base + "/quality" + Q(d));
        free.GetProperty("canDecide").GetBoolean().Should().BeTrue();
        free.GetProperty("pendingCount").GetInt32().Should().Be(1);
        free.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("stepId").GetGuid()).Should().Equal(d.QualityStepLate);
        free.GetProperty("steps")[0].GetProperty("selfInspection").GetBoolean().Should().BeFalse();

        var mine = await OkAsync(involved, Base + "/quality" + Q(d));
        mine.GetProperty("pendingCount").GetInt32().Should().Be(1);
        mine.GetProperty("steps").GetArrayLength().Should().Be(0, "quem apontou etapa da tentativa não decide (PRD-05 CA 5)");

        // NC abertas da mais antiga para a mais nova; a encerrada fica fora.
        var ncs = mine.GetProperty("nonConformities").EnumerateArray().ToList();
        mine.GetProperty("openNonConformities").GetInt32().Should().Be(2);
        ncs.Select(n => n.GetProperty("id").GetGuid()).Should().Equal(d.NcOld, d.NcNew);
        ncs[0].GetProperty("ageDays").GetInt32().Should().BeGreaterThan(ncs[1].GetProperty("ageDays").GetInt32());
        ncs[1].GetProperty("status").GetString().Should().Be("InAction");
        ncs[0].GetProperty("orderNumber").GetString().Should().Be(d.NumberLate);

        // Só production.view: a fila vira contagem e as NC continuam visíveis.
        var (_, viewer) = await ActorAsync(null, "production.view");
        var counted = await OkAsync(viewer, Base + "/quality" + Q(d));
        counted.GetProperty("canDecide").GetBoolean().Should().BeFalse();
        counted.GetProperty("pendingCount").GetInt32().Should().Be(1);
        counted.GetProperty("steps").GetArrayLength().Should().Be(0);
        counted.GetProperty("nonConformities").GetArrayLength().Should().Be(2);

        // Sem outro aprovador elegível, o envolvido decide com a marca "Autoinspeção".
        var others = await WithDbAsync(db => db.Users.Where(u => u.Id != involvedId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync());
        await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Inactive)));
        try
        {
            var self = await OkAsync(involved, Base + "/quality" + Q(d));
            self.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("stepId").GetGuid()).Should().Equal(d.QualityStepLate);
            self.GetProperty("steps")[0].GetProperty("selfInspection").GetBoolean().Should().BeTrue();
        }
        finally
        {
            await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Active)));
        }
    }

    [Fact]
    public async Task Faltas_que_bloqueiam_em_5_dias_uteis_vem_primeiro_com_a_situacao_da_compra()
    {
        var d = await SeedAsync();
        var (_, supervisor) = await ActorAsync("production_supervision");
        var block = await OkAsync(supervisor, Base + "/shortages" + Q(d));
        block.GetProperty("total").GetInt32().Should().Be(3);
        block.GetProperty("blocking").GetInt32().Should().Be(2);
        block.GetProperty("canCreateRequest").GetBoolean().Should().BeFalse();
        var items = block.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("orderNumber").GetString()).Should().Equal(d.NumberLate, d.NumberRisk, d.NumberOnTime);

        items[0].GetProperty("itemId").GetGuid().Should().Be(d.ItemLate);
        items[0].GetProperty("shortQty").GetDecimal().Should().Be(6m);
        items[0].GetProperty("blocks").GetBoolean().Should().BeTrue();
        items[0].GetProperty("blocksInDays").GetInt32().Should().Be(0);
        items[0].GetProperty("purchase").GetString().Should().Be("None");
        items[0].GetProperty("purchaseLabel").GetString().Should().Be("Sem solicitação");
        items[0].GetProperty("projectNumber").GetString().Should().StartWith("PRL");

        items[1].GetProperty("blocksInDays").GetInt32().Should().Be(3);
        items[1].GetProperty("neededOn").GetString().Should().Be(d.RiskStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        items[1].GetProperty("purchaseLabel").GetString().Should().Be("Solicitada " + d.RequestNumber);

        items[2].GetProperty("blocks").GetBoolean().Should().BeFalse();
        items[2].GetProperty("purchase").GetString().Should().Be("Ordered");
        items[2].GetProperty("purchaseLabel").GetString().Should().Be($"Pedido {d.PurchaseOrderNumber} previsto para {d.PurchaseExpected:dd/MM}");

        // "Gerar solicitação" só com purchasing.create; a situação da compra não exige purchasing.view.
        var (_, buyer) = await ActorAsync(null, "production.view", "inventory.view", "purchasing.create");
        (await OkAsync(buyer, Base + "/shortages" + Q(d))).GetProperty("canCreateRequest").GetBoolean().Should().BeTrue();

        // Sem inventory.view: o bloco não é montado.
        var (_, noStock) = await ActorAsync(null, "production.view");
        (await OkAsync(noStock, Base)).GetProperty("blocks").EnumerateArray().Select(b => b.GetString()).Should().NotContain("shortages");
        var denied = await noStock.GetAsync(Base + "/shortages" + Q(d));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("forbidden");
    }

    [Fact]
    public async Task Indicadores_somam_as_horas_paradas_por_motivo_e_a_taxa_de_NC_dos_ultimos_30_dias()
    {
        var d = await SeedAsync();
        var (_, supervisor) = await ActorAsync("production_supervision");
        var now = DateTimeOffset.UtcNow;
        await WithDbAsync(async db =>
        {
            var employee = await EmployeeAsync(db, null);
            LaborEntry Entry(DateTimeOffset start) => new() { OrderId = d.OrderRisk, OrderStepId = d.CutStepRisk, EmployeeId = employee, StartedAt = start, EndedAt = start.AddHours(4), CreatedAt = now };
            var (e1, e2, e3, e4, e5) = (Entry(now.AddDays(-2).AddHours(-5)), Entry(now.AddDays(-1).AddHours(-5)), Entry(now.AddDays(-3).AddHours(-5)), Entry(now.AddDays(-40)), Entry(now.AddDays(-4).AddHours(-5)));
            db.LaborEntries.AddRange(e1, e2, e3, e4, e5);
            StepPause Pause(LaborEntry e, PauseReason reason, int minutes) => new() { LaborEntryId = e.Id, OrderId = e.OrderId, Reason = reason, StartedAt = e.StartedAt.AddMinutes(30), EndedAt = e.StartedAt.AddMinutes(30 + minutes) };
            db.StepPauses.AddRange(Pause(e1, PauseReason.Material, 90), Pause(e2, PauseReason.Machine, 30), Pause(e3, PauseReason.Break, 60), Pause(e4, PauseReason.Material, 60), Pause(e5, PauseReason.Other, 12));
            QualityDecision Decision(int attempt, QualityResult result, int daysAgo) => new() { OrderId = d.OrderLate, StepId = d.QualityStepLate, Attempt = attempt, Result = result, DecidedAt = now.AddDays(-daysAgo) };
            db.QualityDecisions.AddRange(Decision(1, QualityResult.Rejected, 6), Decision(2, QualityResult.Approved, 5), Decision(3, QualityResult.Approved, 4), Decision(4, QualityResult.Approved, 3),
                Decision(5, QualityResult.Rejected, 45));
            return await db.SaveChangesAsync();
        });

        var block = await OkAsync(supervisor, Base + "/indicators" + Q(d));
        block.GetProperty("from").GetString().Should().Be(Today.AddDays(-29).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var stoppages = block.GetProperty("stoppages").EnumerateArray().ToDictionary(s => s.GetProperty("reason").GetString()!);
        stoppages.Keys.Should().Equal("Material", "Machine", "Other");
        stoppages["Material"].GetProperty("hours").GetDecimal().Should().Be(1.5m, "a pausa de 40 dias atrás fica fora");
        stoppages["Material"].GetProperty("label").GetString().Should().Be("Material");
        stoppages["Material"].GetProperty("orders")[0].GetProperty("orderId").GetGuid().Should().Be(d.OrderRisk);
        stoppages["Machine"].GetProperty("hours").GetDecimal().Should().Be(0.5m);
        stoppages["Machine"].GetProperty("label").GetString().Should().Be("Máquina");
        stoppages["Other"].GetProperty("hours").GetDecimal().Should().Be(0.2m);
        block.GetProperty("stoppedHours").GetDecimal().Should().Be(2.2m, "intervalo não é parada");

        var rate = block.GetProperty("qualityRates").EnumerateArray().Single();
        rate.GetProperty("stepCode").GetString().Should().Be("Q");
        rate.GetProperty("decisions").GetInt32().Should().Be(4);
        rate.GetProperty("rejections").GetInt32().Should().Be(1);
        rate.GetProperty("ratePercent").GetDecimal().Should().Be(25.0m);
    }
}
