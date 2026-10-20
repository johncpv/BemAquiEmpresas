using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>CAD-04b: funcionários, com dados pessoais e salário só por rota própria, linha do tempo, desligamento e vínculo.</summary>
public partial class EmployeesTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Usuário num grupo do seed, num grupo com as ações de <c>hr</c> dadas ("hr:view,edit") ou sem grupo; devolve o id também.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "e" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey.StartsWith("hr:", StringComparison.Ordinal))
            {
                var group = new Group { Name = "RH parcial " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
                foreach (var action in seedKey[3..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = action });
                }

                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    /// <summary>Cargo novo com faixa de 3.000 a 3.600 e a jornada de 44 h.</summary>
    private static async Task<Guid> PositionAsync(HttpClient hr, string name = "Montador")
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = name + " " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null });
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        (await hr.PutAsJsonAsync($"/api/v1/hr/positions/{id}/salary", new { salaryMin = 3000m, salaryMax = 3600m, referenceSalary = (decimal?)null })).EnsureSuccessStatusCode();
        return id;
    }

    private static object NewEmployee(Guid positionId, string cpf, decimal salary = 3300m, string? name = null) =>
        new { name = name ?? "Funcionário " + Guid.NewGuid().ToString("N")[..6], cpf, birthDate = "1990-05-10", admissionDate = "2024-03-01", positionId, workScheduleId = (Guid?)null, baseSalary = salary, registration = (string?)null };

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("employee").GetProperty("id").GetGuid();
    }

    /// <summary>Sem ids nem instantes: um GUID pode conter "3500" por acaso.</summary>
    private static string NoIds(string json) => System.Text.RegularExpressions.Regex.Replace(json, @"[0-9a-f]{8}-[0-9a-f-]{27}|\d{4}-\d{2}-\d{2}T[0-9:.+Z-]+", "#");

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private static string NewPis()
    {
        int[] weights = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("1234567890").Take(10).Select(c => c - '0').ToList();
        var check = 11 - (weights.Select((w, i) => w * digits[i]).Sum() % 11);
        digits.Add(check >= 10 ? 0 : check);
        return string.Concat(digits);
    }
}
