import { EffectivePermissionsService } from './effective-permissions.service';
import { priceText } from './commercial-catalog-api.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { businessDateToday, unitLabel } from './operational-labels';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, of, Subscription } from 'rxjs';
import { AdminOrder, AdminOrderAuditEvent, AdminOrderDetail, OrderDataService } from './order-data.service';
import { FormsModule } from '@angular/forms';
import { AdminOrderCorrectionComponent } from './admin-order-correction.component';
import { AdminOperationsApiService, OperationsChange } from './admin-operations-api.service';

type OrderListFilter = 'all' | 'registered' | 'review' | 'noOrder' | 'rejected' | 'late' | 'changes';

@Component({
  selector: 'app-admin-today',
  imports: [FormsModule, AdminOrderCorrectionComponent, RouterLink],
  styleUrl: './admin-today.component.css',
  templateUrl: './admin-today.component.html'
})
export class AdminTodayComponent {
  protected readonly priceText = priceText;
  protected readonly unitLabel = unitLabel;
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly operations = inject(AdminOperationsApiService);
  protected readonly access = inject(EffectivePermissionsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private queryRequest: Subscription | null = null;
  private generation = 0;
  private detailGeneration = 0;
  private requestedOrderId = this.route.snapshot.queryParamMap.get('orderId');

  protected searchText = '';
  protected selectedFilter: OrderListFilter = 'all';
  protected readonly filters: { id: OrderListFilter; label: string }[] = [
    { id: 'all', label: 'Todos' }, { id: 'registered', label: 'Registrados / aceptados' },
    { id: 'review', label: 'Por revisar' }, { id: 'noOrder', label: 'No pide' },
    { id: 'rejected', label: 'Rechazados' }, { id: 'late', label: 'Tardíos' },
    { id: 'changes', label: 'Avisos pendientes' }
  ];
  protected orders: AdminOrder[] = [];
  protected selectedDetail: AdminOrderDetail | null = null;
  protected isLoading = true;
  protected loadingDetailOrderId: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected get today(): string { return businessDateToday(); }
  protected serviceDate = this.route.snapshot.queryParamMap.get('date') || this.today;
  protected correcting = false;
  protected audit: AdminOrderAuditEvent[] | null = null;
  protected loadingAudit = false;
  protected changes: OperationsChange[] = [];
  protected isRefreshing = false;
  protected refreshError: string | null = null;
  protected lastUpdated: string | null = null;

  constructor() {
    this.load();
    const refresh = () => {
      if (!document.hidden && this.lastUpdated && !this.correcting && !this.isLoading && !this.isRefreshing && !this.loadingDetailOrderId && !this.loadingAudit) this.load(true);
    };
    const timer = window.setInterval(refresh, 15_000);
    document.addEventListener('visibilitychange', refresh);
    this.destroyRef.onDestroy(() => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', refresh);
      this.queryRequest?.unsubscribe();
    });
  }

  protected get visibleOrders(): AdminOrder[] {
    const query = this.normalizeSearch(this.searchText);
    return this.orders.filter(order => this.matchesFilter(order, this.selectedFilter)
      && (!query || this.normalizeSearch(`${order.customerName} #${order.sequenceNumber} ${this.statusLabel(order)}`).includes(query)));
  }

  protected filterCount(filter: OrderListFilter): number {
    return this.orders.filter(order => this.matchesFilter(order, filter)).length;
  }

  protected setFilter(filter: OrderListFilter): void {
    if (!this.correcting) this.selectedFilter = filter;
  }

  protected clearFilters(): void {
    if (this.correcting) return;
    this.searchText = '';
    this.selectedFilter = 'all';
  }

  protected showToday(): void {
    if (this.correcting || this.isLoading || this.isRefreshing || this.loadingDetailOrderId) return;
    this.serviceDate = this.today;
    this.load();
  }

  protected statusClass(order: AdminOrder): string {
    switch (order.status) {
      case 'Submitted': case 'Accepted': return 'order-registered';
      case 'PendingAdminReview': return 'order-review';
      case 'NoOrder': return 'order-off';
      case 'Rejected': return 'order-rejected';
      default: return 'order-unknown';
    }
  }

  protected statusLabel(order: AdminOrder): string {
    switch (order.status) {
      case 'Submitted': return 'Registrado';
      case 'Accepted': return order.adminDecision === 'AcceptedWithChanges' ? 'Aceptado con cambios' : 'Aceptado';
      case 'PendingAdminReview': return 'Por revisar';
      case 'NoOrder': return 'No pide en esta fecha';
      case 'Rejected': return 'Rechazado';
      default: return 'Sin clasificar';
    }
  }

  private matchesFilter(order: AdminOrder, filter: OrderListFilter): boolean {
    switch (filter) {
      case 'all': return true;
      case 'registered': return ['Submitted', 'Accepted'].includes(order.status);
      case 'review': return order.status === 'PendingAdminReview';
      case 'noOrder': return order.status === 'NoOrder';
      case 'rejected': return order.status === 'Rejected';
      case 'late': return order.isLate;
      case 'changes': return this.pendingChangeCount(order.id) > 0;
    }
  }

  private normalizeSearch(value: string): string {
    return value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es-MX').trim();
  }

  protected clearDate(): void {
    this.generation++;
    this.detailGeneration++;
    this.loadingDetailOrderId = null; this.loadingAudit = false;
    this.queryRequest?.unsubscribe();
    this.orders = [];
    this.selectedDetail = null;
    this.audit = null;
    this.errorMessage = null;
    this.resultMessage = null;
    this.changes = []; this.lastUpdated = null; this.refreshError = null; this.isRefreshing = false;
  }

  protected load(background = false): void {
    if (!this.serviceDate || this.correcting || this.isRefreshing) return;
    if (!background) this.clearDate();
    this.isLoading = !background;
    this.isRefreshing = background;
    const generation = ++this.generation;
    const selectedId = this.selectedDetail?.id;
    this.queryRequest = forkJoin({
      orders: this.data.loadTodayOrders(this.serviceDate),
      changes: this.access.any(['operations.read','operations.acknowledge']) ? this.operations.getChanges(this.serviceDate, '') : of({ changes: [] }),
      detail: background && selectedId ? this.data.loadOrderDetail(selectedId) : of(null)
    }).subscribe({
      next: ({ orders, changes, detail }) => {
        if (generation !== this.generation) return;
        this.isLoading = false; this.isRefreshing = false;
        // An operator may open a correction while a background request is in flight.
        if (background && this.correcting) return;
        this.orders = orders;
        this.changes = changes.changes;
        if (detail && this.selectedDetail?.id === detail.id) {
          if (this.selectedDetail.version !== detail.version) this.audit = null;
          this.selectedDetail = detail;
        }
        this.refreshError = null;
        this.lastUpdated = new Intl.DateTimeFormat('es-MX', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'America/Mexico_City' }).format(new Date());
        if (this.requestedOrderId) {
          const requested = orders.find(order => order.id === this.requestedOrderId);
          this.requestedOrderId = null;
          if (requested) this.loadDetail(requested);
        }
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (generation !== this.generation) return;
        if (background) this.refreshError = this.formatError(error); else this.errorMessage = this.formatError(error);
        this.isLoading = false; this.isRefreshing = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected loadDetail(order: AdminOrder): void {
    const generation = ++this.detailGeneration;
    this.audit = null;
    this.correcting = false;
    if (this.selectedDetail?.id === order.id) {
      this.selectedDetail = null;
      return;
    }

    this.errorMessage = null;
    this.loadingDetailOrderId = order.id;

    this.data.loadOrderDetail(order.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (detail) => {
        if (generation !== this.detailGeneration) return;
        this.selectedDetail = detail;
        this.orders = this.orders.map((current) => current.id === detail.id ? detail : current);
        this.loadingDetailOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (generation !== this.detailGeneration) return;
        this.errorMessage = this.formatError(error);
        this.loadingDetailOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected canCorrect(detail: AdminOrderDetail): boolean {
    return this.access.has('orders.capture') && ['Submitted', 'Accepted', 'PendingAdminReview'].includes(detail.status);
  }

  protected reloadDetail(order: AdminOrder): void {
    this.correcting = false;
    this.selectedDetail = null;
    this.loadDetail(order);
  }

  protected correctionSaved(order: AdminOrder): void {
    this.resultMessage = 'Corrección guardada con su motivo. Consulta en el plan los avisos de las máquinas afectadas; recibirlos no confirma producción.';
    this.reloadDetail(order);
    const generation = this.generation;
    const date = this.serviceDate;
    if (!this.access.any(['operations.read','operations.acknowledge'])) return;
    this.operations.getChanges(date, '').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { if (generation !== this.generation || date !== this.serviceDate) return; this.changes = result.changes; this.changeDetector.detectChanges(); },
      error: () => { if (generation !== this.generation || date !== this.serviceDate) return; this.refreshError = 'La corrección se guardó, pero no se pudieron consultar sus avisos.'; this.changeDetector.detectChanges(); }
    });
  }

  protected orderChanges(orderId: string) { return this.changes.filter(change => change.orderId === orderId); }
  protected pendingChangeCount(orderId: string) { return this.orderChanges(orderId).filter(change => !change.acknowledgedAt).length; }

  protected loadAudit(orderId: string): void {
    this.loadingAudit = true;
    const generation = this.detailGeneration;
    this.data.loadOrderAudit(orderId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (events) => { if (generation !== this.detailGeneration) return; if (this.selectedDetail?.id === orderId) this.audit = events; this.loadingAudit = false; this.changeDetector.detectChanges(); },
      error: (error: unknown) => { if (generation !== this.detailGeneration) return; if (this.selectedDetail?.id === orderId) this.errorMessage = this.formatError(error); this.loadingAudit = false; this.changeDetector.detectChanges(); }
    });
  }

  protected getSelectedDetail(order: AdminOrder): AdminOrderDetail | null {
    return this.selectedDetail?.id === order.id ? this.selectedDetail : null;
  }

  private formatError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return 'No se pudieron cargar los pedidos de hoy.';
  }
}
