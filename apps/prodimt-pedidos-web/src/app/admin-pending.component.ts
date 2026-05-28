import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { AdminDecisionApiValue } from './customer-orders-api.service';
import { AdminOrder, AdminOrderAuditEvent, OrderDataService } from './order-data.service';

@Component({
  selector: 'app-admin-pending',
  template: `
    <section class="page stack" data-testid="admin-pending">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Pendientes de revision</h2>
      </div>

      @if (isLoading) {
        <div class="notice">Cargando pendientes...</div>
      }

      @if (resultMessage) {
        <div class="alert success">{{ resultMessage }}</div>
      }

      @if (errorMessage) {
        <div class="alert error">{{ errorMessage }}</div>
      }

      @if (!isLoading && !errorMessage) {
        @if (orders.length === 0) {
          <div class="notice">No hay pedidos pendientes de revision.</div>
        } @else {
          <div class="admin-list">
            @for (order of orders; track order.id) {
              <article class="admin-row">
                <div>
                  <strong>{{ order.customerName }}</strong>
                  <small>{{ order.status }} · Pedido #{{ order.sequenceNumber }}</small>
                  <small>Razon: {{ order.reviewReason ?? 'Revision manual' }}</small>
                  @if (order.isLate) {
                    <small>Pedido tardio</small>
                  }
                </div>
                <div class="review-actions">
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="loadingAuditOrderId === order.id"
                    (click)="loadAudit(order)">
                    Ver auditoria
                  </button>
                  <button
                    type="button"
                    class="primary compact"
                    [disabled]="reviewingOrderId === order.id"
                    (click)="review(order, 'Accepted')">
                    Aceptar
                  </button>
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="reviewingOrderId === order.id"
                    (click)="review(order, 'Rejected')">
                    Rechazar
                  </button>
                </div>
                @if (selectedAuditOrderId === order.id) {
                  <div class="audit-list">
                    @if (auditEvents.length === 0) {
                      <small>No hay eventos de auditoria para mostrar.</small>
                    } @else {
                      @for (event of auditEvents; track event.id) {
                        <div class="audit-event">
                          <strong>{{ event.eventType }}</strong>
                          <small>{{ event.occurredAt }} · {{ event.actorType }}</small>
                          <small>{{ event.summary }}</small>
                        </div>
                      }
                    }
                  </div>
                }
              </article>
            }
          </div>
        }
      }
    </section>
  `
})
export class AdminPendingComponent {
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);

  protected orders: AdminOrder[] = [];
  protected auditEvents: AdminOrderAuditEvent[] = [];
  protected isLoading = true;
  protected reviewingOrderId: string | null = null;
  protected loadingAuditOrderId: string | null = null;
  protected selectedAuditOrderId: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;

  constructor() {
    this.load();
  }

  protected review(order: AdminOrder, decision: AdminDecisionApiValue): void {
    this.errorMessage = null;
    this.resultMessage = null;
    this.reviewingOrderId = order.id;

    this.data.reviewOrder(order.id, decision).subscribe({
      next: () => {
        this.resultMessage = decision === 'Accepted'
          ? 'Pedido aceptado.'
          : 'Pedido rechazado.';
        this.load(false);
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo guardar la decision administrativa.');
        this.reviewingOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected loadAudit(order: AdminOrder): void {
    this.errorMessage = null;
    this.loadingAuditOrderId = order.id;
    this.selectedAuditOrderId = order.id;

    this.data.loadOrderAudit(order.id).subscribe({
      next: (events) => {
        this.auditEvents = events;
        this.loadingAuditOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo cargar la auditoria.');
        this.loadingAuditOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  private load(showLoading = true): void {
    if (showLoading) {
      this.isLoading = true;
    }

    this.data.loadPendingReviewOrders().subscribe({
      next: (orders) => {
        this.orders = orders;
        this.auditEvents = [];
        this.selectedAuditOrderId = null;
        this.isLoading = false;
        this.reviewingOrderId = null;
        this.loadingAuditOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudieron cargar los pendientes de revision.');
        this.isLoading = false;
        this.reviewingOrderId = null;
        this.loadingAuditOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  private formatError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return fallback;
  }
}
