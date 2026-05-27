import { Component, inject } from '@angular/core';
import { OrderDataService } from './order-data.service';

@Component({
  selector: 'app-admin-today',
  template: `
    <section class="page stack">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Pedidos de hoy</h2>
      </div>

      <div class="admin-list">
        @for (order of data.todayOrders; track order.id) {
          <article class="admin-row">
            <div>
              <strong>{{ order.customerName }}</strong>
              <small>{{ order.status }} · {{ order.requestedDelivery }}</small>
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
    </section>
  `
})
export class AdminTodayComponent {
  protected readonly data = inject(OrderDataService);
}
