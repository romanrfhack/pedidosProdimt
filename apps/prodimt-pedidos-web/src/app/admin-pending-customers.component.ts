import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminOrderTemplate, OrderDataService, PendingCustomerOrder } from './order-data.service';

@Component({
  selector: 'app-admin-pending-customers',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="admin-pending-customers">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Clientes pendientes</h2>
      </div>

      @if (isLoading) {
        <div class="notice">Cargando clientes pendientes...</div>
      }

      @if (resultMessage) {
        <div class="alert success">{{ resultMessage }}</div>
      }

      @if (errorMessage) {
        <div class="alert error">{{ errorMessage }}</div>
      }

      @if (!isLoading && !errorMessage) {
        @if (customers.length === 0) {
          <div class="notice">No hay clientes pendientes por responder.</div>
        } @else {
          <div class="admin-list">
            @for (customer of customers; track customer.id) {
              <article class="admin-row">
                <div>
                  <strong>{{ customer.name }}</strong>
                  <small>Telefono: {{ customer.phoneNumber }}</small>
                  <small>Entrega: {{ customer.requestedDelivery }}</small>
                  <small>Productos frecuentes: {{ customer.frequentProductsCount }}</small>
                  @if (customer.deliveryNotes) {
                    <small>Notas: {{ customer.deliveryNotes }}</small>
                  }
                </div>
                <div class="review-actions">
                  <button
                    type="button"
                    class="primary compact"
                    [disabled]="loadingCustomerId === customer.id"
                    (click)="startCapture(customer)">
                    Capturar pedido
                  </button>
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="submittingCustomerId === customer.id"
                    (click)="markNoOrder(customer)">
                    No pedir hoy
                  </button>
                </div>

                @if (getTemplate(customer); as selectedTemplate) {
                  <form class="detail-panel stack" (ngSubmit)="submitCapture()">
                    <strong>Captura para {{ selectedTemplate.customerName }}</strong>
                    <label>
                      <span>Hora entrega</span>
                      <input name="requestedDeliveryTime" type="time" [(ngModel)]="selectedTemplate.requestedDeliveryTime">
                    </label>
                    <label>
                      <span>Notas de entrega</span>
                      <input name="deliveryNotes" [(ngModel)]="selectedTemplate.deliveryNotes">
                    </label>
                    <label>
                      <span>Notas internas</span>
                      <input name="captureInternalNotes" [(ngModel)]="internalNotes">
                    </label>
                    <div class="line-list">
                      @for (product of selectedTemplate.products; track product.id) {
                        <label class="line-row">
                          <span>
                            <strong>{{ product.name }}</strong>
                            <small>Sugerido: {{ product.suggestedQuantity }}</small>
                          </span>
                          <input
                            type="number"
                            min="0"
                            step="1"
                            inputmode="numeric"
                            [name]="'captureQuantity' + product.id"
                            [(ngModel)]="product.quantity">
                        </label>
                      }
                    </div>
                    <div class="review-actions">
                      <button type="submit" class="primary compact" [disabled]="submittingCustomerId === customer.id">
                        Guardar pedido
                      </button>
                      <button type="button" class="secondary compact" (click)="cancelCapture()">Cancelar</button>
                    </div>
                  </form>
                }
              </article>
            }
          </div>
        }
      }
    </section>
  `
})
export class AdminPendingCustomersComponent {
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);

  protected customers: PendingCustomerOrder[] = [];
  protected template: AdminOrderTemplate | null = null;
  protected internalNotes: string | null = null;
  protected isLoading = true;
  protected loadingCustomerId: string | null = null;
  protected submittingCustomerId: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;

  constructor() {
    this.load();
  }

  protected startCapture(customer: PendingCustomerOrder): void {
    this.errorMessage = null;
    this.resultMessage = null;
    this.loadingCustomerId = customer.id;

    this.data.loadAdminOrderTemplate(customer.id).subscribe({
      next: (template) => {
        this.template = template;
        this.internalNotes = 'Capturado por administracion.';
        this.loadingCustomerId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo cargar la plantilla del cliente.');
        this.loadingCustomerId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected submitCapture(): void {
    if (!this.template) {
      return;
    }

    if (this.template.products.some((product) => Number(product.quantity) < 0)) {
      this.errorMessage = 'No uses cantidades negativas.';
      this.changeDetector.detectChanges();
      return;
    }

    const lines = this.template.products
      .filter((product) => Number(product.quantity) > 0)
      .map((product) => ({
        productId: product.id,
        quantity: Number(product.quantity),
        notes: this.emptyToNull(product.notes)
      }));

    if (lines.length === 0) {
      this.errorMessage = 'Captura al menos una cantidad o usa No pedir hoy.';
      this.changeDetector.detectChanges();
      return;
    }

    this.errorMessage = null;
    this.resultMessage = null;
    this.submittingCustomerId = this.template.customerId;

    this.data.submitAdminCustomerOrder(this.template.customerId, {
      lines,
      requestedDeliveryTime: this.emptyToNull(this.template.requestedDeliveryTime),
      requestedDeliveryWindowStart: this.emptyToNull(this.template.requestedDeliveryWindowStart),
      requestedDeliveryWindowEnd: this.emptyToNull(this.template.requestedDeliveryWindowEnd),
      deliveryNotes: this.emptyToNull(this.template.deliveryNotes),
      internalNotes: this.emptyToNull(this.internalNotes)
    }).subscribe({
      next: () => {
        this.resultMessage = 'Pedido capturado por administracion.';
        this.cancelCapture();
        this.load(false);
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo capturar el pedido.');
        this.submittingCustomerId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected markNoOrder(customer: PendingCustomerOrder): void {
    this.errorMessage = null;
    this.resultMessage = null;
    this.submittingCustomerId = customer.id;

    this.data.markAdminNoOrder(customer.id, 'Cliente confirmo que no pedira hoy.').subscribe({
      next: () => {
        this.resultMessage = 'No pedir hoy registrado por administracion.';
        this.cancelCapture();
        this.load(false);
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo registrar No pedir hoy.');
        this.submittingCustomerId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected cancelCapture(): void {
    this.template = null;
    this.internalNotes = null;
  }

  protected getTemplate(customer: PendingCustomerOrder): AdminOrderTemplate | null {
    return this.template?.customerId === customer.id ? this.template : null;
  }

  private load(showLoading = true): void {
    if (showLoading) {
      this.isLoading = true;
    }

    this.data.loadPendingCustomers().subscribe({
      next: (customers) => {
        this.customers = customers;
        this.isLoading = false;
        this.loadingCustomerId = null;
        this.submittingCustomerId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudieron cargar los clientes pendientes.');
        this.isLoading = false;
        this.loadingCustomerId = null;
        this.submittingCustomerId = null;
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
