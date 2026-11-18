using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Catalog.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Contracts;
using BemAqui.Identity.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Um passo do checklist "Primeiros passos" (PERF-04 CA 6 a 9). <paramref name="Available"/> falso: o passo ainda não tem tela ("Em breve",
/// chega com a story indicada em <paramref name="ComingWith"/>). <paramref name="Permission"/> é a permissão da tela do link: sem ela, o front
/// mostra o passo sem link, com "Peça a quem administra o sistema.".
/// </summary>
public sealed record FirstStep(string Key, string Title, string Text, bool Done, bool Available, string? Url, string? Permission, string? ComingWith);

/// <summary>
/// O cartão "Primeiros passos" do Início (PERF-04): só para o perfil Comércio (<paramref name="Applies"/>), com o progresso calculado dos
/// dados (nenhuma tabela de "passo concluído") e, com tudo pronto, o próximo passo sugerido.
/// </summary>
public sealed record FirstStepsCard(bool Applies, IReadOnlyList<FirstStep> Steps, int Done, int Total, bool AllDone, string? NextTitle, string? NextUrl);

/// <summary>
/// Monta o checklist pelos contratos dos donos dos dados (padrão da ADR 0028): a empresa (Identity), o regime conferido (Fiscal) e as formas de
/// pagamento conferidas (Finance) e algum produto cadastrado (Catalog, tela de importação da CAT-02). Os passos "Impressora" (IMP-01) e
/// "Abrir o caixa e fazer uma venda de teste" (PDV-01 e PDV-02) aparecem como "Em breve" até as stories deles. A fábrica não tem o cartão
/// (CA 5: nada muda para a Konebras).
/// </summary>
public sealed class FirstStepsService(ITenantContext tenantContext, ICompanyIdentity company, IFiscalSetupStatus fiscal, IPaymentMethods payments, ICatalogStatus catalog, TimeProvider clock)
{
    public const string CompanyUrl = "/app/configuracoes/empresa";
    public const string FiscalUrl = "/app/configuracoes/fiscal";
    public const string PaymentMethodsUrl = "/app/financeiro/formas-de-pagamento";
    public const string ImportProductsUrl = "/app/catalogo/produtos/importar";

    public async Task<FirstStepsCard> GetAsync(CancellationToken cancellationToken = default)
    {
        var profiles = (tenantContext as TenantContextHolder)?.Resolved?.Profiles ?? new HashSet<string>(StringComparer.Ordinal);
        if (!BusinessProfileCatalog.HasFeature(profiles, BusinessProfileCatalog.FirstStepsFeature))
        {
            return new FirstStepsCard(false, [], 0, 0, false, null, null);
        }

        var identity = await company.GetAsync(cancellationToken);
        var regime = await fiscal.RegimeConfirmedAsync(cancellationToken);
        var methods = await payments.ActiveAsync(BusinessTime.Today(clock), cancellationToken);
        var hasProducts = await catalog.HasProductsAsync(cancellationToken);
        var steps = Build(identity.Complete, regime, methods.Count, methods.Count(m => !m.Confirmed), hasProducts);
        var done = steps.Count(s => s.Done);
        var allDone = done == steps.Count;
        return new FirstStepsCard(true, steps, done, steps.Count, allDone, allDone ? NextTitle : null, allDone ? NextUrl : null);
    }

    /// <summary>Próximo passo depois do checklist (CA 8): trazer a equipe da loja, com os grupos do seed já prontos.</summary>
    public const string NextTitle = "Convide a equipe da loja: os grupos Gerente de loja, Operador de caixa e Estoquista já estão prontos.";

    public const string NextUrl = "/app/acessos/usuarios";

    /// <summary>Regra pura dos cinco passos (unidade): cada um concluído pelo dado, nunca por um clique em "feito" (CA 7).</summary>
    public static IReadOnlyList<FirstStep> Build(bool companyComplete, bool regimeConfirmed, int activeMethods, int methodsToConfirm, bool hasProducts)
    {
        var companyStep = !companyComplete
            ? new FirstStep("company", "Empresa e regime tributário", "Complete o endereço e um contato da empresa; depois confira o regime tributário.", false, true, CompanyUrl, "settings.edit", null)
            : new FirstStep("company", "Empresa e regime tributário",
                regimeConfirmed ? "Dados da empresa completos e regime tributário conferido." : "Confira o regime tributário com a contadora e marque como conferido.",
                regimeConfirmed, true, FiscalUrl, "settings.edit", null);
        var paymentsDone = activeMethods > 0 && methodsToConfirm == 0;
        var paymentsText = activeMethods == 0
            ? "Cadastre as formas de pagamento que a loja aceita."
            : paymentsDone ? "Formas de pagamento conferidas." : $"Informe a taxa e o prazo da maquininha e marque como conferido: {methodsToConfirm} {(methodsToConfirm == 1 ? "forma está" : "formas estão")} \"A confirmar\".";
        return
        [
            companyStep,
            new FirstStep("products", "Importar produtos",
                hasProducts ? "Produtos cadastrados." : "Traga os produtos de uma planilha, com código de barras e preço.",
                hasProducts, true, ImportProductsUrl, "catalog.create", null),
            new FirstStep("payments", "Formas de pagamento e taxas", paymentsText, paymentsDone, true, PaymentMethodsUrl, "finance.edit", null),
            new FirstStep("printer", "Impressora", "Ligue a impressora do caixa para o cupom.", false, false, null, null, "IMP-01"),
            new FirstStep("first_sale", "Abrir o caixa e fazer uma venda de teste", "Abra o caixa com o fundo de troco e registre uma venda de teste.", false, false, null, null, "PDV-02"),
        ];
    }
}
