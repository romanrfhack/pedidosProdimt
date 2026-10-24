import { businessDateToday, unitLabel } from './operational-labels';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { AdminOrder, AdminOrderAuditEvent, AdminOrderDetail, OrderDataService } from './order-data.service';
import { FormsModule } from '@angular/forms';
import { AdminOrderCorrectionComponent } from './admin-order-correction.component';

@Component({
  selector: 'app-admin-today',
  imports: [FormsModule, AdminOrderCorrectionComponent],
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

  protected orders: AdminOrder[] = [];
  protected selectedDetail: AdminOrderDetail | null = null;
  protected isLoading = true;
  protected loadingDetailOrderId: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected readonly today = businessDateToday();
  protected serviceDate = this.today;
  protected correcting = false;
  protected audit: AdminOrderAuditEvent[] | null = null;
  protected loadingAudit = false;

  constructor() {
    this.load();
  }

  protected clearDate(): void {
    this.orders = [];
    this.selectedDetail = null;
    this.audit = null;
    this.errorMessage = null;
    this.resultMessage = null;
  }

  protected load(): void {
    if (!this.serviceDate || this.correcting) return;
    this.clearDate();
    this.isLoading = true;
    this.data.loadTodayOrders(this.serviceDate).subscribe({
      next: (orders) => {
        this.orders = orders;
        this.isLoading = false;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error);
        this.isLoading = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected loadDetail(order: AdminOrder): void {
    this.audit = null;
    this.correcting = false;
    if (this.selectedDetail?.id === order.id) {
      this.selectedDetail = null;
      return;
    }

    this.errorMessage = null;
    this.loadingDetailOrderId = order.id;

    this.data.loadOrderDetail(order.id).subscribe({
      next: (detail) => {
        this.selectedDetail = detail;
        this.orders = this.orders.map((current) => current.id === detail.id ? detail : current);
        this.loadingDetailOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
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
    this.resultMessage = 'Corrección guardada. Revisa el plan antes de continuar la preparación.';
    this.reloadDetail(order);
  }

  protected loadAudit(orderId: string): void {
    this.loadingAudit = true;
    this.data.loadOrderAudit(orderId).subscribe({
      next: (events) => { if (this.selectedDetail?.id === orderId) this.audit = events; this.loadingAudit = false; this.changeDetector.detectChanges(); },
      error: (error: unknown) => { if (this.selectedDetail?.id === orderId) this.errorMessage = this.formatError(error); this.loadingAudit = false; this.changeDetector.detectChanges(); }
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
