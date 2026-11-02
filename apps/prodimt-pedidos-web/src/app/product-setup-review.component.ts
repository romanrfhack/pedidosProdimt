import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, EventEmitter, inject, Input, OnChanges, OnDestroy, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin, Subscription } from 'rxjs';
import { CompleteProductSetup, ProductSetup, ProductSetupApiService, ProductSetupPreview } from './product-setup-api.service';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-product-setup-review', imports: [FormsModule, RouterLink],
  templateUrl: './product-setup-review.component.html', styleUrl: './product-setup-review.component.css'
})
export class ProductSetupReviewComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) requestId = '';
  @Input() customerId: string | null = null;
  @Input() refreshKey = 0;
  @Input() allowAdd = false;
  @Input() addDisabled = false;
  @Output() addRequested = new EventEmitter<ProductSetup>();
  @Output() completed = new EventEmitter<void>();
  private readonly api = inject(ProductSetupApiService);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly requests = new Subscription();
  private read: Subscription | null = null;
  private attempt: CompleteProductSetup | null = null;
  protected setup: ProductSetup | null = null;
  protected preview: ProductSetupPreview | null = null;
  protected loading = false;
  protected saving = false;
  protected uncertain = false;
  protected error: string | null = null;
  protected message: string | null = null;
  protected reason = '';
  protected confirmed = false;
  protected readonly unitLabel = unitLabel;
  protected get locked(): boolean { return this.saving || this.uncertain; }
  ngOnChanges(): void {
    if (!this.requestId || this.saving) return;
    if (!this.attempt) this.restoreAttempt();
    if (!this.uncertain) this.load();
  }
  ngOnDestroy(): void { this.read?.unsubscribe(); this.requests.unsubscribe(); }
  protected load(): void {
    if (this.locked) return;
    this.read?.unsubscribe(); this.loading = true; this.error = null; this.confirmed = false; this.preview = null;
    this.read = forkJoin({ setup: this.api.get(this.requestId), preview: this.api.preview(this.requestId) }).subscribe({
      next: result => {
        if (this.customerId && this.customerId !== result.setup.customerId) {
          this.setup = null; this.preview = null; this.error = 'La solicitud corresponde a otro cliente.';
        } else { this.setup = result.setup; this.preview = result.preview; }
        this.loading = false; this.detector.detectChanges();
      },
      error: error => { this.error = this.errorText(error, 'No pudimos consultar la configuración pendiente.'); this.loading = false; this.detector.detectChanges(); }
    });
  }
  protected complete(): void {
    if (this.locked || this.loading || !this.preview?.canComplete || !this.confirmed || !this.reason.trim()) return;
    this.attempt = { operationId: crypto.randomUUID(), expectedVersion: this.preview.version,
      previewFingerprint: this.preview.fingerprint, reason: this.reason.trim() };
    try { sessionStorage.setItem(this.storageKey, JSON.stringify(this.attempt)); } catch { /* Retry remains available in this page. */ }
    this.send();
  }
  protected retry(): void { if (!this.saving && this.attempt) this.send(); }
  private send(): void {
    if (!this.attempt) return;
    this.saving = true; this.error = null;
    this.requests.add(this.api.complete(this.requestId, this.attempt).subscribe({
      next: result => {
        this.saving = false; this.uncertain = false; this.clearAttempt(); this.preview = null;
        if (this.setup) this.setup = { ...this.setup, isResolved: true };
        this.message = `Configuración completada. ${result.pricedLines} partidas recibieron su precio inicial.`;
        this.api.refreshCount(); this.completed.emit(); this.detector.detectChanges();
      },
      error: error => {
        this.saving = false;
        if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
          this.uncertain = false; this.clearAttempt(); this.preview = null;
          this.error = this.errorText(error, 'La configuración cambió. Actualiza la revisión antes de confirmar otra vez.');
        } else {
          this.uncertain = true;
          this.error = 'No pudimos confirmar el resultado. Reintenta con la misma referencia para recuperarlo sin repetir cambios.';
        }
        this.detector.detectChanges();
      }
    }));
  }
  protected price(value: number | null): string {
    return value === null ? 'Sin precio' : new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN', maximumFractionDigits: 4 }).format(value);
  }
  protected date(value: string): string { return value.split('-').reverse().join('/'); }
  private get storageKey(): string { return `prodimt:product-setup:${this.requestId}`; }
  private restoreAttempt(): void {
    try {
      const raw = sessionStorage.getItem(this.storageKey); if (!raw) return;
      const value = JSON.parse(raw);
      if (typeof value.operationId !== 'string' || !/^[0-9a-f-]{36}$/i.test(value.operationId)
        || !Number.isInteger(value.expectedVersion) || value.expectedVersion < 1
        || typeof value.previewFingerprint !== 'string' || !/^[0-9a-f]{64}$/i.test(value.previewFingerprint)
        || typeof value.reason !== 'string' || value.reason.length > 300) { this.clearAttempt(); return; }
      this.attempt = { operationId: value.operationId, expectedVersion: value.expectedVersion,
        previewFingerprint: value.previewFingerprint, reason: value.reason };
      this.reason = value.reason; this.uncertain = true;
      this.error = 'Hay una confirmación sin respuesta verificada. Recupera su resultado con la misma referencia.';
    } catch { /* Storage may be unavailable. */ }
  }
  private clearAttempt(): void { this.attempt = null; try { sessionStorage.removeItem(this.storageKey); } catch { } }
  private errorText(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && typeof error.error?.error === 'string' ? error.error.error : fallback;
  }
}
