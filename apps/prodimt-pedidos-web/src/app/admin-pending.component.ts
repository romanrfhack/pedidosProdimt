import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminDecisionApiValue } from './customer-orders-api.service';
import { AdminOrder, AdminOrderAuditEvent, AdminOrderDetail, OrderDataService } from './order-data.service';

interface ChangeLineForm {
  orderLineId: string;
  productName: string;
  quantity: number;
  notes: string | null;
}

@Component({
  selector: 'app-admin-pending',
  imports: [FormsModule],
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
                    (click)="startAcceptedWithChanges(order)">
                    Cambios
                  </button>
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="reviewingOrderId === order.id"
                    (click)="review(order, 'Rejected')">
                    Rechazar
                  </button>
                </div>

                @if (changeDetail?.id === order.id) {
                  <form class="detail-panel stack" (ngSubmit)="reviewWithChanges(order)">
                    <strong>Aceptar con cambios</strong>
                    <label>
                      <span>Hora entrega</span>
                      <input name="requestedDeliveryTime" type="time" [(ngModel)]="requestedDeliveryTime">
                    </label>
                    <label>
                      <span>Notas de entrega</span>
                      <input name="deliveryNotes" [(ngModel)]="deliveryNotes">
                    </label>
                    <label>
                      <span>Notas internas</span>
                      <input name="internalNotes" [(ngModel)]="internalNotes">
                    </label>
                    <div class="line-list">
                      @for (line of changeLines; track line.orderLineId) {
                        <label class="line-row">
                          <span>
                            <strong>{{ line.productName }}</strong>
                            <small>Cantidad actualizable</small>
                          </span>
                          <input
                            type="number"
                            min="1"
                            step="1"
                            inputmode="numeric"
                            [name]="'lineQuantity' + line.orderLineId"
                            [(ngModel)]="line.quantity">
                        </label>
                      }
                    </div>
                    <div class="review-actions">
                      <button type="submit" class="primary compact" [disabled]="reviewingOrderId === order.id">
                        Aceptar con cambios
                      </button>
                      <button type="button" class="secondary compact" (click)="cancelChanges()">Cancelar</button>
                    </div>
                  </form>
                }

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
  protected changeDetail: AdminOrderDetail | null = null;
  protected changeLines: ChangeLineForm[] = [];
  protected requestedDeliveryTime: string | null = null;
  protected deliveryNotes: string | null = null;
  protected internalNotes: string | null = null;
  protected isLoading = true;
  protected reviewingOrderId: string | null = null;
  protected loadingAuditOrderId: string | null = null;
  protected loadingChangesOrderId: string | null = null;
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

  protected startAcceptedWithChanges(order: AdminOrder): void {
    this.errorMessage = null;
    this.resultMessage = null;
    this.loadingChangesOrderId = order.id;

    this.data.loadOrderDetail(order.id).subscribe({
      next: (detail) => {
        this.changeDetail = detail;
        this.changeLines = detail.lines.map((line) => ({
          orderLineId: line.id,
          productName: line.productName,
          quantity: line.quantity,
          notes: line.notes
        }));
        this.requestedDeliveryTime = null;
        this.deliveryNotes = detail.deliveryNotes;
        this.internalNotes = detail.internalNotes;
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo cargar el detalle del pedido.');
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected reviewWithChanges(order: AdminOrder): void {
    if (this.changeLines.some((line) => Number(line.quantity) <= 0)) {
      this.errorMessage = 'Las cantidades ajustadas deben ser mayores a cero.';
      this.changeDetector.detectChanges();
      return;
    }

    this.errorMessage = null;
    this.resultMessage = null;
    this.reviewingOrderId = order.id;

    this.data.reviewOrderWithRequest(order.id, {
      decision: 'AcceptedWithChanges',
      internalNotes: this.emptyToNull(this.internalNotes),
      requestedDeliveryTime: this.emptyToNull(this.requestedDeliveryTime),
      requestedDeliveryWindowStart: null,
      requestedDeliveryWindowEnd: null,
      deliveryNotes: this.emptyToNull(this.deliveryNotes),
      lineAdjustments: this.changeLines.map((line) => ({
        orderLineId: line.orderLineId,
        quantity: Number(line.quantity),
        notes: this.emptyToNull(line.notes)
      }))
    }).subscribe({
      next: () => {
        this.resultMessage = 'Pedido aceptado con cambios.';
        this.cancelChanges();
        this.load(false);
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo aceptar con cambios.');
        this.reviewingOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected cancelChanges(): void {
    this.changeDetail = null;
    this.changeLines = [];
    this.requestedDeliveryTime = null;
    this.deliveryNotes = null;
    this.internalNotes = null;
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
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudieron cargar los pendientes de revision.');
        this.isLoading = false;
        this.reviewingOrderId = null;
        this.loadingAuditOrderId = null;
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  private emptyToNull(value: string | null): string | null {
    return value && value.trim() ? value : null;
  }

  private formatError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return fallback;
  }
}
