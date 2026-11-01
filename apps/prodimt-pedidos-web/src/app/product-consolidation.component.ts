import { ChangeDetectorRef, Component, ElementRef, EventEmitter, Input, Output, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AdminProductCatalogApiResponse } from './admin-catalog-api.service';
import { CommercialCatalogApiService, ConsolidationPreview, ConsolidationRequest, ConsolidationSelection, DuplicateGroup } from './commercial-catalog-api.service';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-product-consolidation', imports: [FormsModule],
  styleUrl: './product-consolidation.component.css', templateUrl: './product-consolidation.component.html'
})
export class ProductConsolidationComponent {
  @Input() products: AdminProductCatalogApiResponse[] = [];
  @Output() completed = new EventEmitter<void>();
  @ViewChild('dialog') private dialog?: ElementRef<HTMLDialogElement>;
  private readonly api = inject(CommercialCatalogApiService);
  private readonly detector = inject(ChangeDetectorRef);
  protected groups: DuplicateGroup[] = [];
  protected selected = new Set<string>();
  protected target = '';
  protected sharedName = '';
  protected description = '';
  protected reason = '';
  protected confirmed = false;
  protected editing = false;
  protected loading = false;
  protected saving = false;
  protected preview: ConsolidationPreview | null = null;
  protected attempt: ConsolidationRequest | null = null;
  protected error: string | null = null;
  protected message: string | null = null;
  protected readonly unitLabel = unitLabel;
  private selection: ConsolidationSelection | null = null;
  private readonly key = 'prodimt-product-consolidation';

  protected get activeProducts() { return this.products.filter(x => x.isActive && !x.mergedIntoProductId); }
  protected get selectedProducts() { return this.activeProducts.filter(x => this.selected.has(x.id)); }
  protected open(): void {
    this.error = null; this.message = null;
    try {
      const saved: unknown = JSON.parse(sessionStorage.getItem(this.key) ?? 'null');
      if (saved && typeof saved === 'object' && typeof (saved as ConsolidationRequest).operationId === 'string'
        && Array.isArray((saved as ConsolidationRequest).sourceProductIds)) this.attempt = saved as ConsolidationRequest;
    } catch { /* A pending attempt can still be retried in memory. */ }
    this.dialog?.nativeElement.showModal();
    if (!this.attempt) this.load();
  }
  protected close(event?: Event): void {
    if (this.saving) { event?.preventDefault(); return; }
    if (!event) this.dialog?.nativeElement.close();
  }
  protected load(): void {
    this.loading = true; this.error = null;
    this.api.getDuplicateGroups().subscribe({ next: groups => { this.groups = groups; this.loading = false; this.detector.detectChanges(); },
      error: () => { this.loading = false; this.error = 'No se pudieron consultar posibles duplicados.'; this.detector.detectChanges(); } });
  }
  protected choose(group?: DuplicateGroup): void {
    this.editing = true; this.selected = new Set(group?.products.map(x => x.id) ?? []);
    this.target = group?.suggestedTargetId ?? ''; this.sharedName = group?.suggestedName ?? '';
    this.description = ''; this.reason = ''; this.resetPreview(); this.message = null;
  }
  protected toggle(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) this.selected.add(id); else this.selected.delete(id);
    if (!this.selected.has(this.target)) this.target = this.selectedProducts[0]?.id ?? '';
    this.resetPreview();
  }
  protected resetPreview(): void { this.preview = null; this.selection = null; this.confirmed = false; }
  protected inspect(): void {
    if (this.selected.size < 2 || !this.target || this.saving || this.attempt) return;
    const request: ConsolidationSelection = { targetProductId: this.target,
      sourceProductIds: [...this.selected].filter(x => x !== this.target).sort(), sharedName: this.sharedName.trim(),
      description: this.description.trim() || null };
    this.error = null; this.loading = true;
    this.api.previewConsolidation(request).subscribe({
      next: value => { this.selection = request; this.preview = value; this.loading = false; this.confirmed = false; this.detector.detectChanges(); },
      error: (e: unknown) => { this.loading = false; this.error = this.errorText(e, 'No se pudo revisar la consolidación.'); this.detector.detectChanges(); }
    });
  }
  protected apply(): void {
    if (!this.selection || !this.preview?.canApply || !this.confirmed || !this.reason.trim() || this.saving || this.attempt) return;
    this.attempt = { ...this.selection, fingerprint: this.preview.fingerprint,
      reason: this.reason.trim(), operationId: crypto.randomUUID() };
    try { sessionStorage.setItem(this.key, JSON.stringify(this.attempt)); } catch { /* Retain the same reference in memory. */ }
    this.send();
  }
  protected retry(): void { if (this.attempt && !this.saving) this.send(); }
  private send(): void {
    this.saving = true; this.error = null;
    this.api.consolidate(this.attempt!).subscribe({
      next: value => {
        this.saving = false; this.forget(); this.resetPreview(); this.editing = false;
        this.message = `${value.sharedName}: ${value.archivedProducts} duplicados archivados y ${value.updatedProfiles} perfiles actualizados. Revisa su aprobación para captura rápida.`;
        this.completed.emit(); this.load(); this.detector.detectChanges();
      },
      error: (e: unknown) => {
        this.saving = false;
        if (e instanceof HttpErrorResponse && [400, 409, 404].includes(e.status)) {
          this.forget(); this.resetPreview(); this.error = this.errorText(e, 'Revisa de nuevo los productos antes de consolidar.');
        } else this.error = 'No pudimos confirmar la consolidación. Reintenta con la misma referencia; no inicies otra operación.';
        this.detector.detectChanges();
      }
    });
  }
  private forget(): void { this.attempt = null; try { sessionStorage.removeItem(this.key); } catch { /* Already confirmed by server. */ } }
  private errorText(e: unknown, fallback: string): string { return e instanceof HttpErrorResponse && e.error?.error ? e.error.error : fallback; }
}
