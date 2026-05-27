import { Component, inject } from '@angular/core';
import { OrderDataService } from './order-data.service';

@Component({
  selector: 'app-admin-pending',
  template: `
    <section class="page stack">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Pendientes de revision</h2>
      </div>

      <div class="admin-list">
        @for (order of data.pendingReviewOrders; track order.id) {
          <article class="admin-row">
            <div>
              <strong>{{ order.customerName }}</strong>
              <small>{{ order.status }} · {{ order.requestedDelivery }}</small>
            </div>
            <div class="review-actions">
              <button type="button" class="primary compact">Aceptar</button>
              <button type="button" class="secondary compact">Rechazar</button>
            </div>
          </article>
        }
      </div>
    </section>
  `
})
export class AdminPendingComponent {
  protected readonly data = inject(OrderDataService);
}
