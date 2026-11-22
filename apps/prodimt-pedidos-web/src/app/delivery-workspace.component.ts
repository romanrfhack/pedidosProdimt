import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, ElementRef, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription, catchError, of, switchMap, timeout } from 'rxjs';
import { DeliveryApiService, DeliveryAssignment, DeliveryDashboard, DeliveryInstructions, DeliveryLine, DeliveryOrder,
  DeliveryPendingCommand, DeliveryRoute, DeliveryTrip, safeDeliveryMapsUrl } from './delivery-api.service';
import { priceText } from './commercial-catalog-api.service';
import { AuthService } from './auth.service';
import { EffectivePermissionsService } from './effective-permissions.service';
import { businessDateToday, paymentTermLabel, storeKeyLabel, unitLabel } from './operational-labels';
import { AdminOrderingSchedulesComponent } from './admin-ordering-schedules.component';
import { DeliveryRouteCustomersComponent } from './delivery-route-customers.component';
import { CollectionApiService, collectionMoney, parseCollectionAmount, type CollectionPreview } from './collection-api.service';
import { DeliveryCollectionsComponent } from './delivery-collections.component';

type EditorKind = 'count' | 'planned-count' | 'load' | 'deliver' | 'return' | 'route' | 'day-driver' | 'trip'
  | 'assign' | 'location' | 'depart' | 'close' | 'transfer' | 'adjust' | 'release' | 'instructions';
interface DeliveryEditor {
  kind: EditorKind; title: string; operationId: string; assignment: DeliveryAssignment | null;
  order: DeliveryOrder | null; line: DeliveryLine | null; route: DeliveryRoute | null;
  quantity: number | null; reason: string; notes: string; remainderDisposition: string;
  routeCode: string; routeName: string; driverUserId: string; tripNumber: number | null;
  targetTripId: string; mapsUrl: string; address: string; adjustStage: string;
  acknowledgeOrderChange: boolean; isActive: boolean; error: string | null; conflict: boolean;
  locked: boolean; command: DeliveryPendingCommand | null;
  collectionPreview: CollectionPreview | null; collectionAmount: string; collectionDirty: boolean;
  collectionLoading: boolean; collectionError: string | null;
}
interface DeliveryStop {
  orderId: string; customerName: string; position: number | null;
  reference: DeliveryOrder | DeliveryAssignment; assignments: DeliveryAssignment[]; plannedOrder: DeliveryOrder | null;
}
type DeliveryStage = 'count' | 'load' | 'deliver';

interface DriverStageLine {
  kind: DeliveryStage | 'planned-count';
  assignment: DeliveryAssignment | null;
  order: DeliveryOrder | null;
  line: DeliveryLine | null;
}

interface DriverCustomer {
  id: string;
  name: string;
  stops: DeliveryStop[];
  lines: DriverStageLine[];
  summary: string;
}

@Component({
  selector: 'app-delivery-workspace', imports: [FormsModule, NgTemplateOutlet, RouterLink, AdminOrderingSchedulesComponent, DeliveryRouteCustomersComponent, DeliveryCollectionsComponent],
  templateUrl: './delivery-workspace.component.html',
  styleUrls: ['./delivery-workspace.component.css', './delivery-trip.component.css', './delivery-dialog.component.css', './driver-stages.component.css', './delivery-collection-editor.css']
})
export class DeliveryWorkspaceComponent {
  private readonly api = inject(DeliveryApiService);
  private readonly collections = inject(CollectionApiService);
  private readonly auth = inject(AuthService);
  protected readonly access = inject(EffectivePermissionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly destroy = inject(DestroyRef);
  @ViewChild('deliveryDialog') private dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild(AdminOrderingSchedulesComponent) private routeSchedule?: AdminOrderingSchedulesComponent;
  @ViewChild(DeliveryRouteCustomersComponent) private routeCustomers?: DeliveryRouteCustomersComponent;
  protected readonly management = this.route.snapshot.data['deliveryManagement'] === true;
  protected readonly unitLabel = unitLabel;
  protected readonly money = collectionMoney;
  protected readonly price = priceText;
  protected readonly storeKeyLabel = storeKeyLabel;
  protected readonly paymentTermLabel = paymentTermLabel;
  protected readonly mapsUrl = safeDeliveryMapsUrl;
  protected date = this.route.snapshot.queryParamMap.get('date') || '';
  protected dashboard: DeliveryDashboard | null = null;
  protected selectedTripId = '';
  protected selectedStopId: string | null = null;
  protected showHistory = false;
  protected readonly driverStages = [
    { id: 'count', label: 'Registrar conteo', short: 'RC' },
    { id: 'load', label: 'Confirmar carga', short: 'CC' },
    { id: 'deliver', label: 'Registrar entrega', short: 'RE' }
  ] as const;
  protected driverStage: DeliveryStage = 'count';
  protected showAllCustomers = false;
  protected expandedCustomerId: string | null = null;
  protected loading = false;
  protected saving = false;
  protected error: string | null = null;
  protected success: string | null = null;
  protected preparationWarning: string | null = null;
  protected editor: DeliveryEditor | null = null;
  protected recovery: DeliveryPendingCommand | null = null;
  protected storageError: string | null = null;
  protected get routeSettingsBusy(): boolean { return !!(this.routeSchedule?.isSaving || this.routeCustomers?.isSaving); }
  private get routeSettingsDirty(): boolean { return !!(this.routeSchedule?.hasRouteDraft || this.routeCustomers?.hasDrafts); }
  private read: Subscription | null = null;
  private write: Subscription | null = null;
  private collectionRead: Subscription | null = null;
  private collectionTimer: ReturnType<typeof setTimeout> | null = null;
  private collectionGeneration = 0;
  private readonly beforeUnload = (event: BeforeUnloadEvent) => {
    if (this.editor || this.recovery || this.saving) { event.preventDefault(); event.returnValue = ''; }
  };

  constructor() {
    const user = this.access.userId();
    if (user) {
      try { this.recovery = this.api.pending(user); }
      catch { this.storageError = 'No se pudo recuperar una operación guardada. Revisa el almacenamiento antes de registrar otra entrega.'; }
    }
    this.load();
    window.addEventListener('beforeunload', this.beforeUnload);
    this.destroy.onDestroy(() => { this.read?.unsubscribe(); this.write?.unsubscribe(); this.cancelCollectionPreview(); window.removeEventListener('beforeunload', this.beforeUnload); });
  }

  canLeave(): boolean {
    if (!this.auth.isAdmin()) return true;
    if (this.saving || this.routeSettingsBusy) return false;
    if (this.recovery || this.editor?.locked) return window.confirm('Hay una operación de reparto sin confirmación. Se conservará para reintentar con la misma referencia cuando regreses. ¿Salir de esta vista?');
    return !this.editor || window.confirm('Hay una captura de reparto sin guardar. ¿Descartar el borrador y salir?');
  }

  protected load(preserveStop = false, prepare = !preserveStop): void {
    if (this.loading || this.saving) return;
    this.loading = true; this.error = null;
    if (prepare) this.preparationWarning = null;
    this.read?.unsubscribe();
    this.read = this.api.getDashboard(this.date || null, this.management).pipe(
      switchMap(board => {
        if (!prepare || this.recovery || this.editor || !board.canPrepare || !this.canManageTrip || this.storageError) return of(board);
        const operationId = crypto.randomUUID();
        return this.api.execute({
          operationId, path: this.deliveryPath + '/prepare', method: 'POST',
          body: { operationId, date: board.date }, title: 'Preparar primera vuelta',
          management: this.management, tripId: null, summary: board.date
        }).pipe(
          switchMap(() => this.api.getDashboard(board.date, this.management)),
          catchError(() => {
            this.preparationWarning = 'No se pudo comprobar la preparación automática. Vuelve a consultar para reintentar.';
            return of(board);
          })
        );
      })
    ).subscribe({
      next: value => {
        const previousDate = this.dashboard?.date;
        this.dashboard = value; this.date = value.date;
        if (!value.trips.some(trip => trip.id === this.selectedTripId)) {
          const populated = value.trips.find(trip => trip.plannedOrders?.length || trip.assignments.some(line => line.status !== 'Released'));
          this.selectedTripId = populated?.id ?? value.trips[0]?.id ?? '';
          if (!this.management) this.selectTrip();
        }
        if (!this.management && previousDate && previousDate !== value.date) this.selectTrip();
        if (!preserveStop) this.selectedStopId = null;
        this.loading = false; this.detector.detectChanges();
      },
      error: error => { this.error = this.errorMessage(error, 'No se pudo consultar el reparto. Reintenta cuando tengas conexión.'); this.loading = false; this.detector.detectChanges(); }
    });
  }

  protected today(): void { if (this.editor || this.recovery || this.loading) return; this.date = ''; this.load(); }

  protected get trip(): DeliveryTrip | null { return this.dashboard?.trips.find(trip => trip.id === this.selectedTripId) ?? null; }
  private get deliveryPath(): string { return this.management ? '/api/admin/delivery' : '/api/delivery'; }
  private get canManageTrip(): boolean { return this.access.has(this.management ? 'delivery.manage' : 'delivery.operate.own'); }
  protected get stops(): DeliveryStop[] {
    const grouped = new Map<string, DeliveryStop>();
    for (const assignment of this.trip?.assignments ?? []) {
      const stop = grouped.get(assignment.orderId) ?? {
        orderId: assignment.orderId, customerName: assignment.customerName, position: assignment.routeStopOrder,
        reference: assignment, assignments: [], plannedOrder: null
      };
      stop.assignments.push(assignment); grouped.set(stop.orderId, stop);
    }
    for (const order of this.trip?.plannedOrders ?? []) {
      const stop = grouped.get(order.orderId) ?? {
        orderId: order.orderId, customerName: order.customerName, position: order.routeStopOrder,
        reference: order, assignments: [], plannedOrder: null
      };
      stop.plannedOrder = order;
      grouped.set(stop.orderId, stop);
    }
    return [...grouped.values()].sort((a, b) => (a.position ?? Number.MAX_SAFE_INTEGER) - (b.position ?? Number.MAX_SAFE_INTEGER) || a.customerName.localeCompare(b.customerName, 'es'));
  }
  protected get selectedStop(): DeliveryStop | null { return this.stops.find(stop => stop.orderId === this.selectedStopId) ?? null; }
  protected get visibleStops(): DeliveryStop[] { return this.selectedStop ? [this.selectedStop] : this.stops; }
  protected assignmentStage(line: DeliveryAssignment): DeliveryStage | null {
    if (line.custodyQuantity > 0) return 'deliver';
    if (line.transferredInQuantity > 0 || line.status === 'Released' || line.assignedQuantity <= line.releasedQuantity) return null;
    if (line.countedQuantity === null) return 'count';
    return line.loadedQuantity === null ? 'load' : null;
  }

  protected get driverCustomers() {
    const customers = new Map<string, DriverCustomer>();
    const stages: Record<DeliveryStage, DriverCustomer[]> = { count: [], load: [], deliver: [] };
    for (const stop of this.stops) {
      const id = stop.reference.customerId;
      const customer = customers.get(id) ?? { id, name: stop.customerName, stops: [], lines: [], summary: '' };
      customer.stops.push(stop);
      customers.set(id, customer);
    }

    for (const customer of customers.values()) {
      const lines: Record<DeliveryStage, DriverStageLine[]> = { count: [], load: [], deliver: [] };
      for (const stop of customer.stops) {
        const order = stop.plannedOrder;
        for (const line of order?.lines ?? []) {
          lines.count.push({ kind: 'planned-count', assignment: null, order, line });
        }
        for (const assignment of stop.assignments) {
          const kind = this.assignmentStage(assignment);
          if (kind) lines[kind].push({ kind, assignment, order: null, line: null });
        }
      }
      for (const stage of this.driverStages) {
        const pending = lines[stage.id];
        if (pending.length) stages[stage.id].push({ ...customer, lines: pending, summary: this.driverQuantitySummary(pending) });
      }
    }
    return { stages, visible: this.showAllCustomers || this.closed ? [...customers.values()] : stages[this.driverStage] };
  }

  protected get driverStageLabel(): string {
    return this.driverStages.find(stage => stage.id === this.driverStage)!.label;
  }

  private driverQuantitySummary(lines: DriverStageLine[]): string {
    const amounts = new Map<string, number>();
    for (const { kind, assignment, line } of lines) {
      const unit = unitLabel(assignment?.unitCode ?? line?.unitCode);
      let amount = line?.availableQuantity ?? 0;
      if (assignment) {
        const reserved = assignment.assignedQuantity - assignment.releasedQuantity;
        amount = kind === 'count' ? reserved : kind === 'load'
          ? Math.min(assignment.countedQuantity ?? 0, reserved) : assignment.custodyQuantity;
      }
      amounts.set(unit, (amounts.get(unit) ?? 0) + amount);
    }
    return [...amounts].map(([unit, amount]) => `${this.quantity(amount)} ${unit}`).join(' · ');
  }

  protected selectDriverStage(stage: DeliveryStage): void {
    if (this.driverStage !== stage || this.showAllCustomers) this.expandedCustomerId = null;
    this.driverStage = stage;
    this.showAllCustomers = false;
  }

  protected showStops(): void {
    this.showHistory = false;
    if (this.showAllCustomers) this.selectDriverStage(this.driverStage);
  }

  protected showCustomerList(): void {
    this.showHistory = false;
    if (!this.showAllCustomers) this.expandedCustomerId = null;
    this.showAllCustomers = true;
  }

  protected useDriverAction(customer: DriverCustomer): void {
    if (customer.lines.length === 1) {
      const { kind, assignment, order, line } = customer.lines[0];
      this.open(kind, assignment, order, line);
    } else {
      this.expandedCustomerId = customer.id;
    }
  }
  protected get pendingOrders(): DeliveryOrder[] {
    const plannedLines = new Set((this.dashboard?.trips ?? []).flatMap(trip =>
      (trip.plannedOrders ?? []).flatMap(order => order.lines.map(line => line.orderLineId))));
    return (this.dashboard?.orders ?? []).flatMap(order => {
      const lines = order.lines.filter(line => !plannedLines.has(line.orderLineId));
      return lines.some(line => line.availableQuantity > 0) ? [{ ...order, lines }] : [];
    });
  }
  protected get routeCodes(): string[] { return [...new Set([...(this.dashboard?.routes.map(route => route.code) ?? []), ...(this.dashboard?.orders.map(order => order.routeCode).filter((code): code is string => !!code) ?? [])])].sort(); }
  protected get closed(): boolean { return !!this.trip?.closedAt || this.trip?.status.toLowerCase() === 'closed'; }
  protected get canOperate(): boolean { return this.canManageTrip && !this.closed && !this.recovery && !this.storageError; }
  protected get canDepart(): boolean {
    return !!this.dashboard?.operationalDate && this.canOperate && this.trip?.departedAt === null
      && this.trip.date === businessDateToday();
  }
  protected get departureBlocked(): boolean {
    return this.trip?.assignments.some(line =>
      line.assignedQuantity - line.releasedQuantity > line.deliveredQuantity + line.custodyQuantity) ?? false;
  }
  protected get eventList() { return [...(this.trip?.events ?? [])].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt)); }
  protected get completedStops(): number { return this.stops.filter(stop => stop.assignments[0]?.orderIsComplete === true).length; }
  protected selectTrip(): void {
    this.selectedStopId = null;
    this.showHistory = false;
    this.expandedCustomerId = null;
    this.showAllCustomers = false;
    this.driverStage = 'count';
  }
  protected quantity(value: number | null): string { return value === null ? '—' : new Intl.NumberFormat('es-MX', { maximumFractionDigits: 3 }).format(value); }
  protected tripLabel(trip: DeliveryTrip): string {
    return `${trip.routeName} · Vuelta ${trip.tripNumber} · ${trip.driverName} · ${trip.routeCode}`;
  }
  protected loadsForLine(line: DeliveryLine): { id: string; driverName: string; tripNumber: number; quantity: number }[] {
    return (this.dashboard?.trips ?? []).flatMap(trip => trip.assignments
      .filter(assignment => assignment.orderLineId === line.orderLineId && (assignment.loadedQuantity ?? 0) > 0)
      .map(assignment => ({ id: assignment.id, driverName: trip.driverName, tripNumber: trip.tripNumber, quantity: assignment.loadedQuantity! })));
  }
  private firstTripFor(route: DeliveryRoute): DeliveryTrip | undefined {
    return this.dashboard?.trips.find(trip => trip.routeCode === route.code && trip.tripNumber === 1);
  }
  protected canSetDayDriver(route: DeliveryRoute): boolean {
    return !!this.dashboard?.operationalDate && route.isActive && route.dayDriverVersion !== undefined
      && this.firstTripFor(route)?.canChangeDriver !== false;
  }
  protected get dayDriverLabel(): string {
    return this.dashboard?.date === businessDateToday() ? 'Cambiar chofer por hoy' : 'Cambiar chofer para esta fecha';
  }
  protected dayDriverName(route: DeliveryRoute): string | null {
    return this.dashboard?.drivers.find(driver => driver.id === route.dayDriverUserId)?.displayName ?? null;
  }
  protected time(value: string | null): string { return value?.slice(0, 5) || ''; }
  protected schedule(value: DeliveryInstructions): string {
    if (value.requestedDeliveryWindowStart && value.requestedDeliveryWindowEnd) return `${this.time(value.requestedDeliveryWindowStart)} – ${this.time(value.requestedDeliveryWindowEnd)}`;
    return value.requestedDeliveryTime ? `Entrega ${this.time(value.requestedDeliveryTime)}` : 'Horario por confirmar';
  }
  protected timestamp(value: string): string { return new Intl.DateTimeFormat('es-MX', { timeZone: 'America/Mexico_City', dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)); }
  protected status(assignment: DeliveryAssignment): string {
    if (assignment.status === 'Released') return assignment.transferredOutQuantity > 0 ? 'Carga transferida' : 'Reserva liberada';
    if (assignment.status === 'Released') return 'Reserva liberada';
    if (assignment.deliveredQuantity === 0 && this.hasFailedAttempt(assignment)) return assignment.status === 'Incident' ? 'No entregado · incidencia' : 'No entregado';
    if (assignment.status === 'Incident') return 'Incidencia por aclarar';
    if (assignment.pendingQuantity <= 0) return 'Partida completa';
    if (assignment.deliveredQuantity > 0) return 'Entregado parcial';
    if (assignment.custodyQuantity > 0) return 'En camioneta';
    if (assignment.loadedQuantity !== null) return assignment.loadedQuantity === 0 ? 'Sin carga' : 'Carga resuelta';
    if (assignment.countedQuantity !== null) return 'Contado';
    if (assignment.releasedQuantity >= assignment.assignedQuantity) return 'Reserva liberada';
    return 'Por contar';
  }
  protected hasFailedAttempt(assignment: DeliveryAssignment): boolean {
    return this.eventList.some(event => event.assignmentId === assignment.id && (event.type === 'Delivered' || event.type === 'deliver') && event.quantity === 0);
  }
  protected orderForStop(stop: DeliveryStop): DeliveryOrder | null {
    return this.dashboard?.orders.find(order => order.orderId === stop.orderId) ?? stop.plannedOrder;
  }
  protected hasOtherPendingLines(stop: DeliveryStop): boolean {
    return this.orderForStop(stop)?.lines.some(line => line.requestedQuantity > line.deliveredQuantity
      && !stop.assignments.some(assignment => assignment.orderLineId === line.orderLineId)
      && !stop.plannedOrder?.lines.some(planned => planned.orderLineId === line.orderLineId)) ?? false;
  }
  protected stopStatus(stop: DeliveryStop): string {
    if (!stop.assignments.length) return 'Por preparar';
    if (!stop.plannedOrder && stop.assignments.every(line => line.deliveredQuantity === 0 && this.hasFailedAttempt(line))) return 'No entregado';
    if (stop.assignments.some(line => line.status === 'Incident')) return 'Incidencia por aclarar';
    if (stop.assignments[0]?.orderIsComplete === true) return 'Entregado completo';
    if (!stop.plannedOrder && stop.assignments.every(line => line.status === 'Released')) return stop.assignments.some(line => line.transferredOutQuantity > 0) ? 'Carga transferida' : 'Reserva liberada';
    if (!stop.plannedOrder && stop.assignments.some(line => line.deliveredQuantity > 0) && stop.assignments.every(line => line.deliveredQuantity >= line.assignedQuantity - line.releasedQuantity)) return 'Mi parte entregada; pedido con pendientes';
    if (stop.assignments.some(line => line.deliveredQuantity > 0)) return 'Entrega parcial';
    if (stop.assignments.some(line => line.custodyQuantity > 0)) return 'En camioneta';
    return 'En preparación';
  }
  protected eventLabel(type: string): string {
    return ({ Counted: 'Conteo registrado', Loaded: 'Carga registrada', Delivered: 'Entrega registrada', Returned: 'Devolución registrada',
      count: 'Conteo registrado', load: 'Carga registrada', deliver: 'Entrega registrada', return: 'Devolución registrada',
      Assigned: 'Asignación a vuelta', Released: 'Reserva liberada', TransferredOut: 'Transferencia de salida', TransferredIn: 'Transferencia recibida',
      Adjusted: 'Corrección registrada', DeliveredAdjusted: 'Entrega acumulada corregida', InstructionsUpdated: 'Indicaciones de entrega actualizadas', CountAdjusted: 'Conteo corregido', LoadAdjusted: 'Carga corregida',
      ReservationReleased: 'Reserva liberada', UnloadedReservationReleased: 'Reserva sin cargar liberada',
      RemainsInVehicle: 'Producto permanece en camioneta', IncidentUnresolved: 'Incidencia por aclarar', OrderChangeAcknowledged: 'Cambio del pedido revisado',
      Closed: 'Vuelta cerrada', TripClosed: 'Vuelta cerrada', TripCreated: 'Vuelta creada', RouteSaved: 'Ruta actualizada',
      TripDeparted: 'Salida confirmada', TripDriverChanged: 'Repartidor de la vuelta actualizado' } as Record<string, string>)[type] || type;
  }
  protected get targetTrips(): DeliveryTrip[] {
    const editor = this.editor;
    return (this.dashboard?.trips ?? []).filter(trip => !trip.closedAt && trip.status.toLowerCase() !== 'closed'
      && (editor?.kind !== 'assign' || this.acceptsAssignment(trip, editor.line))
      && (editor?.kind !== 'transfer' || trip.id !== this.trip?.id && trip.driverUserId !== this.trip?.driverUserId && trip.routeCode.toUpperCase() === this.trip?.routeCode.toUpperCase())
      && (!editor?.order?.routeCode || trip.routeCode.toUpperCase() === editor.order.routeCode.toUpperCase()));
  }
  protected get activeRoutes(): DeliveryRoute[] { return (this.dashboard?.routes ?? []).filter(route => route.isActive); }
  protected get openTripsCount(): number { return (this.dashboard?.trips ?? []).filter(trip => !trip.closedAt && trip.status.toLowerCase() !== 'closed').length; }
  protected get assignedLineCount(): number { return (this.dashboard?.trips ?? []).reduce((total, trip) => total + trip.assignments.filter(line => line.status !== 'Released').length, 0); }
  protected hasAssignmentTarget(order: DeliveryOrder, line?: DeliveryLine): boolean {
    const lines = line ? [line] : order.lines.filter(item => item.availableQuantity > 0);
    return !!order.routeCode && (this.dashboard?.trips ?? []).some(trip => !trip.closedAt
      && trip.status.toLowerCase() !== 'closed' && trip.routeCode.toUpperCase() === order.routeCode?.toUpperCase()
      && lines.some(item => this.acceptsAssignment(trip, item)));
  }
  private acceptsAssignment(trip: DeliveryTrip, line: DeliveryLine | null): boolean {
    return !trip.departedAt && !(trip.tripNumber === 1 && line && trip.assignments.some(item =>
      item.orderLineId === line.orderLineId && item.countedQuantity !== null && item.releasedQuantity > 0));
  }
  protected get quantityLimit(): number {
    const assignment = this.editor?.assignment;
    if (this.editor?.kind === 'load' && assignment) return Math.min(assignment.countedQuantity ?? 0, Math.max(0, assignment.assignedQuantity - assignment.releasedQuantity));
    if (this.editor?.kind === 'deliver' && assignment) return Math.min(assignment.custodyQuantity, assignment.pendingQuantity);
    return this.expectedQuantity;
  }
  protected get expectedQuantity(): number {
    const assignment = this.editor?.assignment;
    if (!assignment) return this.editor?.line?.availableQuantity ?? 0;
    switch (this.editor?.kind) {
      case 'count': return Math.max(0, assignment.assignedQuantity - assignment.releasedQuantity);
      case 'load': return assignment.countedQuantity ?? 0;
      case 'deliver': case 'return': case 'transfer': return assignment.custodyQuantity;
      case 'adjust': return this.editor.adjustStage === 'count' ? assignment.countedQuantity ?? 0 : this.editor.adjustStage === 'deliver' ? assignment.deliveredQuantity : assignment.loadedQuantity ?? 0;
      default: return 0;
    }
  }
  protected get hasDifference(): boolean { return !!this.editor && ['count', 'planned-count', 'load', 'deliver'].includes(this.editor.kind) && this.editor.quantity !== null && this.editor.quantity !== this.expectedQuantity; }
  protected get deliveryRemainder(): number { return this.editor?.kind === 'deliver' && this.editor.quantity !== null ? Math.max(0, this.expectedQuantity - this.editor.quantity) : 0; }
  protected get reasonRequired(): boolean { return this.hasDifference || !!this.editor && ['return', 'adjust', 'transfer', 'release', 'instructions', 'day-driver'].includes(this.editor.kind); }
  protected get quantityRequired(): boolean { return !!this.editor && ['count', 'planned-count', 'load', 'deliver', 'return', 'assign', 'transfer', 'adjust'].includes(this.editor.kind); }
  protected get quantityUnit(): string { return unitLabel(this.editor?.assignment?.unitCode ?? this.editor?.line?.unitCode); }
  protected get submitLabel(): string {
    return ({ count: 'Guardar conteo', 'planned-count': 'Guardar conteo', load: 'Confirmar carga', deliver: 'Guardar entrega',
      return: 'Guardar devolución', route: 'Guardar ruta', 'day-driver': 'Guardar chofer del día', trip: 'Crear vuelta',
      assign: 'Confirmar asignación', location: 'Guardar ubicación', depart: 'Confirmar salida', close: 'Cerrar vuelta',
      transfer: 'Confirmar transferencia', adjust: 'Guardar corrección', release: 'Liberar reserva', instructions: 'Actualizar indicaciones'
    } as Record<string, string>)[this.editor?.kind || ''] || 'Guardar';
  }

  protected open(kind: EditorKind, assignment: DeliveryAssignment | null = null, order: DeliveryOrder | null = null, line: DeliveryLine | null = null, route: DeliveryRoute | null = null): void {
    if (this.saving || this.editor || this.recovery || this.loading || this.storageError) return;
    const titles: Record<EditorKind, string> = { count: 'Registrar conteo', 'planned-count': 'Registrar conteo', load: 'Confirmar carga',
      deliver: 'Registrar entrega', return: 'Registrar devolución', route: route ? 'Editar ruta' : 'Nueva ruta',
      'day-driver': this.dayDriverLabel, trip: 'Nueva vuelta', assign: 'Asignar a vuelta', location: 'Ubicación del cliente',
      depart: 'Iniciar recorrido', close: 'Cerrar vuelta', transfer: 'Transferir carga', adjust: 'Corregir un registro de reparto',
      release: 'Liberar reserva', instructions: 'Actualizar indicaciones de entrega' };
    this.editor = { kind, title: titles[kind], operationId: crypto.randomUUID(), assignment, order, line, route, quantity: null,
      reason: '', notes: '', remainderDisposition: '', routeCode: route?.code ?? this.routeCodes[0] ?? '', routeName: route?.name ?? '',
      driverUserId: (kind === 'day-driver' ? route?.dayDriverUserId : route?.defaultDriverUserId) ?? '',
      tripNumber: 1, targetTripId: '', mapsUrl: order?.mapsUrl ?? '', address: order?.address ?? '',
      adjustStage: assignment && assignment.transferredInQuantity > 0 ? 'deliver' : assignment?.loadedQuantity === null ? 'count' : 'load', acknowledgeOrderChange: false, isActive: route?.isActive ?? true, error: null, conflict: false, locked: false, command: null, collectionPreview: null, collectionAmount: '', collectionDirty: false, collectionLoading: false, collectionError: null };
    if (this.quantityRequired) this.editor.quantity = Math.min(this.expectedQuantity, this.quantityLimit);
    this.editor.targetTripId = this.targetTrips.find(trip => trip.id === this.selectedTripId)?.id ?? this.targetTrips[0]?.id ?? '';
    if (kind === 'trip') { this.editor.routeCode = this.activeRoutes[0]?.code ?? ''; this.routeChanged(); }
    this.detector.detectChanges(); this.dialog?.nativeElement.showModal();
    if (this.collectionEnabled) this.refreshCollectionPreview();
  }
  protected get collectionEnabled(): boolean {
    return this.editor?.kind === 'deliver' && this.editor.assignment?.paymentTermCode === 'cash_on_delivery'
      && this.access.has('operations.collections.work');
  }
  protected get collectionReady(): boolean {
    return !this.collectionEnabled || !!this.editor?.collectionPreview && !this.editor.collectionLoading && !this.editor.collectionError;
  }
  protected get collectionBalanceAfter(): number | null {
    const preview = this.editor?.collectionPreview;
    const amount = parseCollectionAmount(this.editor?.collectionAmount ?? '');
    if (!preview || preview.amountDue === null || amount === null) return null;
    return Math.max(0, Math.round((preview.amountDue - amount) * 100) / 100);
  }
  protected get collectionCreditAfter(): number | null {
    const preview = this.editor?.collectionPreview;
    const amount = parseCollectionAmount(this.editor?.collectionAmount ?? '');
    if (!preview || preview.amountDue === null || amount === null) return null;
    return Math.round((Math.max(0, amount - preview.amountDue) + (preview.creditBalance ?? 0)) * 100) / 100;
  }
  protected collectionEdited(): void { if (this.editor) this.editor.collectionDirty = true; }
  protected noPayment(): void {
    if (!this.editor) return;
    this.editor.collectionAmount = '0';
    this.editor.collectionDirty = true;
  }
  protected deliveryQuantityChanged(): void {
    if (!this.collectionEnabled) return;
    this.cancelCollectionPreview();
    if (!this.editor) return;
    this.editor.collectionPreview = null;
    this.editor.collectionLoading = true;
    this.editor.collectionError = null;
    this.collectionTimer = setTimeout(() => this.refreshCollectionPreview(), 250);
  }
  protected refreshCollectionPreview(): void {
    this.cancelCollectionPreview();
    const editor = this.editor;
    if (!this.collectionEnabled || !editor?.assignment || editor.locked) return;
    if (editor.quantity === null || !Number.isFinite(editor.quantity) || editor.quantity < 0 || editor.quantity > this.quantityLimit) {
      editor.collectionLoading = false;
      editor.collectionPreview = null;
      editor.collectionError = 'Captura primero una cantidad de entrega válida.';
      return;
    }
    const generation = ++this.collectionGeneration;
    editor.collectionLoading = true;
    editor.collectionError = null;
    this.collectionRead = this.collections.preview(editor.assignment.id, editor.quantity, this.management).pipe(timeout(15_000)).subscribe({
      next: preview => {
        if (this.editor !== editor || generation !== this.collectionGeneration) return;
        editor.collectionPreview = preview;
        editor.collectionLoading = false;
        if (!editor.collectionDirty) editor.collectionAmount = preview.suggestedAmount === null ? '' : String(preview.suggestedAmount);
        this.detector.detectChanges();
      },
      error: error => {
        if (this.editor !== editor || generation !== this.collectionGeneration) return;
        editor.collectionLoading = false;
        editor.collectionError = this.errorMessage(error, 'No se pudo consultar el saldo y el ticket de este cliente.');
        this.detector.detectChanges();
      }
    });
  }
  private cancelCollectionPreview(): void {
    this.collectionGeneration++;
    this.collectionRead?.unsubscribe();
    if (this.collectionTimer !== null) clearTimeout(this.collectionTimer);
    this.collectionTimer = null;
  }

  protected adjustStageChanged(): void { if (this.editor) this.editor.quantity = this.expectedQuantity; }
  protected routeChanged(): void {
    const editor = this.editor;
    if (!editor || editor.kind !== 'trip') return;
    const route = this.dashboard?.routes.find(route => route.code === editor.routeCode);
    editor.driverUserId = route?.dayDriverUserId ?? route?.defaultDriverUserId ?? '';
    editor.tripNumber = Math.max(0, ...(this.dashboard?.trips.filter(trip => trip.routeCode === editor.routeCode).map(trip => trip.tripNumber) ?? [])) + 1;
  }
  protected closeEditor(event?: Event): void {
    event?.preventDefault();
    if (this.saving || this.routeSettingsBusy || this.editor?.locked) return;
    if (this.routeSettingsDirty && !window.confirm('Hay cambios de horario o clientes sin guardar. ¿Cerrar y descartar esos borradores?')) return;
    this.cancelCollectionPreview(); this.dialog?.nativeElement.close(); this.editor = null;
  }

  protected save(): void {
    const editor = this.editor;
    if (!editor || this.saving || this.routeSettingsBusy || editor.conflict || !this.collectionReady || this.storageError) return;
    if (editor.kind === 'route' && this.routeSettingsDirty) {
      editor.error = 'Guarda o descarta los cambios de horario y clientes antes de guardar los datos de la ruta.';
      return;
    }
    const firstAttempt = !editor.command;
    if (!editor.command) {
      const command = this.command(editor);
      if (!command) return;
      editor.command = command;
    }
    this.execute(editor.command, firstAttempt);
  }

  private command(editor: DeliveryEditor): DeliveryPendingCommand | null {
    editor.error = null;
    if (this.quantityRequired && (editor.quantity === null || !Number.isFinite(editor.quantity) || editor.quantity < 0)) { editor.error = 'Captura una cantidad válida, igual o mayor que cero.'; return null; }
    if (this.quantityRequired && !['adjust'].includes(editor.kind) && editor.quantity! > this.quantityLimit) { editor.error = 'La cantidad supera lo disponible para esta operación. Actualiza los datos o registra una corrección con el responsable.'; return null; }
    if (editor.kind === 'return' && editor.quantity === 0) { editor.error = 'La devolución requiere una cantidad mayor que cero.'; return null; }
    if (this.reasonRequired && !editor.reason.trim()) {
      editor.error = editor.kind === 'day-driver' ? 'Indica el motivo del cambio de chofer.' : 'Captura el motivo de la diferencia.';
      return null;
    }
    if (this.deliveryRemainder > 0 && !editor.remainderDisposition) { editor.error = 'Indica dónde quedó el producto cargado que no se entregó.'; return null; }
    if (editor.assignment?.hasOrderChanges && ['count', 'load', 'deliver', 'return'].includes(editor.kind) && !editor.acknowledgeOrderChange) { editor.error = 'Revisa y reconoce los cambios del pedido antes de continuar.'; return null; }
    const trip = this.trip;
    let path = '', method: 'POST' | 'PUT' = 'POST', fields: Record<string, unknown> = {};
    const assignment = editor.assignment;
    if (['count', 'load', 'deliver', 'return'].includes(editor.kind) && assignment) {
      path = `${this.deliveryPath}/assignments/${assignment.id}/${editor.kind}`;
      fields = { expectedVersion: assignment.version, expectedOrderVersion: assignment.currentOrderVersion, quantity: editor.quantity,
        reason: editor.reason.trim() || null, notes: editor.notes.trim() || null, remainderDisposition: editor.remainderDisposition || null, acknowledgeOrderChange: editor.acknowledgeOrderChange };
      if (editor.kind === 'deliver' && this.collectionEnabled) {
        const preview = editor.collectionPreview;
        if (!preview || editor.collectionLoading || editor.collectionError) { editor.error = 'Consulta el saldo del cliente antes de guardar la entrega.'; return null; }
        if (preview.paymentTermCode === 'cash_on_delivery') {
          const amount = parseCollectionAmount(editor.collectionAmount);
          if (amount === null) { editor.error = 'Captura un importe cobrado mayor o igual a cero, con máximo dos decimales.'; return null; }
          fields['collectedAmount'] = amount;
          fields['collectionVersion'] = preview.version;
        }
      }
    } else if (editor.kind === 'planned-count' && trip && editor.order && editor.line) {
      path = `${this.deliveryPath}/trips/${trip.id}/planned-count`;
      fields = { orderId: editor.order.orderId, orderLineId: editor.line.orderLineId, expectedOrderVersion: editor.order.orderVersion,
        expectedTripVersion: trip.version, quantity: editor.quantity, reason: editor.reason.trim() || null, notes: editor.notes.trim() || null };
    } else if (editor.kind === 'route') {
      if (!editor.routeCode.trim() || !editor.routeName.trim()) { editor.error = 'Indica el código y el nombre de la ruta.'; return null; }
      path = '/api/admin/delivery/routes'; fields = { code: editor.routeCode.trim(), name: editor.routeName.trim(), defaultDriverUserId: editor.driverUserId || null, expectedVersion: editor.route?.version ?? null, isActive: editor.isActive };
    } else if (editor.kind === 'day-driver' && editor.route && this.dashboard) {
      path = '/api/admin/delivery/routes/day-driver';
      fields = { date: this.dashboard.date, routeCode: editor.route.code, driverUserId: editor.driverUserId || null,
        expectedVersion: editor.route.dayDriverVersion ?? 0, expectedTripVersion: this.firstTripFor(editor.route)?.version ?? null,
        reason: editor.reason.trim() };
    } else if (editor.kind === 'trip') {
      if (!editor.routeCode || !Number.isInteger(editor.tripNumber) || editor.tripNumber! < 1) { editor.error = 'Selecciona una ruta y un número de vuelta válido.'; return null; }
      path = '/api/admin/delivery/trips'; fields = { date: this.dashboard?.date ?? this.date, routeCode: editor.routeCode, tripNumber: editor.tripNumber, driverUserId: editor.driverUserId || null, reason: editor.reason.trim() || null };
    } else if (editor.kind === 'assign' && editor.order && editor.line) {
      const target = this.dashboard?.trips.find(item => item.id === editor.targetTripId);
      if (!target || editor.quantity! <= 0) { editor.error = 'Selecciona una vuelta abierta y una cantidad mayor que cero.'; return null; }
      path = `/api/admin/delivery/trips/${target.id}/assignments`; fields = { orderId: editor.order.orderId, orderLineId: editor.line.orderLineId, quantity: editor.quantity, expectedTripVersion: target.version, expectedOrderVersion: editor.order.orderVersion };
    } else if (editor.kind === 'location' && editor.order) {
      if (editor.mapsUrl.trim() && !safeDeliveryMapsUrl(editor.mapsUrl.trim())) { editor.error = 'Usa un enlace HTTPS de Google Maps válido.'; return null; }
      method = 'PUT'; path = `/api/admin/delivery/customers/${editor.order.customerId}/location`;
      fields = { mapsUrl: editor.mapsUrl.trim() || null, address: editor.address.trim() || null, expectedVersion: editor.order.locationVersion };
    } else if (editor.kind === 'depart' && trip) {
      path = `${this.deliveryPath}/trips/${trip.id}/depart`; fields = { expectedVersion: trip.version };
    } else if (editor.kind === 'close' && trip) {
      path = `${this.deliveryPath}/trips/${trip.id}/close`; fields = { expectedVersion: trip.version, notes: editor.notes.trim() || null };
    } else if (editor.kind === 'transfer' && assignment) {
      const target = this.dashboard?.trips.find(item => item.id === editor.targetTripId);
      if (!target || editor.quantity! <= 0) { editor.error = 'Selecciona otra vuelta abierta y una cantidad mayor que cero.'; return null; }
      path = `/api/admin/delivery/assignments/${assignment.id}/transfer`; fields = { expectedVersion: assignment.version, targetTripId: target.id, expectedTargetTripVersion: target.version, quantity: editor.quantity, reason: editor.reason.trim() };
    } else if (editor.kind === 'adjust' && assignment) {
      path = `/api/admin/delivery/assignments/${assignment.id}/adjust`; fields = { expectedVersion: assignment.version, stage: editor.adjustStage, quantity: editor.quantity, reason: editor.reason.trim() };
    } else if (editor.kind === 'instructions' && assignment) {
      path = `/api/admin/delivery/assignments/${assignment.id}/instructions`; fields = { expectedVersion: assignment.version, reason: editor.reason.trim() };
    } else if (editor.kind === 'release' && assignment) {
      path = `/api/admin/delivery/assignments/${assignment.id}/release`; fields = { expectedVersion: assignment.version, reason: editor.reason.trim() };
    } else { editor.error = 'No se pudo preparar la operación. Actualiza el reparto.'; return null; }
    return { operationId: editor.operationId, path, method, body: { operationId: editor.operationId, ...fields }, title: editor.title,
      management: this.management, tripId: trip?.id ?? null, summary: [this.dashboard?.date,
        trip && editor.kind !== 'day-driver' ? this.tripLabel(trip) : null,
        assignment?.customerName ?? editor.order?.customerName ?? editor.routeName, assignment?.productName ?? editor.line?.productName,
        this.quantityRequired ? `${this.quantity(editor.quantity)} ${this.quantityUnit}` : ''].filter(Boolean).join(' · ') };
  }

  private execute(command: DeliveryPendingCommand, firstAttempt = false): void {
    const user = this.access.userId();
    if (!user || this.saving) return;
    try { this.api.remember(user, command); }
    catch (error) {
      if (this.editor) this.editor.error = error instanceof Error ? error.message : 'No se pudo conservar la referencia de esta operación en el navegador.';
      try { this.recovery = this.api.pending(user); } catch { this.storageError = 'No se pudo comprobar la referencia guardada.'; }
      return;
    }
    this.saving = true; this.error = null;
    if (this.editor) { this.editor.locked = true; this.editor.error = null; }
    this.write = this.api.execute(command).subscribe({
      next: result => {
        try { this.api.clear(user, command); this.recovery = this.api.pending(user); }
        catch {
          this.saving = false;
          this.recovery = command;
          const message = 'El resultado fue recibido, pero la referencia sigue guardada. Reintenta para comprobarlo antes de registrar otra entrega.';
          if (this.editor) this.editor.error = message; else this.error = message;
          this.detector.detectChanges();
          return;
        }
        this.saving = false;
        this.cancelCollectionPreview(); this.dialog?.nativeElement.close(); this.editor = null;
        this.success = result.replayed ? 'Operación recuperada sin duplicar registros.' : 'Registro guardado. La hora y el responsable quedaron en la bitácora.';
        this.load(true, command.path === '/api/admin/delivery/routes' || command.path === '/api/admin/delivery/routes/day-driver');
        this.detector.detectChanges();
      },
      error: error => {
        this.saving = false;
        const financial = Object.hasOwn(command.body, 'collectedAmount');
        const confirmedRejection = error instanceof HttpErrorResponse && (financial
          ? error.status === 409 && error.error?.retrySameOperation === false || firstAttempt && [400, 403, 404].includes(error.status)
          : error.status >= 400 && error.status < 500);
        const message = confirmedRejection ? this.errorMessage(error, 'No se pudo guardar el registro.')
          : 'No se recibió confirmación del servidor. Conservamos la captura y su referencia; reintenta la misma operación para comprobar el resultado.';
        if (confirmedRejection) {
          try { this.api.clear(user, command); this.recovery = this.api.pending(user); }
          catch { this.recovery = command; this.storageError = 'No se pudo retirar la referencia guardada. Conserva este navegador para revisar el resultado.'; }
          if (this.editor) { this.editor.locked = false; this.editor.command = null; this.editor.operationId = crypto.randomUUID(); this.editor.conflict = error.status === 409; }
        } else this.recovery = command;
        if (this.editor) this.editor.error = message; else this.error = message;
        this.detector.detectChanges();
      }
    });
  }
  protected retryRecovery(): void { if (this.recovery && !this.saving) this.execute(this.recovery); }
  protected reviewConflict(): void {
    const editor = this.editor;
    if (!editor?.conflict || this.loading) return;
    this.loading = true;
    this.read = this.api.getDashboard(this.dashboard?.date ?? this.date, this.management).subscribe({
      next: value => {
        this.dashboard = value; this.loading = false;
        if (editor.assignment) {
          const assignment = value.trips.flatMap(trip => trip.assignments).find(item => item.id === editor.assignment?.id);
          if (!assignment) { editor.error = 'La asignación ya no está disponible. Conservamos tu captura para revisarla con el responsable.'; this.detector.detectChanges(); return; }
          editor.assignment = assignment;
        }
        if (editor.order) {
          const orders = editor.kind === 'planned-count' ? this.trip?.plannedOrders ?? [] : value.orders;
          const order = orders.find(item => item.orderId === editor.order?.orderId);
          if (!order) { editor.error = 'El pedido ya no está disponible para esta operación. Revisa con el responsable.'; this.detector.detectChanges(); return; }
          const line = order.lines.find(item => item.orderLineId === editor.line?.orderLineId) ?? null;
          if (editor.line && !line) { editor.error = 'La partida ya no está disponible en esta vuelta.'; this.detector.detectChanges(); return; }
          editor.order = order; editor.line = line;
        }
        if (editor.route) editor.route = value.routes.find(route => route.code === editor.route?.code) ?? editor.route;
        editor.conflict = false; editor.acknowledgeOrderChange = false;
        editor.error = 'Datos actuales consultados. Revisa las cantidades y vuelve a confirmar; tu borrador se conservó.';
        if (this.collectionEnabled) this.refreshCollectionPreview();
        this.detector.detectChanges();
      }, error: error => { this.loading = false; editor.error = this.errorMessage(error, 'No se pudieron consultar los datos actuales.'); this.detector.detectChanges(); }
    });
  }
  private errorMessage(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && typeof error.error?.error === 'string' ? error.error.error : fallback;
  }
}
