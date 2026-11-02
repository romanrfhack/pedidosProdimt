import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, ElementRef, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, Observable, of, Subscription, switchMap, throwError } from 'rxjs';
import { EffectivePermissionsService } from './effective-permissions.service';
import { AdminCatalogApiService, AdminProductCatalogApiResponse } from './admin-catalog-api.service';
import {
  AcknowledgeChangeRequest, AdminOperationsApiService, OperationsChange, OperationsChanges, OperationsPlan,
  ProductionAllocation, ProductionBatch, ProductionCommandResponse, ProductionDashboard, ProductionLine, ProductionOrder
} from './admin-operations-api.service';
import { businessDateToday, unitLabel } from './operational-labels';

type EditorKind = 'batch' | 'machine' | 'allocation' | 'reverse';
interface ProductionEditor {
  kind: EditorKind; operationId: string; order: ProductionOrder | null; line: ProductionLine | null;
  allocation: ProductionAllocation | null; machineId: string; productId: string; batchId: string;
  quantity: number | null; reason: string; notes: string; error: string | null;
  sourceBatch: ProductionBatch | null;
  locked: boolean; conflict: boolean; submitted: (() => Observable<ProductionCommandResponse>) | null;
}

@Component({
  selector: 'app-admin-operations',
  imports: [FormsModule, RouterLink],
  templateUrl: './admin-operations.component.html',
  styleUrls: ['./admin-operations.component.css', './admin-production-dialog.css']
})
export class AdminOperationsComponent {
  private readonly api = inject(AdminOperationsApiService);
  private readonly catalogs = inject(AdminCatalogApiService);
  protected readonly access = inject(EffectivePermissionsService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  @ViewChild('productionDialog') private productionDialog?: ElementRef<HTMLDialogElement>;
  private planRequest: Subscription | null = null;
  private requestGeneration = 0;
  private appliedFilter: { date: string; routeCode: string } | null = null;
  private readonly acknowledgeRequests = new Map<string, AcknowledgeChangeRequest>();
  private selectionInitialized = false;
  protected date = this.route.snapshot.queryParamMap.get('date') || businessDateToday();
  protected routeCode = '';
  protected plan: OperationsPlan | null = null;
  protected production: ProductionDashboard | null = null;
  protected isLoading = false;
  protected isRefreshing = false;
  protected readonly selectedMachines = new Set<string>();
  protected changes: OperationsChange[] = [];
  protected lastUpdated: string | null = null;
  protected refreshError: string | null = null;
  protected acknowledgeError: string | null = null;
  protected readonly acknowledgingIds = new Set<string>();
  protected errorMessage: string | null = null;
  protected successMessage: string | null = null;
  protected editor: ProductionEditor | null = null;
  protected saving = false;
  protected loadingProducts = false;
  protected productionProducts: AdminProductCatalogApiResponse[] = [];
  protected readonly unitLabel = unitLabel;

  constructor() {
    this.load();
    const refresh = () => {
      if (!document.hidden && this.plan && this.appliedFilter && !this.editor && !this.acknowledgingIds.size && !this.isLoading && !this.isRefreshing) this.load(true);
    };
    const timer = window.setInterval(refresh, 15_000);
    document.addEventListener('visibilitychange', refresh);
    this.destroyRef.onDestroy(() => {
      window.clearInterval(timer); document.removeEventListener('visibilitychange', refresh); this.planRequest?.unsubscribe();
    });
  }

  protected clearPlan(): void {
    this.requestGeneration++; this.planRequest?.unsubscribe(); this.planRequest = null;
    this.plan = null; this.production = null; this.changes = []; this.appliedFilter = null;
    this.errorMessage = null; this.refreshError = null; this.acknowledgeError = null;
    this.isLoading = false; this.isRefreshing = false;
  }

  protected load(background = false): void {
    if (!this.date || this.isLoading || this.isRefreshing || (background && !this.appliedFilter)) return;
    if (!background) this.clearPlan();
    const filter = background ? this.appliedFilter! : { date: this.date, routeCode: this.routeCode.trim() };
    const generation = ++this.requestGeneration;
    this.isLoading = !background; this.isRefreshing = background;
    this.planRequest = this.readConsistentPlan(filter).subscribe({
      next: ({ plan, changes, production }) => {
        if (generation !== this.requestGeneration) return;
        this.plan = plan; this.production = production; this.changes = changes.changes; this.appliedFilter = filter;
        if (!this.selectionInitialized) { this.selectAllMachines(); this.selectionInitialized = true; }
        this.lastUpdated = this.formatTimestamp(new Date().toISOString());
        this.isLoading = false; this.isRefreshing = false; this.refreshError = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (generation !== this.requestGeneration) return;
        const message = this.message(error, 'No se pudo consultar el plan de producción.');
        if (background) this.refreshError = message; else this.errorMessage = message;
        this.isLoading = false; this.isRefreshing = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  private readConsistentPlan(filter: { date: string; routeCode: string }, attempt = 1): Observable<{ plan: OperationsPlan; changes: OperationsChanges; production: ProductionDashboard }> {
    return this.api.getPlan(filter.date, filter.routeCode).pipe(switchMap(plan =>
      this.api.getChanges(filter.date, filter.routeCode).pipe(switchMap(changes => {
        const versions = new Map(plan.orders.map(order => [order.orderId, order.orderVersion]));
        // A cancelled order or its last production line can leave the current
        // plan while its demand-removal notice still needs acknowledgement.
        if (!changes.changes.every(change => !versions.has(change.orderId) || versions.get(change.orderId)! >= change.orderVersion)) {
          if (attempt < 3) return this.readConsistentPlan(filter, attempt + 1);
          return throwError(() => new Error('El plan cambió durante la consulta. Vuelve a consultar para recibir los avisos con sus cantidades actualizadas.'));
        }
        return this.api.getProduction(filter.date, filter.routeCode).pipe(switchMap(production => {
          const productionIds = new Set(production.orders.map(order => order.orderId));
          if (production.orders.some(order => !versions.has(order.orderId) || versions.get(order.orderId) !== order.orderVersion)
            || plan.orders.some(order => !productionIds.has(order.orderId))) {
            if (attempt < 3) return this.readConsistentPlan(filter, attempt + 1);
            return throwError(() => new Error('La producción cambió durante la consulta. Vuelve a consultar el plan.'));
          }
          return of({ plan, changes, production });
        }));
      }))));
  }

  protected get availableMachines(): Array<{ id: string; label: string }> {
    const values = new Map<string, string>();
    for (const machine of this.production?.machines ?? []) values.set(machine.machineId, this.machineLabel(machine.machineNumber, machine.machineName));
    for (const item of this.plan?.machineDemand ?? []) if (item.machineId) values.set(item.machineId, this.machineLabel(item.machineNumber, item.machineName));
    for (const change of this.changes) if (change.machineId) values.set(change.machineId, this.machineLabel(change.machineNumber, change.machineName));
    return Array.from(values, ([id, label]) => ({ id, label })).sort((a, b) => a.label.localeCompare(b.label, 'es', { numeric: true }));
  }
  protected selectAllMachines(): void { this.selectedMachines.clear(); this.selectedMachines.add('unassigned'); for (const item of this.availableMachines) this.selectedMachines.add(item.id); }
  protected toggleMachine(id: string): void { this.selectedMachines.has(id) ? this.selectedMachines.delete(id) : this.selectedMachines.add(id); }
  private matchesMachine(id: string | null): boolean { return this.selectedMachines.has(id ?? 'unassigned'); }
  protected machineCount(id: string | null): number { return (this.production?.orders ?? []).flatMap(order => order.lines).filter(line => line.assignedMachineId === id).length; }
  protected get pendingChanges() { return this.changes.filter(change => !change.acknowledgedAt && this.matchesMachine(change.machineId)); }
  protected get receivedChanges() { return this.changes.filter(change => !!change.acknowledgedAt && this.matchesMachine(change.machineId)); }
  protected get visibleDemand() { return (this.plan?.machineDemand ?? []).filter(item => this.matchesMachine(item.machineId)); }
  protected get visibleOrders() { return (this.production?.orders ?? []).filter(order => this.visibleLines(order).length > 0); }
  protected visibleLines(order: ProductionOrder) { return order.lines.filter(line => this.matchesMachine(line.assignedMachineId)); }
  protected get visibleBatches() { return (this.production?.batches ?? []).filter(batch => this.matchesMachine(batch.machineId)); }
  protected get visibleWarnings() { return (this.plan?.warnings ?? []).filter(warning => !warning.orderLineId || this.visibleOrders.some(order => this.visibleLines(order).some(line => line.orderLineId === warning.orderLineId))); }
  protected hasPendingOrderChange(id: string) { return this.pendingChanges.some(change => change.orderId === id); }
  protected status(order: ProductionOrder): string {
    const lines = this.visibleLines(order);
    if (lines.some(line => !line.assignedMachineId)) return 'Sin asignar';
    if (lines.some(line => line.unitCode !== 'kg')) return 'Revisar preparación';
    if (lines.every(line => line.pendingAllocationQuantity <= 0)) return 'Asignado a pedido';
    return lines.some(line => line.allocatedQuantity > 0) ? 'Asignación parcial' : 'Por preparar';
  }
  protected lineAllocations(line: ProductionLine): ProductionAllocation[] { return (this.production?.allocations ?? []).filter(item => item.orderLineId === line.orderLineId && !item.reversedAt); }
  protected matchingBatches(line: ProductionLine): ProductionBatch[] { return (this.production?.batches ?? []).filter(batch => batch.machineId === line.assignedMachineId && batch.productId === line.productId && batch.unitCode === line.unitCode && batch.availableQuantity > 0); }
  protected get editorBatches(): ProductionBatch[] { return this.editor?.sourceBatch ? [this.editor.sourceBatch] : this.editor?.line ? this.matchingBatches(this.editor.line) : []; }
  protected get activeMachines() { return (this.production?.machines ?? []).filter(machine => machine.isActive !== false); }
  protected get selectedMachineLabels(): string[] { return [...(this.selectedMachines.has('unassigned') ? ['Sin máquina asignada'] : []), ...this.availableMachines.filter(machine => this.selectedMachines.has(machine.id)).map(machine => machine.label)]; }
  protected get selectedBatch(): ProductionBatch | undefined { return this.editorBatches.find(batch => batch.id === this.editor?.batchId); }
  protected get allocationLimit(): number { return Math.min(this.editor?.line?.pendingAllocationQuantity ?? 0, this.selectedBatch?.availableQuantity ?? 0); }
  protected canWrite(): boolean { return !this.isLoading && !this.isRefreshing && !this.refreshError && !this.saving; }
  protected get pendingConfirmation(): boolean { return !!(this.editor?.locked && this.editor.submitted && !this.editor.conflict); }
  protected quantity(value: number): string { return new Intl.NumberFormat('es-MX', { maximumFractionDigits: 2 }).format(value); }
  protected batchTargets(batch: ProductionBatch): Array<{ key: string; label: string; order: ProductionOrder; line: ProductionLine }> {
    return (this.production?.orders ?? []).flatMap(order => order.lines.filter(line => line.assignedMachineId === batch.machineId && line.productId === batch.productId && line.unitCode === batch.unitCode && line.pendingAllocationQuantity > 0)
      .map(line => ({ key: `${order.orderId}:${line.orderLineId}`, label: `${order.customerName} · Pedido #${order.sequenceNumber} · faltan ${this.quantity(line.pendingAllocationQuantity)} kg`, order, line })));
  }
  protected openBatchAllocation(batch: ProductionBatch): void {
    const target = this.batchTargets(batch)[0];
    if (!target) return;
    this.openEditor('allocation', target.order, target.line);
    if (this.editor) { this.editor.sourceBatch = batch; this.editor.batchId = batch.id; }
  }
  protected changeAllocationTarget(key: string): void {
    const editor = this.editor;
    if (!editor?.sourceBatch) return;
    const target = this.batchTargets(editor.sourceBatch).find(item => item.key === key);
    if (target) { editor.order = target.order; editor.line = target.line; editor.quantity = null; }
  }

  protected openEditor(kind: EditorKind, order: ProductionOrder | null = null, line: ProductionLine | null = null, allocation: ProductionAllocation | null = null, machineId = ''): void {
    const permission = kind === 'batch' ? 'operations.produce' : kind === 'machine' ? 'operations.assign' : 'operations.allocate';
    if (!this.canWrite() || !this.access.has(permission) || this.pendingConfirmation) return;
    this.loadingProducts = false;
    const batches = line ? this.matchingBatches(line) : [];
    this.editor = { kind, operationId: crypto.randomUUID(), order, line, allocation, machineId: machineId || line?.assignedMachineId || '',
      productId: line?.productId || '', batchId: batches[0]?.id || '', quantity: null, reason: '', notes: '', error: null, sourceBatch: null, locked: false, conflict: false, submitted: null };
    this.changeDetector.detectChanges(); this.productionDialog?.nativeElement.showModal();
    if (kind === 'batch') {
      this.loadingProducts = true;
      const current = this.editor;
      const subscription = this.catalogs.getProducts().subscribe({
        next: products => { if (this.editor !== current) return; this.productionProducts = products.filter(product => product.isActive && product.unitCode === 'kg' && product.requiresMachine !== false); this.loadingProducts = false; this.changeDetector.detectChanges(); },
        error: error => { if (this.editor !== current) return; current.error = this.message(error, 'No se pudo consultar el catálogo de productos.'); this.loadingProducts = false; this.changeDetector.detectChanges(); }
      });
      this.destroyRef.onDestroy(() => subscription.unsubscribe());
    }
  }
  protected closeEditor(event?: Event): void {
    if (this.saving || this.pendingConfirmation) { event?.preventDefault(); return; }
    this.productionDialog?.nativeElement.close(); this.editor = null; this.loadingProducts = false;
  }
  protected closeAndRefresh(): void { this.closeEditor(); this.load(true); }
  protected saveEditor(): void {
    const editor = this.editor;
    if (!editor || this.saving || editor.conflict) return;
    if (!editor.submitted) {
      let command: () => Observable<ProductionCommandResponse>;
      if (editor.kind === 'batch') {
        if (!editor.machineId || !editor.productId || !this.validQuantity(editor.quantity)) { editor.error = 'Selecciona máquina, producto y kilos mayores a cero, con máximo dos decimales.'; return; }
        const request = { operationId: editor.operationId, date: this.production!.date, machineId: editor.machineId, productId: editor.productId, quantity: editor.quantity!, notes: editor.notes.trim() || null };
        command = () => this.api.registerProduction(request);
      } else if (editor.kind === 'allocation') {
        if (!editor.batchId || !this.validQuantity(editor.quantity) || editor.quantity! > this.allocationLimit) { editor.error = 'La cantidad debe ser mayor a cero y no superar el saldo del lote ni lo pendiente del pedido.'; return; }
        const request = { operationId: editor.operationId, batchId: editor.batchId, orderId: editor.order!.orderId, orderLineId: editor.line!.orderLineId, quantity: editor.quantity!, expectedOrderVersion: editor.order!.orderVersion };
        command = () => this.api.allocateProduction(request);
      } else if (editor.kind === 'machine') {
        if (!editor.reason.trim()) { editor.error = 'Escribe el motivo de la asignación.'; return; }
        const request = { operationId: editor.operationId, machineId: editor.machineId || null, expectedOrderVersion: editor.order!.orderVersion, reason: editor.reason.trim() };
        command = () => this.api.assignMachine(editor.order!.orderId, editor.line!.orderLineId, request);
      } else {
        if (!editor.reason.trim()) { editor.error = 'Escribe el motivo para devolver los kilos al lote.'; return; }
        const request = { operationId: editor.operationId, reason: editor.reason.trim() };
        command = () => this.api.reverseAllocation(editor.allocation!.id, request);
      }
      editor.submitted = command;
    }
    this.saving = true; editor.locked = true; editor.error = null;
    const subscription = editor.submitted().pipe(finalize(() => { this.saving = false; if (!this.destroyRef.destroyed) this.changeDetector.detectChanges(); })).subscribe({
      next: () => {
        editor.submitted = null; editor.locked = false;
        this.saving = false; this.closeEditor();
        this.successMessage = editor.kind === 'batch' ? 'Producción registrada. Los kilos están disponibles para asignar a pedidos.' : editor.kind === 'allocation' ? 'Kilos asignados al pedido.' : editor.kind === 'reverse' ? 'Asignación revertida. Los kilos vuelven a estar disponibles en el lote.' : 'Máquina del pedido actualizada.';
        this.load(true);
      },
      error: (error: unknown) => {
        editor.error = this.message(error, 'No se recibió confirmación. Puedes reintentar la misma operación para comprobar su resultado.');
        if (error instanceof HttpErrorResponse && [400, 404, 409, 403].includes(error.status)) {
          editor.conflict = error.status !== 400;
          if (error.status === 400) { editor.locked = false; editor.submitted = null; editor.operationId = crypto.randomUUID(); }
          if (error.status === 403) void this.access.refresh();
        }
        this.changeDetector.detectChanges();
      }
    });
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }
  private validQuantity(value: number | null): boolean { return value !== null && Number.isFinite(value) && value > 0 && Math.abs(value * 100 - Math.round(value * 100)) < 0.000001; }
  private message(error: unknown, fallback: string): string { return error instanceof HttpErrorResponse ? typeof error.error?.error === 'string' ? error.error.error : fallback : error instanceof Error ? error.message : fallback; }

  protected acknowledge(change: OperationsChange): void {
    if (change.acknowledgedAt || this.acknowledgingIds.has(change.id) || this.isLoading || this.isRefreshing || this.refreshError) return;
    const generation = this.requestGeneration;
    const request = this.acknowledgeRequests.get(change.id) ?? { operationKey: crypto.randomUUID(), expectedVersion: change.version };
    this.acknowledgeRequests.set(change.id, request); this.acknowledgingIds.add(change.id); this.acknowledgeError = null;
    const subscription = this.api.acknowledge(change.id, request).pipe(finalize(() => {
      this.acknowledgingIds.delete(change.id); if (!this.destroyRef.destroyed) this.changeDetector.detectChanges();
    })).subscribe({
      next: updated => { if (generation !== this.requestGeneration) return; this.changes = this.changes.map(item => item.id === updated.id ? updated : item); this.acknowledgeRequests.delete(change.id); this.changeDetector.detectChanges(); },
      error: (error: unknown) => { if (generation !== this.requestGeneration) return; this.acknowledgeError = error instanceof HttpErrorResponse && error.status === 409 ? 'El aviso cambió. Consulta el plan para verificar si ya fue recibido por otra persona.' : 'No se recibió confirmación. El aviso sigue visible; puedes reintentar «Cambio recibido» con la misma referencia.'; this.changeDetector.detectChanges(); }
    });
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }
  protected formatTimestamp(value: string): string { return new Intl.DateTimeFormat('es-MX', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'America/Mexico_City' }).format(new Date(value)); }
  protected deltaLabel(quantity: number, unit: string | null): string { return quantity === 0 ? 'Cantidad sin cambio; revisa la nota' : `${quantity > 0 ? '+' : ''}${quantity} ${unitLabel(unit)} solicitados`; }
  protected machineLabel(number: number | null, name: string | null): string { return number !== null ? `Máquina ${number}${name && name !== `Máquina ${number}` && name !== `Maquina ${number}` ? ` · ${name}` : ''}` : name || 'Sin máquina asignada'; }
  protected deliveryWindow(order: ProductionOrder): string {
    if (order.requestedDeliveryTime) return order.requestedDeliveryTime.slice(0, 5);
    const start = order.requestedDeliveryWindowStart?.slice(0, 5), end = order.requestedDeliveryWindowEnd?.slice(0, 5);
    return start && end ? `${start} – ${end}` : start ? `Desde ${start}` : end ? `Antes de ${end}` : '';
  }
  protected warningContext(warning: OperationsPlan['warnings'][number]): string {
    const order = this.plan?.orders.find(item => item.orderId === warning.orderId), pending = this.plan?.pendingReview.find(item => item.orderId === warning.orderId);
    const line = order?.lines.find(item => item.orderLineId === warning.orderLineId);
    return order || pending ? `${(order ?? pending)!.customerName} · Pedido #${(order ?? pending)!.sequenceNumber}${line ? ` · ${line.productName}` : ''}` : `Pedido ${warning.orderId}`;
  }
  protected print(): void { if (this.plan) window.print(); }
}
