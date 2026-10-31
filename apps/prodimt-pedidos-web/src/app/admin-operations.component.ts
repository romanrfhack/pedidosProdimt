import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, Observable, of, Subscription, switchMap, throwError } from 'rxjs';
import { AcknowledgeChangeRequest, AdminOperationsApiService, OperationsChange, OperationsChanges, OperationsOrder, OperationsPlan } from './admin-operations-api.service';
import { businessDateToday, paymentTermLabel, storeKeyLabel, unitLabel } from './operational-labels';

@Component({
  selector: 'app-admin-operations',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="page stack operations-plan" data-testid="operations-plan">
      <div class="page-title">
        <p class="eyebrow">Operación · planeación</p>
        <h2>Plan del día</h2>
        <p>Consulta la demanda autorizada y las instrucciones para preparar cada pedido.</p>
      </div>

      <form class="operations-filters no-print" (ngSubmit)="load()">
        <label><span>Fecha de servicio</span>
          <input name="serviceDate" type="date" required [(ngModel)]="date" (ngModelChange)="clearPlan()" [disabled]="isLoading">
        </label>
        <label><span>Código de ruta</span>
          <input name="routeCode" [(ngModel)]="routeCode" maxlength="80" placeholder="Todas las rutas" (ngModelChange)="clearPlan()" [disabled]="isLoading">
        </label>
        <button class="primary" type="submit" [disabled]="isLoading || !date">Consultar plan</button>
        <label><span>Máquina a consultar</span>
          <select name="selectedMachine" [(ngModel)]="selectedMachine">
            <option value="">Todas las máquinas</option>
            <option value="unassigned">Sin máquina asignada</option>
            @for (machine of availableMachines; track machine.id) { <option [value]="machine.id">{{ machine.label }}</option> }
          </select>
        </label>
      </form>

      @if (isLoading) { <p class="notice" aria-live="polite">Cargando plan...</p> }
      @if (errorMessage) { <p class="alert error" role="alert">{{ errorMessage }}</p> }
      @if (refreshError) { <p class="alert warning" role="alert">{{ refreshError }} Las cantidades y avisos mostrados pueden estar desactualizados. Vuelve a consultar antes de continuar.</p> }

      @if (plan; as current) {
        <div class="operations-heading">
          <div><strong>{{ current.date }} · {{ current.routeCode || 'Todas las rutas' }}</strong>
            <small>{{ current.orders.length }} pedidos incluidos · {{ current.pendingReview.length }} pendientes de revisión</small>
            @if (lastUpdated) { <small>Última consulta: {{ lastUpdated }} · actualización automática cada 15 segundos mientras esta pantalla está visible.</small> }
          </div>
          <button class="secondary no-print" type="button" (click)="print()">Imprimir plan</button>
        </div>
        <p class="notice">Documento interno de planeación. Las cantidades son lo solicitado; no registran lo producido. Los datos de entrega corresponden al momento de capturar cada pedido.</p>

        <section class="stack change-board" aria-labelledby="changes-heading" data-testid="operations-changes">
          <div class="section-heading"><h3 id="changes-heading">Cambios para la máquina</h3><span class="badge" [class.danger]="pendingChanges.length > 0" [class.neutral]="pendingChanges.length === 0" role="status">{{ pendingChanges.length }} pendientes de recibir</span></div>
          <p>Revisa estos avisos aunque ya hayas terminado un pedido. Permanecen pendientes hasta marcar «Cambio recibido»; recibirlo no confirma que ya se produjo ni resuelve un excedente.</p>
          @if (acknowledgeError) { <p class="alert error" role="alert">{{ acknowledgeError }}</p> }
          @if (!pendingChanges.length) { <p class="notice">No hay cambios pendientes para la fecha, ruta y máquina seleccionadas.</p> }
          @for (change of pendingChanges; track change.id) {
            <article class="change-card" data-testid="pending-operation-change">
              <div class="operations-heading"><div><h4>{{ change.customerName }} · {{ machineLabel(change.machineNumber, change.machineName) }}</h4><small>{{ change.kind === 'review' ? 'Pedido autorizado' : 'Pedido corregido' }} · Versión {{ change.orderVersion }} · {{ formatTimestamp(change.occurredAt) }}</small></div><span class="badge danger">Pendiente de recibir</span></div>
              <p><b>Motivo:</b> {{ change.reason }}</p>
              <small>Registró: {{ change.actorDisplayName || 'Administración' }} · Servicio {{ current.date }}</small>
              <ul class="change-lines">@for (line of change.lines; track $index) {
                <li><strong>{{ line.productName }}</strong><span>Antes: {{ line.beforeQuantity }} {{ unitLabel(line.unitCode) }} → Después: {{ line.afterQuantity }} {{ unitLabel(line.unitCode) }}</span><b>{{ deltaLabel(line.deltaQuantity, line.unitCode) }}</b>
                  @if (line.deltaQuantity < 0) { <small>Si ya se elaboró, revisa con administración el excedente; no se descuenta producción automáticamente.</small> }
                  @if (line.beforeNotes !== line.afterNotes) { <small>Nota anterior: {{ line.beforeNotes || 'Sin nota' }} · Nota actual: {{ line.afterNotes || 'Sin nota' }}</small> }
                </li>
              }</ul>
              <div class="review-actions no-print"><button class="primary" type="button" (click)="acknowledge(change)" [disabled]="acknowledgingIds.has(change.id) || isLoading || isRefreshing || !!refreshError">{{ acknowledgingIds.has(change.id) ? 'Guardando recepción…' : 'Cambio recibido' }}</button><a routerLink="/admin/pedidos" [queryParams]="{ date: current.date, orderId: change.orderId }">Ver pedido y sus cambios</a></div>
            </article>
          }
          @if (receivedChanges.length) {
            <details class="received-changes"><summary>{{ receivedChanges.length }} avisos recibidos · ver historial</summary>
              @for (change of receivedChanges; track change.id) {
                <article class="detail-panel" data-testid="received-operation-change"><strong>{{ change.customerName }} · {{ machineLabel(change.machineNumber, change.machineName) }} · Versión {{ change.orderVersion }}</strong><small>Recibido por {{ change.acknowledgedBy || 'Administración' }} · {{ formatTimestamp(change.acknowledgedAt!) }}</small><p>{{ change.reason }}</p>
                  @for (line of change.lines; track $index) { <small>{{ line.productName }} · {{ line.beforeQuantity }} → {{ line.afterQuantity }} {{ unitLabel(line.unitCode) }} · {{ deltaLabel(line.deltaQuantity, line.unitCode) }}</small> }
                </article>
              }
            </details>
          }
        </section>

        @if (current.warnings.length) {
          <section class="alert warning" aria-label="Advertencias del plan">
            <strong>Datos por resolver</strong>
            <ul>@for (warning of current.warnings; track $index) {
              <li>
                @if (warning.orderId) {
                  <a [href]="'#pedido-' + warning.orderId">{{ warningContext(warning) }}</a>: 
                }
                {{ warning.message }}
              </li>
            }</ul>
          </section>
        }

        <section class="stack" aria-labelledby="machine-demand-heading">
          <div class="section-heading"><h3 id="machine-demand-heading">Por producir</h3><span class="badge neutral">Demanda planeada</span></div>
          @if (!visibleDemand.length) {
            <p class="notice">No hay demanda con unidad confirmada para este filtro.</p>
          } @else {
            <div class="table-scroll">
              <table class="operations-table" data-testid="machine-demand">
                <thead><tr><th scope="col">Máquina</th><th scope="col">Producto</th><th scope="col">Cantidad</th></tr></thead>
                <tbody>@for (item of visibleDemand; track $index) {
                  <tr><td>{{ machineLabel(item.machineNumber, item.machineName) }}</td><td>{{ item.productName }} @if (hasPendingProductChange(item.machineId, item.productId)) { <span class="badge danger">Cambio pendiente</span> }</td><td class="quantity-cell">{{ item.quantity }} {{ unitLabel(item.unitCode) }}</td></tr>
                }</tbody>
              </table>
            </div>
          }
        </section>

        <section class="stack" aria-labelledby="preparation-heading">
          <div class="section-heading"><h3 id="preparation-heading">Por preparar</h3><span class="badge neutral">Lista por cliente</span></div>
          @if (!visibleOrders.length) { <p class="notice">No hay pedidos incluidos para esta fecha, ruta y máquina.</p> }
          @for (order of visibleOrders; track order.orderId) {
            <article class="preparation-card" data-testid="preparation-order" [id]="'pedido-' + order.orderId">
              <div class="operations-heading">
                <div><h4>{{ order.customerName }}</h4><small>Ruta {{ order.routeCode || 'por confirmar' }} · Visita {{ order.routeStopOrder ?? 'por confirmar' }} · Pedido #{{ order.sequenceNumber }}</small></div>
                <div class="badges"><span class="badge neutral">Por preparar</span>@if (hasPendingOrderChange(order.orderId)) { <span class="badge danger">Cambio pendiente de recibir</span> } @else if (hasOrderChange(order.orderId)) { <span class="badge warning">Con cambios registrados</span> }</div>
              </div>
              <div class="delivery-details">
                <p><b>Acceso:</b> {{ storeKeyLabel(order.hasStoreKey) }}</p>
                <p><b>Cobro:</b> {{ paymentTermLabel(order.paymentTermCode) }}</p>
                <p><b>Horario solicitado:</b> {{ deliveryWindow(order) }}</p>
                <p class="delivery-instructions"><b>Instrucciones internas:</b> {{ order.internalDeliveryInstructions || 'Pendientes de confirmar' }}</p>
                @if (order.deliveryNotes) { <p class="delivery-instructions"><b>Notas de entrega:</b> {{ order.deliveryNotes }}</p> }
              </div>
              <ul class="preparation-lines">
                @for (line of visibleLines(order); track line.orderLineId) {
                  <li><div><strong>{{ line.productName }}</strong><small>{{ machineLabel(line.assignedMachineNumber, line.assignedMachineName) }}</small>@if (line.notes) { <small>{{ line.notes }}</small> }</div><span class="line-quantity">{{ line.quantity }} {{ unitLabel(line.unitCode) }}</span></li>
                }
              </ul>
              <small class="order-reference">Referencia {{ order.orderId }} · Perfil {{ order.orderingProfileVersion ?? 'sin versión registrada' }}</small>
            </article>
          }
        </section>

        @if (current.pendingReview.length) {
          <section class="stack pending-plan" data-testid="plan-pending-review">
            <h3>Pendientes de revisión</h3>
            <p>Estos pedidos todavía no forman parte de la demanda por producir.</p>
            <ul>@for (order of current.pendingReview; track order.orderId) { <li [id]="'pedido-' + order.orderId">{{ order.customerName }} · Pedido #{{ order.sequenceNumber }}</li> }</ul>
            <a class="no-print" routerLink="/admin/pendientes">Ir a revisión administrativa</a>
          </section>
        }
        <p class="print-footer">Plan de preparación. No es comprobante de entrega ni de pago.</p>
      }
    </section>
  `,
  styles: [`
    .change-board { border: 2px solid #b45309; border-radius: 12px; padding: 1rem; background: #fffaf0; }
    .change-card { display: grid; gap: .7rem; border: 1px solid #d97706; border-left: 5px solid #b45309; border-radius: 8px; padding: 1rem; background: #fff; }
    .change-lines { padding-left: 1.25rem; margin: 0; display: grid; gap: .8rem; }
    .change-lines li { overflow-wrap: anywhere; }
    .change-lines li > * { display: block; }
    .received-changes summary { cursor: pointer; font-weight: 650; padding: .6rem 0; }
    .received-changes article { margin-top: .5rem; }
    .change-card small, .received-changes small { display: block; overflow-wrap: anywhere; }
    @media print { .change-card { break-inside: avoid; } .change-board { background: white; } }
  `]
})
export class AdminOperationsComponent {
  private readonly api = inject(AdminOperationsApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private planRequest: Subscription | null = null;
  private requestGeneration = 0;
  private appliedFilter: { date: string; routeCode: string } | null = null;
  private readonly acknowledgeRequests = new Map<string, AcknowledgeChangeRequest>();
  protected date = this.route.snapshot.queryParamMap.get('date') || businessDateToday();
  protected routeCode = '';
  protected plan: OperationsPlan | null = null;
  protected isLoading = false;
  protected isRefreshing = false;
  protected selectedMachine = '';
  protected changes: OperationsChange[] = [];
  protected lastUpdated: string | null = null;
  protected refreshError: string | null = null;
  protected acknowledgeError: string | null = null;
  protected readonly acknowledgingIds = new Set<string>();
  protected errorMessage: string | null = null;
  protected readonly unitLabel = unitLabel;
  protected readonly storeKeyLabel = storeKeyLabel;
  protected readonly paymentTermLabel = paymentTermLabel;

  constructor() {
    this.load();
    const refresh = () => {
      if (!document.hidden && this.plan && this.appliedFilter && !this.acknowledgingIds.size && !this.isLoading && !this.isRefreshing) this.load(true);
    };
    const timer = window.setInterval(refresh, 15_000);
    document.addEventListener('visibilitychange', refresh);
    this.destroyRef.onDestroy(() => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', refresh);
      this.planRequest?.unsubscribe();
    });
  }

  protected clearPlan(): void {
    this.requestGeneration++;
    this.planRequest?.unsubscribe();
    this.plan = null; this.changes = []; this.appliedFilter = null; this.lastUpdated = null;
    this.errorMessage = null; this.refreshError = null; this.acknowledgeError = null;
    this.isLoading = false; this.isRefreshing = false;
  }

  protected load(background = false): void {
    if (!this.date || this.isLoading || this.isRefreshing) return;
    if (!background) this.clearPlan();
    const filter = background ? this.appliedFilter! : { date: this.date, routeCode: this.routeCode.trim() };
    const generation = ++this.requestGeneration;
    this.isLoading = !background; this.isRefreshing = background;
    this.planRequest = this.readConsistentPlan(filter).subscribe({
      next: ({ plan, changes }) => {
        if (generation !== this.requestGeneration) return;
        this.plan = plan; this.changes = changes.changes; this.appliedFilter = filter;
        this.lastUpdated = this.formatTimestamp(new Date().toISOString());
        this.isLoading = false; this.isRefreshing = false; this.refreshError = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (generation !== this.requestGeneration) return;
        const message = error instanceof HttpErrorResponse && typeof error.error?.error === 'string'
          ? error.error.error : error instanceof Error ? error.message : 'No se pudo consultar el plan y sus avisos.';
        if (background) this.refreshError = message; else this.errorMessage = message;
        this.isLoading = false; this.isRefreshing = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  private readConsistentPlan(filter: { date: string; routeCode: string }, attempt = 1): Observable<{ plan: OperationsPlan; changes: OperationsChanges }> {
    // Read notices after the plan. A correction between the queries is detected
    // by its order version, so a new notice cannot be received against old totals.
    return this.api.getPlan(filter.date, filter.routeCode).pipe(
      switchMap(plan => this.api.getChanges(filter.date, filter.routeCode).pipe(
        switchMap(changes => {
          const versions = new Map(plan.orders.map(order => [order.orderId, order.orderVersion]));
          const consistent = changes.changes.every(change => (versions.get(change.orderId) ?? -1) >= change.orderVersion);
          if (consistent) return of({ plan, changes });
          if (attempt < 3) return this.readConsistentPlan(filter, attempt + 1);
          return throwError(() => new Error('El plan cambió durante la consulta. Vuelve a consultar para recibir los avisos con sus cantidades actualizadas.'));
        })
      ))
    );
  }

  protected get availableMachines(): Array<{ id: string; label: string }> {
    const values = new Map<string, string>();
    for (const item of this.plan?.machineDemand ?? []) if (item.machineId) values.set(item.machineId, this.machineLabel(item.machineNumber, item.machineName));
    for (const change of this.changes) if (change.machineId) values.set(change.machineId, this.machineLabel(change.machineNumber, change.machineName));
    return Array.from(values, ([id, label]) => ({ id, label })).sort((a, b) => a.label.localeCompare(b.label, 'es', { numeric: true }));
  }

  private matchesMachine(machineId: string | null): boolean {
    return !this.selectedMachine || (this.selectedMachine === 'unassigned' ? machineId === null : machineId === this.selectedMachine);
  }

  protected get pendingChanges() { return this.changes.filter(change => !change.acknowledgedAt && this.matchesMachine(change.machineId)); }
  protected get receivedChanges() { return this.changes.filter(change => !!change.acknowledgedAt && this.matchesMachine(change.machineId)); }
  protected get visibleDemand() { return (this.plan?.machineDemand ?? []).filter(item => this.matchesMachine(item.machineId)); }
  protected get visibleOrders() { return (this.plan?.orders ?? []).filter(order => this.visibleLines(order).length > 0); }
  protected visibleLines(order: OperationsOrder) { return order.lines.filter(line => this.matchesMachine(line.assignedMachineId)); }
  protected hasPendingOrderChange(orderId: string) { return this.pendingChanges.some(change => change.orderId === orderId); }
  protected hasOrderChange(orderId: string) { return this.changes.some(change => change.orderId === orderId && this.matchesMachine(change.machineId)); }
  protected hasPendingProductChange(machineId: string | null, productId: string) { return this.pendingChanges.some(change => change.machineId === machineId && change.lines.some(line => line.productId === productId)); }

  protected acknowledge(change: OperationsChange): void {
    if (change.acknowledgedAt || this.acknowledgingIds.has(change.id) || this.isLoading || this.isRefreshing || this.refreshError) return;
    const generation = this.requestGeneration;
    const request = this.acknowledgeRequests.get(change.id) ?? { operationKey: crypto.randomUUID(), expectedVersion: change.version };
    this.acknowledgeRequests.set(change.id, request);
    this.acknowledgingIds.add(change.id); this.acknowledgeError = null;
    const subscription = this.api.acknowledge(change.id, request).pipe(finalize(() => {
      this.acknowledgingIds.delete(change.id);
      if (!this.destroyRef.destroyed) this.changeDetector.detectChanges();
    })).subscribe({
      next: updated => {
        if (generation !== this.requestGeneration) return;
        this.changes = this.changes.map(item => item.id === updated.id ? updated : item);
        this.acknowledgeRequests.delete(change.id);
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (generation !== this.requestGeneration) return;
        this.acknowledgeError = error instanceof HttpErrorResponse && error.status === 409
          ? 'El aviso cambió. Consulta el plan para verificar si ya fue recibido por otra persona.'
          : 'No se recibió confirmación. El aviso sigue visible; puedes reintentar «Cambio recibido» con la misma referencia.';
        this.changeDetector.detectChanges();
      }
    });
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  protected formatTimestamp(value: string): string {
    return new Intl.DateTimeFormat('es-MX', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'America/Mexico_City' }).format(new Date(value));
  }

  protected deltaLabel(quantity: number, unit: string | null): string {
    return quantity === 0 ? 'Cantidad sin cambio; revisa la nota' : `${quantity > 0 ? '+' : ''}${quantity} ${unitLabel(unit)} solicitados`;
  }

  protected machineLabel(number: number | null, name: string | null): string {
    return number !== null ? `Máquina #${number}${name ? ` · ${name}` : ''}` : name || 'Sin máquina asignada';
  }

  protected deliveryWindow(order: OperationsOrder): string {
    if (order.requestedDeliveryTime) return order.requestedDeliveryTime.slice(0, 5);
    const start = order.requestedDeliveryWindowStart?.slice(0, 5);
    const end = order.requestedDeliveryWindowEnd?.slice(0, 5);
    if (start && end) return `${start} – ${end}`;
    if (start) return `Desde ${start}`;
    if (end) return `Antes de ${end}`;
    return 'Por confirmar';
  }

  protected warningContext(warning: OperationsPlan['warnings'][number]): string {
    const order = this.plan?.orders.find((item) => item.orderId === warning.orderId);
    const pending = this.plan?.pendingReview.find((item) => item.orderId === warning.orderId);
    const line = order?.lines.find((item) => item.orderLineId === warning.orderLineId);
    if (order || pending) return `${(order ?? pending)!.customerName} · Pedido #${(order ?? pending)!.sequenceNumber}${line ? ` · ${line.productName}` : ''}`;
    return `Pedido ${warning.orderId}`;
  }

  protected print(): void { if (this.plan) window.print(); }
}
