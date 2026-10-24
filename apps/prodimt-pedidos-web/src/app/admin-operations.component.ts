import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminOperationsApiService, OperationsOrder, OperationsPlan } from './admin-operations-api.service';
import { businessDateToday, paymentTermLabel, storeKeyLabel, unitLabel } from './operational-labels';

@Component({
  selector: 'app-admin-operations',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="page stack operations-plan" data-testid="operations-plan">
      <div class="page-title">
        <p class="eyebrow">Operación · planeación</p>
        <h2>Plan del día</h2>
        <p>Consulta la demanda autorizada y las instrucciones para preparar cada pedido.</p>
      </div>

      <form class="operations-filters no-print" (ngSubmit)="load()">
        <label><span>Fecha de servicio</span>
          <input name="serviceDate" type="date" required [(ngModel)]="date" (ngModelChange)="clearPlan()" [disabled]="isLoading">
        </label>
        <label><span>Código de ruta</span>
          <input name="routeCode" [(ngModel)]="routeCode" maxlength="80" placeholder="Todas las rutas" (ngModelChange)="clearPlan()" [disabled]="isLoading">
        </label>
        <button class="primary" type="submit" [disabled]="isLoading || !date">Consultar plan</button>
      </form>

      @if (isLoading) { <p class="notice" aria-live="polite">Cargando plan...</p> }
      @if (errorMessage) { <p class="alert error" role="alert">{{ errorMessage }}</p> }

      @if (plan; as current) {
        <div class="operations-heading">
          <div><strong>{{ current.date }} · {{ current.routeCode || 'Todas las rutas' }}</strong>
            <small>{{ current.orders.length }} pedidos incluidos · {{ current.pendingReview.length }} pendientes de revisión</small>
          </div>
          <button class="secondary no-print" type="button" (click)="print()">Imprimir plan</button>
        </div>
        <p class="notice">Documento interno de planeación. Los datos de entrega corresponden al momento de capturar cada pedido. Verifica cambios con administración antes de salir.</p>

        @if (current.warnings.length) {
          <section class="alert warning" aria-label="Advertencias del plan">
            <strong>Datos por resolver</strong>
            <ul>@for (warning of current.warnings; track $index) {
              <li>
                @if (warning.orderId) {
                  <a [href]="'#pedido-' + warning.orderId">{{ warningContext(warning) }}</a>: 
                }
                {{ warning.message }}
              </li>
            }</ul>
          </section>
        }

        <section class="stack" aria-labelledby="machine-demand-heading">
          <div class="section-heading"><h3 id="machine-demand-heading">Por producir</h3><span class="badge neutral">Demanda planeada</span></div>
          @if (!current.machineDemand.length) {
            <p class="notice">No hay demanda con unidad confirmada para este filtro.</p>
          } @else {
            <div class="table-scroll">
              <table class="operations-table" data-testid="machine-demand">
                <thead><tr><th scope="col">Máquina</th><th scope="col">Producto</th><th scope="col">Cantidad</th></tr></thead>
                <tbody>@for (item of current.machineDemand; track $index) {
                  <tr><td>{{ machineLabel(item.machineNumber, item.machineName) }}</td><td>{{ item.productName }}</td><td class="quantity-cell">{{ item.quantity }} {{ unitLabel(item.unitCode) }}</td></tr>
                }</tbody>
              </table>
            </div>
          }
        </section>

        <section class="stack" aria-labelledby="preparation-heading">
          <div class="section-heading"><h3 id="preparation-heading">Por preparar</h3><span class="badge neutral">Lista por cliente</span></div>
          @if (!current.orders.length) { <p class="notice">No hay pedidos incluidos para esta fecha y ruta.</p> }
          @for (order of current.orders; track order.orderId) {
            <article class="preparation-card" data-testid="preparation-order" [id]="'pedido-' + order.orderId">
              <div class="operations-heading">
                <div><h4>{{ order.customerName }}</h4><small>Ruta {{ order.routeCode || 'por confirmar' }} · Visita {{ order.routeStopOrder ?? 'por confirmar' }} · Pedido #{{ order.sequenceNumber }}</small></div>
                <span class="badge neutral">Por preparar</span>
              </div>
              <div class="delivery-details">
                <p><b>Acceso:</b> {{ storeKeyLabel(order.hasStoreKey) }}</p>
                <p><b>Cobro:</b> {{ paymentTermLabel(order.paymentTermCode) }}</p>
                <p><b>Horario solicitado:</b> {{ deliveryWindow(order) }}</p>
                <p class="delivery-instructions"><b>Instrucciones internas:</b> {{ order.internalDeliveryInstructions || 'Pendientes de confirmar' }}</p>
                @if (order.deliveryNotes) { <p class="delivery-instructions"><b>Notas de entrega:</b> {{ order.deliveryNotes }}</p> }
              </div>
              <ul class="preparation-lines">
                @for (line of order.lines; track line.orderLineId) {
                  <li><div><strong>{{ line.productName }}</strong><small>{{ machineLabel(line.assignedMachineNumber, line.assignedMachineName) }}</small>@if (line.notes) { <small>{{ line.notes }}</small> }</div><span class="line-quantity">{{ line.quantity }} {{ unitLabel(line.unitCode) }}</span></li>
                }
              </ul>
              <small class="order-reference">Referencia {{ order.orderId }} · Perfil {{ order.orderingProfileVersion ?? 'sin versión registrada' }}</small>
            </article>
          }
        </section>

        @if (current.pendingReview.length) {
          <section class="stack pending-plan" data-testid="plan-pending-review">
            <h3>Pendientes de revisión</h3>
            <p>Estos pedidos todavía no forman parte de la demanda por producir.</p>
            <ul>@for (order of current.pendingReview; track order.orderId) { <li [id]="'pedido-' + order.orderId">{{ order.customerName }} · Pedido #{{ order.sequenceNumber }}</li> }</ul>
            <a class="no-print" routerLink="/admin/pendientes">Ir a revisión administrativa</a>
          </section>
        }
        <p class="print-footer">Plan de preparación. No es comprobante de entrega ni de pago.</p>
      }
    </section>
  `
})
export class AdminOperationsComponent {
  private readonly api = inject(AdminOperationsApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  protected date = businessDateToday();
  protected routeCode = '';
  protected plan: OperationsPlan | null = null;
  protected isLoading = false;
  protected errorMessage: string | null = null;
  protected readonly unitLabel = unitLabel;
  protected readonly storeKeyLabel = storeKeyLabel;
  protected readonly paymentTermLabel = paymentTermLabel;

  constructor() { this.load(); }

  protected clearPlan(): void { this.plan = null; this.errorMessage = null; }

  protected load(): void {
    if (!this.date || this.isLoading) return;
    this.clearPlan();
    this.isLoading = true;
    this.api.getPlan(this.date, this.routeCode).subscribe({
      next: (plan) => { this.plan = plan; this.isLoading = false; this.changeDetector.detectChanges(); },
      error: (error: unknown) => {
        this.errorMessage = error instanceof HttpErrorResponse && typeof error.error?.error === 'string'
          ? error.error.error : 'No se pudo cargar el plan. Vuelve a consultar.';
        this.isLoading = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected machineLabel(number: number | null, name: string | null): string {
    return number !== null ? `Máquina #${number}${name ? ` · ${name}` : ''}` : name || 'Sin máquina asignada';
  }

  protected deliveryWindow(order: OperationsOrder): string {
    if (order.requestedDeliveryTime) return order.requestedDeliveryTime.slice(0, 5);
    const start = order.requestedDeliveryWindowStart?.slice(0, 5);
    const end = order.requestedDeliveryWindowEnd?.slice(0, 5);
    if (start && end) return `${start} – ${end}`;
    if (start) return `Desde ${start}`;
    if (end) return `Antes de ${end}`;
    return 'Por confirmar';
  }

  protected warningContext(warning: OperationsPlan['warnings'][number]): string {
    const order = this.plan?.orders.find((item) => item.orderId === warning.orderId);
    const pending = this.plan?.pendingReview.find((item) => item.orderId === warning.orderId);
    const line = order?.lines.find((item) => item.orderLineId === warning.orderLineId);
    if (order || pending) return `${(order ?? pending)!.customerName} · Pedido #${(order ?? pending)!.sequenceNumber}${line ? ` · ${line.productName}` : ''}`;
    return `Pedido ${warning.orderId}`;
  }

  protected print(): void { if (this.plan) window.print(); }
}
