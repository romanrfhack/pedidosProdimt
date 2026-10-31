import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';
import {
  CustomerCaptureApiService, CustomerCapturePreview, CustomerCaptureProduct,
  CustomerCaptureReceipt, CustomerCaptureRequest, CustomerNextOrder, CustomerNoOrderRequest
} from './customer-capture-api.service';
import { unitLabel } from './operational-labels';

interface ProductDraft extends CustomerCaptureProduct { quantity: number; notes: string | null; }
type SaveAttempt = { kind: 'order'; request: CustomerCaptureRequest } | { kind: 'no-order'; request: CustomerNoOrderRequest };

@Component({
  selector: 'app-customer-today',
  imports: [FormsModule],
  templateUrl: './customer-today.component.html',
  styleUrl: './customer-today.component.css'
})
export class CustomerTodayComponent implements OnDestroy {
  protected readonly unitLabel = unitLabel;
  private readonly auth = inject(AuthService);
  private readonly api = inject(CustomerCaptureApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly requests = new Subscription();
  private previewRequest: Subscription | null = null;
  private previewGeneration = 0;
  private loadedCustomerId: string | null = null;
  private attempt: SaveAttempt | null = null;
  private manualDirty = false;
  private pendingManual = false;

  protected customerName = 'Cliente';
  protected customerToken = environment.demoCustomerToken;
  protected nextOrder: CustomerNextOrder | null = null;
  protected products: ProductDraft[] = [];
  protected mode: 'quick' | 'manual' = 'quick';
  protected text = '';
  protected deliveryNotes = '';
  protected preview: CustomerCapturePreview | null = null;
  protected isLoading = false;
  protected isAuthenticating = false;
  protected isSubmitting = false;
  protected interpreting = false;
  protected uncertain = false;
  protected noOrderConfirmation = false;
  protected loadError: string | null = null;
  protected tokenLoginError: string | null = null;
  protected error: string | null = null;
  protected notice: string | null = null;
  protected actionMessage: string | null = null;

  constructor() {
    const fragmentToken = new URLSearchParams(this.route.snapshot.fragment ?? '').get('token');
    const token = fragmentToken || this.route.snapshot.queryParamMap.get('token');
    if (token) {
      this.customerToken = token;
      // An explicit link selects its own identity; a failed exchange must not retain another customer.
      this.auth.logout();
      // Remove both URL forms before exchanging the credential, including failed logins.
      void this.router.navigate(['/cliente'], { replaceUrl: true }).then(() => this.loginCustomer());
    } else if (this.auth.isCustomer()) {
      this.loadNext();
    }
  }

  ngOnDestroy(): void { this.stopPreview(); this.requests.unsubscribe(); }

  protected get isCustomerAuthenticated(): boolean { return this.auth.isCustomer(); }
  protected get locked(): boolean { return this.isLoading || this.isSubmitting || this.uncertain; }
  protected get linkExpiresAt(): string | null { return this.auth.session()?.accessLinkExpiresAt ?? null; }
  protected get hasActiveOrder(): boolean { return this.nextOrder?.hasActiveOrder === true; }
  protected get sequence(): ProductDraft[] {
    return this.products.slice().sort((a, b) => (a.interpretationPosition ?? 999) - (b.interpretationPosition ?? 999));
  }
  protected get canSave(): boolean {
    if (this.locked || this.interpreting || this.hasActiveOrder || !this.nextOrder) return false;
    return this.mode === 'quick'
      ? !!this.preview && this.preview.text === this.text.trim() && this.nextOrder.isProfileApproved
      : this.products.some(x => Number(x.quantity) > 0) && this.products.every(x => this.validQuantity(x.quantity) && (!x.quantity || !!x.unitCode));
  }
  protected get deliveryLabel(): string {
    const next = this.nextOrder;
    if (next?.preferredDeliveryTime) return next.preferredDeliveryTime.slice(0, 5);
    const start = next?.preferredDeliveryWindowStart?.slice(0, 5);
    const end = next?.preferredDeliveryWindowEnd?.slice(0, 5);
    return start && end ? `${start}–${end}` : start ? `Desde ${start}` : end ? `Antes de ${end}` : '';
  }

  protected loginCustomer(): void {
    if (this.isAuthenticating || !this.customerToken.trim()) return;
    this.tokenLoginError = null; this.isAuthenticating = true;
    this.requests.add(this.auth.loginCustomerWithToken(this.customerToken.trim()).subscribe({
      next: session => {
        this.customerToken = ''; this.customerName = session.customerName ?? 'Cliente';
        this.isAuthenticating = false;
        this.loadNext(this.loadedCustomerId === session.customerId);
      },
      error: error => {
        this.tokenLoginError = this.message(error, 'El enlace o token no es válido, venció o fue revocado.');
        this.isAuthenticating = false; this.changeDetector.detectChanges();
      }
    }));
  }

  protected textChanged(): void {
    this.pendingManual = false;
    this.stopPreview(); this.error = null; this.notice = null; this.attempt = null; this.actionMessage = null;
    this.manualDirty = false;
  }

  protected interpret(): void {
    const customerId = this.auth.getCustomerId();
    if (!customerId || this.mode !== 'quick' || this.locked || this.hasActiveOrder || !this.nextOrder) return;
    if (this.preview?.text === this.text.trim()) return;
    this.stopPreview();
    if (!this.text.trim()) return;
    this.interpreting = true; this.error = null;
    const generation = this.previewGeneration;
    this.previewRequest = this.api.preview(customerId, this.text.trim(), this.nextOrder.deliveryDate).subscribe({
      next: result => {
        if (generation !== this.previewGeneration || this.mode !== 'quick') return;
        this.preview = result; this.interpreting = false;
        const quantities = new Map(result.lines.map(x => [x.productId, x.quantity]));
        this.products.forEach(x => x.quantity = quantities.get(x.productId) ?? 0);
        if (this.pendingManual) { this.mode = 'manual'; this.pendingManual = false; }
        this.manualDirty = false; this.changeDetector.detectChanges();
      },
      error: error => {
        if (generation !== this.previewGeneration) return;
        if (this.pendingManual) { this.mode = 'manual'; this.pendingManual = false;
          this.notice = 'La entrada sin desglosar no se aplicó. Revisa las cantidades manuales; tu texto se conserva en captura rápida.'; }
        this.interpreting = false; this.handleDefiniteError(error, 'No pudimos desglosar el pedido. Revisa las cantidades o usa captura manual.');
        this.changeDetector.detectChanges();
      }
    });
  }

  protected changeMode(mode: 'quick' | 'manual'): void {
    if (this.locked || mode === this.mode) return;
    if (mode === 'manual' && this.text.trim() && !this.preview && this.nextOrder?.isProfileApproved) {
      this.pendingManual = true;
      if (!this.interpreting) this.interpret();
      return;
    }
    if (mode === 'quick' && this.manualDirty) {
      const formatted = this.formatQuickDraft();
      if (formatted === null) {
        this.error = 'La configuración no permite representar estas cantidades en captura rápida. Puedes conservarlas y confirmar en captura manual.';
        return;
      }
      this.text = formatted;
    }
    const unresolved = this.mode === 'quick' && !!this.text.trim() && !this.preview;
    this.stopPreview(); this.pendingManual = false; this.mode = mode; this.error = null; this.attempt = null; this.noOrderConfirmation = false;
    this.notice = unresolved && mode === 'manual' ? 'La entrada sin desglosar no se aplicó. Revisa las cantidades manuales; tu texto se conserva en captura rápida.' : null;
    if (mode === 'quick' && this.text.trim()) this.interpret();
  }

  protected manualChanged(): void {
    this.manualDirty = true; this.stopPreview(); this.attempt = null; this.error = null; this.notice = null; this.actionMessage = null;
  }

  protected save(): void {
    const next = this.nextOrder;
    if (!this.canSave || !next) return;
    this.error = null; this.notice = null; this.noOrderConfirmation = false;
    const request: CustomerCaptureRequest = {
      operationId: crypto.randomUUID(), expectedDeliveryDate: next.deliveryDate,
      expectedProfileVersion: this.mode === 'quick' ? this.preview!.profileVersion : next.profileVersion,
      ...(this.mode === 'quick' ? { text: this.text.trim(), previewFingerprint: this.preview!.fingerprint }
        : { lines: this.products.filter(x => Number(x.quantity) > 0).map(x => ({ productId: x.productId,
            quantity: Number(x.quantity), expectedUnitCode: x.unitCode, notes: x.notes })) }),
      deliveryNotes: this.deliveryNotes.trim() || null
    };
    this.attempt = { kind: 'order', request }; this.sendAttempt();
  }

  protected confirmNoOrder(): void {
    if (this.locked || this.hasActiveOrder || !this.noOrderConfirmation || !this.nextOrder) return;
    this.attempt = { kind: 'no-order', request: { operationId: crypto.randomUUID(), expectedDeliveryDate: this.nextOrder.deliveryDate } };
    this.noOrderConfirmation = false; this.sendAttempt();
  }

  protected retrySave(): void { if (this.attempt && this.uncertain && !this.isSubmitting) this.sendAttempt(); }

  protected refresh(): void { if (!this.locked) this.loadNext(true); }

  protected formatDeliveryDate(value: string): string {
    return new Intl.DateTimeFormat('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'America/Mexico_City' })
      .format(new Date(`${value}T12:00:00Z`));
  }

  protected formatTimestamp(value: string): string {
    return new Intl.DateTimeFormat('es-MX', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'America/Mexico_City' }).format(new Date(value));
  }

  protected currentOrderTitle(): string {
    const status = this.nextOrder?.currentOrder?.status;
    if (status === 'NoOrder') return 'Confirmaste que no pedirás para esta entrega';
    if (status === 'PendingAdminReview') return 'Pedido recibido y pendiente de revisión';
    if (status === 'Rejected') return 'Pedido rechazado';
    if (status === 'Cancelled') return 'Pedido cancelado';
    return 'Pedido registrado';
  }

  private sendAttempt(): void {
    const attempt = this.attempt;
    const customerId = this.auth.getCustomerId();
    if (!attempt || !customerId) return;
    this.isSubmitting = true; this.error = null;
    const call = attempt.kind === 'order' ? this.api.submit(customerId, attempt.request) : this.api.markNoOrder(customerId, attempt.request);
    this.requests.add(call.subscribe({
      next: (receipt: CustomerCaptureReceipt) => {
        this.isSubmitting = false; this.uncertain = false; this.attempt = null;
        this.actionMessage = attempt.kind === 'no-order' ? 'Se registró que no pedirás para esta entrega.'
          : `Pedido confirmado para el ${this.formatDeliveryDate(receipt.order.orderDate)}${receipt.order.requiresAdminReview ? '; pendiente de revisión.' : '.'}`;
        this.loadNext(false);
      },
      error: error => {
        this.isSubmitting = false;
        const definite = error instanceof HttpErrorResponse && ([400, 401, 403, 404, 409, 429].includes(error.status)
          || error.error?.code === 'PersistenceNotSupported');
        this.uncertain = !definite;
        if (definite) { this.attempt = null; this.handleDefiniteError(error, 'No pudimos confirmar el pedido.'); }
        else this.error = this.message(error, 'No pudimos confirmar si el pedido quedó guardado.');
        this.changeDetector.detectChanges();
      }
    }));
  }

  private loadNext(preserveDraft = false): void {
    const customerId = this.auth.getCustomerId();
    if (!customerId) return;
    this.stopPreview(); this.pendingManual = false; this.isLoading = true; this.loadError = null;
    this.requests.add(this.api.getNext(customerId).subscribe({
      next: next => {
        const previous = new Map(this.products.map(x => [x.productId, x]));
        const changedUnits = preserveDraft && next.products.some(x => previous.has(x.productId)
          && previous.get(x.productId)!.unitCode !== x.unitCode && Number(previous.get(x.productId)!.quantity) > 0);
        this.products = next.products.slice().sort((a, b) => a.sortOrder - b.sortOrder).map(x => ({ ...x,
          quantity: preserveDraft && previous.get(x.productId)?.unitCode === x.unitCode ? previous.get(x.productId)!.quantity : 0,
          notes: preserveDraft ? previous.get(x.productId)?.notes ?? null : null }));
        this.nextOrder = next; this.customerName = next.customerName; this.loadedCustomerId = customerId;
        if (changedUnits) this.notice = 'Cambió la unidad de un producto. Revisa el desglose antes de confirmar; las cantidades manuales de unidades cambiadas se dejaron en cero.';
        if (!preserveDraft) { this.text = ''; this.deliveryNotes = ''; this.manualDirty = false; this.mode = next.isProfileApproved ? 'quick' : 'manual'; }
        this.isLoading = false; this.noOrderConfirmation = false;
        if (preserveDraft && this.mode === 'quick' && this.text.trim() && next.isProfileApproved && !next.hasActiveOrder) this.interpret();
        this.changeDetector.detectChanges();
      },
      error: error => {
        this.loadError = this.message(error, 'No pudimos consultar tu próximo pedido.'); this.isLoading = false;
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.tokenLoginError = 'El acceso venció o fue revocado. Solicita un enlace vigente para continuar.'; this.auth.logout();
        }
        this.changeDetector.detectChanges();
      }
    }));
  }

  private handleDefiniteError(error: unknown, fallback: string): void {
    this.error = this.message(error, fallback);
    if (!(error instanceof HttpErrorResponse)) return;
    if (error.status === 401) {
      this.tokenLoginError = 'El acceso venció o fue revocado. Solicita un enlace vigente para continuar.';
      this.auth.logout();
    } else if (error.status === 409) {
      if (error.error?.code === 'DeliveryDateChanged' && error.error.deliveryDate) {
        this.notice = `El horario de corte cambió la entrega al ${this.formatDeliveryDate(error.error.deliveryDate)}. Conservamos tus cantidades; revisa la fecha y confirma de nuevo.`;
      }
      this.loadNext(true);
    }
  }

  private stopPreview(): void { this.previewGeneration++; this.previewRequest?.unsubscribe(); this.preview = null; this.interpreting = false; }

  private validQuantity(value: unknown): boolean {
    const number = Number(value);
    return Number.isFinite(number) && number >= 0 && number <= 9999999999999999 && Math.abs(number * 100 - Math.round(number * 100)) < 0.000001;
  }

  private formatQuickDraft(): string | null {
    const items = this.sequence;
    if (items.some((x, index) => x.interpretationPosition !== index + 1 || !x.unitCode || !this.validQuantity(x.quantity))) return null;
    const quantities = items.map(x => Number(x.quantity));
    while (quantities.length && quantities[quantities.length - 1] === 0) quantities.pop();
    const text = quantities.join('+');
    return text.length <= 240 ? text : null;
  }

  private message(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse ? error.error?.error || error.error?.detail || error.error?.title || fallback : fallback;
  }
}
