import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, Input, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { environment } from '../environments/environment';
import { DeliveryRoute } from './delivery-api.service';

interface RouteCustomer {
  customerId: string;
  customerName: string;
  routeCode: string | null;
  routeStopOrder: number | null;
  updatedAt: string;
  isActive: boolean;
}
interface StopDraft { value: number | null; updatedAt: string; }

@Component({
  selector: 'app-delivery-route-customers',
  imports: [FormsModule],
  templateUrl: './delivery-route-customers.component.html',
  styles: [`
    :host{display:block}
    h3,p{margin:0}
    .route-customers{overflow-wrap:anywhere}
    .route-customers label{display:grid;gap:5px;font-size:.9rem}
    .route-customers input,.route-customers select{width:100%;min-width:0;font-size:16px}
    .route-member{border:1px solid #dfe5e8;border-radius:8px;padding:10px}
    .route-member-actions{display:flex;gap:8px;flex-wrap:wrap}
    .route-customer-notice{color:#566871;font-size:.86rem}
  `]
})
export class DeliveryRouteCustomersComponent implements OnInit {
  @Input({ required: true }) route!: DeliveryRoute;
  @Input() blocked = false;
  private readonly http = inject(HttpClient);
  private readonly destroy = inject(DestroyRef);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly baseUrl = environment.apiBaseUrl + '/api/admin/customers';
  private customers: RouteCustomer[] = [];
  private readonly drafts = new Map<string, StopDraft>();
  protected selectedId = '';
  protected loading = false;
  private readonly saving = signal(false);
  protected loaded = false;
  protected needsRefresh = false;
  protected error: string | null = null;
  protected notice: string | null = null;

  ngOnInit(): void { this.refresh(); }

  get isSaving(): boolean { return this.saving(); }
  get hasDrafts(): boolean { return !!this.selectedId || this.drafts.size > 0; }
  protected get members(): RouteCustomer[] {
    return this.customers.filter(customer => this.belongs(customer))
      .sort((a, b) => (a.routeStopOrder ?? Number.MAX_SAFE_INTEGER) - (b.routeStopOrder ?? Number.MAX_SAFE_INTEGER)
        || a.customerName.localeCompare(b.customerName, 'es'));
  }
  protected get candidates(): RouteCustomer[] {
    return this.customers.filter(customer => customer.isActive && !this.belongs(customer))
      .sort((a, b) => a.customerName.localeCompare(b.customerName, 'es'));
  }
  protected get selected(): RouteCustomer | undefined {
    return this.candidates.find(customer => customer.customerId === this.selectedId);
  }
  protected get busy(): boolean { return this.saving() || this.loading || this.blocked; }

  protected refresh(): void {
    if (this.busy) return;
    this.loading = true;
    this.error = null;
    this.notice = null;
    this.http.get<RouteCustomer[]>(this.baseUrl + '/route-assignments').pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: customers => {
        this.customers = customers;
        this.loaded = true;
        this.loading = false;
        this.needsRefresh = false;
        for (const [id, draft] of this.drafts) {
          const customer = this.customers.find(row => row.customerId === id);
          if (!customer || !this.belongs(customer)) {
            this.drafts.delete(id);
            this.notice = 'Cambió la ruta de un cliente con un orden pendiente. Revisa la lista actual.';
          } else if (customer.routeStopOrder === draft.value) this.drafts.delete(id);
        }
        if (!this.selected) this.selectedId = '';
        this.detector.detectChanges();
      },
      error: error => {
        this.loading = false;
        this.needsRefresh = true;
        this.error = this.message(error, 'No se pudieron consultar los clientes.');
        this.detector.detectChanges();
      }
    });
  }

  protected stop(customer: RouteCustomer): number | null {
    const draft = this.drafts.get(customer.customerId);
    return draft ? draft.value : customer.routeStopOrder;
  }
  protected changeStop(customer: RouteCustomer, value: number | null): void {
    if (value === customer.routeStopOrder) this.discardStop(customer);
    else this.drafts.set(customer.customerId, { value, updatedAt: customer.updatedAt });
  }
  protected discardStop(customer: RouteCustomer): void { this.drafts.delete(customer.customerId); }
  protected hasStopDraft(customer: RouteCustomer): boolean { return this.drafts.has(customer.customerId); }
  protected stopConflict(customer: RouteCustomer): boolean {
    const draft = this.drafts.get(customer.customerId);
    return !!draft && draft.updatedAt !== customer.updatedAt;
  }
  protected canSaveStop(customer: RouteCustomer): boolean {
    const draft = this.drafts.get(customer.customerId);
    return !!draft && !this.stopConflict(customer)
      && (draft.value === null || (Number.isInteger(draft.value) && draft.value > 0 && draft.value <= 2147483647));
  }
  protected assignSelected(): void {
    const customer = this.selected;
    if (customer && this.route.isActive) this.update(customer, this.route.code, null, 'Cliente asignado a la ruta.');
  }
  protected saveStop(customer: RouteCustomer): void {
    if (this.belongs(customer) && this.canSaveStop(customer)) {
      this.update(customer, this.route.code, this.stop(customer), 'Orden de visita guardado.');
    }
  }
  protected remove(customer: RouteCustomer): void {
    if (this.belongs(customer)) this.update(customer, null, null, 'Cliente retirado de la ruta.');
  }

  private belongs(customer: RouteCustomer): boolean {
    return customer.routeCode?.trim().toUpperCase() === this.route.code.trim().toUpperCase();
  }
  private update(customer: RouteCustomer, routeCode: string | null, routeStopOrder: number | null, notice: string): void {
    if (this.busy || this.needsRefresh) return;
    this.saving.set(true);
    this.error = null;
    this.notice = null;
    this.http.put<RouteCustomer>(this.baseUrl + '/' + encodeURIComponent(customer.customerId) + '/route',
      { routeCode, routeStopOrder, expectedUpdatedAt: customer.updatedAt }).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: updated => {
        this.customers = this.customers.map(row => row.customerId === updated.customerId ? updated : row);
        this.discardStop(customer);
        if (this.selectedId === customer.customerId) this.selectedId = '';
        this.notice = notice;
        this.saving.set(false);
        this.detector.detectChanges();
      },
      error: error => {
        this.saving.set(false);
        this.needsRefresh = !(error instanceof HttpErrorResponse) || error.status === 409 || error.status === 0 || error.status >= 500;
        this.error = this.message(error, 'No se pudo comprobar el cambio.');
        if (this.needsRefresh) this.error += ' Actualiza los clientes antes de volver a guardar.';
        this.detector.detectChanges();
      }
    });
  }
  private message(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && typeof error.error?.error === 'string' ? error.error.error : fallback;
  }
}
