import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FrequentProduct, OrderDataService } from './order-data.service';

@Component({
  selector: 'app-customer-today',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="customer-today">
      <div class="page-title">
        <p class="eyebrow">{{ customerName }}</p>
        <h2>Mi pedido de hoy</h2>
      </div>

      <div class="product-list">
        @for (product of frequentProducts; track product.id) {
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
  private readonly data = inject(OrderDataService);

  protected customerName = this.data.customerName;

  protected frequentProducts: FrequentProduct[] = this.data.frequentProducts.map((product) => ({ ...product }));

  constructor() {
    this.data.loadCustomerToday().subscribe((today) => {
      this.customerName = today.customerName;
      this.frequentProducts = today.frequentProducts;
    });
  }
}
