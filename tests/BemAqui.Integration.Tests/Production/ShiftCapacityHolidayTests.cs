using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>CAD-03 CA 10, 12, 13, 15 e 16 sem banco: horas dos turnos, capacidade por dia e feriados nacionais.</summary>
public class ShiftCapacityHolidayTests
{
    private static readonly Weekday[] MonToFri = [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];

    [Fact]
    public void Horas_por_dia_saem_dos_turnos()
    {
        Shift[] shifts = [new("07:00", "11:00"), new("12:00", "16:48")];
        ShiftMath.Validate(shifts).Should().BeNull();
        ShiftMath.HoursPerDay(shifts).Should().Be(8.80m);
        ShiftMath.Validate([new("7:00", "11:00"), new("11:00", "12:00")]).Should().BeNull("encostar não é sobrepor");
    }

    [Theory]
    [InlineData("07:00", "11:00", "10:00", "12:00", "Os turnos não podem se sobrepor.")]
    [InlineData("22:00", "06:00", "07:00", "08:00", "Turno que vira o dia não é aceito.")]
    [InlineData("07:00", "07:00", "08:00", "09:00", "Turno que vira o dia não é aceito.")]
    [InlineData("7h", "11:00", "12:00", "13:00", "Informe início e fim de cada turno no formato 00:00.")]
    public void Turnos_invalidos_tem_mensagem_do_campo(string a1, string a2, string b1, string b2, string message) =>
        ShiftMath.Validate([new(a1, a2), new(b1, b2)]).Should().Be(message);

    [Fact]
    public void Sem_turno_e_recusado() => ShiftMath.Validate([]).Should().Be("Informe ao menos um turno.");

    [Fact]
    public void Capacidade_com_feriado_e_parada_parcial_da_28_horas_na_semana()
    {
        var exceptions = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 11, 20)] = 0m, [new DateOnly(2026, 11, 18)] = 4m };
        var days = CapacityCalculator.Days(true, new DateOnly(2026, 1, 1), 8m, MonToFri, exceptions, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 22));
        days.Select(d => d.Hours).Should().Equal(8m, 8m, 4m, 8m, 0m, 0m, 0m);
        days.Sum(d => d.Hours).Should().Be(28m);
    }

    [Fact]
    public void Excecao_acima_das_horas_do_centro_e_limitada()
    {
        var exceptions = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 11, 16)] = 12m };
        CapacityCalculator.Days(true, new DateOnly(2026, 1, 1), 8m, MonToFri, exceptions, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16)).Single().Hours.Should().Be(8m);
    }

    [Fact]
    public void Centro_inativo_ou_antes_de_existir_tem_zero()
    {
        var none = new Dictionary<DateOnly, decimal>();
        CapacityCalculator.Days(false, new DateOnly(2026, 1, 1), 8m, MonToFri, none, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20)).Should().OnlyContain(d => d.Hours == 0m);
        CapacityCalculator.Days(true, new DateOnly(2026, 11, 18), 8m, MonToFri, none, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20))
            .Select(d => d.Hours).Should().Equal(0m, 0m, 8m, 8m, 8m);
    }

    [Theory]
    [InlineData(2026, "2026-04-05", "2026-04-03", "2026-02-17", "2026-06-04")]
    [InlineData(2027, "2027-03-28", "2027-03-26", "2027-02-09", "2027-05-27")]
    public void Pascoa_e_feriados_moveis(int year, string easter, string goodFriday, string carnivalTuesday, string corpusChristi)
    {
        NationalHolidays.Easter(year).Should().Be(DateOnly.Parse(easter, System.Globalization.CultureInfo.InvariantCulture));
        var holidays = NationalHolidays.For(year);
        holidays.Should().ContainSingle(h => h.Name == "Sexta-feira Santa" && h.Date == DateOnly.Parse(goodFriday, System.Globalization.CultureInfo.InvariantCulture) && !h.Optional);
        holidays.Should().ContainSingle(h => h.Name == "Carnaval (terça-feira)" && h.Date == DateOnly.Parse(carnivalTuesday, System.Globalization.CultureInfo.InvariantCulture) && h.Optional);
        holidays.Should().ContainSingle(h => h.Name == "Corpus Christi" && h.Date == DateOnly.Parse(corpusChristi, System.Globalization.CultureInfo.InvariantCulture) && h.Optional);
        holidays.Where(h => !h.Optional).Should().HaveCount(10, "9 fixos e a Sexta-feira Santa");
        holidays.Should().Contain(h => h.Date == new DateOnly(year, 11, 20) && !h.Optional);
        holidays.Should().BeInAscendingOrder(h => h.Date);
    }
}
