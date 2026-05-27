import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrderDataService } from './order-data.service';

@Component({
  selector: 'app-customer-today',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="customer-today">
      <div class="page-title">
        <p class="eyebrow">{{ data.customerName }}</p>
        <h2>Mi pedido de hoy</h2>
      </div>

      <div class="product-list">
        @for (product of data.frequentProducts; track product.id) {
          <label class="product-row">
            <span>
              <strong>{{ product.name }}</strong>
              <small>Cantidad sugerida: {{ product.suggestedQuantity }}</small>
            </span>
            <input
              type="number"
              min="0"
              step="1"
              inputmode="numeric"
              [(ngModel)]="product.quantity"
              [attr.aria-label]="'Cantidad ' + product.name">
          </label>
        }
      </div>

      <div class="action-bar">
        <button type="button" class="primary">Enviar pedido</button>
        <button type="button" class="secondary">No pedir hoy</button>
      </div>
    </section>
  `
})
export class CustomerTodayComponent {
  protected readonly data = inject(OrderDataService);
}
