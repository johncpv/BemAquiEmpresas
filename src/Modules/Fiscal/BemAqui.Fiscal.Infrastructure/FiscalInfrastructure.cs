using System.Net;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Certificates;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Application.TaxRules;
using BemAqui.Fiscal.Domain;
using BemAqui.Fiscal.Infrastructure.Emissor;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Fiscal.Infrastructure;

public static class FiscalInfrastructure
{
    /// <summary>
    /// Integração com o EmissorFiscal (FIS-01, ADR 0027): opções, o adaptador HTTP (cliente nomeado sem redirect e sem log), o acesso por
    /// tenant, a tela de integração, o envio e a reconsulta das notas, o provedor de webhook <c>emissor-fiscal</c> (FND-16), a conferência
    /// de pendentes, a auditoria e o dono de anexo de sistema <c>OutgoingInvoice</c> (só o XML guardado pelo sistema; ninguém envia).
    /// </summary>
    public static IServiceCollection AddFiscalInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<FiscalIntegrationOptions>(configuration.GetSection(FiscalIntegrationOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(EmissorFiscalEmitterFactory.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All, UseProxy = false })
            .RemoveAllLoggers();
        services.TryAddSingleton<EmissorFiscalRateGate>(); // um limite para o processo inteiro (10 req/s por IP no Emissor)
        services.TryAddScoped<IFiscalEmitterFactory, EmissorFiscalEmitterFactory>();
        services.TryAddScoped<FiscalEmitterAccess>();
        services.TryAddScoped<FiscalAlerts>();
        services.TryAddScoped<FiscalIntegrationService>();
        services.TryAddScoped<OutgoingInvoiceReconciler>();
        services.TryAddScoped<NfeSubmissionService>();
        // FIS-03a: a nota de saída monta o próprio pedido (a conferência reenvia com a mesma chave e o mesmo payload).
        services.TryAddScoped<OutgoingNfePayloads>();
        services.TryAddScoped<IOutgoingNfePayloads>(sp => sp.GetRequiredService<OutgoingNfePayloads>());
        services.TryAddScoped<OutgoingInvoiceEffects>();
        services.TryAddScoped<OutgoingInvoiceCalculator>();
        services.TryAddScoped<OutgoingInvoiceService>();
        services.TryAddScoped<BemAqui.Fiscal.Contracts.IOutgoingInvoices, OutgoingInvoicesContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, OutgoingInvoiceAgendaSource>());
        services.AddAuditableEntity<OutgoingInvoiceItem>("fiscal");
        services.AddAuditableEntity<NumberGap>("fiscal");
        services.TryAddScoped<FiscalPendingChecker>();
        services.TryAddTransient<FiscalPendingCheckJob>();
        services.AddWebhookProvider(EmissorFiscalWebhook.Provider);
        services.AddAuditableEntity<FiscalIntegrationSettings>("settings");
        services.AddAuditableEntity<OutgoingInvoice>("fiscal");

        // FIS-02a: Configurações → Fiscal, certificado digital, vencimento e pauta; a tela é de Configurações (histórico com settings.view).
        services.AddMemoryCache();
        services.TryAddScoped<FiscalSettingsService>();
        services.TryAddScoped<IFiscalSettings>(sp => sp.GetRequiredService<FiscalSettingsService>());
        services.TryAddScoped<FiscalSettingsExport>();
        services.TryAddScoped<FiscalCertificateService>();
        services.TryAddScoped<FiscalCertificateExpiryChecker>();
        services.TryAddTransient<FiscalCertificateExpiryJob>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, FiscalAgendaSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Tenancy.ITenantSeeder, FiscalSettingsSeeder>());
        services.AddAuditableEntity<FiscalSettings>("settings");
        services.AddAuditableEntity<SettingConfirmation>("settings");

        // FIS-02b: regras fiscais por operação, resolução, simulação e o aviso de revisão (o consumidor é registrado no HostEvents).
        services.TryAddScoped<TaxRuleService>();
        services.TryAddScoped<TaxRuleResolver>();
        services.TryAddScoped<TaxSimulationService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Tenancy.ITenantSeeder, TaxRuleSeeder>());
        services.AddAuditableEntity<TaxRule>("fiscal");
        // XML e DANFE (FIS-03a): só o sistema grava; leitura com fiscal.view pelos endpoints da nota (CA 18).
        services.AddAttachmentOwner(OutgoingInvoiceReconciler.AttachmentOwner, new AttachmentOwnerRules("fiscal", ["application/xml", "application/pdf"], 5 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IFiscalDb>().OutgoingInvoices.AnyAsync(i => i.Id == id, ct),
            CanAttach: (_, _, _) => Task.FromResult<Error?>(Error.Forbidden(Error.Codes.Forbidden, "O XML e o DANFE da nota são guardados pelo sistema."))));
        return services;
    }
}
