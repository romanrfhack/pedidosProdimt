import { ChangeDetectorRef, Component, ElementRef, EventEmitter, Input, OnChanges, Output, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { CommercialCatalogApiService, CustomerProductPrice, PriceChange, priceDate, priceRecordedAt, priceText } from './commercial-catalog-api.service';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-customer-product-price',
  imports: [FormsModule],
  styleUrl: './customer-product-price.component.css',
  templateUrl: './customer-product-price.component.html'
})
export class CustomerProductPriceComponent implements OnChanges {
  @Input({ required: true }) customerId = '';
  @Input({ required: true }) productId = '';
  @Input({ required: true }) productName = '';
  @Input() unitCode: string | null = null;
  @Input() price: CustomerProductPrice | null = null;
  @Input() loaded = false;
  @Input() configured = false;
  @Input() active = true;
  @Output() changed = new EventEmitter<void>();
  @ViewChild('dialog') private dialog?: ElementRef<HTMLDialogElement>;
  private readonly api = inject(CommercialCatalogApiService);
  private readonly detector = inject(ChangeDetectorRef);
  protected readonly priceText = priceText;
  protected readonly priceDate = priceDate;
  protected readonly recordedAt = priceRecordedAt;
  protected readonly unitLabel = unitLabel;
  protected amount: number | null = null;
  protected effectiveFrom = '';
  protected reason = '';
  protected clearPrice = false;
  protected saving = false;
  protected error: string | null = null;
  protected message: string | null = null;
  protected attempt: PriceChange | null = null;

  ngOnChanges(): void {
    this.attempt = this.restore();
    if (this.attempt && this.price?.history.some(x => x.operationId === this.attempt?.operationId)) {
      this.forget(); this.message = 'Cambio anterior confirmado en el historial.';
    }
  }

  protected get available(): boolean {
    return this.loaded && this.configured && this.active && !!this.unitCode && (!this.price || this.price.unitCode === this.unitCode);
  }

  protected open(): void {
    this.error = null;
    this.message = null;
    const draft = this.attempt;
    this.amount = draft?.unitPrice ?? this.price?.unitPrice ?? null;
    this.clearPrice = !!draft && draft.unitPrice === null;
    this.effectiveFrom = draft?.effectiveFrom ?? this.price?.asOfDate ?? '';
    this.reason = draft?.reason ?? '';
    this.dialog?.nativeElement.showModal();
  }

  protected close(event?: Event): void {
    if (this.saving) { event?.preventDefault(); return; }
    if (!event) this.dialog?.nativeElement.close();
  }

  protected save(): void {
    if (!this.available || this.saving || this.attempt) return;
    if (!this.effectiveFrom || !this.reason.trim() || (!this.clearPrice && (this.amount === null || this.amount < 0))) {
      this.error = 'Captura precio, fecha de vigencia y motivo.'; return;
    }
    this.attempt = { unitPrice: this.clearPrice ? null : Number(this.amount), effectiveFrom: this.effectiveFrom,
      reason: this.reason.trim(), expectedVersion: this.price?.version ?? 0,
      expectedUnitCode: this.unitCode!, operationId: crypto.randomUUID() };
    try { sessionStorage.setItem(this.key, JSON.stringify(this.attempt)); } catch { /* Keep the attempt in memory if storage is unavailable. */ }
    this.send();
  }

  protected retry(): void { if (this.attempt && !this.saving) this.send(); }
  protected refresh(): void { this.changed.emit(); }

  private send(): void {
    const request = this.attempt!;
    this.saving = true; this.error = null; this.message = null;
    this.api.changePrice(this.customerId, this.productId, request).subscribe({
      next: value => {
        this.price = value; this.saving = false; this.forget();
        this.message = value.unchanged ? 'Ese precio, vigencia y motivo ya estaban guardados. Se conserva el registro existente.' : request.effectiveFrom > value.asOfDate
          ? 'Precio programado. El precio de hoy se conserva hasta esa fecha.'
          : 'Cambio guardado con fecha, usuario y motivo. Los pedidos existentes conservan su precio.';
        this.changed.emit(); this.detector.detectChanges();
      },
      error: (e: unknown) => {
        this.saving = false;
        if (e instanceof HttpErrorResponse && [400, 409, 404].includes(e.status)) {
          this.forget(); this.error = e.error?.error ?? 'No se pudo aplicar el cambio. Actualiza y revisa el historial.';
          if (e.status === 409) this.changed.emit();
        } else this.error = 'No pudimos confirmar el cambio. Reintenta con la misma referencia para consultar o guardar una sola revisión.';
        this.detector.detectChanges();
      }
    });
  }

  private get key(): string { return `prodimt-price:${this.customerId}:${this.productId}`; }
  private forget(): void {
    this.attempt = null;
    try { sessionStorage.removeItem(this.key); } catch { /* The server still owns the durable operation. */ }
  }
  private restore(): PriceChange | null {
    try {
      const raw: unknown = JSON.parse(sessionStorage.getItem(this.key) ?? 'null');
      if (!raw || typeof raw !== 'object') return null;
      const value = raw as PriceChange;
      return typeof value.operationId === 'string' && /^[0-9a-f-]{36}$/i.test(value.operationId)
        && typeof value.reason === 'string' && typeof value.effectiveFrom === 'string'
        && Number.isInteger(value.expectedVersion) && typeof value.expectedUnitCode === 'string'
        && (value.unitPrice === null || typeof value.unitPrice === 'number') ? value : null;
    } catch { return this.attempt; }
  }
}
