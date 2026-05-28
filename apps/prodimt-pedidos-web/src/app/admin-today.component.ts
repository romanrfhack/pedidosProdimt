import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { AdminOrder, AdminOrderDetail, OrderDataService } from './order-data.service';

@Component({
  selector: 'app-admin-today',
  template: `
    <section class="page stack" data-testid="admin-today">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Pedidos de hoy</h2>
      </div>

      @if (isLoading) {
        <div class="notice">Cargando pedidos...</div>
      }

      @if (errorMessage) {
        <div class="alert error">{{ errorMessage }}</div>
      }

      @if (!isLoading && !errorMessage) {
        @if (orders.length === 0) {
          <div class="notice">No hay pedidos registrados hoy.</div>
        } @else {
          <div class="admin-list">
            @for (order of orders; track order.id) {
              <article class="admin-row">
                <div>
                  <strong>{{ order.customerName }}</strong>
                  <small>{{ order.status }} · Enviado {{ order.submittedAt }}</small>
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
                    [disabled]="loadingDetailOrderId === order.id"
                    (click)="loadDetail(order)">
                    Ver detalle
                  </button>
                </div>
                @if (getSelectedDetail(order); as detail) {
                  <section class="detail-panel">
                    <div>
                      <strong>Detalle interno</strong>
                      <small>Canal: {{ detail.salesChannel }}</small>
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
                            <small>Cantidad: {{ line.quantity }}</small>
                            @if (line.notes) {
                              <small>Notas: {{ line.notes }}</small>
                            }
                          </span>
                          <small>Maquina: {{ line.machineLabel }}</small>
                        </div>
                      }
                    </div>
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
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);

  protected orders: AdminOrder[] = [];
  protected selectedDetail: AdminOrderDetail | null = null;
  protected isLoading = true;
  protected loadingDetailOrderId: string | null = null;
  protected errorMessage: string | null = null;

  constructor() {
    this.data.loadTodayOrders().subscribe({
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
    if (this.selectedDetail?.id === order.id) {
      this.selectedDetail = null;
      return;
    }

    this.errorMessage = null;
    this.loadingDetailOrderId = order.id;

    this.data.loadOrderDetail(order.id).subscribe({
      next: (detail) => {
        this.selectedDetail = detail;
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
