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
        // LGPD-01a: provedor do Fiscal (a NF-e ainda não guarda o destinatário; a FIS-03a exporta as notas do titular).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Privacy.IPersonalDataProvider, FiscalPersonalDataProvider>());
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
        // FIS-03b: cancelamento, carta de correção, pedidos de cancelamento e inutilização; o lembrete semanal, a expiração e a pauta.
        services.TryAddScoped<NumberGapService>();
        services.TryAddScoped<FiscalInvalidationReminder>();
        services.TryAddTransient<FiscalInvalidationReminderJob>();
        services.TryAddScoped<CancellationRequestExpiry>();
        services.TryAddTransient<CancellationRequestExpiryJob>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, FiscalEventAgendaSource>());
        services.AddAuditableEntity<FiscalEvent>("fiscal");
        services.AddAuditableEntity<CancellationRequest>("fiscal");
        // PAT-01a CA 7: seletor da NF de entrada do bem; sem notas até a FIS-04a (que troca este registro).
        services.TryAddScoped<BemAqui.Fiscal.Contracts.IIncomingInvoices, NoIncomingInvoices>();
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
        // FIS-04a: nota de entrada por XML. O dono IncomingInvoice aceita só XML (o PDF da FIS-05 tem o dono IncomingInvoicePdf), até 2 MB; lê com fiscal.view e grava
        // com fiscal.import_incoming, só pela importação (a rota genérica de anexos é fechada) e debaixo do prefixo do tenant.
        services.TryAddSingleton<BemAqui.Fiscal.Application.Incoming.INfeTrustAnchors, BemAqui.Fiscal.Application.Incoming.IcpBrasilTrustAnchors>();
        services.TryAddScoped<BemAqui.Fiscal.Application.Incoming.IncomingInvoiceService>();
        // FIS-04b: o recebimento pela nota (Purchasing) lê a nota conferida e faz as transições pelo contrato, na transação dele.
        services.TryAddScoped<BemAqui.Fiscal.Contracts.IIncomingInvoiceReceipts, BemAqui.Fiscal.Application.Incoming.IncomingInvoicesContract>();
        services.AddAuditableEntity<IncomingInvoice>("fiscal");
        services.AddAuditableEntity<IncomingInvoiceItem>("fiscal");
        services.AddAttachmentOwner(BemAqui.Fiscal.Application.Incoming.IncomingInvoiceService.AttachmentOwner, new AttachmentOwnerRules("fiscal", ["application/xml"],
            BemAqui.Fiscal.Domain.IncomingInvoiceRules.MaxBytes,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IFiscalDb>().IncomingInvoices.AnyAsync(i => i.Id == id, ct),
            ReadAction: "view", WriteAction: "import_incoming",
            CanAttach: (_, _, _) => Task.FromResult<Error?>(Error.Forbidden(Error.Codes.Forbidden, "O XML da nota de entrada entra pela importação."))));
        // FIS-05: NF-e de entrada pelo PDF da DANFE (worker isolado fiscal-pdf-reader pelo socket Unix) ou digitada. O PDF é anexo de sistema do
        // rascunho (dono IncomingInvoicePdf, só PDF, até 5 MB), gravado só pela importação e lido com fiscal.view pelo registro.
        services.Configure<BemAqui.Fiscal.Infrastructure.PdfReader.PdfReaderOptions>(configuration.GetSection(BemAqui.Fiscal.Infrastructure.PdfReader.PdfReaderOptions.Section));
        services.TryAddSingleton<BemAqui.Fiscal.Application.Incoming.IPdfInvoiceReader, BemAqui.Fiscal.Infrastructure.PdfReader.SocketPdfInvoiceReader>();
        services.TryAddSingleton<BemAqui.Fiscal.Application.Incoming.IIncomingXmlDownloader, BemAqui.Fiscal.Application.Incoming.NoIncomingXmlDownloader>();
        services.TryAddScoped<BemAqui.Fiscal.Application.Incoming.IncomingReadingDependencies>();
        services.TryAddTransient<BemAqui.Fiscal.Application.Incoming.PdfInvoiceReadingJob>();
        services.AddAuditableEntity<IncomingInvoiceReading>("fiscal");
        services.AddAttachmentOwner(BemAqui.Fiscal.Application.Incoming.IncomingInvoiceService.PdfAttachmentOwner, new AttachmentOwnerRules("fiscal", ["application/pdf"],
            IncomingReadingRules.MaxPdfBytes,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IFiscalDb>().IncomingInvoiceReadings.AnyAsync(r => r.Id == id, ct),
            ReadAction: "view", WriteAction: "import_incoming",
            CanAttach: (_, _, _) => Task.FromResult<Error?>(Error.Forbidden(Error.Codes.Forbidden, "O PDF da nota de entrada entra pela importação do PDF."))));

        // XML e DANFE (FIS-03a): só o sistema grava; leitura com fiscal.view pelos endpoints da nota (CA 18). FIS-06: o PDF ou o XML da NFS-e
        // emitida no portal da prefeitura entra só pelo "Registrar NFS-e" (fiscal.create, triado pelo antivírus); a rota genérica segue fechada.
        services.AddAttachmentOwner(OutgoingInvoiceReconciler.AttachmentOwner, new AttachmentOwnerRules("fiscal", ["application/xml", "application/pdf"], 5 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IFiscalDb>().OutgoingInvoices.AnyAsync(i => i.Id == id, ct),
            WriteAction: "create",
            CanAttach: (_, _, _) => Task.FromResult<Error?>(Error.Forbidden(Error.Codes.Forbidden, "O XML e o DANFE da nota são guardados pelo sistema."))));

        // FIS-06: NFS-e da instalação (fila pelo aceite técnico, emissão pelo Emissor quando habilitada, registro do portal, cancelamento registrado).
        services.TryAddScoped<BemAqui.Fiscal.Application.Invoices.Nfse.NfsePayloads>();
        services.TryAddScoped<BemAqui.Fiscal.Application.Invoices.Nfse.NfseReconciler>();
        services.TryAddScoped<BemAqui.Fiscal.Application.Invoices.Nfse.NfseSubmissionService>();
        services.TryAddScoped<BemAqui.Fiscal.Application.Invoices.Nfse.NfsePendingCheck>();
        services.TryAddScoped<BemAqui.Fiscal.Application.Invoices.Nfse.NfseInvoiceService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, BemAqui.Fiscal.Application.Invoices.Nfse.NfseAgendaSource>());
        return services;
    }
}
