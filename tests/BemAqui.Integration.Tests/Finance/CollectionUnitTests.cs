using BemAqui.BuildingBlocks.Email;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-09, unidade: passo do dia (antes, no dia, depois, dia não útil), elegibilidade (implantação fora), campos do texto e escape.</summary>
public class CollectionUnitTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 28);
    private static readonly int[] Steps = [-3, 0, 3, 10];

    [Fact]
    public void Passo_do_dia_antes_no_vencimento_e_depois()
    {
        var previous = Wednesday.AddDays(-1);
        CollectionRules.StepOn(Wednesday.AddDays(3), Steps, Wednesday, previous).Should().Be(-3);
        CollectionRules.StepOn(Wednesday, Steps, Wednesday, previous).Should().Be(0);
        CollectionRules.StepOn(Wednesday.AddDays(-3), Steps, Wednesday, previous).Should().Be(3);
        CollectionRules.StepOn(Wednesday.AddDays(-10), Steps, Wednesday, previous).Should().Be(10);
        CollectionRules.StepOn(Wednesday.AddDays(-1), Steps, Wednesday, previous).Should().BeNull();
        CollectionRules.StepOn(Wednesday.AddDays(2), Steps, Wednesday, previous).Should().BeNull();
    }

    [Fact]
    public void Passo_de_fim_de_semana_sai_no_proximo_dia_util()
    {
        var monday = new DateOnly(2026, 11, 2);
        var friday = new DateOnly(2026, 10, 30);
        // Vencimento no sábado: o passo 0 sai na segunda; vencimento na quinta: o +3 (domingo) também.
        CollectionRules.StepOn(new DateOnly(2026, 10, 31), Steps, monday, friday).Should().Be(0);
        CollectionRules.StepOn(new DateOnly(2026, 10, 29), Steps, monday, friday).Should().Be(3);
        // Dois passos no mesmo dia útil: vale o mais adiantado.
        CollectionRules.StepOn(new DateOnly(2026, 11, 1), [-1, 0], monday, friday).Should().Be(0);
        // Faixa de vencimentos com passo na segunda: de (sexta − 10 + 1) a (segunda + 3).
        CollectionRules.DueWindow(Steps, monday, friday).Should().Be((new DateOnly(2026, 10, 21), new DateOnly(2026, 11, 5)));
    }

    [Fact]
    public void Implantacao_fica_fora_e_so_titulo_em_aberto_com_saldo_entra()
    {
        Receivable Title(DocumentOrigin origin, ReceivableStatus status = ReceivableStatus.Open) =>
            new() { Number = "CR-1", Amount = 100m, Status = status, Origin = origin, DueDate = Wednesday };

        CollectionEligibility.StateReason(Title(DocumentOrigin.Manual)).Should().BeNull();
        CollectionEligibility.StateReason(Title(DocumentOrigin.Contract, ReceivableStatus.PartiallyPaid)).Should().BeNull();
        CollectionEligibility.StateReason(Title(DocumentOrigin.Opening)).Should().Be(CollectionEligibility.OpeningReason);
        CollectionEligibility.StateReason(Title(DocumentOrigin.Manual, ReceivableStatus.Paid)).Should().Be(CollectionEligibility.PaidReason);
        CollectionEligibility.StateReason(Title(DocumentOrigin.Manual, ReceivableStatus.Cancelled)).Should().Be(CollectionEligibility.CancelledReason);
        CollectionEligibility.StateReason(Title(DocumentOrigin.Manual, ReceivableStatus.Planned)).Should().Be(CollectionEligibility.NotOpenReason);
    }

    [Theory]
    [InlineData("Olá, {cliente}. Título {numero} de {valor}, vence em {vencimento}. {linha_digitavel} {dias_de_atraso} {empresa}", true)]
    [InlineData("Texto <b>com HTML</b> & aspas \"literal\"", true)]
    [InlineData("Olá {nome}", false)]
    [InlineData("Olá {{ cliente }}", false)]
    [InlineData("{% if x %}oi{% endif %}", false)]
    [InlineData("{{cliente}}", false)]
    [InlineData("chave solta }", false)]
    public void Texto_so_aceita_os_campos_da_lista(string text, bool ok)
    {
        var error = CollectionRules.TextError(text);
        if (ok)
        {
            error.Should().BeNull();
        }
        else
        {
            error.Should().Be("Use só os campos da lista: {empresa}, {cliente}, {numero}, {valor}, {vencimento}, {linha_digitavel}, {dias_de_atraso}.");
        }
    }

    [Fact]
    public void Passos_tem_limites_e_nao_repetem()
    {
        CollectionRules.StepsError(CollectionRules.DefaultSteps.ToList()).Should().BeNull();
        CollectionRules.StepsError([]).Should().NotBeNull();
        CollectionRules.StepsError([.. Enumerable.Range(0, 7).Select(i => new CollectionStep(i, "oi"))])!.Value.Message.Should().Be("A régua tem até 6 passos.");
        CollectionRules.StepsError([new(-31, "oi")])!.Value.Field.Should().Be("steps[0].days");
        CollectionRules.StepsError([new(91, "oi")])!.Value.Field.Should().Be("steps[0].days");
        CollectionRules.StepsError([new(3, "oi"), new(3, "de novo")])!.Value.Field.Should().Be("steps[1].days");
        CollectionRules.StepsError([new(3, new string('x', 1001))])!.Value.Field.Should().Be("steps[0].text");
        CollectionRules.SendTimeValid(new TimeOnly(6, 59)).Should().BeFalse();
        CollectionRules.SendTimeValid(new TimeOnly(20, 0)).Should().BeTrue();
        CollectionRules.SendTimeValid(new TimeOnly(20, 1)).Should().BeFalse();
    }

    [Fact]
    public void Montagem_e_literal_e_o_html_sai_escapado()
    {
        var values = CollectionReminderMailer.Values("Konebras", "<script>alert(1)</script> {numero}", "CR-9", "R$ 10,00", Wednesday, null, Wednesday.AddDays(4));
        var text = CollectionRules.Render("Olá {cliente}, título {numero} ({dias_de_atraso} dias) <b>x</b> {{ tenant_name }}", values);
        // Valor com "{numero}" não é trocado de novo; a sintaxe do template fica como texto.
        text.Should().Be("Olá <script>alert(1)</script> {numero}, título CR-9 (4 dias) <b>x</b> {{ tenant_name }}");

        var rendered = new EmailTemplateRenderer().Render(EmailDispatchLimiter.CollectionReminderTemplate,
            CollectionReminderMailer.Model("Konebras", "CR-9", Wednesday, Wednesday.AddDays(4), text, null, "Konebras"));
        rendered.Html.Should().NotContain("<script>").And.NotContain("<b>x</b>").And.Contain("&lt;script&gt;").And.Contain("{{ tenant_name }}");
        rendered.Html.Should().NotContain("Linha digitável");
        rendered.Subject.Should().Be("Título CR-9 está em atraso há 4 dias — Konebras");
        rendered.Text.Should().Contain("<b>x</b>");
    }

    [Fact]
    public void Com_boleto_o_email_traz_a_linha_digitavel()
    {
        var rendered = new EmailTemplateRenderer().Render(EmailDispatchLimiter.CollectionReminderTemplate,
            CollectionReminderMailer.Model("Konebras", "CR-9", Wednesday.AddDays(3), Wednesday, "Lembrete", "0779 0001", "Konebras"));
        rendered.Html.Should().Contain("Linha digitável do boleto").And.Contain("0779 0001");
        rendered.Subject.Should().Be("Título CR-9 vence em 3 dias — Konebras");
    }
}
