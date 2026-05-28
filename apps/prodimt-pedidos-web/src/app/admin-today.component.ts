import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { AdminOrder, OrderDataService } from './order-data.service';

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
  protected isLoading = true;
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

  private formatError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return 'No se pudieron cargar los pedidos de hoy.';
  }
}
