import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, EventEmitter, inject, Input, OnDestroy, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin, Subscription } from 'rxjs';
import { AdminCatalogApiService, AdminCustomerOrderingProfileApiResponse } from './admin-catalog-api.service';
import { AdminOrder, AdminOrderTemplate, OrderDataService } from './order-data.service';
import { CaptureOrderRequest, CapturePreview, OrderCaptureApiService } from './order-capture-api.service';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-admin-order-capture', imports: [FormsModule, RouterLink],
  templateUrl: './admin-order-capture.component.html', styleUrl: './admin-order-capture.component.css'
})
export class AdminOrderCaptureComponent implements OnInit, OnDestroy {
  @Input({ required: true }) template!: AdminOrderTemplate;
  @Input({ required: true }) initialProfile!: AdminCustomerOrderingProfileApiResponse;
  @Input({ required: true }) serviceDate!: string;
  @Input() existingOrders: AdminOrder[] = [];
  @Output() saved = new EventEmitter<boolean>();
  @Output() dismissed = new EventEmitter<void>();
  @Output() busy = new EventEmitter<boolean>();
  private readonly api = inject(OrderCaptureApiService);
  private readonly catalog = inject(AdminCatalogApiService);
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly requests = new Subscription();
  private previewRequest: Subscription | null = null;
  private previewGeneration = 0;
  private attempt: CaptureOrderRequest | null = null;
  protected config!: AdminOrderTemplate;
  protected profile!: AdminCustomerOrderingProfileApiResponse;
  protected readonly unitLabel = unitLabel;
  protected mode: 'quick' | 'manual' = 'quick';
  protected text = '';
  protected preview: CapturePreview | null = null;
  protected error: string | null = null;
  protected notice: string | null = null;
  protected interpreting = false;
  protected saving = false;
  protected confirming = false;
  protected refreshing = false;
  protected uncertain = false;
  protected separateDelivery = false;
  protected deliveryNotes = '';
  protected internalNotes = '';

  ngOnInit(): void {
    this.config = { ...this.template, products: this.template.products.map(x => ({ ...x, quantity: 0 })) };
    this.profile = this.initialProfile;
    this.existingOrders = [...this.existingOrders];
    this.deliveryNotes = this.template.deliveryNotes ?? '';
  }
  ngOnDestroy(): void { this.previewGeneration++; this.previewRequest?.unsubscribe(); this.requests.unsubscribe(); }
  protected get locked(): boolean { return this.saving || this.confirming || this.refreshing || this.uncertain; }
  protected get sequence() {
    return this.profile.items.filter(x => x.isActive && x.productIsActive !== false).slice()
      .sort((a, b) => (a.interpretationPosition ?? 999) - (b.interpretationPosition ?? 999));
  }
  protected get deliveryLabel(): string {
    const source = this.preview;
    const time = (source ? source.preferredDeliveryTime : this.config.requestedDeliveryTime)?.slice(0, 5);
    if (time) return time;
    const start = (source ? source.preferredDeliveryWindowStart : this.config.requestedDeliveryWindowStart)?.slice(0, 5);
    const end = (source ? source.preferredDeliveryWindowEnd : this.config.requestedDeliveryWindowEnd)?.slice(0, 5);
    return start && end ? `${start}–${end}` : start ? `Desde ${start}` : end ? `Antes de ${end}` : 'Sin horario configurado';
  }
  protected get canSave(): boolean {
    if (this.locked || this.interpreting) return false;
    return this.mode === 'quick'
      ? !!this.preview && this.preview.text === this.text.trim() && this.preview.profile.isOrderProfileApproved
      : this.config.products.some(x => x.quantity > 0) && this.config.products.every(x =>
          Number.isFinite(Number(x.quantity)) && Number(x.quantity) >= 0 && (!x.quantity || !!x.unitCode));
  }
  protected textChanged(): void {
    this.stopPreview(); this.error = null; this.notice = null; this.attempt = null;
  }
  private stopPreview(): void { this.previewGeneration++; this.previewRequest?.unsubscribe(); this.preview = null; this.interpreting = false; }
  protected interpret(): void {
    if (this.mode !== 'quick' || this.locked) return;
    if (this.preview?.text === this.text.trim()) return;
    this.stopPreview();
    if (!this.text.trim()) { this.error = null; return; }
    const generation = this.previewGeneration;
    this.interpreting = true; this.error = null;
    this.previewRequest = this.api.preview(this.config.customerId, this.text.trim(), this.serviceDate).subscribe({
      next: result => {
        if (generation !== this.previewGeneration || this.mode !== 'quick') return;
        this.preview = result; this.profile = result.profile; this.interpreting = false;
        const quantities = new Map(result.lines.map(x => [x.productId, x.quantity]));
        const previous = new Map(this.config.products.map(x => [x.id, x]));
        this.config.products = result.profile.items.filter(x => x.isActive && x.productIsActive !== false).slice()
          .sort((a, b) => a.sortOrder - b.sortOrder).map(x => ({ id: x.productId, name: x.productName,
            description: previous.get(x.productId)?.description ?? null, suggestedQuantity: x.defaultQuantity ?? 0,
            unitCode: x.unitCode ?? null, quantity: quantities.get(x.productId) ?? 0, notes: null }));
        this.changeDetector.detectChanges();
      },
      error: error => {
        if (generation !== this.previewGeneration) return;
        this.error = this.message(error, 'No pudimos desglosar el pedido. Revisa la entrada o usa captura manual.');
        this.interpreting = false; this.changeDetector.detectChanges();
      }
    });
  }
  protected changeMode(mode: 'quick' | 'manual'): void {
    if (this.locked || mode === this.mode) return;
    const unresolved = this.mode === 'quick' && !!this.text.trim() && !this.preview;
    this.stopPreview(); this.mode = mode; this.error = null; this.attempt = null;
    this.notice = unresolved && mode === 'manual' ? 'Revisa las cantidades manuales. La entrada sin resolver no se aplicó.' : null;
    if (mode === 'quick' && this.text.trim()) this.interpret();
  }
  protected confirmProfile(): void {
    const preview = this.preview;
    if (!preview || this.locked || preview.profile.isOrderProfileApproved) return;
    this.confirming = true; this.busy.emit(true); this.error = null;
    this.requests.add(this.catalog.updateFrequentProducts(this.config.customerId, {
      approveForInterpretation: true, expectedProfileVersion: preview.profile.orderingProfileVersion,
      items: preview.profile.items.map(x => ({ productId: x.productId, expectedUnitCode: x.unitCode ?? null,
        defaultQuantity: x.defaultQuantity, sortOrder: x.sortOrder, isActive: x.isActive,
        preferredMachineId: x.preferredMachineId, interpretationPosition: x.interpretationPosition, captureAlias: x.captureAlias ?? '' }))
    }).subscribe({
      next: () => { this.confirming = false; this.busy.emit(false); this.stopPreview(); this.interpret(); this.changeDetector.detectChanges(); },
      error: error => { this.confirming = false; this.busy.emit(false); this.error = this.message(error, 'No pudimos confirmar el perfil.'); this.stopPreview(); this.changeDetector.detectChanges(); }
    }));
  }
  protected refreshConfiguration(): void {
    if (this.locked) return;
    this.stopPreview(); this.refreshing = true; this.error = null; this.attempt = null; this.busy.emit(true);
    this.requests.add(forkJoin({ template: this.data.loadAdminOrderTemplate(this.config.customerId),
      profile: this.catalog.getOrderingProfile(this.config.customerId) }).subscribe({
      next: result => {
        const old = new Map(this.config.products.map(x => [x.id, x]));
        this.config = { ...result.template, products: result.template.products.map(x => ({ ...x,
          quantity: old.get(x.id)?.unitCode === x.unitCode ? old.get(x.id)?.quantity ?? 0 : 0 })) };
        this.profile = result.profile; this.refreshing = false; this.busy.emit(false);
        this.notice = 'Configuración actualizada. Revisa las cantidades; si cambió una unidad, su cantidad se dejó en cero.';
        if (this.mode === 'quick' && this.text.trim()) this.interpret();
        this.changeDetector.detectChanges();
      },
      error: error => { this.refreshing = false; this.busy.emit(false); this.error = this.message(error, 'No pudimos actualizar la configuración.'); this.changeDetector.detectChanges(); }
    }));
  }
  protected save(): void {
    if (!this.canSave) return;
    this.error = null; this.saving = true; this.busy.emit(true);
    const known = new Set(this.existingOrders.map(x => x.id));
    this.requests.add(this.data.loadTodayOrders(this.serviceDate).subscribe({
      next: orders => {
        const existing = orders.filter(x => x.customerId === this.config.customerId && ['Submitted','Accepted','PendingAdminReview'].includes(x.status));
        if (existing.some(x => !known.has(x.id))) this.separateDelivery = false;
        this.existingOrders = existing;
        if (existing.length && !this.separateDelivery) {
          this.saving = false; this.busy.emit(false);
          this.error = 'Ya existe un pedido. Ábrelo para corregirlo o confirma que se trata de otra entrega independiente.';
          this.changeDetector.detectChanges(); return;
        }
        this.attempt = {
          operationId: crypto.randomUUID(), serviceDate: this.serviceDate,
          expectedProfileVersion: this.mode === 'quick' ? this.preview!.profile.orderingProfileVersion : this.profile.orderingProfileVersion,
          ...(this.mode === 'quick' ? { text: this.text.trim(), previewFingerprint: this.preview!.fingerprint }
            : { lines: this.config.products.filter(x => Number(x.quantity) > 0).map(x => ({ productId: x.id,
                quantity: Number(x.quantity), expectedUnitCode: x.unitCode, notes: x.notes })) }),
          confirmSeparateDelivery: this.separateDelivery,
          deliveryNotes: this.deliveryNotes === (this.template.deliveryNotes ?? '') ? null : this.deliveryNotes,
          internalNotes: this.internalNotes.trim() || null
        };
        this.sendAttempt();
      },
      error: error => { this.saving = false; this.busy.emit(false); this.error = this.message(error, 'No se pudieron verificar los pedidos existentes. Vuelve a intentar antes de guardar.'); this.changeDetector.detectChanges(); }
    }));
  }
  protected retrySave(): void { if (this.attempt && this.uncertain && !this.saving) { this.saving = true; this.busy.emit(true); this.sendAttempt(); } }
  private sendAttempt(): void {
    if (!this.attempt) return;
    this.requests.add(this.api.submit(this.config.customerId, this.attempt).subscribe({
      next: () => { this.saving = false; this.uncertain = false; this.busy.emit(false); this.saved.emit(this.attempt!.confirmSeparateDelivery); },
      error: error => {
        this.saving = false;
        const definite = error instanceof HttpErrorResponse && ([400,401,403,404,409].includes(error.status)
          || error.error?.code === 'PersistenceNotSupported');
        this.uncertain = !definite;
        if (definite) { this.attempt = null; if (error.status === 409) this.stopPreview(); }
        this.error = this.message(error, 'No pudimos confirmar el guardado.');
        this.busy.emit(this.uncertain); this.changeDetector.detectChanges();
      }
    }));
  }
  protected cancel(): void { if (!this.locked) this.dismissed.emit(); }
  private message(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse ? error.error?.error || error.error?.detail || error.error?.title || fallback : fallback;
  }
}
