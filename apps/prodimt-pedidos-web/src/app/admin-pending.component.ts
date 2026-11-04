import { environment } from '../environments/environment';
import { businessDateToday, unitLabel } from './operational-labels';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { AdminDecisionApiValue } from './customer-orders-api.service';
import { AdminOrder, AdminOrderAuditEvent, AdminOrderDetail, OrderDataService } from './order-data.service';

interface ChangeLineForm {
  orderLineId: string;
  productName: string;
  unitCode: string | null;
  quantity: number;
  notes: string | null;
}

@Component({
  selector: 'app-admin-pending',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="admin-pending">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Pedidos por confirmar</h2><p>Estas solicitudes esperan una decisión y todavía no forman parte de la producción ni del reparto.</p>
      </div>

      <form class="operations-filters" (ngSubmit)="load()">
        <label><span>Fecha de servicio</span><input name="reviewServiceDate" type="date" required [(ngModel)]="serviceDate" (ngModelChange)="changeDate()" [disabled]="isLoading || !!reviewingOrderId || !!loadingChangesOrderId || !!loadingAuditOrderId"></label>
        <button type="submit" class="primary" [disabled]="isLoading || !serviceDate || !!reviewingOrderId || !!loadingChangesOrderId">Consultar pendientes</button>
      </form>

      @if (notification; as message) {
        <section class="notice stack" data-testid="decision-whatsapp-draft"><strong>Avisar al cliente</strong><p style="white-space:pre-wrap">{{ message.message }}</p>
          @if (message.whatsAppUrl) { <a class="primary" [href]="message.whatsAppUrl" target="_blank" rel="noopener noreferrer">Abrir WhatsApp con este mensaje</a> }
          @else { <p>El cliente no tiene un teléfono válido. Puedes copiar el texto y comunicarte por otro medio.</p> }
          <small>El mensaje se abre para tu revisión. Abrirlo no confirma que se haya enviado. Proponer otra fecha tampoco registra un pedido nuevo.</small>
        </section>
      }
      @if (notificationLoading) { <p class="notice" role="status">Preparando aviso al cliente…</p> }
      @if (notificationErrorMessage) {
        <section class="notice stack" data-testid="decision-notification-error" aria-label="Aviso al cliente pendiente">
          <p class="alert warning" role="alert">La decisión se guardó. No pudimos preparar el aviso al cliente: {{ notificationErrorMessage }}</p>
          <button type="button" class="secondary" (click)="retryNotification()" [disabled]="notificationLoading">Reintentar preparar aviso</button>
        </section>
      }
      @if (isLoading) {
        <div class="notice">Cargando pendientes...</div>
      }

      @if (resultMessage) {
        <div class="alert success">{{ resultMessage }}</div>
      }

      @if (errorMessage) {
        <div class="alert error">{{ errorMessage }}</div>
      }

      @if (!isLoading && !errorMessage) {
        @if (orders.length === 0) {
          <div class="notice">No hay pedidos pendientes de revision.</div>
        } @else {
          <div class="admin-list">
            @for (order of orders; track order.id) {
              <article class="admin-row">
                <div>
                  <strong>{{ order.customerName }}</strong><small>Ruta {{ order.routeCode || 'sin asignar' }} · Recibido {{ order.submittedAt }}</small>
                  <small>Pendiente de confirmación · Servicio {{ order.orderDate }} · Pedido #{{ order.sequenceNumber }}</small>
                  <small>{{ order.reviewReason === 'LateSubmission' ? 'Recibido fuera del horario de confirmación automática' : 'Solicitud que requiere revisión' }}</small>
                  @if (order.isLate) {
                    <small>Pedido tardio</small>
                  }
                </div>
                <div class="review-actions">
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="loadingAuditOrderId === order.id"
                    (click)="loadAudit(order)">
                    Ver auditoria
                  </button>
                  <button
                    type="button"
                    class="primary compact"
                    [disabled]="!!reviewingOrderId"
                    (click)="review(order, 'Accepted')">
                    Aceptar
                  </button>
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="!!reviewingOrderId"
                    (click)="startAcceptedWithChanges(order)">
                    Ver detalle / ajustar
                  </button>
                  <button
                    type="button"
                    class="secondary compact"
                    [disabled]="!!reviewingOrderId"
                    (click)="review(order, 'Rejected')">
                    Rechazar
                  </button>
                </div>

                @if (changeDetail?.id === order.id) {
                  <form class="detail-panel stack" (ngSubmit)="reviewWithChanges(order)">
                    <strong>Aceptar con cambios</strong>
                    <label>
                      <span>Hora entrega</span>
                      <input name="requestedDeliveryTime" type="time" [(ngModel)]="requestedDeliveryTime">
                    </label>
                    <label>
                      <span>Notas de entrega</span>
                      <input name="deliveryNotes" maxlength="500" [(ngModel)]="deliveryNotes">
                    </label>
                    <label>
                      <span>Notas internas</span>
                      <input name="internalNotes" maxlength="1000" [(ngModel)]="internalNotes">
                    </label>
                    <div class="line-list">
                      @for (line of changeLines; track line.orderLineId) {
                        <label class="line-row">
                          <span>
                            <strong>{{ line.productName }}</strong>
                            <small>Cantidad actualizable · {{ unitLabel(line.unitCode) }}</small>
                          </span>
                          <input
                            type="number"
                            min="0.01"
                            step="0.01"
                            inputmode="decimal"
                            [name]="'lineQuantity' + line.orderLineId"
                            [(ngModel)]="line.quantity">
                        </label>
                      }
                    </div>
                    <div class="review-actions">
                      <button type="submit" class="primary compact" [disabled]="!!reviewingOrderId">
                        Aceptar con cambios
                      </button>
                      <button type="button" class="secondary compact" (click)="cancelChanges()">Cancelar</button>
                    </div>
                  </form>
                }

                @if (selectedAuditOrderId === order.id) {
                  <div class="audit-list" [attr.aria-busy]="loadingAuditOrderId === order.id">
                    @if (loadingAuditOrderId === order.id) {
                      <small role="status">Cargando auditoría...</small>
                    } @else if (auditErrorMessage) {
                      <small role="alert">{{ auditErrorMessage }}</small>
                    } @else if (auditEvents.length === 0) {
                      <small>No hay eventos de auditoria para mostrar.</small>
                    } @else {
                      @for (event of auditEvents; track event.id) {
                        <div class="audit-event">
                          <strong>{{ event.eventType }}</strong>
                          <small>{{ event.occurredAt }} · {{ event.actorType }}</small>
                          <small>{{ event.summary }}</small>
                          @if (event.correction; as correction) {
                            <p><b>Motivo:</b> {{ correction.reason }}</p>
                            @for (change of correction.changes; track $index) { <div><strong>{{ change.productName }}</strong><small>Antes: {{ change.before }} → Después: {{ change.after }}</small></div> }
                          }
                        </div>
                      }
                    }
                  </div>
                }
              </article>
            }
          </div>
        }
      }
    </section>
  `
})
export class AdminPendingComponent {
  protected readonly unitLabel = unitLabel;
  private readonly data = inject(OrderDataService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private auditSubscription: Subscription | null = null;
  private auditGeneration = 0;
  private pendingSubscription: Subscription | null = null;
  private pendingGeneration = 0;
  private changeSubscription: Subscription | null = null;
  private changeGeneration = 0;

  private readonly http = inject(HttpClient);
  protected notification: { message: string; whatsAppUrl: string | null } | null = null;
  protected notificationErrorMessage: string | null = null;
  protected notificationLoading = false;
  private notificationOrderId: string | null = null;
  private notificationGeneration = 0;
  private notificationSubscription: Subscription | null = null;
  private prepareNotification(id: string): void {
    this.resetNotification();
    this.notificationOrderId = id;
    this.retryNotification();
  }
  protected retryNotification(): void {
    const id = this.notificationOrderId;
    if (!id || this.notificationLoading || this.destroyRef.destroyed) return;
    const generation = ++this.notificationGeneration;
    const date = this.serviceDate;
    this.notificationErrorMessage = null;
    this.notificationLoading = true;
    const current = () => !this.destroyRef.destroyed && generation === this.notificationGeneration
      && id === this.notificationOrderId && date === this.serviceDate;
    this.notificationSubscription = this.http.get<{ message: string; whatsAppUrl: string | null }>(environment.apiBaseUrl + '/api/admin/orders/' + id + '/notification').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        if (!current()) return;
        this.notification = result; this.notificationLoading = false;
        this.changeDetector.detectChanges();
      },
      error: e => {
        if (!current()) return;
        this.notificationErrorMessage = this.formatError(e, 'El servicio de avisos no está disponible.');
        this.notificationLoading = false;
        this.changeDetector.detectChanges();
      }
    });
  }
  private resetNotification(): void {
    this.notificationGeneration++;
    this.notificationSubscription?.unsubscribe();
    this.notificationSubscription = null;
    this.notificationOrderId = null;
    this.notification = null;
    this.notificationErrorMessage = null;
    this.notificationLoading = false;
  }
  protected orders: AdminOrder[] = [];
  protected auditEvents: AdminOrderAuditEvent[] = [];
  protected changeDetail: AdminOrderDetail | null = null;
  protected changeLines: ChangeLineForm[] = [];
  protected requestedDeliveryTime: string | null = null;
  protected deliveryNotes: string | null = null;
  protected internalNotes: string | null = null;
  protected isLoading = true;
  protected reviewingOrderId: string | null = null;
  protected loadingAuditOrderId: string | null = null;
  protected loadingChangesOrderId: string | null = null;
  protected selectedAuditOrderId: string | null = null;
  protected auditErrorMessage: string | null = null;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected serviceDate = this.route.snapshot.queryParamMap.get('date') || businessDateToday();

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.resetNotification();
      this.resetAudit();
      this.cancelPendingLoad();
      this.cancelChangeLoad();
    });
    this.load();
  }

  protected changeDate(): void {
    this.resetNotification();
    this.reviewingOrderId = null;
    this.orders = [];
    this.resetAudit();
    this.cancelPendingLoad();
    this.isLoading = false;
    this.errorMessage = null;
    this.resultMessage = null;
    this.cancelChanges();
  }

  protected review(order: AdminOrder, decision: AdminDecisionApiValue): void {
    if (this.destroyRef.destroyed || this.reviewingOrderId) return;
    const date = this.serviceDate;
    this.resetNotification();
    this.errorMessage = null;
    this.resultMessage = null;
    this.reviewingOrderId = order.id;

    this.data.reviewOrder(order.id, decision).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (this.destroyRef.destroyed || date !== this.serviceDate) return;
        this.resultMessage = decision === 'Accepted'
          ? 'Pedido aceptado.'
          : 'No fue posible aceptar el pedido para esa fecha.';
        this.prepareNotification(order.id);
        this.load(false);
      },
      error: (error: unknown) => {
        if (this.destroyRef.destroyed || date !== this.serviceDate) return;
        this.errorMessage = this.formatError(error, 'No se pudo guardar la decision administrativa.');
        this.reviewingOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected startAcceptedWithChanges(order: AdminOrder): void {
    this.cancelChangeLoad();
    if (this.destroyRef.destroyed || this.isLoading || this.reviewingOrderId) return;
    this.resetNotification();
    const generation = this.changeGeneration;
    const date = this.serviceDate;
    this.errorMessage = null;
    this.resultMessage = null;
    this.loadingChangesOrderId = order.id;

    this.changeSubscription = this.data.loadOrderDetail(order.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (detail) => {
        if (!this.isCurrentChangeLoad(generation, order.id, date)) return;
        this.changeDetail = detail;
        this.changeLines = detail.lines.map((line) => ({
          orderLineId: line.id,
          productName: line.productName,
          unitCode: line.unitCode,
          quantity: line.quantity,
          notes: line.notes
        }));
        this.requestedDeliveryTime = null;
        this.deliveryNotes = detail.deliveryNotes;
        this.internalNotes = detail.internalNotes;
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (!this.isCurrentChangeLoad(generation, order.id, date)) return;
        this.errorMessage = this.formatError(error, 'No se pudo cargar el detalle del pedido.');
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected reviewWithChanges(order: AdminOrder): void {
    if (this.destroyRef.destroyed || this.reviewingOrderId) return;
    const date = this.serviceDate;
    if (this.changeLines.some((line) => Number(line.quantity) <= 0)) {
      this.errorMessage = 'Las cantidades ajustadas deben ser mayores a cero.';
      this.changeDetector.detectChanges();
      return;
    }

    this.resetNotification();
    this.errorMessage = null;
    this.resultMessage = null;
    this.reviewingOrderId = order.id;

    this.data.reviewOrderWithRequest(order.id, {
      decision: 'AcceptedWithChanges',
      expectedVersion: this.changeDetail?.version ?? null,
      internalNotes: this.emptyToNull(this.internalNotes),
      requestedDeliveryTime: this.emptyToNull(this.requestedDeliveryTime),
      requestedDeliveryWindowStart: null,
      requestedDeliveryWindowEnd: null,
      deliveryNotes: this.emptyToNull(this.deliveryNotes),
      lineAdjustments: this.changeLines.map((line) => ({
        orderLineId: line.orderLineId,
        quantity: Number(line.quantity),
        notes: this.emptyToNull(line.notes)
      }))
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (this.destroyRef.destroyed || date !== this.serviceDate) return;
        this.resultMessage = 'Pedido aceptado con cambios.'; this.prepareNotification(order.id);
        this.cancelChanges();
        this.load(false);
      },
      error: (error: unknown) => {
        if (this.destroyRef.destroyed || date !== this.serviceDate) return;
        this.errorMessage = this.formatError(error, 'No se pudo aceptar con cambios.');
        this.reviewingOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected cancelChanges(): void {
    this.cancelChangeLoad();
    this.changeDetail = null;
    this.changeLines = [];
    this.requestedDeliveryTime = null;
    this.deliveryNotes = null;
    this.internalNotes = null;
  }

  protected loadAudit(order: AdminOrder): void {
    this.resetAudit();
    if (this.destroyRef.destroyed || this.isLoading) return;
    const generation = this.auditGeneration;
    const date = this.serviceDate;
    this.loadingAuditOrderId = order.id;
    this.selectedAuditOrderId = order.id;

    this.auditSubscription = this.data.loadOrderAudit(order.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (events) => {
        if (!this.isCurrentAudit(generation, order.id, date)) return;
        this.auditEvents = events;
        this.loadingAuditOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (!this.isCurrentAudit(generation, order.id, date)) return;
        this.auditErrorMessage = this.formatError(error, 'No se pudo cargar la auditoria.');
        this.loadingAuditOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  protected load(showLoading = true): void {
    this.resetAudit();
    this.cancelPendingLoad();
    this.cancelChangeLoad();
    if (this.destroyRef.destroyed || !this.serviceDate) return;
    const generation = this.pendingGeneration;
    const date = this.serviceDate;
    this.errorMessage = null;
    if (showLoading) {
      this.isLoading = true;
    }

    this.pendingSubscription = this.data.loadPendingReviewOrders(date).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (orders) => {
        if (this.destroyRef.destroyed || generation !== this.pendingGeneration || date !== this.serviceDate) return;
        this.orders = orders;
        this.resetAudit();
        this.isLoading = false;
        this.reviewingOrderId = null;
        this.loadingAuditOrderId = null;
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (this.destroyRef.destroyed || generation !== this.pendingGeneration || date !== this.serviceDate) return;
        this.errorMessage = this.formatError(error, 'No se pudieron cargar los pendientes de revision.');
        this.isLoading = false;
        this.reviewingOrderId = null;
        this.loadingAuditOrderId = null;
        this.loadingChangesOrderId = null;
        this.changeDetector.detectChanges();
      }
    });
  }

  private resetAudit(): void {
    this.auditGeneration++;
    this.auditSubscription?.unsubscribe();
    this.auditSubscription = null;
    this.auditEvents = [];
    this.selectedAuditOrderId = null;
    this.loadingAuditOrderId = null;
    this.auditErrorMessage = null;
  }

  private isCurrentAudit(generation: number, orderId: string, date: string): boolean {
    return !this.destroyRef.destroyed && generation === this.auditGeneration
      && this.selectedAuditOrderId === orderId && this.serviceDate === date;
  }

  private cancelPendingLoad(): void {
    this.pendingGeneration++;
    this.pendingSubscription?.unsubscribe();
    this.pendingSubscription = null;
  }

  private cancelChangeLoad(): void {
    this.changeGeneration++;
    this.changeSubscription?.unsubscribe();
    this.changeSubscription = null;
    this.loadingChangesOrderId = null;
  }

  private isCurrentChangeLoad(generation: number, orderId: string, date: string): boolean {
    return !this.destroyRef.destroyed && generation === this.changeGeneration
      && this.loadingChangesOrderId === orderId && this.serviceDate === date;
  }

  private emptyToNull(value: string | null): string | null {
    return value && value.trim() ? value : null;
  }

  private formatError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return fallback;
  }
}
