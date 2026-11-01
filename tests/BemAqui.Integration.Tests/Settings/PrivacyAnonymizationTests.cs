using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Privacy;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.Integration.Tests.Settings;

/// <summary>
/// LGPD-01b: tabela de retenção, prévia por módulo, anonimização com o nome digitado e o 2FA de novo, uma transação por módulo com o resultado
/// no pedido, parcial e retentativa, canário em todas as tabelas do tenant, no outbox, nos argumentos do job e no log, arquivos apagados e
/// retidos, e o <c>SubjectAnonymized</c> consumido pelo Financeiro. Banco desta classe (tenant compartilhado pelos testes: contagens relativas).
/// </summary>
public partial class PrivacyAnonymizationTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string HostA = "a.localhost";
    private const string Api = "/api/v1/settings/privacy-requests";

    private readonly FakeScanner _scanner = new();
    private readonly CapturingJobClient _jobs = new();
    private readonly LogSink _logs = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bqe-lgpd01b-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= Build(_ => { });

    private WebApplicationFactory<Program> Build(Action<IServiceCollection> extra) => tenancy.Factory.WithWebHostBuilder(b => b
        .UseSetting("Storage:Root", _storage)
        .ConfigureTestServices(s =>
        {
            s.AddSingleton<IVirusScanner>(_scanner);
            s.RemoveAll<IBackgroundJobClient>();
            s.AddSingleton<IBackgroundJobClient>(_jobs);
            s.AddSingleton<ILogEventSink>(_logs);
            extra(s);
        }));

    public void Dispose()
    {
        _factory?.Dispose();
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Tudo o que passou pelo Serilog do host (mensagem, propriedades e exceção), para o canário do log (CA 6).</summary>
    private sealed class LogSink : ILogEventSink
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            Lines.Enqueue(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(' ', logEvent.Properties.Values) + " " + logEvent.Exception);
        }
    }

    private sealed class Authenticator(byte[] secret)
    {
        private long _last = long.MinValue;

        public string Next()
        {
            // O servidor aceita o passo anterior, o atual e o próximo. O código do passo anterior vence quando o relógio vira o passo: nos
            // últimos 10 s do passo ele podia vencer no meio da requisição (2FA "inválido" intermitente), então ali já vai o código do atual.
            var now = DateTimeOffset.UtcNow;
            var late = now.ToUnixTimeSeconds() % Totp.StepSeconds >= Totp.StepSeconds - 10;
            var step = Math.Max(_last + 1, Totp.StepAt(now) - (late ? 0 : 1));
            _last = step;
            return Totp.Code(secret, step);
        }
    }

    private sealed record Actor(Guid Id, HttpClient Client, Authenticator? Codes, string Email);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> action, WebApplicationFactory<Program>? factory = null)
    {
        var f = factory ?? Factory;
        var info = (await f.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await f.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action) => InTenantAsync(sp => action(sp.GetRequiredService<TenantDbContext>()));

    /// <summary>Usuário num grupo do seed ou num grupo próprio ("settings:view,edit"), com o 2FA ligado quando pedido.</summary>
    private async Task<Actor> ActorAsync(string? grants, bool totp = false, WebApplicationFactory<Program>? factory = null)
    {
        var f = factory ?? Factory;
        var email = "a" + Guid.NewGuid().ToString("N")[..10] + "@teste.com";
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        var secret = Totp.NewSecret();
        stamp = await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            if (grants is not null)
            {
                Guid groupId;
                if (grants.Contains(':', StringComparison.Ordinal))
                {
                    var group = new Group { Name = "Anonimização " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                    db.Groups.Add(group);
                    foreach (var part in grants.Split(';'))
                    {
                        var module = part[..part.IndexOf(':', StringComparison.Ordinal)];
                        db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                        foreach (var action in part[(module.Length + 1)..].Split(','))
                        {
                            db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                        }
                    }

                    groupId = group.Id;
                }
                else
                {
                    groupId = await db.Groups.Where(g => g.SeedKey == grants).Select(g => g.Id).SingleAsync();
                }

                db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            }

            var user = await db.Users.SingleAsync(u => u.Id == id);
            if (totp)
            {
                user.BeginTotpSetup(f.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenancy.TenantA.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
                user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return user.SecurityStamp;
        }, f);
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Host = HostA;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, HostA, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        return new Actor(id, client, totp ? new Authenticator(secret) : null, email);
    }

    private static HttpRequestMessage Post(string url, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static string Today => DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(DateTimeOffset.UtcNow).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Pedido de Anonimização (ou Eliminação) do titular, já com a identidade conferida.</summary>
    private static async Task<JsonElement> RequestAsync(HttpClient owner, string subjectType, Guid subjectId, string kind = "Anonymization", bool verify = true)
    {
        var created = await owner.SendAsync(Post(Api, new { subjectType, subjectId, kind, requestedOn = Today, channel = "Email", notes = "Pedido por e-mail" }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var request = await JsonAsync(created);
        if (!verify)
        {
            return request;
        }

        var verified = await owner.SendAsync(Post($"{Api}/{request.GetProperty("id").GetGuid()}/identity", new { method = "DocumentPresented", checkedOn = Today, notes = "RG conferido", version = request.GetProperty("version").GetUInt32() }));
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        return await JsonAsync(verified);
    }

    private static async Task<JsonElement> PreviewAsync(HttpClient owner, Guid id)
    {
        var response = await owner.SendAsync(Post($"{Api}/{id}/anonymization/preview", null));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    /// <summary>Itens da prévia de um módulo, como (ação, rótulo, contagem, retido até).</summary>
    private static List<(string Action, string Label, int Count, string? Until)> Items(JsonElement preview, string module) =>
        preview.GetProperty("modules").EnumerateArray().Where(m => m.GetProperty("module").GetString() == module)
            .SelectMany(m => m.GetProperty("items").EnumerateArray())
            .Select(i => (i.GetProperty("action").GetString()!, i.GetProperty("label").GetString()!, i.GetProperty("count").GetInt32(),
                i.TryGetProperty("retainedUntil", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null)).ToList();

    private static Task<HttpResponseMessage> AnonymizeAsync(Actor owner, JsonElement request, string confirmation, string? code) =>
        owner.Client.SendAsync(Post($"{Api}/{request.GetProperty("id").GetGuid()}/anonymization", new { confirmation, twoFactorCode = code, version = request.GetProperty("version").GetUInt32() }));

    /// <summary>Roda o último job de anonimização capturado (só tenantId e ids nos argumentos) e devolve o pedido atualizado.</summary>
    private async Task<JsonElement> RunJobAsync(Actor owner, Guid requestId, WebApplicationFactory<Program>? factory = null)
    {
        var job = _jobs.Created.Last(j => j.Job.Type == typeof(PrivacyAnonymizationJob)).Job;
        job.Args[0].Should().Be(tenancy.TenantA.Id, "job com tenantId (§4.3)");
        job.Args[1].Should().BeEquivalentTo(new PrivacyAnonymizationArgs(requestId, owner.Id), "só ids nos argumentos");
        await ActivatorUtilities.CreateInstance<PrivacyAnonymizationJob>((factory ?? Factory).Services).ExecuteAsync(tenancy.TenantA.Id, (PrivacyAnonymizationArgs)job.Args[1], CancellationToken.None);
        return await JsonAsync(await owner.Client.GetAsync($"{Api}/{requestId}"));
    }

    /// <summary>Processa o outbox do tenant até não sobrar mensagem pendente (o dispatcher do host pode pegar parte; SKIP LOCKED).</summary>
    private async Task DrainAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            if (await DbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1))) == 0)
            {
                return;
            }

            await Task.Delay(250);
        }
    }

    /// <summary>
    /// Canário (CA 6): onde, no banco do tenant fora da auditoria, aparece algum dos valores (toda coluna de texto ou jsonb de toda tabela,
    /// inclusive o outbox e a inbox). Devolve "schema.tabela.coluna" de cada achado.
    /// </summary>
    private Task<List<string>> CanaryHitsAsync(params string[] values) => DbAsync(async db =>
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        var columns = new List<(string Schema, string Table, string Column)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.table_schema, c.table_name, c.column_name FROM information_schema.columns c
                JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
                WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema', 'audit') AND c.data_type IN ('text', 'character varying', 'jsonb', 'json', 'ARRAY')
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var hits = new List<string>();
        foreach (var (schema, table, column) in columns)
        {
            await using var command = connection.CreateCommand();
            var conditions = string.Join(" OR ", values.Select((_, i) => $"\"{column}\"::text ILIKE @p{i}"));
            command.CommandText = $"SELECT count(*) FROM \"{schema}\".\"{table}\" WHERE {conditions}";
            for (var i = 0; i < values.Length; i++)
            {
                var p = command.CreateParameter();
                p.ParameterName = "p" + i;
                p.Value = "%" + values[i] + "%";
                command.Parameters.Add(p);
            }

            if (Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0)
            {
                hits.Add($"{schema}.{table}.{column}");
            }
        }

        return hits;
    });

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private static async Task<Guid> PositionAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Montador " + Guid.NewGuid().ToString("N")[..6], cbo = "7243-15", defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string Name, string Cpf)> EmployeeAsync(HttpClient owner, string? name = null)
    {
        var cpf = NewCpf();
        name ??= "Funcionaria " + Guid.NewGuid().ToString("N")[..6] + " Lima";
        var created = await owner.PostAsJsonAsync("/api/v1/hr/employees", new { name, cpf, birthDate = "1990-05-10", admissionDate = "2015-01-02", positionId = await PositionAsync(owner), workScheduleId = (Guid?)null, baseSalary = 3_000m, registration = (string?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await JsonAsync(created)).GetProperty("employee").GetProperty("id").GetGuid(), name, cpf);
    }

    /// <summary>Provedor de teste que falha na primeira anonimização (CA 5) e depois funciona.</summary>
    private sealed class FlakyProvider(FlakyState state) : IPersonalDataProvider
    {
        public string Module => "zz.teste";

        public string Title => "Módulo de teste";

        public Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default) => Task.FromResult<PersonalDataSection?>(null);

        public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult<PersonalDataAnonymizationPlan?>(new PersonalDataPlanBuilder(Module, Title).Anonymize("Campo de teste", "teste", 1).Build());

        public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default)
        {
            if (state.Next() == 1)
            {
                throw new InvalidOperationException("falha simulada");
            }

            return PlanAnonymizationAsync(context, cancellationToken);
        }
    }

    private sealed class FlakyState
    {
        private int _calls;

        public int Next() => Interlocked.Increment(ref _calls);
    }
}
