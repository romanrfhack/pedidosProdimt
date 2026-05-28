import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';
import { FrequentProduct, OrderDataService } from './order-data.service';
import { CustomerCurrentOrderSummaryApiResponse } from './customer-orders-api.service';

@Component({
  selector: 'app-customer-today',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="customer-today">
      <div class="page-title">
        <p class="eyebrow">{{ customerName }}</p>
        <h2>Mi pedido de hoy</h2>
      </div>

      @if (!isCustomerAuthenticated) {
        <form class="login-form" (ngSubmit)="loginCustomer()">
          <label>
            <span>Token de acceso</span>
            <input name="customerToken" autocomplete="one-time-code" [(ngModel)]="customerToken">
          </label>

          @if (tokenLoginError) {
            <div class="alert error">{{ tokenLoginError }}</div>
          }

          <button type="submit" class="primary" [disabled]="isAuthenticating">Entrar</button>
        </form>
      } @else {
        @if (isLoading) {
          <div class="notice">Cargando pedido de hoy...</div>
        }

        @if (loadError) {
          <div class="alert error">{{ loadError }}</div>
        }

        @if (!isLoading && !loadError) {
          @if (currentOrder) {
            <section class="status-panel" aria-live="polite">
              <strong>{{ currentOrderTitle(currentOrder) }}</strong>
              <small>Pedido #{{ currentOrder.sequenceNumber }} · {{ formatSubmittedAt(currentOrder.submittedAt) }}</small>
              <div class="badges">
                @if (currentOrder.isLate) {
                  <span class="badge warning">Tardio</span>
                }
                @if (currentOrder.requiresAdminReview) {
                  <span class="badge danger">Revision</span>
                }
                @if (currentOrder.sequenceNumber > 1) {
                  <span class="badge neutral">Adicional</span>
                }
              </div>
              @if (currentOrder.status !== 'NoOrder') {
                <p>Ya existe un pedido; un nuevo envio sera tratado como pedido adicional sujeto a revision.</p>
              }
            </section>
          }

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
                  [ngModel]="product.quantity"
                  (ngModelChange)="product.quantity = normalizeQuantity($event)"
                  [attr.aria-label]="'Cantidad ' + product.name">
              </label>
            }
          </div>

          @if (validationMessage) {
            <div class="alert warning">{{ validationMessage }}</div>
          }

          @if (actionMessage) {
            <div class="alert success">{{ actionMessage }}</div>
          }

          @if (actionError) {
            <div class="alert error">{{ actionError }}</div>
          }

          <div class="action-bar">
            <button type="button" class="primary" [disabled]="isSubmitting" (click)="submitOrder()">Enviar pedido</button>
            <button type="button" class="secondary" [disabled]="isSubmitting" (click)="markNoOrder()">No pedir hoy</button>
          </div>
        }
      }
    </section>
  `
})
export class CustomerTodayComponent {
  private readonly auth = inject(AuthService);
  private readonly data = inject(OrderDataService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly changeDetector = inject(ChangeDetectorRef);

  protected customerName = 'Cliente';

  protected customerToken = environment.demoCustomerToken;
  protected frequentProducts: FrequentProduct[] = [];
  protected currentOrder: CustomerCurrentOrderSummaryApiResponse | null = null;
  protected isLoading = false;
  protected isAuthenticating = false;
  protected isSubmitting = false;
  protected loadError: string | null = null;
  protected tokenLoginError: string | null = null;
  protected validationMessage: string | null = null;
  protected actionMessage: string | null = null;
  protected actionError: string | null = null;

  constructor() {
    const tokenFromQuery = this.route.snapshot.queryParamMap.get('token');

    if (tokenFromQuery) {
      this.customerToken = tokenFromQuery;
      this.loginCustomer(true);
      return;
    }

    if (this.auth.isCustomer()) {
      this.loadToday();
    }
  }

  protected get isCustomerAuthenticated(): boolean {
    return this.auth.isCustomer();
  }

  protected loginCustomer(replaceUrl = false): void {
    this.tokenLoginError = null;
    this.isAuthenticating = true;

    this.auth.loginCustomerWithToken(this.customerToken).subscribe({
      next: (session) => {
        this.customerName = session.customerName ?? 'Cliente';
        this.isAuthenticating = false;
        if (replaceUrl) {
          void this.router.navigate(['/cliente'], { replaceUrl: true });
        }
        this.loadToday();
      },
      error: (error: unknown) => {
        this.tokenLoginError = this.formatError(error, 'Token de cliente invalido.');
        this.isAuthenticating = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected submitOrder(): void {
    this.validationMessage = null;
    this.actionMessage = null;
    this.actionError = null;

    if (this.frequentProducts.some((product) => product.quantity < 0)) {
      this.validationMessage = 'No uses cantidades negativas.';
      this.changeDetector.detectChanges();
      return;
    }

    const lines = this.frequentProducts
      .filter((product) => product.quantity > 0)
      .map((product) => ({
        productId: product.id,
        quantity: product.quantity,
        notes: null
      }));

    if (lines.length === 0) {
      this.validationMessage = 'Captura al menos una cantidad o usa No pedir hoy.';
      this.changeDetector.detectChanges();
      return;
    }

    this.isSubmitting = true;
    const customerId = this.getAuthenticatedCustomerId();

    if (!customerId) {
      this.actionError = 'Inicia sesion con token de cliente.';
      this.isSubmitting = false;
      return;
    }

    this.data.submitCustomerOrder(lines, customerId).subscribe({
      next: (order) => {
        this.actionMessage = order.requiresAdminReview
          ? 'Pedido enviado y pendiente de revision administrativa.'
          : 'Pedido enviado correctamente.';
        this.loadToday(false);
      },
      error: (error: unknown) => {
        this.actionError = this.formatError(error, 'No se pudo enviar el pedido.');
        this.isSubmitting = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected markNoOrder(): void {
    this.validationMessage = null;
    this.actionMessage = null;
    this.actionError = null;
    this.isSubmitting = true;
    const customerId = this.getAuthenticatedCustomerId();

    if (!customerId) {
      this.actionError = 'Inicia sesion con token de cliente.';
      this.isSubmitting = false;
      return;
    }

    this.data.markNoOrder(customerId).subscribe({
      next: () => {
        this.actionMessage = 'No pedir hoy registrado.';
        this.loadToday(false);
      },
      error: (error: unknown) => {
        this.actionError = this.formatError(error, 'No se pudo registrar No pedir hoy.');
        this.isSubmitting = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected normalizeQuantity(value: unknown): number {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  }

  protected currentOrderTitle(order: CustomerCurrentOrderSummaryApiResponse): string {
    if (order.status === 'NoOrder') {
      return 'No pedir hoy registrado';
    }

    if (order.requiresAdminReview) {
      return 'Pedido pendiente de revision';
    }

    return 'Pedido enviado';
  }

  protected formatSubmittedAt(value: string): string {
    return new Intl.DateTimeFormat('es-MX', {
      dateStyle: 'short',
      timeStyle: 'short'
    }).format(new Date(value));
  }

  private loadToday(showLoading = true): void {
    const customerId = this.getAuthenticatedCustomerId();

    if (!customerId) {
      this.isLoading = false;
      return;
    }

    if (showLoading) {
      this.isLoading = true;
    }

    this.loadError = null;
    this.data.loadCustomerToday(customerId).subscribe((today) => {
      this.customerName = today.customerName;
      this.frequentProducts = today.frequentProducts;
      this.currentOrder = today.currentOrder;
      this.isLoading = false;
      this.isSubmitting = false;
      this.changeDetector.detectChanges();
    }, (error: unknown) => {
      this.loadError = this.formatError(error, 'No se pudo cargar el pedido de hoy.');
      this.isLoading = false;
      this.isSubmitting = false;
      this.changeDetector.detectChanges();
    });
  }

  private getAuthenticatedCustomerId(): string | null {
    return this.auth.getCustomerId();
  }

  private formatError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return fallback;
  }
}
