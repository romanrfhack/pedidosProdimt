import { businessDateToday, unitLabel } from './operational-labels';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EMPTY, forkJoin, Subscription, of, switchMap } from 'rxjs';
import { AdminCatalogApiService, AdminCustomerOrderingProfileApiResponse } from './admin-catalog-api.service';
import { AdminOrderCaptureComponent } from './admin-order-capture.component';
import { AdminOrder, AdminOrderTemplate, OrderDataService, PendingCustomerOrder } from './order-data.service';

@Component({
  selector: 'app-admin-pending-customers',
  imports: [FormsModule, AdminOrderCaptureComponent],
  styleUrl: './admin-pending-customers.component.css',
  templateUrl: './admin-pending-customers.component.html'
})
export class AdminPendingCustomersComponent {
  protected readonly unitLabel = unitLabel;
  private readonly data = inject(OrderDataService);
  private readonly catalog = inject(AdminCatalogApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private captureRequest: Subscription | null = null;
  private captureGeneration = 0;

  private pendingCustomers: PendingCustomerOrder[] = [];
  protected includeRestCustomers = false;
  private listGeneration = 0;
  protected template: AdminOrderTemplate | null = null;
  protected profile: AdminCustomerOrderingProfileApiResponse | null = null;
  protected isLoading = true;
  protected loadingCustomerId: string | null = null;
  protected submittingCustomerId: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected serviceDate = businessDateToday();
  protected automaticDeliveryDate = true;
  protected existingOrders: AdminOrder[] = [];

  protected get scheduledRestCount(): number { return this.pendingCustomers.filter(customer=>customer.isScheduledRest).length; }
  protected get customers(): PendingCustomerOrder[] {
    return this.pendingCustomers.filter(customer=>this.includeRestCustomers || !customer.isScheduledRest)
      .slice().sort((a,b)=>Number(a.isScheduledRest)-Number(b.isScheduledRest) || a.name.localeCompare(b.name,'es-MX'));
  }

  protected isToday(): boolean { return this.serviceDate === businessDateToday(); }

  protected changeDate(): void {
    this.listGeneration++;
    this.automaticDeliveryDate = false;
    this.cancelCapture();
    this.pendingCustomers = [];
    this.errorMessage = null;
    this.resultMessage = null;
  }

  protected changeDateMode(): void {
    this.cancelCapture();
    this.load();
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
      orders: this.data.loadTodayOrders(date),
      profile: this.catalog.getOrderingProfile(customer.id)
    }).subscribe({
      next: ({ template, orders, profile }) => {
        if (generation !== this.captureGeneration || date !== this.serviceDate) return;
        this.template = template;
        this.existingOrders = orders.filter(x => x.customerId === customer.id && ['Submitted', 'Accepted', 'PendingAdminReview'].includes(x.status));
        this.profile = profile;
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

  protected captureSaved(independent: boolean): void {
    this.resultMessage = independent ? 'Pedido independiente capturado; verifica su revisión antes de producir.' : 'Pedido capturado por administracion.';
    this.cancelCapture();
    this.load(false);
  }

  protected markNoOrder(customer: PendingCustomerOrder): void {
    if (this.submittingCustomerId || this.loadingCustomerId) return;
    this.errorMessage = null;
    this.resultMessage = null;
    this.submittingCustomerId = customer.id;

    this.data.markAdminNoOrder(customer.id, 'Cliente confirmó que no pedirá en la fecha de servicio.', this.serviceDate, this.automaticDeliveryDate).subscribe({
      next: () => {
        this.resultMessage = this.isToday() ? 'No pedir hoy registrado por administracion.' : 'No pide en esta fecha registrado por administración.';
        this.cancelCapture();
        this.load(false);
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo registrar No pedir hoy.');
        if (error instanceof HttpErrorResponse && error.error?.code === 'DeliveryDateChanged'
          && /^\d{4}-\d{2}-\d{2}$/.test(error.error?.deliveryDate)) {
          this.serviceDate = error.error.deliveryDate;
          this.load(false);
        }
        this.submittingCustomerId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected cancelCapture(): void {
    this.captureGeneration++;
    this.captureRequest?.unsubscribe();
    this.template = null;
    this.profile = null;
    this.submittingCustomerId = null;
    this.existingOrders = [];
    this.loadingCustomerId = null;
  }

  protected getTemplate(customer: PendingCustomerOrder): AdminOrderTemplate | null {
    return this.template?.customerId === customer.id ? this.template : null;
  }

  protected load(showLoading = true): void {
    if (!this.serviceDate) return;
    const generation=++this.listGeneration;
    let date=this.serviceDate;
    this.errorMessage=null;
    if (showLoading) {
      this.isLoading = true;
    }

    (this.automaticDeliveryDate ? this.catalog.getNextDeliveryDate() : of({ deliveryDate: this.serviceDate })).pipe(
      switchMap(metadata => {
        if (generation !== this.listGeneration) return EMPTY;
        date = metadata.deliveryDate;
        this.serviceDate = date;
        return this.data.loadPendingCustomers(date);
      })
    ).subscribe({
      next: (customers) => {
        if (generation!==this.listGeneration || date!==this.serviceDate) return;
        this.pendingCustomers = customers;
        this.isLoading = false;
        this.loadingCustomerId = null;
        this.submittingCustomerId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (generation!==this.listGeneration || date!==this.serviceDate) return;
        this.errorMessage = this.formatError(error, 'No se pudieron cargar los clientes pendientes.');
        this.isLoading = false;
        this.loadingCustomerId = null;
        this.submittingCustomerId = null;
        this.changeDetector.detectChanges();
      }
    });
  }



  private formatError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return fallback;
  }
}
