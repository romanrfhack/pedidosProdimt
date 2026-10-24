import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminCatalogApiService } from './admin-catalog-api.service';
import { AdminDailyOrdersApiService, CorrectOrderRequest } from './admin-daily-orders-api.service';
import { AdminOrderDetail } from './order-data.service';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-admin-order-correction',
  imports: [FormsModule],
  template: `
    <form class="detail-panel stack" (ngSubmit)="submit()" data-testid="order-correction">
      <h3>Corregir pedido</h3>
      <p class="notice">Estas líneas reemplazarán todas las del pedido del {{ detail.orderDate }}. Las cantidades no se suman a las anteriores. El historial conserva el cambio y su motivo.</p>
      @if (errorMessage) { <div class="alert error" role="alert">{{ errorMessage }}</div> }
      @for (warning of snapshotWarnings; track $index) { <p class="alert warning">{{ warning }}</p> }
      @if (conflict) { <button type="button" class="secondary" (click)="reload.emit()">Recargar pedido actualizado</button> }
      <label><span>Motivo de la corrección</span><textarea name="correctionReason" rows="2" required maxlength="1000" [(ngModel)]="reason" [disabled]="isBusy || attempted || conflict"></textarea></label>
      @for (line of lines; track $index; let i = $index) {
        <div class="catalog-line detail-panel" data-testid="correction-line">
          <label><span>Producto {{ i + 1 }}</span><select [name]="'correctionProduct' + i" required [(ngModel)]="line.productId" [disabled]="isBusy || attempted || conflict">
            <option value="">Selecciona un producto</option>
            @for (product of products; track product.id) { <option [value]="product.id">{{ product.name }} · {{ unitLabel(product.unitCode) }}</option> }
          </select></label>
          <label><span>Cantidad corregida {{ i + 1 }}</span><input type="number" required min="0.01" step="0.01" inputmode="decimal" [name]="'correctionQuantity' + i" [(ngModel)]="line.quantity" [disabled]="isBusy || attempted || conflict"></label>
          <label><span>Nota del producto {{ i + 1 }}</span><input [name]="'correctionNote' + i" maxlength="500" [(ngModel)]="line.notes" [disabled]="isBusy || attempted || conflict"></label>
          @if (originalLine(line.productId); as original) { <small>Producto original del pedido: {{ original.productName }} · cantidades en {{ unitLabel(original.unitCode) }}.</small> }
          <button type="button" class="secondary compact" (click)="removeLine(i)" [disabled]="isBusy || attempted || conflict">Quitar producto {{ i + 1 }}</button>
        </div>
      }
      <div class="review-actions">
        <button type="button" class="secondary" (click)="addLine()" [disabled]="isBusy || attempted || conflict || isLoading">Agregar producto</button>
        <button type="submit" class="primary" [disabled]="isBusy || isLoading || conflict || !lines.length || !reason.trim()">{{ attempted ? 'Reintentar misma corrección' : 'Guardar corrección' }}</button>
        <button type="button" class="secondary" (click)="cancel.emit()" [disabled]="isBusy || (attempted && !conflict)">Cancelar corrección</button>
      </div>
      @if (attempted && !conflict) { <p class="notice">El envío conserva la misma referencia y contenido. Reintenta para recuperar el resultado si se perdió la respuesta.</p> }
    </form>
  `
})
export class AdminOrderCorrectionComponent implements OnInit {
  @Input({ required: true }) detail!: AdminOrderDetail;
  @Output() saved = new EventEmitter<void>();
  @Output() cancel = new EventEmitter<void>();
  @Output() reload = new EventEmitter<void>();
  protected readonly unitLabel = unitLabel;
  private readonly api = inject(AdminDailyOrdersApiService);
  private readonly catalogs = inject(AdminCatalogApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly operationKey = crypto.randomUUID();
  private sentRequest: CorrectOrderRequest | null = null;
  protected products: Array<{ id: string; name: string; unitCode: string | null }> = [];
  protected snapshotWarnings: string[] = [];
  protected lines: Array<{ productId: string; quantity: number; notes: string | null }> = [];
  protected reason = '';
  protected isBusy = false;
  protected isLoading = true;
  protected attempted = false;
  protected conflict = false;
  protected errorMessage: string | null = null;

  ngOnInit(): void {
    this.lines = this.detail.lines.map((line) => ({ productId: line.productId, quantity: line.quantity, notes: line.notes }));
    this.catalogs.getProducts().subscribe({
      next: (products) => {
        this.products = products.filter((product) => product.isActive && !!product.unitCode);
        // Existing lines keep the name and unit recorded on the order, even after a catalog change.
        for (const original of this.detail.lines) {
          const current = products.find((product) => product.id === original.productId);
          if (current && (current.unitCode !== original.unitCode || current.name !== original.productName)) {
            this.snapshotWarnings.push(`El catálogo cambió para ${original.productName}. Esta corrección conserva el producto y la unidad del pedido: ${unitLabel(original.unitCode)}.`);
          }
          this.products = this.products.filter((product) => product.id !== original.productId);
          this.products.push({ id: original.productId, name: original.productName, unitCode: original.unitCode });
        }
        this.isLoading = false;
        if (this.lines.some((line) => !this.products.some((product) => product.id === line.productId && !!product.unitCode))) {
          this.errorMessage = 'Algún producto original no tiene unidad confirmada. Selecciona otro producto o quita esa línea explícitamente.';
        }
        this.changeDetector.detectChanges();
      },
      error: () => {
        this.errorMessage = 'No se pudo cargar el catálogo de productos. Cierra la corrección e inténtalo de nuevo.';
        this.changeDetector.detectChanges();
      }
    });
  }

  protected addLine(): void { this.lines.push({ productId: '', quantity: 1, notes: null }); }
  protected removeLine(index: number): void { this.lines.splice(index, 1); }
  protected originalLine(productId: string) { return this.detail.lines.find((line) => line.productId === productId); }

  protected submit(): void {
    if (this.isBusy || this.isLoading || this.conflict) return;
    if (!this.sentRequest) {
      if (!this.reason.trim() || !this.lines.length || this.lines.some((line) => !Number.isFinite(Number(line.quantity)) || Number(line.quantity) <= 0 || !this.products.some((product) => product.id === line.productId && !!product.unitCode))) {
        this.errorMessage = 'Indica el motivo y productos con unidad confirmada y cantidades mayores que cero.';
        return;
      }
      if (new Set(this.lines.map((line) => line.productId)).size !== this.lines.length) {
        this.errorMessage = 'Cada producto debe aparecer una sola vez.';
        return;
      }
      this.sentRequest = {
        operationKey: this.operationKey, expectedVersion: this.detail.version, reason: this.reason.trim(),
        lines: this.lines.map((line) => ({ productId: line.productId, quantity: Number(line.quantity), notes: line.notes?.trim() || null }))
      };
    }
    this.isBusy = true;
    this.attempted = true;
    this.errorMessage = null;
    this.api.correct(this.detail.id, this.sentRequest).subscribe({
      next: () => { this.isBusy = false; this.saved.emit(); },
      error: (error: unknown) => {
        this.isBusy = false;
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.conflict = true;
          this.errorMessage = 'El pedido cambió o la referencia ya se usó con otro contenido. Recarga el pedido actualizado antes de preparar una nueva corrección.';
        } else {
          this.errorMessage = error instanceof HttpErrorResponse ? (error.error?.error ?? error.error?.detail ?? 'No se recibió confirmación. Reintenta la misma corrección para comprobar el resultado.') : 'No se recibió confirmación. Reintenta la misma corrección para comprobar el resultado.';
          if (error instanceof HttpErrorResponse && ((error.status >= 400 && error.status < 500) || error.error?.code === 'PersistenceNotSupported')) {
            this.sentRequest = null;
            this.attempted = false;
          }
        }
        this.changeDetector.detectChanges();
      }
    });
  }
}
