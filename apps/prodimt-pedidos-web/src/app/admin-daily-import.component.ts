import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminDailyOrdersApiService, DailyOrdersRequest, DailyOrdersResult } from './admin-daily-orders-api.service';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-admin-daily-import',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="page stack" data-testid="daily-import">
      <div class="page-title"><p class="eyebrow">Admin · captura</p><h2>Importar pedidos diarios</h2></div>
      <p class="notice">Carga una fila por producto con fecha, cliente, unidad y referencia de pedido. La vista previa muestra qué se creará y qué ya existe. Para cambiar un pedido registrado, abre su detalle y usa Corregir pedido.</p>
      <div class="review-actions"><a routerLink="/admin/importacion">Importar catálogos</a><a routerLink="/admin/pedidos">Consultar pedidos</a></div>
      @if (errorMessage) { <div class="alert error" role="alert">{{ errorMessage }}</div> }
      @if (resultMessage) { <div class="alert success" role="status">{{ resultMessage }}</div> }
      <section class="detail-panel stack">
        <div class="catalog-grid">
          <label><span>Origen de los pedidos</span><input name="source" maxlength="80" [(ngModel)]="source" (ngModelChange)="invalidate()" [disabled]="isBusy || hasAttemptedApply" placeholder="Ejemplo: captura-ruta"></label>
          <label><span>Referencia del lote</span><input name="batchKey" maxlength="100" [(ngModel)]="batchKey" (ngModelChange)="invalidate()" [disabled]="isBusy || hasAttemptedApply"></label>
          <label><span>Archivo de pedidos CSV</span><input name="dailyCsvFile" type="file" accept=".csv,text/csv" (change)="onFileSelected($event)" [disabled]="isBusy || hasAttemptedApply"></label>
        </div>
        <small>En origen y referencias usa letras sin acentos, números, guion, guion bajo, punto o dos puntos; sin espacios. Conserva estos valores al reenviar un archivo: la referencia identifica el mismo pedido incluso en otro lote.</small>
        <details><summary>Formato y ejemplo ficticio</summary><p>Encabezado requerido:</p><code>{{ header }}</code><p>Usa order para un pedido; no_order sólo cuando el cliente confirmó que no pide en esa fecha, con producto, cantidad y unidad vacíos. Una X por producto no cancela el pedido completo.</p><button type="button" class="secondary compact" (click)="downloadTemplate()">Descargar plantilla CSV</button></details>
        <label><span>Contenido CSV de pedidos</span><textarea name="dailyContent" rows="9" [(ngModel)]="content" (ngModelChange)="invalidate()" [disabled]="isBusy || hasAttemptedApply"></textarea></label>
        <div class="review-actions">
          <button type="button" class="primary" (click)="validate()" [disabled]="isBusy || hasAttemptedApply || !content.trim() || !source.trim() || !batchKey.trim()">Validar pedidos</button>
          <button type="button" class="primary" (click)="apply()" [disabled]="isBusy || !validatedRequest || !preview?.canApply || !!preview?.applied">{{ hasAttemptedApply ? 'Reintentar mismo lote' : 'Aplicar pedidos' }}</button>
          <button type="button" class="secondary" (click)="startNewBatch()" [disabled]="isBusy || (hasAttemptedApply && !preview?.applied)">Preparar otro lote</button>
        </div>
        @if (hasAttemptedApply && !preview?.applied) { <p class="notice">El envío conserva exactamente el mismo contenido y referencia. Si se perdió la respuesta, reintenta este lote para recuperar el resultado sin duplicarlo.</p> }
      </section>
      @if (preview; as result) {
        <section class="stack" data-testid="daily-import-preview">
          <h3>{{ result.applied ? 'Resultado guardado' : 'Vista previa' }}</h3>
          <p>{{ result.orders.length }} pedidos · {{ result.errors.length }} errores · {{ result.warnings.length }} advertencias</p>
          @for (error of result.errors; track $index) { <p class="alert error" role="alert">{{ error.rowNumber ? 'Fila ' + error.rowNumber + ': ' : '' }}{{ error.message }}</p> }
          @for (warning of result.warnings; track $index) { <p class="alert warning">{{ warning }}</p> }
          @for (order of result.orders; track order.externalOrderKey) {
            <article class="detail-panel" data-testid="daily-preview-order">
              <div><strong>{{ order.customerName }}</strong><small>{{ order.serviceDate }} · Referencia {{ order.externalOrderKey }}</small><span class="badge neutral">{{ order.disposition === 'existing' ? 'Ya registrado' : (result.applied ? 'Registrado' : 'Por crear') }}</span></div>
              @if (order.action === 'no_order') { <p>No pide en esta fecha.</p> }
              @for (line of order.lines; track $index) { <div class="line-row"><span>{{ line.productName }}@if (line.notes) { <small>{{ line.notes }}</small> }</span><b>{{ line.quantity }} {{ unitLabel(line.unitCode) }}</b></div> }
            </article>
          }
        </section>
      }
    </section>
  `
})
export class AdminDailyImportComponent {
  protected readonly unitLabel = unitLabel;
  protected readonly header = 'externalOrderKey,serviceDate,customerExternalCode,action,productExternalCode,quantity,unitCode,notes';
  private readonly api = inject(AdminDailyOrdersApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  protected source = '';
  protected batchKey = crypto.randomUUID();
  protected content = '';
  protected fileName: string | null = null;
  protected preview: DailyOrdersResult | null = null;
  protected validatedRequest: DailyOrdersRequest | null = null;
  protected isBusy = false;
  protected hasAttemptedApply = false;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;

  protected invalidate(): void {
    this.preview = null;
    this.validatedRequest = null;
    this.errorMessage = null;
    this.resultMessage = null;
  }

  protected async onFileSelected(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.invalidate();
    this.content = '';
    this.fileName = null;
    if (file.size > 2 * 1024 * 1024) {
      this.errorMessage = 'El archivo supera el límite de 2 MB.';
      return;
    }
    this.isBusy = true;
    try {
      this.content = await file.text();
      this.fileName = file.name;
    } catch {
      this.errorMessage = 'No se pudo leer el archivo. Selecciónalo de nuevo.';
    } finally {
      this.isBusy = false;
      this.changeDetector.detectChanges();
    }
  }

  protected validate(): void {
    if (this.isBusy || this.hasAttemptedApply) return;
    this.invalidate();
    const request = { content: this.content, fileName: this.fileName, source: this.source.trim(), batchKey: this.batchKey.trim() };
    this.isBusy = true;
    this.api.validate(request).subscribe({
      next: (result) => {
        this.preview = result;
        if (result.applied) this.resultMessage = 'Lote ya aplicado: resultado recuperado sin duplicar pedidos.';
        this.validatedRequest = result.canApply ? request : null;
        this.isBusy = false;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => this.fail(error, 'No se pudieron validar los pedidos.')
    });
  }

  protected apply(): void {
    if (this.isBusy || !this.validatedRequest || !this.preview?.canApply || this.preview.applied) return;
    this.hasAttemptedApply = true;
    this.isBusy = true;
    this.errorMessage = null;
    this.api.apply(this.validatedRequest).subscribe({
      next: (result) => {
        this.preview = result;
        this.isBusy = false;
        this.resultMessage = result.applied ? (result.replayed ? 'Lote ya aplicado: resultado recuperado sin duplicar pedidos.' : 'Pedidos aplicados. Consulta su estado antes de producir.') : null;
        if (!result.applied) { this.hasAttemptedApply = false; this.validatedRequest = null; }
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        // A deterministic rejection permits editing; a lost response must keep the same operation identity.
        if (error instanceof HttpErrorResponse && ((error.status >= 400 && error.status < 500) || error.error?.code === 'PersistenceNotSupported')) {
          this.hasAttemptedApply = false;
          this.validatedRequest = null;
        }
        this.fail(error, 'No se recibió confirmación. Reintenta el mismo lote para comprobar el resultado.');
      }
    });
  }

  protected startNewBatch(): void {
    if (this.isBusy || (this.hasAttemptedApply && !this.preview?.applied)) return;
    this.hasAttemptedApply = false;
    this.batchKey = crypto.randomUUID();
    this.fileName = null;
    this.content = '';
    this.invalidate();
  }

  protected downloadTemplate(): void {
    const blob = new Blob(['\uFEFF' + this.header + '\r\n'], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'pedidos-diarios.csv';
    anchor.click();
    URL.revokeObjectURL(url);
  }

  private fail(error: unknown, fallback: string): void {
    this.errorMessage = error instanceof HttpErrorResponse ? (error.error?.error ?? error.error?.detail ?? fallback) : fallback;
    this.isBusy = false;
    this.changeDetector.detectChanges();
  }
}
