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

@Component({
  selector: 'app-admin-today',
  imports: [FormsModule, AdminOrderCorrectionComponent, RouterLink],
  template: `
    <section class="page stack" data-testid="admin-today">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>{{ serviceDate === today ? 'Pedidos de hoy' : 'Pedidos por fecha' }}</h2>
      </div>

      <form class="operations-filters" (ngSubmit)="load()">
        <label><span>Fecha de servicio</span><input name="ordersServiceDate" type="date" required [(ngModel)]="serviceDate" (ngModelChange)="clearDate()" [disabled]="isLoading || !!loadingDetailOrderId || correcting"></label>
        <button type="submit" class="primary" [disabled]="isLoading || !serviceDate || correcting">Consultar pedidos</button>
      </form>
      <p class="notice">Si el cliente modifica un pedido, abre su detalle y usa «Corregir pedido». Por ejemplo, 15 kg más 5 kg se corrigen a un total de 20 kg con motivo. Una captura separada requiere revisión.</p>
      @if (lastUpdated) { <small>Última consulta: {{ lastUpdated }}. Se actualiza cada 15 segundos mientras la pantalla está visible; una corrección abierta conserva lo que estás editando.</small> }
      @if (refreshError) { <div class="alert warning" role="alert">{{ refreshError }} Los datos visibles pueden estar desactualizados.</div> }

      @if (isLoading) {
        <div class="notice">Cargando pedidos...</div>
      }

      @if (errorMessage) {
        <div class="alert error" role="alert">{{ errorMessage }}</div>
      }
      @if (resultMessage) { <div class="alert success" role="status">{{ resultMessage }}</div> }

      @if (!isLoading && !errorMessage) {
        @if (orders.length === 0) {
          <div class="notice">No hay pedidos registrados para esta fecha.</div>
        } @else {
          <div class="admin-list">
            @for (order of orders; track order.id) {
              <article class="admin-row">
                <div>
                  <strong>{{ order.customerName }}</strong>
                  <small>{{ order.status }} · Servicio {{ order.orderDate }} · Enviado {{ order.submittedAt }}</small>
                  <small>Entrega: {{ order.requestedDelivery }}</small>
                </div>
                <div class="badges">
                  @if (orderChanges(order.id).length) { <span class="badge warning">Con cambios registrados</span> }
                  @if (pendingChangeCount(order.id)) { <a class="badge danger" routerLink="/admin/operacion" [queryParams]="{ date: serviceDate }">{{ pendingChangeCount(order.id) }} avisos pendientes de recibir</a> }
                  @if (order.isLate) {
                    <span class="badge warning">Tardio</span>
                  }
                  @if (order.requiresReview) {
                    <span class="badge danger">Revision</span>
                  }
                </div>
                <div class="review-actions">
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="!!loadingDetailOrderId || correcting"
                    (click)="loadDetail(order)">
                    Ver detalle
                  </button>
                </div>
                @if (getSelectedDetail(order); as detail) {
                  <section class="detail-panel">
                    <div>
                      <strong>Detalle interno</strong>
                      <small>Versión {{ detail.version }}</small>
                      <small>Canal: {{ detail.salesChannel }}</small>
                      @if (detail.source) { <small>Origen: {{ detail.source }} · Referencia {{ detail.externalOrderKey }}</small> }
                      <small>Entrega: {{ detail.requestedDelivery }}</small>
                      @if (detail.deliveryNotes) {
                        <small>Notas entrega: {{ detail.deliveryNotes }}</small>
                      }
                      @if (detail.internalNotes) {
                        <small>Notas internas: {{ detail.internalNotes }}</small>
                      }
                    </div>
                    <div class="line-list">
                      @for (line of detail.lines; track line.id) {
                        <div class="line-row">
                          <span>
                            <strong>{{ line.productName }}</strong>
                            <small>Cantidad: {{ line.quantity }} {{ unitLabel(line.unitCode) }}</small>
                            @if (line.notes) {
                              <small>Notas: {{ line.notes }}</small>
                            }
                          </span>
                          <small>Maquina: {{ line.machineLabel }}</small>
                        </div>
                      }
                    </div>
                    <div class="review-actions">
                      @if (canCorrect(detail)) { <button type="button" class="secondary" (click)="correcting = true" [disabled]="correcting">Corregir pedido</button> }
                      <button type="button" class="secondary" (click)="loadAudit(detail.id)" [disabled]="loadingAudit || correcting">Ver historial</button>
                    </div>
                    @if (correcting) {
                      <app-admin-order-correction [detail]="detail" (saved)="correctionSaved(order)" (cancel)="correcting = false" (reload)="reloadDetail(order)" />
                    }
                    @if (audit !== null) {
                      <section class="audit-list" aria-label="Historial del pedido">
                        @for (event of audit; track event.id) {
                          <article class="audit-event"><strong>{{ event.summary }}</strong><small>{{ event.occurredAt }} · {{ event.actorType }}</small>
                            @if (event.correction; as correction) {
                              <p><b>Motivo:</b> {{ correction.reason }}</p>
                              @for (change of correction.changes; track $index) {
                                <div><strong>{{ change.productName }}</strong><small>Antes: {{ change.before }} → Después: {{ change.after }}</small>
                                  @if (change.beforeNotes !== change.afterNotes) { <small>Nota anterior: {{ change.beforeNotes || 'Sin nota' }} · Nota nueva: {{ change.afterNotes || 'Sin nota' }}</small> }
                                </div>
                              }
                            }
                          </article>
                        }
                        @if (!audit.length) { <p>No hay eventos disponibles.</p> }
                      </section>
                    }
                  </section>
                }
              </article>
            }
          </div>
        }
      }
    </section>
  `
})
export class AdminTodayComponent {
  protected readonly unitLabel = unitLabel;
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly operations = inject(AdminOperationsApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private queryRequest: Subscription | null = null;
  private generation = 0;
  private detailGeneration = 0;
  private requestedOrderId = this.route.snapshot.queryParamMap.get('orderId');

  protected orders: AdminOrder[] = [];
  protected selectedDetail: AdminOrderDetail | null = null;
  protected isLoading = true;
  protected loadingDetailOrderId: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected readonly today = businessDateToday();
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
      changes: this.operations.getChanges(this.serviceDate, ''),
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
    return ['Submitted', 'Accepted', 'PendingAdminReview'].includes(detail.status);
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
