using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Security;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class MachineTeamTests
{
    private static readonly CancellationToken Ct = default;
    private static readonly AdminActorContext Administrator = new(Guid.NewGuid().ToString(), "Administración");
    private static readonly AdminActorContext Operator = new(Guid.NewGuid().ToString(), "Responsable");

    [Fact]
    public async Task ATransferIsAtomicReplaysOnceAndRestoresBothPositionsWithoutChangingTomorrow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = await fixture.RegisterWorker();
        await fixture.SaveTemplate(fixture.First, fixture.Clock.Today, worker);
        var first = await fixture.Team(fixture.First);
        var move = new ChangeMachineTeamRequest(Guid.NewGuid(), first.Date, 1, "move", null, fixture.Second.Id, 1, first.Version);
        var moved = await fixture.Service.ChangeDailyAsync(fixture.First.Id, move, Administrator, Ct);
        var replay = await fixture.Service.ChangeDailyAsync(fixture.First.Id, move, Administrator, Ct);

        Assert.Equal(moved.Id, replay.Id);
        Assert.True(replay.Replayed);
        Assert.Equal("moved-out", (await fixture.Team(fixture.First)).Slots[0].Status);
        Assert.Equal(worker, (await fixture.Team(fixture.Second)).Slots[0].WorkerId);
        Assert.Equal(worker, (await fixture.Team(fixture.First, first.Date.AddDays(1))).Slots[0].WorkerId);
        Assert.Null((await fixture.Team(fixture.Second, first.Date.AddDays(1))).Slots[0].WorkerId);

        // Undo a later absence, followed by the original move.
        await fixture.Daily(fixture.Second, "absence");
        await fixture.Daily(fixture.Second, "reset");
        await fixture.Daily(fixture.Second, "reset");
        Assert.Equal(worker, (await fixture.Team(fixture.First)).Slots[0].WorkerId);
        Assert.Null((await fixture.Team(fixture.Second)).Slots[0].WorkerId);
        Assert.Equal(6, await fixture.Db.Set<MachineTeamChange>().CountAsync(Ct));
    }

    [Fact]
    public async Task AnOperatorCannotMovePastTheirScopeAndOccupiedWorkersCannotBeAssignedTwice()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = await fixture.RegisterWorker();
        await fixture.SaveTemplate(fixture.First, fixture.Clock.Today, worker);
        var first = await fixture.Team(fixture.First);
        var second = await fixture.Team(fixture.Second);

        var visible = await fixture.Service.GetAsync(fixture.First.Id, first.Date, Operator, Ct);
        Assert.Empty(visible.Destinations);
        var move = new ChangeMachineTeamRequest(Guid.NewGuid(), first.Date, 1, "move", null, fixture.Second.Id, 1, first.Version);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.ChangeDailyAsync(fixture.First.Id, move, Operator, Ct));

        var cover = new ChangeMachineTeamRequest(Guid.NewGuid(), second.Date, 1, "cover", worker, null, null, second.Version);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            fixture.Service.ChangeDailyAsync(fixture.Second.Id, cover, Administrator, Ct));
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            fixture.SaveTemplate(fixture.Second, fixture.Clock.Today.AddDays(1), worker));

        await fixture.Daily(fixture.First, "absence");
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() =>
            fixture.Service.ChangeDailyAsync(fixture.First.Id, move, Administrator, Ct));
        Assert.Equal("absent", (await fixture.Team(fixture.First)).Slots[0].Status);
        Assert.Equal(1, await fixture.Db.Set<MachineTeamChange>().CountAsync(Ct));
    }

    [Fact]
    public async Task ScheduledTemplateChecksOverlapAndTakesEffectFromItsOwnDate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = await fixture.RegisterWorker();
        var today = fixture.Clock.Today;
        await fixture.SaveTemplate(fixture.Second, today.AddDays(2), worker);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => fixture.SaveTemplate(fixture.First, today.AddDays(1), worker));

        await fixture.SaveTemplate(fixture.First, today.AddDays(2), null);
        await fixture.SaveTemplate(fixture.First, today.AddDays(1), worker);
        Assert.Null((await fixture.Team(fixture.First, today)).Slots[0].WorkerId);
        Assert.Equal(worker, (await fixture.Team(fixture.First, today.AddDays(1))).Slots[0].WorkerId);
        Assert.Null((await fixture.Team(fixture.First, today.AddDays(2))).Slots[0].WorkerId);
        Assert.Equal(worker, (await fixture.Team(fixture.Second, today.AddDays(2))).Slots[0].WorkerId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public PedidosDbContext Db { get; }
        public MachineTeamService Service { get; }
        public Clock Clock { get; } = new();
        public Machine First { get; } = Machine.Create(1, "Máquina uno");
        public Machine Second { get; } = Machine.Create(2, "Máquina dos");

        private Fixture(SqliteConnection connection, PedidosDbContext db)
        {
            this.connection = connection;
            Db = db;
            var machines = new EfMachineRepository(db);
            var access = new MachineWorkspaceAccess(new EfSecurityStore(db), machines, new EfMachineWorkspaceRepository(db));
            Service = new(new EfMachineTeamRepository(db), machines, access, new EfOrderOperationStore(db),
                new EfAuditLogRepository(db), Clock);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Ct);
            var db = new PedidosDbContext(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlite(connection).Options);
            var fixture = new Fixture(connection, db);
            await db.Database.EnsureCreatedAsync(Ct);
            db.Machines.AddRange(fixture.First, fixture.Second);
            var manage = SecurityDefaults.Permissions.Single(x => x.Key == MachineWorkspaceAccess.ManagePermission);
            var work = SecurityDefaults.Permissions.Single(x => x.Key == MachineWorkspaceAccess.WorkPermission);
            var admin = AdminUser.Create("team-admin", Administrator.ActorDisplayName!, "test-fixture", fixture.Clock.Now);
            var operatorUser = AdminUser.Create("team-operator", Operator.ActorDisplayName!, "test-fixture", fixture.Clock.Now);
            admin.Id = Guid.Parse(Administrator.ActorId!);
            operatorUser.Id = Guid.Parse(Operator.ActorId!);
            db.AddRange(manage, work, admin, operatorUser,
                new SecurityUserPermission { UserId = admin.Id, PermissionId = manage.Id },
                new SecurityUserPermission { UserId = operatorUser.Id, PermissionId = work.Id },
                new MachineOperator { MachineId = fixture.First.Id, UserId = operatorUser.Id });
            await db.SaveChangesAsync(Ct);
            return fixture;
        }

        public Task<MachineTeamResponse> Team(Machine machine, DateOnly? date = null) =>
            Service.GetAsync(machine.Id, date, Administrator, Ct);

        public async Task<Guid> RegisterWorker() =>
            (await Service.RegisterWorkerAsync(new(Guid.NewGuid(), "Trabajador de prueba"), Administrator, Ct)).Id;

        public async Task SaveTemplate(Machine machine, DateOnly date, Guid? workerId)
        {
            var team = await Team(machine, date);
            await Service.SaveTemplateAsync(machine.Id, new(Guid.NewGuid(), date,
                [new(1, workerId), new(2, null), new(3, null), new(4, null)], team.Version), Administrator, Ct);
        }

        public async Task Daily(Machine machine, string action)
        {
            var team = await Team(machine);
            await Service.ChangeDailyAsync(machine.Id,
                new(Guid.NewGuid(), team.Date, 1, action, null, null, null, team.Version), Administrator, Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 11, 16, 14, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 11, 16);
        public TimeOnly LocalTimeOfDay => new(8, 0);
    }

}
