using System.Security.Cryptography;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed class MachineTeamService(IMachineTeamRepository repository, IMachineRepository machines,
    MachineWorkspaceAccess access, IOrderOperationStore operations, IAuditLogRepository audit, IDateTimeProvider clock)
{
    private static readonly string[] Labels = ["Maquinista", "Recolector 1", "Recolector 2", "Empaquetador"];

    public async Task<MachineTeamResponse> GetAsync(Guid machineId, DateOnly? date,
        AdminActorContext actor, CancellationToken token)
    {
        await access.RequireAsync(machineId, actor, token);
        var today = clock.Today;
        var rosterDate = date ?? today;
        var state = await ReadAsync(rosterDate, token);
        var template = state.Template(machineId);
        var workers = (await repository.GetWorkersAsync(token)).ToDictionary(x => x.Id);
        string? WorkerName(Guid? id) => id is { } key && workers.TryGetValue(key, out var worker) ? worker.Name : null;
        var destinations = (await machines.GetByIdsAsync((await access.GetMachineIdsAsync(actor, token))
            .Where(x => x != machineId), token)).Where(x => x.IsActive).OrderBy(x => x.Number)
            .Select(x => new MachineTeamDestinationResponse(x.Id, x.Name ?? $"Máquina {x.Number}", x.Number)).ToArray();

        var slots = Enumerable.Range(1, Labels.Length).Select(position =>
        {
            var current = state.Slot(machineId, position);
            var regularId = WorkerAt(template, position);
            return new MachineTeamSlotResponse(position, Labels[position - 1], regularId, WorkerName(regularId),
                current.WorkerId, WorkerName(current.WorkerId), current.Status);
        }).ToArray();
        var history = state.Changes.Where(x => x.MachineId == machineId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Revision)
            .Select(x => new MachineTeamHistoryResponse(x.Position, Labels[x.Position - 1], x.Action,
                WorkerName(x.WorkerId ?? x.PreviousWorkerId), x.OtherMachineId, x.CreatedAt, x.ActorName)).ToArray();
        return new(machineId, rosterDate, today, template?.EffectiveDate, Version(state, machineId),
            rosterDate == today, await CanManageAsync(actor, token), slots,
            workers.Values.OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new MachineWorkerResponse(x.Id, x.Name, x.IsActive)).ToArray(),
            history, destinations);
    }

    public async Task<ProductionCommandResponse> RegisterWorkerAsync(RegisterMachineWorkerRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        await access.RequireManageAsync(actor, token);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            throw new ArgumentException("Escribe el nombre del trabajador, con un máximo de 160 caracteres.");

        return await ExecuteAsync("WORKER", request.OperationId, request, actor, async ct =>
        {
            await access.RequireManageAsync(actor, ct);
            var worker = new MachineWorker { Id = Guid.NewGuid(), Name = request.Name.Trim() };
            await repository.AddWorkerAsync(worker, ct);
            await AuditAsync(worker.Id, "MachineWorkerRegistered", actor, "Alta de trabajador de producción.",
                new { worker.Id, worker.Name }, ct);
            return new(worker.Id, null);
        }, token);
    }

    public async Task<ProductionCommandResponse> SaveTemplateAsync(Guid machineId, SaveMachineTeamTemplateRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        await access.RequireAsync(machineId, actor, token);
        if (request.Slots is null || request.Slots.Count != Labels.Length || request.Slots.Any(x => x is null) ||
            !request.Slots.Select(x => x.Position).Order().SequenceEqual(Enumerable.Range(1, Labels.Length)))
            throw new ArgumentException("La plantilla debe incluir las cuatro posiciones de la máquina.");
        var workerIds = request.Slots.OrderBy(x => x.Position).Select(x => x.WorkerId).ToArray();
        if (workerIds.OfType<Guid>().Distinct().Count() != workerIds.OfType<Guid>().Count())
            throw new ArgumentException("Una persona no puede ocupar más de una posición del equipo fijo.");

        return await ExecuteAsync("TEMPLATE", request.OperationId, new { machineId, request }, actor, async ct =>
        {
            await access.RequireAsync(machineId, actor, ct);
            if (request.EffectiveDate < clock.Today)
                throw new ArgumentException("El equipo fijo solo se puede actualizar desde hoy o una fecha futura.");
            var state = await ReadAsync(request.EffectiveDate, ct);
            RequireVersion(state, machineId, request.ExpectedVersion);
            var workers = (await repository.GetWorkersAsync(ct)).Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
            if (workerIds.OfType<Guid>().Any(x => !workers.Contains(x)))
                throw new ArgumentException("Selecciona trabajadores activos.");
            var template = state.Templates.SingleOrDefault(x => x.MachineId == machineId && x.EffectiveDate == request.EffectiveDate);
            var previous = TemplateWorkers(state.Template(machineId)).ToArray();
            if (template is not null && previous.SequenceEqual(workerIds))
                return new(template.Id, null);
            if (request.EffectiveDate == clock.Today && state.Changes.Any(x => x.MachineId == machineId))
                throw new CustomerOrderConflictException("Ya hay ajustes en esta máquina para hoy. Cambia el equipo fijo desde otra fecha.");

            var activeIds = (await machines.GetAllAsync(ct)).Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
            EnsureTemplateAvailable(state, machineId, activeIds, workerIds);
            if (template is null)
            {
                template = new MachineTeamTemplate { Id = Guid.NewGuid(), MachineId = machineId, EffectiveDate = request.EffectiveDate };
                WriteTemplateWorkers(template, workerIds);
                await repository.AddTemplateAsync(template, ct);
            }
            else
            {
                WriteTemplateWorkers(template, workerIds);
                template.Revision++;
            }

            await AuditAsync(machineId, "MachineTeamTemplateSaved", actor, "Actualización de plantilla fija de producción.",
                new { machineId, request.EffectiveDate, template.Revision, previous, workerIds }, ct);
            return new(template.Id, null);
        }, token);
    }

    public async Task<ProductionCommandResponse> ChangeDailyAsync(Guid machineId, ChangeMachineTeamRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        await access.RequireAsync(machineId, actor, token);
        RequirePosition(request.Position);
        if (request.Action is not ("absence" or "cover" or "move" or "reset"))
            throw new ArgumentException("Indica un ajuste válido para el equipo del día.");

        if (request.Action == "move")
        {
            if (request.OtherMachineId is not { } otherId || otherId == machineId || request.OtherPosition is not { } position)
                throw new ArgumentException("Selecciona otra máquina y la posición de destino.");
            RequirePosition(position);
            await access.RequireAsync(otherId, actor, token);
        }

        return await ExecuteAsync("DAILY", request.OperationId, new { machineId, request }, actor, async ct =>
        {
            await access.RequireAsync(machineId, actor, ct);
            if (request.Date != clock.Today)
                throw new ArgumentException("Los ajustes de equipo se registran para el día en curso.");
            var state = await ReadAsync(request.Date, ct);
            RequireVersion(state, machineId, request.ExpectedVersion);
            var source = state.Slot(machineId, request.Position);
            var candidates = new List<MachineTeamChange>();
            var now = clock.Now;
            MachineTeamChange Change(Guid targetId, int position, string action, string status, Guid? workerId,
                Guid? relatedId = null, int? relatedPosition = null, Guid? transferId = null)
            {
                var current = state.Slot(targetId, position);
                return new()
                {
                    Id = Guid.NewGuid(), MachineId = targetId, Position = position, Date = state.Date,
                    Revision = (current.Change?.Revision ?? 0) + 1, Action = action, Status = status,
                    WorkerId = workerId, PreviousWorkerId = current.WorkerId, OtherMachineId = relatedId,
                    OtherPosition = relatedPosition, TransferId = transferId, CreatedAt = now,
                    ActorName = actor.ActorDisplayName is { Length: > 120 } name ? name[..120] : actor.ActorDisplayName
                };
            }

            switch (request.Action)
            {
                case "absence":
                    if (source.WorkerId is null) throw new CustomerOrderConflictException("Esta posición no tiene una persona activa.");
                    candidates.Add(Change(machineId, request.Position, "absence", "absent", null));
                    break;
                case "cover":
                    if (request.WorkerId is not { } substitute || !(await repository.GetWorkersAsync(ct)).Any(x => x.Id == substitute && x.IsActive))
                        throw new ArgumentException("Selecciona un suplente activo.");
                    if (source.WorkerId is not null)
                        throw new CustomerOrderConflictException("Libera la posición antes de asignar un suplente.");
                    candidates.Add(Change(machineId, request.Position, "cover", "covered", substitute));
                    break;
                case "move":
                    var targetId = request.OtherMachineId!.Value;
                    var targetPosition = request.OtherPosition!.Value;
                    await access.RequireAsync(targetId, actor, ct);
                    if (source.WorkerId is null) throw new CustomerOrderConflictException("Esta posición no tiene a una persona que se pueda mover.");
                    if (state.Slot(targetId, targetPosition).WorkerId is not null)
                        throw new CustomerOrderConflictException("La posición de destino ya está ocupada.");
                    candidates.Add(Change(machineId, request.Position, "move-out", "moved-out", null,
                        targetId, targetPosition, request.OperationId));
                    candidates.Add(Change(targetId, targetPosition, "move-in", "moved-in", source.WorkerId,
                        machineId, request.Position, request.OperationId));
                    break;
                case "reset":
                    var applied = state.ActiveChanges(machineId, request.Position);
                    if (applied.Count == 0)
                        throw new CustomerOrderConflictException("La posición no tiene un ajuste para quitar.");
                    if (applied[^1] is { TransferId: { } transferId, OtherMachineId: { } relatedId, OtherPosition: { } relatedPosition })
                    {
                        await access.RequireAsync(relatedId, actor, ct);
                        var related = state.ActiveChanges(relatedId, relatedPosition);
                        if (related.LastOrDefault()?.TransferId != transferId)
                            throw new CustomerOrderConflictException("La otra posición cambió después del traslado. Resuelve primero el ajuste más reciente.");
                        RestorePrevious(relatedId, relatedPosition, related);
                    }
                    RestorePrevious(machineId, request.Position, applied);
                    break;
            }

            void RestorePrevious(Guid targetId, int position, IReadOnlyList<MachineTeamChange> applied)
            {
                var previous = applied.Count > 1 ? applied[^2] : null;
                var workerId = previous is null ? WorkerAt(state.Template(targetId), position) : previous.WorkerId;
                var status = previous?.Status ?? (workerId.HasValue ? "regular" : "vacant");
                candidates.Add(Change(targetId, position, "reset", status, workerId,
                    previous?.OtherMachineId, previous?.OtherPosition, previous?.TransferId));
            }

            var activeIds = (await machines.GetAllAsync(ct)).Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
            EnsureDailyAvailable(state, activeIds, candidates);
            foreach (var change in candidates)
            {
                await repository.AddChangeAsync(change, ct);
                await AuditAsync(change.MachineId, "MachineTeamDailyChanged", actor, "Ajuste al equipo del día.",
                    new { change.Id, change.Date, change.Position, change.Action, change.Status, change.PreviousWorkerId,
                        change.WorkerId, change.OtherMachineId, change.OtherPosition, change.TransferId }, ct);
            }
            return new(candidates[0].Id, null);
        }, token);
    }

    private async Task<RosterState> ReadAsync(DateOnly date, CancellationToken token) =>
        new(date, await repository.GetTemplatesAsync(token), await repository.GetChangesAsync(date, token));

    private async Task<bool> CanManageAsync(AdminActorContext actor, CancellationToken token)
    {
        try { await access.RequireManageAsync(actor, token); return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string Version(RosterState state, Guid machineId)
    {
        var template = state.Template(machineId);
        return Hash(new { state.Date, machineId, TemplateId = template?.Id, Revision = template?.Revision,
            Changes = Enumerable.Range(1, Labels.Length).Select(x => state.Slot(machineId, x).Change?.Id).ToArray() });
    }

    private static void RequireVersion(RosterState state, Guid machineId, string? expectedVersion)
    {
        if (!string.Equals(Version(state, machineId), expectedVersion, StringComparison.Ordinal))
            throw new CustomerOrderConflictException("El equipo cambió. Actualiza la vista antes de guardar.");
    }

    private static void RequirePosition(int position)
    {
        if (position < 1 || position > Labels.Length)
            throw new ArgumentException("Selecciona una posición válida del equipo.");
    }

    private static void EnsureTemplateAvailable(RosterState state, Guid machineId, HashSet<Guid> activeIds, Guid?[] workerIds)
    {
        var nextLocal = state.Templates.Where(x => x.MachineId == machineId && x.EffectiveDate > state.Date)
            .Select(x => (DateOnly?)x.EffectiveDate).Min();
        var otherMachines = activeIds.Where(x => x != machineId).ToArray();
        var dates = state.Templates.Where(x => activeIds.Contains(x.MachineId) && x.MachineId != machineId &&
                x.EffectiveDate > state.Date && (nextLocal is null || x.EffectiveDate < nextLocal))
            .Select(x => x.EffectiveDate).Append(state.Date).Distinct();
        var selected = workerIds.OfType<Guid>().ToHashSet();
        if (dates.Any(date => otherMachines.Any(id => TemplateWorkers(state.Template(id, date))
            .OfType<Guid>().Any(selected.Contains))))
            throw new CustomerOrderConflictException("Un trabajador ya pertenece al equipo fijo de otra máquina durante ese periodo.");

        var assignedToday = otherMachines.SelectMany(id => Enumerable.Range(1, Labels.Length).Select(p => state.Slot(id, p).WorkerId));
        if (assignedToday.OfType<Guid>().Any(selected.Contains))
            throw new CustomerOrderConflictException("Un trabajador tiene un ajuste activo en otra máquina para la fecha de inicio.");
    }

    private static void EnsureDailyAvailable(RosterState state, HashSet<Guid> activeIds, IReadOnlyList<MachineTeamChange> candidates)
    {
        var replacements = candidates.ToDictionary(x => (x.MachineId, x.Position));
        foreach (var candidate in candidates.Where(x => x.WorkerId.HasValue))
        {
            var assignedElsewhere = activeIds.SelectMany(id => Enumerable.Range(1, Labels.Length).Select(position => (id, position)))
                .Where(slot => slot != (candidate.MachineId, candidate.Position))
                .Any(slot => (replacements.TryGetValue(slot, out var replacement)
                    ? replacement.WorkerId : state.Slot(slot.id, slot.position).WorkerId) == candidate.WorkerId);
            if (assignedElsewhere)
                throw new CustomerOrderConflictException("La persona ya ocupa otra posición. Usa un traslado para moverla de máquina.");
        }
    }

    private static IEnumerable<Guid?> TemplateWorkers(MachineTeamTemplate? template) =>
        [template?.OperatorId, template?.FirstCollectorId, template?.SecondCollectorId, template?.PackerId];

    private static Guid? WorkerAt(MachineTeamTemplate? template, int position) => position switch
    {
        1 => template?.OperatorId, 2 => template?.FirstCollectorId,
        3 => template?.SecondCollectorId, 4 => template?.PackerId,
        _ => throw new ArgumentOutOfRangeException(nameof(position))
    };

    private static void WriteTemplateWorkers(MachineTeamTemplate template, Guid?[] workers)
    {
        template.OperatorId = workers[0];
        template.FirstCollectorId = workers[1];
        template.SecondCollectorId = workers[2];
        template.PackerId = workers[3];
    }

    private async Task<ProductionCommandResponse> ExecuteAsync(string action, Guid operationId, object payload,
        AdminActorContext actor, Func<CancellationToken, Task<ProductionCommandResponse>> command, CancellationToken token)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Indica una clave de operación válida.");
        var receipt = await operations.ExecuteAsync($"MACHINE-TEAM:{action}:{operationId:N}",
            Hash(new { payload, actor.ActorId }), clock.Now, command, token);
        return receipt.Result with { Replayed = receipt.Replayed };
    }

    private Task AuditAsync(Guid id, string kind, AdminActorContext actor, string summary, object metadata, CancellationToken token) =>
        audit.AddAsync(AuditLog.Create("MachineTeam", id.ToString(), kind, clock.Now, AuditActorType.Admin,
            summary, actor.ActorId, actor.ActorDisplayName, JsonSerializer.Serialize(metadata)), token);

    private static string Hash(object payload) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));

    private sealed record RosterState(DateOnly Date, IReadOnlyList<MachineTeamTemplate> Templates,
        IReadOnlyList<MachineTeamChange> Changes)
    {
        public MachineTeamTemplate? Template(Guid id, DateOnly? date = null) => Templates
            .Where(t => t.MachineId == id && t.EffectiveDate <= (date ?? Date))
            .OrderByDescending(t => t.EffectiveDate).FirstOrDefault();

        public SlotState Slot(Guid id, int position)
        {
            var change = Changes.Where(x => x.MachineId == id && x.Position == position)
                .OrderByDescending(x => x.Revision).FirstOrDefault();
            if (change is not null) return new(change.WorkerId, change.Status, change);
            var worker = WorkerAt(Template(id), position);
            return new(worker, worker.HasValue ? "regular" : "vacant", null);
        }

        public IReadOnlyList<MachineTeamChange> ActiveChanges(Guid id, int position)
        {
            var applied = new List<MachineTeamChange>();
            foreach (var change in Changes.Where(x => x.MachineId == id && x.Position == position).OrderBy(x => x.Revision))
            {
                if (change.Action == "reset") applied.RemoveAt(applied.Count - 1);
                else applied.Add(change);
            }
            return applied;
        }
    }

    private sealed record SlotState(Guid? WorkerId, string Status, MachineTeamChange? Change);
}
