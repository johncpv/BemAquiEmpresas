using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Integration.Tests.Webhooks;
using BemAqui.Platform.Contracts;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.SharedKernel;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// Tenants A (homologação permitida, como o <c>demo</c>) e B (<c>None</c>, como o <c>konebras</c> antes da GOL-01b) reais (Testcontainers)
/// e um Host com o <see cref="FakeFiscalEmitterFactory"/> no lugar do adaptador HTTP (FIS-01 CA 3): nenhum teste do módulo chama a rede.
/// Logs, alertas e jobs ficam capturados.
/// </summary>
public sealed class FiscalFixture : IAsyncLifetime
{
    public const string PlatformHost = "plataforma.bemaquiapp.com.br";
    public const string HostA = "a.localhost";
    public const string HostB = "b.localhost";
    public const string ApiKey = "ef_chave-canario-fiscal-0123456789";
    public const string WebhookSecret = "segredo-canario-webhook-0123456789";

    /// <summary>Os testes do certificado somam mais de 5 envios no tenant A; o limite de verdade é conferido no tenant B com este valor.</summary>
    public const int CertificateUploadsPerHour = 30;

    /// <summary>Limite de importação de XML por usuário no teste (o de produção é 60 por hora, FIS-04a CA 1); cada teste usa usuários novos.</summary>
    public const int IncomingImportsPerHour = 8;

    /// <summary>Limite de PDFs para leitura por usuário no teste (o de produção é 30 por hora, FIS-05 CA 1).</summary>
    public const int PdfReadingsPerHour = 6;

    /// <summary>Tempo limite do worker de teste (o de produção é 30 s): o teste do "falhou por tempo" não espera meio minuto.</summary>
    public const int PdfTimeoutSeconds = 3;

    private readonly CancellationTokenSource _pdfStop = new();
    private Task? _pdfServer;
    private int _pdfKills;

    /// <summary>Socket do worker fiscal-pdf-reader que roda dentro do teste (FIS-05): a API fala com o leitor de verdade.</summary>
    public string PdfSocket { get; } = Path.Combine(Path.GetTempPath(), "bq-fx-" + Guid.NewGuid().ToString("N")[..12] + ".sock");

    /// <summary>Troca a leitura do worker de teste (ex.: uma leitura presa, para o tempo limite); null = o leitor de verdade.</summary>
    internal Func<byte[], CancellationToken, BemAqui.PdfReader.ReadResponse>? PdfBehavior { get; set; }

    /// <summary>Quantas vezes o worker foi "morto" no tempo limite (e reiniciado, como o Docker faz).</summary>
    public int PdfReaderKills => Volatile.Read(ref _pdfKills);

    /// <summary>Antivírus de teste (sem clamd): limpo por padrão.</summary>
    public BemAqui.Integration.Tests.Attachments.FakeScanner Scanner { get; } = new();

    public TenancyFixture Tenancy { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public MemorySink Logs { get; } = new();

    public CapturingAlertSink Alerts { get; } = new();

    public CapturingJobClient Jobs { get; } = new();

    public FakeFiscalEmitterFactory Emitters { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Tenancy.InitializeAsync();
        Factory = Tenancy.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Webhooks:PerIpPerMinute", "100000");
            b.UseSetting("Fiscal:Emissor:TestsPerMinute", "5");
            b.UseSetting("Fiscal:Emissor:IncomingImportsPerHour", IncomingImportsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.UseSetting("Fiscal:Emissor:PdfReadingsPerHour", PdfReadingsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.UseSetting("Fiscal:PdfReader:SocketPath", PdfSocket);
            b.UseSetting("Fiscal:PdfReader:TimeoutSeconds", PdfTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.UseSetting("Fiscal:Emissor:CertificateUploadsPerHour", CertificateUploadsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<ILogEventSink>(Logs);
                s.RemoveAll<IAlertSink>();
                s.AddSingleton<IAlertSink>(Alerts);
                s.RemoveAll<IBackgroundJobClient>();
                s.AddSingleton<IBackgroundJobClient>(Jobs);
                s.RemoveAll<IFiscalEmitterFactory>();
                s.AddSingleton<IFiscalEmitterFactory>(Emitters);
                s.RemoveAll<BemAqui.BuildingBlocks.Attachments.IVirusScanner>();
                s.AddSingleton<BemAqui.BuildingBlocks.Attachments.IVirusScanner>(Scanner);
                s.RemoveAll<BemAqui.Fiscal.Application.Incoming.INfeTrustAnchors>();
                s.AddSingleton(NfeSamples.Anchors);
            });
        });
        _ = Factory.Server;
        await SetAllowanceAsync(Tenancy.TenantA, FiscalEnvironmentAllowance.Homologation);

        // O worker fiscal-pdf-reader de verdade, num socket temporário; morto no tempo limite, sobe de novo (como o restart do Docker).
        _pdfServer = Task.Run(async () =>
        {
            while (!_pdfStop.IsCancellationRequested)
            {
                var server = new BemAqui.PdfReader.ReaderServer(PdfSocket, TimeSpan.FromSeconds(PdfTimeoutSeconds), (pdf, ct) => (PdfBehavior ?? BemAqui.PdfReader.DanfePdfReader.Read)(pdf, ct),
                    _ => { }, () => Interlocked.Increment(ref _pdfKills));
                try
                {
                    await server.RunAsync(_pdfStop.Token);
                }
                catch (Exception) when (!_pdfStop.IsCancellationRequested)
                {
                    await Task.Delay(100);
                }
            }
        });
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(PdfSocket) && DateTime.UtcNow < until)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>Roda a leitura do PDF como o Hangfire faria (o job com o tenantId e o id do rascunho).</summary>
    public async Task RunPdfReadingAsync(Tenant tenant, Guid readingId)
    {
        var job = ActivatorUtilities.CreateInstance<BemAqui.Fiscal.Application.Incoming.PdfInvoiceReadingJob>(Factory.Services);
        await job.ExecuteAsync(tenant.Id, new BemAqui.Fiscal.Application.Incoming.PdfInvoiceReadingArgs(readingId), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _pdfStop.CancelAsync();
        if (_pdfServer is not null)
        {
            await _pdfServer.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        _pdfStop.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Tenancy.DisposeAsync();
    }

    public FakeFiscalEmitter Fake(Tenant tenant) => Emitters.Of(tenant.Id);

    public Task SetAllowanceAsync(Tenant tenant, FiscalEnvironmentAllowance allowance) =>
        Tenancy.UpdateTenantAsync(tenant.Id, t => t.FiscalEnvironmentAllowed = allowance);

    public async Task<AsyncServiceScope> ScopeAsync(Tenant tenant)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    public async Task<T> QueryAsync<T>(Tenant tenant, Func<BemAquiDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<BemAquiDbContext>());
    }

    public async Task<T> FiscalAsync<T>(Tenant tenant, Func<IFiscalDb, Task<T>> query)
    {
        await using var scope = await ScopeAsync(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<IFiscalDb>());
    }

    public async Task<T> WithAsync<T, TService>(Tenant tenant, Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = await ScopeAsync(tenant);
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>Integração gravada direto (sem a tela), já testada e ativa por padrão; o fake do tenant fica no estado padrão.</summary>
    public async Task ConfigureAsync(Tenant tenant, bool active = true, string apiKey = ApiKey, string webhookSecret = WebhookSecret)
    {
        var fake = Fake(tenant);
        fake.Next = FakeIssue.Authorize;
        fake.Down = false;
        fake.NoForceConsult = false;
        fake.SefazOnline = true;
        fake.Scopes = ["consultar", "nfe:emitir", "nfe:eventos"];
        fake.Environment = FiscalEnvironment.Homologation;
        fake.Accounts.Clear();
        fake.Certificate = null;
        fake.RejectCertificate = false;
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IFiscalDb>();
        var shared = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        var secrets = Factory.Services.GetRequiredService<ISecretProtector>();
        var now = DateTimeOffset.UtcNow;
        var settings = await db.FiscalIntegrationSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new FiscalIntegrationSettings { BaseUrl = "https://api.fiscal.bemaquiapp.com.br/" };
            db.FiscalIntegrationSettings.Add(settings);
        }

        settings.BaseUrl = "https://api.fiscal.bemaquiapp.com.br/";
        settings.SetApiKey(secrets.Protect(SecretScope.ForTenant(tenant.Id), SecretKind.FiscalApiKey, new Secret(apiKey)), now);
        var account = fake.DefaultAccount();
        if (active)
        {
            settings.RecordApprovedTest(new FiscalAccountSnapshot(account.ClientId, account.TenantId, "47225005000186", "WSP", FiscalEnvironment.Homologation, account.Scopes, null), now);
        }
        else
        {
            settings.Deactivate();
        }

        var credential = await shared.WebhookCredentials.FirstOrDefaultAsync(c => c.Provider == EmissorFiscalWebhook.ProviderCode && c.ScopeId == Guid.Empty);
        if (credential is null)
        {
            credential = new WebhookCredential { Provider = EmissorFiscalWebhook.ProviderCode, ScopeId = Guid.Empty, Kind = WebhookCredentialKind.HmacSecret, CreatedAt = now };
            shared.WebhookCredentials.Add(credential);
        }

        credential.Rotate(null, secrets.Protect(SecretScope.ForTenant(tenant.Id), SecretKind.WebhookSecret, new Secret(webhookSecret)), null, now);
        await db.SaveChangesAsync();
        await Factory.Services.GetRequiredService<IWebhookTenantKeys>().EnsureAsync(tenant.Id);
    }

    /// <summary>Apaga a integração (para os testes de "não configurada").</summary>
    public async Task RemoveIntegrationAsync(Tenant tenant)
    {
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IFiscalDb>();
        db.FiscalIntegrationSettings.RemoveRange(await db.FiscalIntegrationSettings.ToListAsync());
        await db.SaveChangesAsync();
    }

    public async Task<Guid> InvoiceAsync(Tenant tenant, decimal total = 1000m)
    {
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IFiscalDb>();
        var invoice = OutgoingInvoice.Create(total, DateTimeOffset.UtcNow);
        db.OutgoingInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    public Task<OutgoingInvoice> InvoiceOfAsync(Tenant tenant, Guid id) =>
        FiscalAsync(tenant, db => db.OutgoingInvoices.AsNoTracking().SingleAsync(i => i.Id == id));

    public Task<Result<NfeSubmission>> SubmitAsync(Tenant tenant, Guid invoiceId, NfeIssueRequest? request = null) =>
        WithAsync<Result<NfeSubmission>, NfeSubmissionService>(tenant, s => s.SubmitAsync(invoiceId, request ?? FiscalSamples.Sample()));

    /// <summary>Recua o início da espera (a conferência só pega nota pendente há mais de 5 min, CA 14).</summary>
    public Task AgeAsync(Tenant tenant, Guid invoiceId, TimeSpan by, bool contingency = false) =>
        QueryAsync(tenant, db => contingency
            ? db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal.outgoing_invoices SET processing_since = processing_since - {by}, contingency_since = contingency_since - {by} WHERE id = {invoiceId}")
            : db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal.outgoing_invoices SET processing_since = processing_since - {by} WHERE id = {invoiceId}"));

    public async Task<FiscalPendingCheckResult> CheckPendingAsync(Tenant tenant)
    {
        await using var scope = await ScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<FiscalPendingChecker>().CheckAsync();
    }

    public async Task RunPendingJobAsync(Tenant tenant)
    {
        var job = ActivatorUtilities.CreateInstance<FiscalPendingCheckJob>(Factory.Services);
        await job.ExecuteAsync(tenant.Id, new FiscalPendingCheckArgs(), CancellationToken.None);
    }

    public HttpClient Platform(string host = PlatformHost)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    public Task<string> WebhookKeyAsync(Tenant tenant) => Factory.Services.GetRequiredService<IWebhookTenantKeys>().EnsureAsync(tenant.Id);

    /// <summary>POST do Emissor (assinatura <c>t=…,v1=…</c> sobre <c>{timestamp}.{corpo}</c>), pela URL pública (host da plataforma, como chega pelo stack-nginx).</summary>
    public async Task<(HttpResponseMessage Response, string Delivery)> PostWebhookAsync(Tenant tenant, string body, string eventType, string secret = WebhookSecret, DateTimeOffset? at = null, string host = PlatformHost,
        string? delivery = null, Func<string, long, string>? signature = null)
    {
        var key = await WebhookKeyAsync(tenant);
        var ts = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        delivery ??= Guid.CreateVersion7().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/" + EmissorFiscalWebhook.ProviderCode + "/" + key) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-BemAqui-Signature", signature?.Invoke(body, ts) ?? FakeEmissorServer.Sign(secret, body, ts));
        request.Headers.Add("X-BemAqui-Timestamp", ts.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-BemAqui-Event", eventType);
        request.Headers.Add("X-BemAqui-Delivery-Id", delivery);
        return (await Platform(host).SendAsync(request), delivery);
    }

    /// <summary>Processa o evento recebido como o Hangfire faria (o job com o tenantId e o id do evento).</summary>
    public async Task<WebhookEvent> ProcessWebhookAsync(Tenant tenant, string delivery)
    {
        var evt = await QueryAsync(tenant, db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.Provider == EmissorFiscalWebhook.ProviderCode && e.ExternalId == delivery));
        var job = ActivatorUtilities.CreateInstance<ProcessWebhookEventJob>(Factory.Services);
        try
        {
            await job.ExecuteAsync(tenant.Id, new WebhookEventJobArgs(evt.Id), CancellationToken.None);
        }
        catch (WebhookProcessingFailedException)
        {
            // falha registrada no evento; o teste confere a situação
        }

        return await QueryAsync(tenant, db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.Id == evt.Id));
    }

    /// <summary>Usuário do tenant com um grupo das ações dadas ou do Proprietário.</summary>
    public async Task<(Guid Id, HttpClient Client)> ActorAsync(Tenant tenant, string host, bool owner = false, params string[] grants)
    {
        var (id, stamp) = await Tenancy.SeedUserAsync(tenant, "f" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid? groupId = null;
            if (owner)
            {
                groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            }
            else if (grants.Length > 0)
            {
                var group = new Group { Name = "Fiscal " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                foreach (var module in grants.Select(g => g.Split('.')[0]).Distinct(StringComparer.Ordinal))
                {
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                }

                db.GroupPermissions.AddRange(grants.Select(g => new GroupGrant { GroupId = group.Id, ModuleCode = g.Split('.')[0], Action = g.Split('.')[1] }));
                groupId = group.Id;
            }

            if (groupId is { } gid)
            {
                db.UserGroups.Add(new UserGroup { UserId = id, GroupId = gid });
            }

            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return (id, client);
    }

    /// <summary>
    /// Proprietário com o 2FA ligado (ligar troca o security stamp, então o token sai depois) e um gerador de código: cada chamada libera
    /// o passo atual de novo (o anti-replay aceita cada código uma vez só) e devolve o código dele.
    /// </summary>
    public async Task<(Guid Id, HttpClient Client, Func<Task<string>> Code)> OwnerWithTotpAsync(Tenant tenant, string host)
    {
        var (id, _) = await ActorAsync(tenant, host, owner: true);
        var secret = Totp.NewSecret();
        string stamp;
        await using (var scope = await ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.BeginTotpSetup(Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
            user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            await db.SaveChangesAsync();
            stamp = user.SecurityStamp;
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        async Task<string> Code()
        {
            var step = Totp.StepAt(DateTimeOffset.UtcNow);
            await QueryAsync(tenant, db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET totp_last_step = {step - 10} WHERE id = {id}"));
            return Totp.Code(secret, step);
        }

        return (id, client, Code);
    }

    public async Task SetModuleAsync(Tenant tenant, string module, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenant.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    public string AllLogs()
    {
        lock (Logs.Events)
        {
            return string.Join("\n", Logs.Events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + e.Exception));
        }
    }
}
