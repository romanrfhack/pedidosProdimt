import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, ElementRef, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, timeout } from 'rxjs';
import { AuthService } from './auth.service';
import { EffectivePermissionsService } from './effective-permissions.service';
import {
  MachineWorkspaceApiService, machineWriteActions, type MachineOperators, type MachineTeam, type MachineWorkspace,
  type MachineWrite, type PendingMachineWrite, type TeamAction, type TeamPosition, type TeamSlot,
  type WorkspaceLine, type WorkspaceLot, type WorkspaceMachine, type WorkspaceOrder
} from './machine-workspace-api.service';

type OrderItem = { order: WorkspaceOrder; line: WorkspaceLine };
interface CustomerRow {
  id: string; name: string; orders: WorkspaceOrder[]; items: OrderItem[];
  requested: number; produced: number; pending: number; released: number; complete: boolean; ready: boolean;
}
type DialogView =
  | { kind: 'customer'; customer: CustomerRow }
  | { kind: 'lots' }
  | { kind: 'quantity'; action: 'capture' | 'release' | 'handoff' | 'allocate' | 'lots'; title: string;
      customer?: CustomerRow; item?: OrderItem; lot?: WorkspaceLot; choices?: OrderItem[] }
  | { kind: 'ready'; order: WorkspaceOrder; customer: CustomerRow }
  | { kind: 'daily'; slot: TeamSlot }
  | { kind: 'worker' }
  | { kind: 'operators' };

const kilograms = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 2 });
const serviceDate = new Intl.DateTimeFormat('es-MX', { timeZone: 'UTC', dateStyle: 'long' });
const stamp = new Intl.DateTimeFormat('es-MX', { timeZone: 'America/Mexico_City', dateStyle: 'short', timeStyle: 'short' });
const names: Record<TeamSlot['status'], string> = {
  regular: 'Habitual', absent: 'Ausencia', covered: 'Suplencia', 'moved-out': 'Traslado a otra máquina',
  'moved-in': 'Traslado recibido', vacant: 'Vacante'
};
const actionNames: Record<string, string> = {
  absence: 'Ausencia', cover: 'Suplencia', move: 'Traslado', reset: 'Ajuste retirado',
  'move-out': 'Traslado a otra máquina', 'move-in': 'Traslado recibido'
};

@Component({
  selector: 'app-machine-workspace',
  imports: [FormsModule],
  templateUrl: './machine-workspace.component.html',
  styleUrls: ['./machine-workspace.component.css', './admin-production-dialog.css']
})
export class MachineWorkspaceComponent {
  private readonly api = inject(MachineWorkspaceApiService);
  private readonly access = inject(EffectivePermissionsService);
  private readonly auth = inject(AuthService);
  private readonly destroy = inject(DestroyRef);
  private readonly cd = inject(ChangeDetectorRef);
  @ViewChild('editor', { static: true }) private editor!: ElementRef<HTMLDialogElement>;

  protected machines: WorkspaceMachine[] = [];
  protected machineId = '';
  protected board: MachineWorkspace | null = null;
  protected customers: CustomerRow[] = [];
  protected currentTeam: MachineTeam | null = null;
  protected fixedTeam: MachineTeam | null = null;
  protected fixedDate = '';
  protected fixedWorkers: Record<number, string> = {};
  protected teamMode: 'daily' | 'fixed' = 'daily';
  protected teamOpen = false;
  protected loading = false;
  protected teamLoading = false;
  protected fixedLoading = false;
  protected saving = false;
  protected error = '';
  protected teamError = '';
  protected dialogError = '';
  protected message = '';
  protected dialog: DialogView | null = null;
  protected amount = '';
  protected productId = '';
  protected lineId = '';
  protected dailyAction: TeamAction = 'absence';
  protected workerId = '';
  protected destinationId = '';
  protected destinationPosition: TeamPosition = 1;
  protected workerName = '';
  protected operators: MachineOperators | null = null;
  protected operatorIds = new Set<string>();
  protected pending: PendingMachineWrite | null = null;
  protected pendingCount = 0;
  protected pendingError = '';
  protected storageError = '';

  private readonly beforeUnload = (event: BeforeUnloadEvent) => {
    if (this.saving || this.pending || this.hasDraft()) { event.preventDefault(); event.returnValue = ''; }
  };
  private readonly storageChanged = (event: StorageEvent) => {
    if (event.key === null || event.key === this.storageKey || event.key.startsWith(this.storageKey + '.')) { this.readPending(); this.render(); }
  };

  constructor() {
    this.readPending();
    void this.loadMachines();
    const refresh = () => {
      if (!document.hidden && this.machineId && !this.dialog && !this.teamOpen && !this.saving && !this.loading) void this.load(this.machineId);
    };
    const timer = window.setInterval(refresh, 15_000);
    window.addEventListener('beforeunload', this.beforeUnload);
    window.addEventListener('storage', this.storageChanged);
    document.addEventListener('visibilitychange', refresh);
    this.destroy.onDestroy(() => {
      window.clearInterval(timer);
      window.removeEventListener('beforeunload', this.beforeUnload);
      window.removeEventListener('storage', this.storageChanged);
      document.removeEventListener('visibilitychange', refresh);
    });
  }

  protected get manageAccess(): boolean { return this.access.has('operations.machine.manage'); }
  protected get covered(): number { return this.currentTeam?.slots.filter(slot => slot.workerId).length ?? 0; }
  protected get canWrite(): boolean { return !!this.board && !this.loading && !this.saving && !this.pending && !this.storageError; }
  protected get availableLots(): WorkspaceLot[] { return this.board?.batches.filter(lot => lot.availableQuantity > 0) ?? []; }
  protected get coverageWorkers() {
    const current = this.currentTeam;
    if (!current || this.dialog?.kind !== 'daily') return [];
    const slot = this.dialog.slot;
    const occupied = new Set(current.slots.filter(other => other.position !== slot.position).map(other => other.workerId));
    return current.workers.filter(person => person.isActive && !occupied.has(person.id));
  }

  canLeave(): boolean {
    if (!this.auth.isAdmin()) return true;
    if (this.saving) return false;
    if (this.pending) return window.confirm('La operación sin confirmar quedará guardada para reintentar con la misma referencia. ¿Salir?');
    return !this.hasDraft() || window.confirm('¿Descartar los cambios sin guardar?');
  }

  protected async loadMachines(): Promise<void> {
    this.loading = true;
    this.error = '';
    try {
      this.machines = await firstValueFrom(this.api.machines().pipe(timeout(15_000)));
      if (!this.machines.some(machine => machine.id === this.machineId)) {
        this.machineId = this.machines[0]?.id ?? '';
        this.board = null;
        this.currentTeam = null;
        this.fixedTeam = null;
      }
      if (this.machineId) await this.load(this.machineId);
    } catch (error) { this.error = this.errorText(error, 'No se pudieron consultar las máquinas.'); }
    finally { this.loading = false; this.render(); }
  }

  protected changeMachine(id: string, control: HTMLSelectElement): void {
    if (this.saving || id === this.machineId) return;
    if (this.hasDraft() && !window.confirm('¿Descartar los cambios sin guardar?')) {
      control.value = this.machineId;
      return;
    }
    this.machineId = id;
    this.board = null;
    this.currentTeam = null;
    this.fixedTeam = null;
    this.fixedDate = '';
    this.teamOpen = false;
    this.teamMode = 'daily';
    this.message = '';
    this.closeDialog();
    void this.load(id);
  }

  private async load(id: string): Promise<void> {
    this.loading = true;
    this.error = '';
    try {
      const board = await firstValueFrom(this.api.workspace(id).pipe(timeout(15_000)));
      if (this.machineId !== id || this.destroy.destroyed) return;
      this.board = board;
      this.customers = this.group(board.orders);
      if (!this.fixedDate || this.fixedDate < board.date) this.fixedDate = this.tomorrow(board.date);
      await this.loadTeam(id);
    } catch (error) {
      if (this.machineId === id) {
        this.error = this.errorText(error, 'No se pudo actualizar la producción de esta máquina.');
        this.board = null;
      }
    } finally {
      if (this.machineId === id) this.loading = false;
      this.render();
    }
  }

  protected async loadTeam(id: string): Promise<void> {
    this.teamLoading = true;
    this.teamError = '';
    try {
      const value = await firstValueFrom(this.api.team(id).pipe(timeout(15_000)));
      if (this.machineId === id) this.currentTeam = value;
    } catch (error) {
      if (this.machineId === id) {
        this.currentTeam = null;
        this.teamError = this.errorText(error, 'No se pudo consultar el equipo de esta máquina.');
      }
    } finally {
      if (this.machineId === id) this.teamLoading = false;
      this.render();
    }
  }

  protected toggleTeam(): void {
    if (this.teamOpen && this.hasFixedDraft() && !window.confirm('¿Descartar los cambios del equipo fijo?')) return;
    this.teamOpen = !this.teamOpen;
    if (!this.teamOpen) this.fixedTeam = null;
    if (this.teamOpen && this.teamMode === 'fixed') void this.loadFixed();
  }

  protected setTeamMode(mode: 'daily' | 'fixed'): void {
    if (mode === this.teamMode || this.saving) return;
    if (this.hasFixedDraft() && !window.confirm('¿Descartar los cambios del equipo fijo?')) return;
    this.teamMode = mode;
    this.fixedTeam = null;
    if (mode === 'fixed') void this.loadFixed();
  }

  protected async loadFixed(): Promise<void> {
    if (!this.machineId || !this.fixedDate || this.fixedLoading || this.saving) return;
    const id = this.machineId, date = this.fixedDate;
    this.fixedLoading = true;
    this.teamError = '';
    this.fixedTeam = null;
    try {
      const team = await firstValueFrom(this.api.team(id, date).pipe(timeout(15_000)));
      if (this.machineId !== id || this.fixedDate !== date) return;
      this.fixedWorkers = Object.fromEntries(team.slots.map(slot => [slot.position, slot.regularWorkerId ?? '']));
      this.fixedTeam = team;
    } catch (error) {
      if (this.machineId === id) this.teamError = this.errorText(error, 'No se pudo consultar la plantilla para la fecha elegida.');
    } finally {
      this.fixedLoading = false;
      this.render();
    }
  }

  protected changeFixedDate(date: string, control: HTMLInputElement): void {
    const today = this.currentTeam?.today ?? this.board?.date ?? '';
    if (!date || date < today || this.hasFixedDraft() && !window.confirm('¿Descartar los cambios del equipo fijo?')) {
      control.value = this.fixedDate;
      return;
    }
    this.fixedDate = date;
    this.fixedTeam = null;
    void this.loadFixed();
  }

  protected status(customer: CustomerRow): string {
    if (customer.ready) return 'Listo';
    if (customer.complete) return 'Por marcar listo';
    if (customer.released > 0) return 'Parcial liberado';
    return customer.produced > 0 ? 'En proceso' : 'Pendiente';
  }
  protected teamStatus(slot: TeamSlot): string { return names[slot.status] ?? slot.status; }
  protected teamAction(action: string): string { return actionNames[action] ?? action; }
  protected machineName(id: string): string {
    return this.machines.find(machine => machine.id === id)?.name
      ?? this.currentTeam?.destinations.find(machine => machine.id === id)?.name ?? 'Otra máquina';
  }
  protected qty(quantity: number): string { return kilograms.format(quantity); }
  protected serviceDate(date: string): string { return serviceDate.format(new Date(date + 'T00:00:00Z')); }
  protected time(date: string): string { return stamp.format(new Date(date)); }
  protected lotBalance(lot: WorkspaceLot): number { return Math.max(0, this.sum([lot.availableQuantity, -lot.handedOffQuantity])); }
  protected orderCanRelease(order: WorkspaceOrder): boolean {
    return order.lines.every(line => line.pendingQuantity === 0) && order.lines.some(line => line.releasedQuantity < line.quantity);
  }
  protected lineCanRelease(line: WorkspaceLine): boolean { return this.lineBalance(line) > 0; }
  protected lineBalance(line: WorkspaceLine): number { return this.sum([Math.min(line.quantity, line.allocatedQuantity), -line.releasedQuantity]); }

  protected openCustomer(customer: CustomerRow): void { this.open({ kind: 'customer', customer }); }

  protected openCapture(customer: CustomerRow): void {
    if (!this.canWrite) return;
    const choices = customer.items.filter(item => item.line.pendingQuantity > 0);
    if (choices.length) this.openQuantity({ kind: 'quantity', action: 'capture', title: 'Registrar kilos', customer, choices });
  }

  protected openRelease(customer: CustomerRow): void {
    if (!this.canWrite) return;
    if (customer.orders.length === 1 && this.orderCanRelease(customer.orders[0])) this.open({ kind: 'ready', order: customer.orders[0], customer });
    else this.openCustomer(customer);
  }

  protected openOrderRelease(customer: CustomerRow, order: WorkspaceOrder): void {
    if (this.canWrite && this.orderCanRelease(order)) this.open({ kind: 'ready', order, customer });
  }

  protected openLine(customer: CustomerRow, order: WorkspaceOrder, line: WorkspaceLine, action: 'capture' | 'release'): void {
    if (!this.canWrite) return;
    const title = action === 'capture' ? 'Registrar kilos' : 'Liberar kilos';
    this.openQuantity({ kind: 'quantity', action, title, customer, item: { order, line } });
  }

  protected openLots(): void { this.open({ kind: 'lots' }); }

  protected openNewLot(): void {
    if (this.canWrite) this.openQuantity({ kind: 'quantity', action: 'lots', title: 'Registrar lote sin cliente' });
  }

  protected lotChoices(lot: WorkspaceLot): OrderItem[] {
    if (this.lotBalance(lot) <= 0) return [];
    return this.customers.flatMap(customer => customer.items.filter(item => item.line.productId === lot.productId && item.line.pendingQuantity > 0));
  }

  protected openLotAction(lot: WorkspaceLot, action: 'allocate' | 'handoff'): void {
    if (!this.canWrite || this.lotBalance(lot) <= 0) return;
    const title = action === 'allocate' ? 'Vincular kilos al pedido' : 'Entregar a Embarques';
    const choices = action === 'allocate' ? this.lotChoices(lot) : undefined;
    if (!choices || choices.length) this.openQuantity({ kind: 'quantity', action, title, lot, choices });
  }

  private openQuantity(view: Extract<DialogView, { kind: 'quantity' }>): void {
    this.amount = '';
    this.productId = '';
    this.lineId = view.choices?.[0]?.line.orderLineId ?? '';
    this.open(view);
  }

  protected quantityLimit(view: Extract<DialogView, { kind: 'quantity' }>): number | null {
    const item = this.lineFor(view);
    if (view.action === 'capture') return item?.line.pendingQuantity ?? 0;
    if (view.action === 'release') return item ? this.lineBalance(item.line) : 0;
    if (view.action === 'handoff') return this.lotBalance(view.lot!);
    if (view.action === 'allocate') return Math.min(item?.line.pendingQuantity ?? 0, this.lotBalance(view.lot!));
    return null;
  }

  protected confirmQuantity(view: Extract<DialogView, { kind: 'quantity' }>): void {
    if (!this.canWrite) return;
    const input = this.amount.trim().replace(',', '.');
    const quantity = Number(input);
    const max = this.quantityLimit(view);
    if (!/^\d+(\.\d{1,2})?$/.test(input) || !Number.isFinite(quantity) || quantity <= 0 || (max !== null && quantity > max)) {
      this.dialogError = max === null ? 'Captura kilos mayores a cero con máximo dos decimales.'
        : `Captura una cantidad mayor a cero y de hasta ${this.qty(max)} kg, con máximo dos decimales.`;
      return;
    }
    const operationId = crypto.randomUUID();
    if (view.action === 'lots') {
      if (!this.productId) { this.dialogError = 'Selecciona el producto fabricado.'; return; }
      void this.submit({ action: 'lots', body: { operationId, productId: this.productId, quantity, date: this.board!.date } });
      return;
    }
    if (view.action === 'handoff') {
      void this.submit({ action: 'handoff', body: { operationId, batchId: view.lot!.id, quantity } });
      return;
    }
    const item = this.lineFor(view);
    if (!item) return;
    const body = { operationId, orderId: item.order.orderId, orderLineId: item.line.orderLineId,
      expectedOrderVersion: item.order.orderVersion, quantity };
    if (view.action === 'allocate') void this.submit({ action: 'allocate', body: { ...body, batchId: view.lot!.id } });
    if (view.action === 'capture') void this.submit({ action: 'capture', body });
    if (view.action === 'release') void this.submit({ action: 'release', body });
  }

  protected confirmReady(order: WorkspaceOrder): void {
    if (!this.canWrite || !this.orderCanRelease(order)) return;
    void this.submit({ action: 'release', body: { operationId: crypto.randomUUID(), orderId: order.orderId,
      orderLineId: null, quantity: null, expectedOrderVersion: order.orderVersion } });
  }

  private lineFor(view: Extract<DialogView, { kind: 'quantity' }>): OrderItem | undefined {
    return view.item ?? view.choices?.find(item => item.line.orderLineId === this.lineId);
  }

  protected adjust(slot: TeamSlot): void {
    if (!this.canWrite || !this.currentTeam?.canEditDaily) return;
    this.dailyAction = slot.workerId ? 'absence' : 'cover';
    this.workerId = '';
    this.destinationId = '';
    this.destinationPosition = slot.position;
    this.open({ kind: 'daily', slot });
  }

  protected saveDaily(slot: TeamSlot): void {
    const team = this.currentTeam;
    if (!this.canWrite || !team?.canEditDaily) return;
    if (this.dailyAction === 'cover' && !this.workerId) { this.dialogError = 'Selecciona quién cubre la plaza.'; return; }
    if (this.dailyAction === 'move' && !this.destinationId) { this.dialogError = 'Selecciona la máquina y plaza de destino.'; return; }
    if (this.dailyAction === 'move' && !slot.workerId) return;
    void this.submit({ action: 'team/daily', body: {
      operationId: crypto.randomUUID(), date: team.date, expectedVersion: team.version, position: slot.position,
      action: this.dailyAction, workerId: this.dailyAction === 'cover' ? this.workerId : null,
      otherMachineId: this.dailyAction === 'move' ? this.destinationId : null,
      otherPosition: this.dailyAction === 'move' ? this.destinationPosition : null
    } });
  }

  protected saveFixed(): void {
    const team = this.fixedTeam;
    if (!this.canWrite || !team || team.date !== this.fixedDate || !this.hasFixedDraft()) return;
    const slots = team.slots.map(slot => ({ position: slot.position, workerId: this.fixedWorkers[slot.position] || null }));
    const ids = slots.flatMap(slot => slot.workerId ?? []);
    if (new Set(ids).size !== ids.length) { this.teamError = 'Una persona solo puede cubrir una plaza en la plantilla.'; return; }
    void this.submit({ action: 'team/regular', body: {
      operationId: crypto.randomUUID(), effectiveDate: team.date, expectedVersion: team.version, slots
    } });
  }

  protected openWorker(): void {
    if (!this.canWrite || !this.currentTeam?.canManageWorkers) return;
    this.workerName = '';
    this.open({ kind: 'worker' });
  }

  protected saveWorker(): void {
    const name = this.workerName.trim();
    if (!name) { this.dialogError = 'Captura el nombre de la persona.'; return; }
    if (this.canWrite && this.currentTeam?.canManageWorkers) void this.submit({ action: 'workers', body: { operationId: crypto.randomUUID(), name } });
  }

  protected async openOperators(): Promise<void> {
    if (!this.canWrite || !this.manageAccess) return;
    const id = this.machineId;
    this.operators = null;
    this.open({ kind: 'operators' });
    try {
      const result = await firstValueFrom(this.api.operators(id).pipe(timeout(15_000)));
      if (this.machineId !== id || this.dialog?.kind !== 'operators') return;
      this.operators = result;
      this.operatorIds = new Set(result.userIds);
    } catch (error) {
      if (this.machineId === id && this.dialog?.kind === 'operators') this.dialogError = this.errorText(error, 'No se pudieron consultar las cuentas.');
    } finally { this.render(); }
  }

  protected toggleOperator(id: string, active: boolean): void {
    if (active) this.operatorIds.add(id); else this.operatorIds.delete(id);
  }

  protected saveOperators(): void {
    if (!this.canWrite || !this.operators || !this.manageAccess) return;
    void this.submit({ action: 'operators', body: { operationId: crypto.randomUUID(),
      expectedVersion: this.operators.version, userIds: [...this.operatorIds] } });
  }

  private open(view: DialogView): void {
    this.dialogError = '';
    this.dialog = view;
    this.render();
    if (!this.editor.nativeElement.open) this.editor.nativeElement.showModal();
    this.editor.nativeElement.querySelector<HTMLElement>('input:not([disabled]),select:not([disabled])')?.focus();
  }

  protected closeDialog(event?: Event): void {
    if (this.saving) { event?.preventDefault(); return; }
    this.editor.nativeElement.close();
    this.dialog = null;
    this.dialogError = '';
  }

  protected backFromQuantity(view: Extract<DialogView, { kind: 'quantity' }>): void {
    if (view.customer) this.openCustomer(view.customer);
    else if (view.lot || view.action === 'lots') this.openLots();
    else this.closeDialog();
  }

  protected retry(): void {
    if (this.pending && !this.saving && !this.storageError) void this.send(this.pending, false);
  }

  private async submit(write: MachineWrite): Promise<void> {
    if (!this.canWrite) return;
    const userId = this.access.userId();
    if (!userId || !this.machineId) return;
    const command: PendingMachineWrite = { ...write, userId, machineId: this.machineId };
    this.readPending();
    if (this.pending || this.storageError) return;
    try {
      const serialized = JSON.stringify(command);
      const key = this.commandKey(command);
      localStorage.setItem(key, serialized);
      if (localStorage.getItem(key) !== serialized) throw new Error();
      this.readPending();
      if (this.storageError) throw new Error();
    } catch {
      this.storageError = 'No se pudo guardar la referencia en este navegador. La operación sigue sin enviarse.';
      return;
    }
    await this.send(command, true);
  }

  private async send(command: PendingMachineWrite, first: boolean): Promise<void> {
    if (this.saving || this.access.userId() !== command.userId) return;
    this.saving = true;
    this.pendingError = '';
    this.dialogError = '';
    try {
      await firstValueFrom(this.api.write(command).pipe(timeout(20_000)));
      this.clearPending(command);
      this.message = ({
        capture: 'Kilos registrados para el pedido.', lots: 'Lote sin cliente registrado.', allocate: 'Kilos vinculados al pedido.',
        release: 'Producción liberada para Embarques.', handoff: 'Entrega del lote a Embarques registrada.',
        'team/daily': 'Equipo de hoy actualizado.', 'team/regular': 'Equipo fijo actualizado.',
        workers: 'Persona registrada en el catálogo.', operators: 'Cuentas responsables actualizadas.'
      })[command.action];
      this.saving = false;
      this.closeDialog();
      await this.load(this.machineId);
      if (this.teamOpen && this.teamMode === 'fixed' && (command.action === 'team/regular' || command.action === 'workers')) await this.loadFixed();
    } catch (error) {
      const known = first && error instanceof HttpErrorResponse &&
        ([400, 403, 404].includes(error.status) || error.status === 409 && error.error?.retrySameOperation === false);
      if (known) {
        try { this.clearPending(command); }
        catch { this.pendingError = 'Se recibió una respuesta, pero la referencia sigue guardada. Reintenta para comprobarla.'; }
      }
      if (error instanceof HttpErrorResponse && error.status === 403) void this.access.refresh();
      const detail = this.errorText(error, known ? 'No se pudo guardar el cambio.' : 'No se recibió confirmación. Reintenta con la misma referencia.');
      if (this.dialog) this.dialogError = detail;
      else if (known && command.action === 'team/regular') this.teamError = detail;
      else this.pendingError ||= detail;
      if (known && error instanceof HttpErrorResponse && [404, 409].includes(error.status)) {
        // La respuesta rechazada no puede confirmar otro intento anterior.
        this.saving = false;
        this.closeDialog();
        this.pendingError = detail;
        await this.load(this.machineId);
        if (this.teamOpen && this.teamMode === 'fixed' && command.action === 'team/regular') await this.loadFixed();
      }
    } finally {
      this.saving = false;
      this.render();
    }
  }

  private get storageKey(): string { return 'prodimt.machine-workspace.pending.v1.' + this.access.userId(); }

  private readPending(): void {
    const userId = this.access.userId();
    this.storageError = '';
    try {
      if (!userId) throw new Error();
      const commands = new Map<string, PendingMachineWrite>();
      this.pending = null;
      this.pendingCount = 0;
      for (let index = 0; index < localStorage.length; index++) {
        const key = localStorage.key(index);
        if (key !== this.storageKey && !key?.startsWith(this.storageKey + '.')) continue;
        const value = JSON.parse(localStorage.getItem(key) ?? 'null') as PendingMachineWrite;
        if (!value || value.userId !== userId || !machineWriteActions.has(value.action) ||
          !/^[\w-]{1,128}$/.test(value.machineId) || !/^[a-f0-9-]{36}$/i.test(value.body?.operationId) ||
          key !== this.storageKey && key !== this.commandKey(value))
          throw new Error();
        const existing = commands.get(value.body.operationId);
        if (existing && JSON.stringify(existing) !== JSON.stringify(value)) throw new Error();
        commands.set(value.body.operationId, value);
      }
      const recovered = [...commands.values()].sort((first, second) => first.body.operationId.localeCompare(second.body.operationId));
      this.pending = recovered[0] ?? null;
      this.pendingCount = recovered.length;
    } catch { this.storageError = 'No se pudo recuperar la referencia guardada. Revisa el almacenamiento del navegador antes de capturar nuevamente.'; }
  }

  private commandKey(command: PendingMachineWrite): string { return this.storageKey + '.' + command.body.operationId; }

  private clearPending(command: PendingMachineWrite): void {
    const key = this.commandKey(command);
    const serialized = JSON.stringify(command);
    const previous = localStorage.getItem(key);
    if (previous !== null && previous !== serialized) throw new Error();
    const legacy = localStorage.getItem(this.storageKey);
    if (legacy === serialized) {
      localStorage.removeItem(this.storageKey);
      if (localStorage.getItem(this.storageKey) !== null) throw new Error();
    }
    localStorage.removeItem(key);
    if (localStorage.getItem(key) !== null) throw new Error();
    this.readPending();
  }

  private group(orders: WorkspaceOrder[]): CustomerRow[] {
    const rows = new Map<string, CustomerRow>();
    for (const order of orders) {
      const id = order.customerId ?? order.orderId;
      const items = order.lines.map(line => ({ order, line }));
      const row = rows.get(id);
      if (row) { row.orders.push(order); row.items.push(...items); }
      else rows.set(id, { id, name: order.customerName, orders: [order], items,
        requested: 0, produced: 0, pending: 0, released: 0, ready: false, complete: false });
    }
    for (const row of rows.values()) {
      row.requested = this.sum(row.items.map(({ line }) => line.quantity));
      row.produced = this.sum(row.items.map(({ line }) => line.allocatedQuantity));
      row.pending = this.sum(row.items.map(({ line }) => line.pendingQuantity));
      row.released = this.sum(row.items.map(({ line }) => line.releasedQuantity));
      row.complete = row.pending === 0;
      row.ready = row.items.every(({ line }) => line.releasedQuantity >= line.quantity);
    }
    return [...rows.values()].sort((first, next) => Number(first.ready) - Number(next.ready) || first.name.localeCompare(next.name, 'es-MX'));
  }

  private hasDraft(): boolean { return !!this.dialog && !['lots', 'customer'].includes(this.dialog.kind) || this.hasFixedDraft(); }
  protected hasFixedDraft(): boolean {
    return this.fixedTeam?.slots.some(slot => (slot.regularWorkerId ?? '') !== this.fixedWorkers[slot.position]) ?? false;
  }
  private sum(values: number[]): number { return values.reduce((total, value) => total + Math.round(value * 100), 0) / 100; }
  private tomorrow(date: string): string {
    const value = new Date(`${date}T00:00:00Z`);
    value.setUTCDate(value.getUTCDate() + 1);
    return value.toISOString().slice(0, 10);
  }
  private errorText(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && typeof error.error?.error === 'string' ? error.error.error : fallback;
  }
  private render(): void { if (!this.destroy.destroyed) this.cd.detectChanges(); }
}
