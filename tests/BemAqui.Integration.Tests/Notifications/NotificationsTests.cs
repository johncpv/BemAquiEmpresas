using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Notifications;

/// <summary>FND-07 CA 22 e 23: notificação sempre com link de ação; cada usuário vê e marca só as suas.</summary>
[Collection(PostgresTestGroup.Name)]
public class NotificationsTests(PostgresFixture postgres)
{
    private static readonly Guid Ana = Guid.CreateVersion7();
    private static readonly Guid Beto = Guid.CreateVersion7();

    [Theory]
    [InlineData("")]
    [InlineData("https://golpe.example.com")]
    [InlineData("//golpe.example.com")]
    [InlineData("app/finance")]
    public async Task Sem_link_de_acao_do_app_e_recusada(string link)
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("notif_link"));
        await using var scope = provider.CreateAsyncScope();

        var result = scope.ServiceProvider.GetRequiredService<INotificationService>().Notify(Ana, "payment_order.pending", "OP aguardando aprovação", link);

        result.Error!.Code.Should().StartWith("notification.");
    }

    [Fact]
    public async Task Usuario_ve_so_as_suas_com_contagem_de_nao_lidas_e_marca_como_lida()
    {
        var connection = await postgres.CreateDatabaseAsync("notif");
        await using (var setup = await TestPersistence.CreateAsync(connection, user: new FixedUser(Ana, null)))
        await using (var scope = setup.CreateAsyncScope())
        {
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            notifications.Notify(Ana, "payment_order.pending", "OP-2026-0001 aguardando aprovação", "/app/finance/payment-orders/1").IsSuccess.Should().BeTrue();
            notifications.Notify(Ana, "quote.expiring", "ORC-2026-0003 vence amanhã", "/app/commercial/quotes/3").IsSuccess.Should().BeTrue();
            notifications.Notify(Beto, "stock.min", "Cabo de aço abaixo do mínimo", "/app/inventory/items/9").IsSuccess.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().SaveChangesAsync();
        }

        await using var asAna = await TestPersistence.CreateAsync(connection, user: new FixedUser(Ana, null));
        Guid first;
        await using (var scope = asAna.CreateAsyncScope())
        {
            var page = (await scope.ServiceProvider.GetRequiredService<NotificationService>().ListMineAsync(new PageRequest())).Value;
            page.Unread.Should().Be(2);
            page.Page.Items.Should().HaveCount(2).And.OnlyContain(n => n.Link.StartsWith("/app/", StringComparison.Ordinal));
            first = page.Page.Items[0].Id;
            (await scope.ServiceProvider.GetRequiredService<NotificationService>().MarkReadAsync(first)).IsSuccess.Should().BeTrue();
        }

        await using (var scope = asAna.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<NotificationService>().ListMineAsync(new PageRequest())).Value.Unread.Should().Be(1);
        }

        await using var asBeto = await TestPersistence.CreateAsync(connection, user: new FixedUser(Beto, null));
        await using (var scope = asBeto.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NotificationService>();
            (await service.MarkReadAsync(first)).Error!.Type.Should().Be(ErrorType.NotFound, "não marca nem revela a notificação de outra pessoa");
            (await service.ListMineAsync(new PageRequest())).Value.Page.Items.Should().ContainSingle().Which.Type.Should().Be("stock.min");
        }
    }

    [Fact]
    public async Task Sem_usuario_autenticado_nao_lista()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("notif_anon"), user: new FixedUser(null, null));
        await using var scope = provider.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<NotificationService>().ListMineAsync(new PageRequest())).Error!.Type.Should().Be(ErrorType.Forbidden);
    }
}
