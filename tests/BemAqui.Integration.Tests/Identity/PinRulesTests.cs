using BemAqui.BuildingBlocks.Identity;
using BemAqui.Identity.Application.Shopfloor;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>
/// Unidade (PRD-03b, plano de testes): formato e PIN óbvio (CA 12), bloqueio por par e por aparelho (CA 14), perfil sensível com a
/// exceção da qualidade (CA 17 e 18), prazos absolutos das duas sessões (CA 15 e 21), segredo rotativo (CA 7) e validade do aparelho
/// (CA 8).
/// </summary>
public class PinRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("")]
    [InlineData(null)]
    public void PIN_tem_exatamente_6_numeros(string? pin) =>
        PinRules.Problem(pin, pin, null).Should().Be(PinRules.FormatMessage);

    [Fact]
    public void Os_dois_PINs_precisam_ser_iguais() =>
        PinRules.Problem("483920", "483921", null).Should().Be(PinRules.MismatchMessage);

    [Theory]
    [InlineData("000000")]
    [InlineData("777777")]
    [InlineData("123456")]
    [InlineData("234567")]
    [InlineData("987654")]
    [InlineData("654321")]
    [InlineData("890123")]
    [InlineData("121212")]
    [InlineData("123123")]
    [InlineData("909090")]
    public void PIN_obvio_e_recusado(string pin) =>
        PinRules.Problem(pin, pin, null).Should().Be(PinRules.ObviousMessage);

    [Theory]
    [InlineData("150385")] // ddMMyy
    [InlineData("031585")] // MMddyy
    [InlineData("850315")] // yyMMdd
    [InlineData("031985")] // MMyyyy
    [InlineData("198503")] // yyyyMM
    public void Data_de_nascimento_conhecida_e_recusada(string pin)
    {
        var birth = new DateOnly(1985, 3, 15);
        PinRules.Problem(pin, pin, birth).Should().Be(PinRules.ObviousMessage);
        PinRules.Problem(pin, pin, null).Should().BeNull("sem a data conhecida, o mesmo PIN serve");
    }

    [Theory]
    [InlineData("483920")]
    [InlineData("705218")]
    [InlineData("112358")]
    public void PIN_comum_e_aceito(string pin) =>
        PinRules.Problem(pin, pin, new DateOnly(1990, 7, 1)).Should().BeNull();

    [Fact]
    public void Cinco_erros_seguidos_bloqueiam_o_par_por_15_minutos_e_o_decimo_em_24h_apaga()
    {
        var pin = UserPin.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", Now);
        Enumerable.Range(0, 4).Select(i => pin.RegisterFailure(Now.AddMinutes(i))).ToList().Should().AllBeEquivalentTo(PinFailure.Counted);

        pin.RegisterFailure(Now.AddMinutes(4)).Should().Be(PinFailure.Locked);
        pin.IsLocked(Now.AddMinutes(10)).Should().BeTrue();
        pin.IsLocked(Now.AddMinutes(20)).Should().BeFalse("15 min depois do bloqueio");

        Enumerable.Range(0, 4).Select(i => pin.RegisterFailure(Now.AddMinutes(30 + i))).ToList().Should().AllBeEquivalentTo(PinFailure.Counted);
        pin.RegisterFailure(Now.AddMinutes(40)).Should().Be(PinFailure.Erased, "o 10º erro em 24 h apaga o PIN");
    }

    [Fact]
    public void Acerto_zera_os_erros_seguidos_mas_nao_a_conta_de_24h()
    {
        var pin = UserPin.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", Now);
        for (var i = 0; i < 4; i++)
        {
            pin.RegisterFailure(Now.AddMinutes(i));
        }

        pin.RegisterSuccess(Now.AddMinutes(5));
        pin.RegisterFailure(Now.AddMinutes(6)).Should().Be(PinFailure.Counted, "os seguidos recomeçaram");
        pin.LastUsedAt.Should().Be(Now.AddMinutes(5));
        pin.FailuresInWindow.Should().Be(5);
        pin.RegisterFailure(Now.AddHours(25)).Should().Be(PinFailure.Counted);
        pin.FailuresInWindow.Should().Be(1, "a janela de 24 h recomeça");
    }

    [Fact]
    public void Vinte_erros_no_aparelho_em_15_minutos_bloqueiam_a_entrada_por_PIN()
    {
        var device = ShopfloorDevice.Register("Tablet Linha 1", "h", Guid.NewGuid(), Now);
        Enumerable.Range(0, 19).Select(i => device.RegisterPinFailure(Now.AddSeconds(i * 30))).ToList().Should().AllBeEquivalentTo(false);

        device.RegisterPinFailure(Now.AddMinutes(10)).Should().BeTrue();
        device.IsPinLocked(Now.AddMinutes(20)).Should().BeTrue();
        device.IsPinLocked(Now.AddMinutes(26)).Should().BeFalse();
    }

    [Fact]
    public void Erros_espalhados_alem_de_15_minutos_nao_bloqueiam_o_aparelho()
    {
        var device = ShopfloorDevice.Register("Tablet", "h", Guid.NewGuid(), Now);
        Enumerable.Range(0, 40).Select(i => device.RegisterPinFailure(Now.AddMinutes(i))).ToList().Should().NotContain(true);
    }

    [Fact]
    public void Segredo_anterior_vale_so_dentro_da_tolerancia_e_depois_e_reuso()
    {
        var device = ShopfloorDevice.Register("Tablet", "antigo", Guid.NewGuid(), Now);
        device.Rotate("novo", Now.AddHours(2));
        var tolerance = TimeSpan.FromSeconds(10);

        device.Match("novo", Now.AddHours(2), tolerance).Should().Be(DeviceSecretMatch.Current);
        device.Match("antigo", Now.AddHours(2).AddSeconds(5), tolerance).Should().Be(DeviceSecretMatch.PreviousInTolerance);
        device.Match("antigo", Now.AddHours(2).AddSeconds(30), tolerance).Should().Be(DeviceSecretMatch.Reused);
        device.Match("outro", Now.AddHours(2), tolerance).Should().Be(DeviceSecretMatch.None);
    }

    [Fact]
    public void Aparelho_vale_180_dias_e_cai_depois_de_30_dias_sem_uso()
    {
        var device = ShopfloorDevice.Register("Tablet", "h", Guid.NewGuid(), Now);
        device.ExpiresAt.Should().Be(Now.AddDays(180));
        device.StaleReason(Now.AddDays(29)).Should().BeNull();
        device.StaleReason(Now.AddDays(30)).Should().Be("unused");

        device.Touch(Now.AddDays(100));
        device.StaleReason(Now.AddDays(120)).Should().BeNull();
        device.StaleReason(Now.AddDays(180)).Should().Be("expired");
        device.IsActive(Now.AddDays(180)).Should().BeFalse();

        device.Revoke(Guid.NewGuid(), "manual", Now.AddDays(1));
        device.IsActive(Now.AddDays(2)).Should().BeFalse();
    }

    private static EffectivePermissions Perms(params string[] permissions) => new(permissions.ToHashSet(StringComparer.Ordinal), false);

    [Theory]
    [InlineData("production.approve")]
    [InlineData("finance.approve")]
    [InlineData("fiscal.approve")]
    [InlineData("finance.pay")]
    [InlineData("hr.view_sensitive")]
    [InlineData("hr.view_health")]
    [InlineData("identity.edit")]
    [InlineData("settings.edit")]
    public void Perfil_com_acao_reservada_nao_usa_PIN(string reserved)
    {
        var effective = Perms("shopfloor.view", "shopfloor.create", reserved);

        PinProfile.SensitiveActions(effective).Should().Equal(reserved);
        PinProfile.CanUsePin(effective).Should().BeFalse();
    }

    [Fact]
    public void Inspetor_com_a_acao_da_qualidade_usa_PIN() =>
        PinProfile.CanUsePin(Perms("shopfloor.view", "shopfloor.create", "production.view", "production.approve_quality")).Should().BeTrue();

    [Fact]
    public void Proprietario_e_quem_nao_aponta_nao_usam_PIN()
    {
        PinProfile.CanUsePin(new EffectivePermissions(new HashSet<string>(["shopfloor.create"], StringComparer.Ordinal), true)).Should().BeFalse();
        PinProfile.CanUsePin(Perms("shopfloor.view")).Should().BeFalse("sem shopfloor.create, o PIN não serve");
    }

    [Fact]
    public void Sessao_completa_tem_prazo_absoluto_e_a_renovacao_nao_passa_dele()
    {
        var start = Now;
        var absolute = SessionLifetimes.FullAbsolute(start, 30);
        absolute.Should().Be(start.AddDays(30));

        SessionLifetimes.RefreshExpiry(start.AddDays(10), TimeSpan.FromDays(7), absolute).Should().Be(start.AddDays(17));
        SessionLifetimes.RefreshExpiry(start.AddDays(28), TimeSpan.FromDays(7), absolute).Should().Be(absolute, "a renovação de 7 dias para no prazo absoluto");
        SessionLifetimes.FullAbsolute(start, 0).Should().Be(start.AddDays(30), "parâmetro inválido volta ao padrão");
    }

    [Fact]
    public void Sessao_por_PIN_dura_12_horas() =>
        SessionLifetimes.PinAbsolute(Now).Should().Be(Now.AddHours(12));

    [Theory]
    [InlineData("Diego Santos", "Diego S.")]
    [InlineData("  ana   maria souza ", "ana S.")]
    [InlineData("Joaquim", "Joaquim")]
    [InlineData("", "Operador")]
    public void Lista_do_aparelho_mostra_so_primeiro_nome_e_inicial(string name, string expected) =>
        PinNames.ShortName(name).Should().Be(expected);
}