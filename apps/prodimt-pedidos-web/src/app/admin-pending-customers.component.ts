import { businessDateToday, unitLabel } from './operational-labels';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin, Subscription } from 'rxjs';
import { AdminOrder, AdminOrderTemplate, OrderDataService, PendingCustomerOrder } from './order-data.service';

@Component({
  selector: 'app-admin-pending-customers',
  imports: [FormsModule, RouterLink],
  styles: [`
    fieldset { border: 0; margin: 0; padding: 0; min-width: 0; }
    .check-row { display: flex; align-items: flex-start; gap: .6rem; }
    .check-row input { width: auto; flex: none; }
    .check-row span, .alert a { overflow-wrap: anywhere; }
  `],
  template: `
    <section class="page stack" data-testid="admin-pending-customers">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Clientes pendientes</h2>
      </div>

      <form class="operations-filters" (ngSubmit)="load()">
        <label><span>Fecha de servicio</span><input name="serviceDate" type="date" required [(ngModel)]="serviceDate" (ngModelChange)="changeDate()" [disabled]="isLoading || !!submittingCustomerId || !!loadingCustomerId"></label>
        <button type="submit" class="primary" [disabled]="isLoading || !serviceDate || !!submittingCustomerId || !!loadingCustomerId">Consultar clientes</button>
      </form>

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
                    [disabled]="!!loadingCustomerId || !!submittingCustomerId"
                    (click)="startCapture(customer)">
                    Capturar pedido
                  </button>
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="!!loadingCustomerId || !!submittingCustomerId"
                    (click)="markNoOrder(customer)">
                    {{ isToday() ? 'No pedir hoy' : 'No pide en esta fecha' }}
                  </button>
                </div>

                @if (getTemplate(customer); as selectedTemplate) {
                  <form class="detail-panel stack" (ngSubmit)="submitCapture()">
                    <strong>Captura para {{ selectedTemplate.customerName }}</strong>
                    <p class="notice">Fecha de servicio: {{ serviceDate }}. Si el cliente cambió los kilos, abre su pedido y corrige el total con una nota.</p>
                    @if (captureError) { <p class="alert error" role="alert">{{ captureError }}</p> }
                    @if (existingOrders.length) {
                      <section class="alert warning stack" data-testid="capture-existing-orders">
                        <strong>Este cliente ya tiene pedidos para esta fecha.</strong>
                        <p>Verifica si el mensaje corresponde a uno de ellos antes de crear otro.</p>
                        @for (order of existingOrders; track order.id) {
                          <a routerLink="/admin/pedidos" [queryParams]="{ date: serviceDate, orderId: order.id }">Abrir pedido #{{ order.sequenceNumber }} y corregir</a>
                        }
                        <label class="check-row"><input type="checkbox" name="confirmSeparateDelivery" [(ngModel)]="confirmSeparateDelivery" [disabled]="!!submittingCustomerId"><span>Confirmo que es una entrega adicional independiente; crear otro pedido sujeto a revisión.</span></label>
                      </section>
                    }
                    <fieldset class="stack" [disabled]="!!submittingCustomerId">
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
                            <small>Sugerido: {{ product.suggestedQuantity }} {{ unitLabel(product.unitCode) }}</small>
                          </span>
                          <input
                            type="number"
                            min="0"
                            step="0.01"
                            inputmode="decimal"
                            [name]="'captureQuantity' + product.id"
                            [(ngModel)]="product.quantity">
                        </label>
                      }
                    </div>
                    <div class="review-actions">
                      <button type="submit" class="primary compact" [disabled]="!!loadingCustomerId || !!submittingCustomerId">
                        Guardar pedido
                      </button>
                      <button type="button" class="secondary compact" [disabled]="!!submittingCustomerId" (click)="cancelCapture()">Cancelar</button>
                    </div>
                    </fieldset>
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
  protected readonly unitLabel = unitLabel;
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private captureRequest: Subscription | null = null;
  private captureGeneration = 0;

  protected customers: PendingCustomerOrder[] = [];
  protected template: AdminOrderTemplate | null = null;
  protected internalNotes: string | null = null;
  protected isLoading = true;
  protected loadingCustomerId: string | null = null;
  protected submittingCustomerId: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected serviceDate = businessDateToday();
  protected existingOrders: AdminOrder[] = [];
  protected confirmSeparateDelivery = false;
  protected captureError: string | null = null;

  protected isToday(): boolean { return this.serviceDate === businessDateToday(); }

  protected changeDate(): void {
    this.cancelCapture();
    this.customers = [];
    this.errorMessage = null;
    this.resultMessage = null;
  }

  constructor() {
    this.load();
    this.destroyRef.onDestroy(() => this.captureRequest?.unsubscribe());
  }

  protected startCapture(customer: PendingCustomerOrder): void {
    if (this.loadingCustomerId || this.submittingCustomerId) return;
    this.cancelCapture();
    this.errorMessage = null;
    this.resultMessage = null;
    this.loadingCustomerId = customer.id;
    const generation = ++this.captureGeneration;
    const date = this.serviceDate;
    this.captureRequest = forkJoin({
      template: this.data.loadAdminOrderTemplate(customer.id),
      orders: this.data.loadTodayOrders(date)
    }).subscribe({
      next: ({ template, orders }) => {
        if (generation !== this.captureGeneration || date !== this.serviceDate) return;
        this.template = template;
        this.existingOrders = this.activeOrders(orders, customer.id);
        this.internalNotes = 'Capturado por administracion.';
        this.loadingCustomerId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (generation !== this.captureGeneration) return;
        this.errorMessage = this.formatError(error, 'No se pudo cargar la plantilla y verificar los pedidos del cliente.');
        this.loadingCustomerId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected submitCapture(): void {
    if (!this.template || this.submittingCustomerId || this.loadingCustomerId) {
      return;
    }

    if (this.template.products.some((product) => Number(product.quantity) < 0)) {
      this.captureError = 'No uses cantidades negativas.';
      this.changeDetector.detectChanges();
      return;
    }

    if (this.template.products.some((product) => Number(product.quantity) > 0 && !product.unitCode)) {
      this.captureError = 'Confirma la unidad de los productos en Catálogos antes de capturar el pedido.';
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
      this.captureError = 'Captura al menos una cantidad o usa No pedir hoy.';
      this.changeDetector.detectChanges();
      return;
    }

    this.captureError = null;
    this.resultMessage = null;
    this.submittingCustomerId = this.template.customerId;

    const template = this.template;
    const date = this.serviceDate;
    const generation = this.captureGeneration;
    const knownIds = new Set(this.existingOrders.map(order => order.id));
    // Check again immediately before writing: another capture channel may have
    // submitted while this form was open. The server remains the final guard.
    this.captureRequest = this.data.loadTodayOrders(date).subscribe({
      next: orders => {
        if (generation !== this.captureGeneration) return;
        const existing = this.activeOrders(orders, template.customerId);
        const hasNewOrder = existing.some(order => !knownIds.has(order.id));
        this.existingOrders = existing;
        if (hasNewOrder) this.confirmSeparateDelivery = false;
        if (existing.length && !this.confirmSeparateDelivery) {
          this.captureError = 'Ya existe un pedido. Ábrelo para corregirlo o confirma que se trata de otra entrega independiente.';
          this.submittingCustomerId = null;
          this.changeDetector.detectChanges();
          return;
        }
        this.captureRequest = this.data.submitAdminCustomerOrder(template.customerId, {
          serviceDate: date,
          lines,
          requestedDeliveryTime: this.emptyToNull(template.requestedDeliveryTime),
          requestedDeliveryWindowStart: this.emptyToNull(template.requestedDeliveryWindowStart),
          requestedDeliveryWindowEnd: this.emptyToNull(template.requestedDeliveryWindowEnd),
          deliveryNotes: this.emptyToNull(template.deliveryNotes),
          internalNotes: this.emptyToNull(this.internalNotes)
        }).subscribe({
          next: () => {
            if (generation !== this.captureGeneration) return;
            this.resultMessage = existing.length ? 'Pedido independiente capturado; verifica su revisión antes de producir.' : 'Pedido capturado por administracion.';
            this.cancelCapture();
            this.load(false);
          },
          error: (error: unknown) => {
            if (generation !== this.captureGeneration) return;
            this.captureError = this.formatError(error, 'No se pudo capturar el pedido.');
            this.submittingCustomerId = null;
            this.changeDetector.detectChanges();
          }
        });
      },
      error: (error: unknown) => {
        if (generation !== this.captureGeneration) return;
        this.captureError = this.formatError(error, 'No se pudieron verificar los pedidos existentes. Vuelve a intentar antes de guardar.');
        this.submittingCustomerId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  private activeOrders(orders: AdminOrder[], customerId: string): AdminOrder[] {
    return orders.filter(order => order.customerId === customerId && ['Submitted', 'Accepted', 'PendingAdminReview'].includes(order.status));
  }

  protected markNoOrder(customer: PendingCustomerOrder): void {
    if (this.submittingCustomerId || this.loadingCustomerId) return;
    this.errorMessage = null;
    this.resultMessage = null;
    this.submittingCustomerId = customer.id;

    this.data.markAdminNoOrder(customer.id, 'Cliente confirmó que no pedirá en la fecha de servicio.', this.serviceDate).subscribe({
      next: () => {
        this.resultMessage = this.isToday() ? 'No pedir hoy registrado por administracion.' : 'No pide en esta fecha registrado por administración.';
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
    this.captureGeneration++;
    this.captureRequest?.unsubscribe();
    this.template = null;
    this.internalNotes = null;
    this.existingOrders = [];
    this.confirmSeparateDelivery = false;
    this.captureError = null;
    this.loadingCustomerId = null;
  }

  protected getTemplate(customer: PendingCustomerOrder): AdminOrderTemplate | null {
    return this.template?.customerId === customer.id ? this.template : null;
  }

  protected load(showLoading = true): void {
    if (!this.serviceDate) return;
    if (showLoading) {
      this.isLoading = true;
    }

    this.data.loadPendingCustomers(this.serviceDate).subscribe({
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
