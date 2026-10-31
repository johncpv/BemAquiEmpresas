using System.Reflection;
using System.Runtime.CompilerServices;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// RH-01a: regras do ponto sem banco (sequência sugerida, duplo toque, localização reduzida, textos e jornada) e as regras de arquitetura
/// (marcação imutável, DTOs de lista sem localização, rotas do "Meu ponto" com o vínculo e sem rota para editar ou apagar marcação).
/// </summary>
public class TimeClockRulesTests
{
    private static string Hr => Path.Combine(Solution.RootDirectory, "src", "Modules", "HR");

    [Theory]
    [InlineData(null, TimeEntryType.In)]
    [InlineData(TimeEntryType.In, TimeEntryType.BreakStart)]
    [InlineData(TimeEntryType.BreakStart, TimeEntryType.BreakEnd)]
    [InlineData(TimeEntryType.BreakEnd, TimeEntryType.Out)]
    [InlineData(TimeEntryType.Out, TimeEntryType.In)]
    public void Proxima_marcacao_sugerida_segue_a_sequencia(TimeEntryType? last, TimeEntryType expected)
    {
        TimeClockRules.Suggest(last).Should().Be(expected);
        TimeClockRules.IsOutOfSequence(last, expected).Should().BeFalse();
    }

    [Fact]
    public void Fora_de_ordem_e_aceito_mas_marcado()
    {
        TimeClockRules.IsOutOfSequence(null, TimeEntryType.Out).Should().BeTrue("esqueceu a entrada (CA 4)");
        TimeClockRules.IsOutOfSequence(TimeEntryType.In, TimeEntryType.In).Should().BeTrue();
    }

    [Fact]
    public void Duplo_toque_so_dentro_de_um_minuto()
    {
        var now = new DateTimeOffset(2026, 10, 21, 11, 2, 0, TimeSpan.Zero);
        TimeClockRules.IsDuplicate(now.AddSeconds(-59), now).Should().BeTrue();
        TimeClockRules.IsDuplicate(now.AddSeconds(-60), now).Should().BeFalse();
        TimeClockRules.IsDuplicate(null, now).Should().BeFalse();
    }

    [Theory]
    [InlineData(-23.550520, -23.5505)]
    [InlineData(-23.55055, -23.5506)]
    [InlineData(-46.633308, -46.6333)]
    [InlineData(12.34565, 12.3457)]
    public void Localizacao_reduzida_a_4_casas(double raw, double expected) =>
        TimeClockRules.Round((decimal)raw).Should().Be((decimal)expected);

    [Fact]
    public void Localizacao_invalida_vira_sem_localizacao()
    {
        TimeClockRules.Normalize(-23.550520m, -46.633308m, 18.6m).Should().Be((-23.5505m, -46.6333m, (int?)19));
        TimeClockRules.Normalize(null, -46.6m, 10m).Should().BeNull();
        TimeClockRules.Normalize(91m, -46.6m, 10m).Should().BeNull();
        TimeClockRules.Normalize(-23m, 181m, 10m).Should().BeNull();
        TimeClockRules.Normalize(-23m, -46m, -1m)!.Value.AccuracyM.Should().BeNull();
    }

    [Fact]
    public void Textos_do_comprovante_e_da_recusa()
    {
        var local = new DateTimeOffset(2026, 10, 21, 8, 2, 0, TimeSpan.FromHours(-3));
        TimeClockRules.ReceiptText(TimeEntryType.In, local).Should().Be("Entrada registrada às 08:02 de 21/10/2026");
        TimeClockRules.Done(TimeEntryType.BreakEnd).Should().Be("Volta do intervalo registrada");
        TimeClockRules.Verb(TimeEntryType.BreakStart).Should().Be("Registrar saída para o intervalo");
        TimeClockRules.DuplicateMessage(TimeEntryType.In, local).Should().Be("Você já registrou entrada às 08:02.");
        TimeClockRules.ReceiptTitle.Should().Be("Comprovante de controle interno").And.NotContainEquivalentOf("registro de ponto");
        TimeClockRules.InternalControlBanner.Should().Be("Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).");
    }

    [Fact]
    public void Jornada_do_dia_no_texto_curto()
    {
        IReadOnlyList<ScheduleDay> days = [new(1, "08:00", "17:48", 60), new(6, "08:00", "12:00", 0), new(2, "07:00", "16:00", 90)];
        TimeClockRules.ScheduleText(days, new DateOnly(2026, 10, 19)).Should().Be("08:00–17:48, intervalo 1 h", "segunda");
        TimeClockRules.ScheduleText(days, new DateOnly(2026, 10, 20)).Should().Be("07:00–16:00, intervalo 1 h 30 min");
        TimeClockRules.ScheduleText(days, new DateOnly(2026, 10, 24)).Should().Be("08:00–12:00, sem intervalo");
        TimeClockRules.ScheduleText(days, new DateOnly(2026, 10, 25)).Should().Be("Sem jornada hoje", "domingo");
        TimeClockRules.ScheduleText(null, new DateOnly(2026, 10, 19)).Should().Be("Sem jornada hoje");
        TimeClockRules.FirstName("  Ana  Souza ").Should().Be("Ana");
    }

    /// <summary>CA 14: marcação imutável: hora e tipo só na criação (init), sem método de alteração.</summary>
    [Fact]
    public void TimeEntry_nao_tem_alteracao_de_hora_nem_de_tipo()
    {
        foreach (var name in new[] { nameof(TimeEntry.At), nameof(TimeEntry.Type), nameof(TimeEntry.Day), nameof(TimeEntry.EmployeeId) })
        {
            var setter = typeof(TimeEntry).GetProperty(name)!.SetMethod;
            setter.Should().NotBeNull();
            setter!.ReturnParameter.GetRequiredCustomModifiers().Should().Contain(typeof(IsExternalInit), $"{name} só na criação");
        }

        typeof(TimeEntry).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName)
            .Should().BeEmpty("nenhum método muda a marcação; o ajuste (RH-01b) desconsidera e cria outra");
    }

    /// <summary>CA 11 e 12: os DTOs que saem com hr.view (e o comprovante, guardado no Idempotency-Key) não têm propriedade de localização.</summary>
    [Fact]
    public void Dtos_de_lista_e_comprovante_sem_localizacao()
    {
        var application = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Application");
        foreach (var dto in new[] { "TimeEntryRow", "TimeEntryList", "TimeEntryReceipt", "MyTimeEntryToday", "MyTimeClock" })
        {
            application.GetTypes().Single(t => t.Name == dto).GetProperties().Select(p => p.Name).Should()
                .NotContain(["Latitude", "Longitude", "AccuracyM", "Lat", "Lng"], dto + " não leva a localização");
        }

        foreach (var name in new[] { nameof(TimeEntry.Latitude), nameof(TimeEntry.Longitude), nameof(TimeEntry.AccuracyM) })
        {
            typeof(TimeEntry).GetProperty(name)!.GetCustomAttribute<BemAqui.SharedKernel.PersonalDataAttribute>().Should().NotBeNull();
        }
    }

    /// <summary>CA 6, 7, 14 e 15: as rotas do ponto têm o vínculo ou a permissão, só hoje e marcar no PIN, e nenhuma edita ou apaga marcação.</summary>
    [Fact]
    public void Rotas_do_ponto_com_vinculo_ou_permissao_e_sem_editar_nem_apagar()
    {
        var endpoints = File.ReadAllText(Path.Combine(Hr, "BemAqui.HR.Endpoints", "HrEndpoints.TimeClock.cs"));
        var statements = endpoints.Split(';').Where(s => s.Contains(".WithName(", StringComparison.Ordinal)).ToList();
        statements.Should().HaveCount(7);
        foreach (var name in new[] { "GetMyTimeClock", "RecordMyTimeEntry", "AcceptMyGeoConsent", "ListMyTimeEntries" })
        {
            statements.Single(s => s.Contains($"\"{name}\"", StringComparison.Ordinal)).Should().Contain(".RequireActiveEmployeeLink(", name).And.NotContain("RequirePermission");
        }

        statements.Where(s => s.Contains(".AllowPinSession()", StringComparison.Ordinal)).Should().HaveCount(2);
        statements.Single(s => s.Contains("\"RecordMyTimeEntry\"", StringComparison.Ordinal)).Should().Contain(".RequireIdempotencyKey()");
        statements.Single(s => s.Contains("\"ListEmployeeTimeEntryLocations\"", StringComparison.Ordinal)).Should().Contain("RequirePermission(\"hr.view_sensitive\")").And.Contain("/sensitive");
        endpoints.Should().NotContain("MapPut").And.NotContain("MapDelete").And.NotContain("MapPatch");
    }
}
