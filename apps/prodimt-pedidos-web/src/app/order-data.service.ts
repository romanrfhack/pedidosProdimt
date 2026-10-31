import { Injectable } from '@angular/core';
import { Observable, forkJoin, map } from 'rxjs';
import { environment } from '../environments/environment';
import { businessDateToday, unitLabel } from './operational-labels';
import { AdminCatalogApiService } from './admin-catalog-api.service';
import {
  AdminDecisionApiValue,
  AdminOrderDetailApiResponse,
  AdminOrderAuditApiResponse,
  AdminOrderSummaryApiResponse,
  AdminOrderTemplateApiResponse,
  AdminSubmitCustomerOrderApiRequest,
  CustomerCurrentOrderSummaryApiResponse,
  CustomerOrderApiResponse,
  CustomerOrdersApiService,
  PendingCustomerOrderApiResponse,
  ReviewOrderApiRequest,
  SubmitCustomerOrderLineApiRequest
} from './customer-orders-api.service';

export interface FrequentProduct {
  id: string;
  name: string;
  description: string | null;
  suggestedQuantity: number;
  unitCode: string | null;
  quantity: number;
}

export interface CustomerToday {
  customerName: string;
  orderDate: string;
  currentOrder: CustomerCurrentOrderSummaryApiResponse | null;
  frequentProducts: FrequentProduct[];
}

export interface AdminOrder {
  id: string;
  customerId: string | null;
  orderDate: string;
  customerName: string;
  status: string;
  submittedAt: string;
  requestedDelivery: string;
  isLate: boolean;
  requiresReview: boolean;
  reviewReason: string | null;
  sequenceNumber: number;
  adminDecision: string | null;
}

export interface AdminOrderLine {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  notes: string | null;
  machineLabel: string;
  unitCode: string | null;
}

export interface AdminOrderDetail extends AdminOrder {
  version: number;
  source: string | null;
  externalOrderKey: string | null;
  deliveryNotes: string | null;
  internalNotes: string | null;
  salesChannel: string;
  lines: AdminOrderLine[];
}

export interface AdminOrderAuditEvent {
  id: string;
  eventType: string;
  occurredAt: string;
  actorType: string;
  summary: string;
  correction: { reason: string; changes: Array<{ productName: string; before: string; after: string; beforeNotes: string | null; afterNotes: string | null }> } | null;
}

export interface PendingCustomerOrder {
  id: string;
  name: string;
  phoneNumber: string;
  requestedDelivery: string;
  deliveryNotes: string | null;
  frequentProductsCount: number;
  isScheduledRest?: boolean;
}

export interface AdminOrderTemplateProduct {
  id: string;
  name: string;
  description: string | null;
  suggestedQuantity: number;
  unitCode: string | null;
  quantity: number;
  notes: string | null;
}

export interface AdminOrderTemplate {
  customerId: string;
  customerName: string;
  requestedDeliveryTime: string | null;
  requestedDeliveryWindowStart: string | null;
  requestedDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
  products: AdminOrderTemplateProduct[];
}

@Injectable({ providedIn: 'root' })
export class OrderDataService {
  constructor(private readonly customerOrdersApi: CustomerOrdersApiService, private readonly catalog: AdminCatalogApiService) {}

  loadCustomerToday(customerId = environment.demoCustomerId): Observable<CustomerToday> {
    return this.customerOrdersApi.getToday(customerId).pipe(
      map((response) => ({
        customerName: response.customerName,
        orderDate: response.orderDate,
        currentOrder: response.currentOrder,
        frequentProducts: response.products.map((product) => ({
          id: product.productId,
          name: product.name,
          description: product.description,
          unitCode: product.unitCode,
          suggestedQuantity: product.suggestedQuantity,
          quantity: product.suggestedQuantity
        }))
      }))
    );
  }

  submitCustomerOrder(
    lines: SubmitCustomerOrderLineApiRequest[],
    customerId = environment.demoCustomerId
  ): Observable<CustomerOrderApiResponse> {
    return this.customerOrdersApi.submitOrder(customerId, { lines });
  }

  markNoOrder(customerId = environment.demoCustomerId): Observable<CustomerOrderApiResponse> {
    return this.customerOrdersApi.markNoOrder(customerId);
  }

  loadTodayOrders(date?: string): Observable<AdminOrder[]> {
    return this.customerOrdersApi.getTodayOrders(date).pipe(
      map((orders) => orders.map((order) => this.mapAdminOrder(order)))
    );
  }

  loadPendingReviewOrders(date?: string): Observable<AdminOrder[]> {
    return this.customerOrdersApi.getPendingReviewOrders(date).pipe(
      map((orders) => orders.map((order) => this.mapAdminOrder(order)))
    );
  }

  loadOrderDetail(orderId: string): Observable<AdminOrderDetail> {
    return this.customerOrdersApi.getOrderDetail(orderId).pipe(
      map((order) => this.mapAdminOrderDetail(order))
    );
  }

  reviewOrder(orderId: string, decision: AdminDecisionApiValue): Observable<AdminOrder> {
    return this.reviewOrderWithRequest(orderId, { decision, internalNotes: null });
  }

  reviewOrderWithRequest(orderId: string, request: ReviewOrderApiRequest): Observable<AdminOrder> {
    return this.customerOrdersApi.reviewOrder(orderId, request).pipe(map((order) => this.mapAdminOrder(order)));
  }

  loadOrderAudit(orderId: string): Observable<AdminOrderAuditEvent[]> {
    return this.customerOrdersApi.getOrderAudit(orderId).pipe(
      map((events) => events.map((event) => this.mapAuditEvent(event)))
    );
  }

  loadPendingCustomers(date?: string): Observable<PendingCustomerOrder[]> {
    const serviceDate=date ?? businessDateToday();
    // Rest days are ISO weekdays from the server's customer configuration.
    // UTC noon maps the explicit service date, independently of browser timezone.
    const weekday=((new Date(serviceDate+'T12:00:00Z').getUTCDay()+6)%7)+1;
    return forkJoin({pending:this.customerOrdersApi.getPendingCustomers(serviceDate), customers:this.catalog.getCustomers()}).pipe(
      map(({pending,customers}) => {
        const calendars=new Map(customers.map(customer=>[customer.id,customer.restDays??[]]));
        return pending.map(customer=>({...this.mapPendingCustomer(customer),
          isScheduledRest:calendars.get(customer.customerId)?.includes(weekday)??false}));
      })
    );
  }

  loadAdminOrderTemplate(customerId: string): Observable<AdminOrderTemplate> {
    return this.customerOrdersApi.getAdminOrderTemplate(customerId).pipe(
      map((template) => this.mapOrderTemplate(template))
    );
  }

  submitAdminCustomerOrder(customerId: string, request: AdminSubmitCustomerOrderApiRequest): Observable<AdminOrder> {
    return this.customerOrdersApi.submitAdminCustomerOrder(customerId, request).pipe(
      map((order) => this.mapAdminOrder(order))
    );
  }

  markAdminNoOrder(customerId: string, internalNotes: string | null, serviceDate?: string, autoDeliveryDate = false): Observable<AdminOrder> {
    return this.customerOrdersApi.markAdminCustomerNoOrder(customerId, { internalNotes, serviceDate,
      ...(autoDeliveryDate ? { autoDeliveryDate: true, expectedDeliveryDate: serviceDate } : {}) }).pipe(
      map((order) => this.mapAdminOrder(order))
    );
  }

  private mapAdminOrder(order: AdminOrderSummaryApiResponse): AdminOrder {
    return {
      id: order.orderId,
      customerId: order.customerId,
      orderDate: order.orderDate,
      customerName: order.customerName,
      status: order.status,
      submittedAt: this.formatDateTime(order.submittedAt),
      requestedDelivery: this.formatRequestedDelivery(order),
      isLate: order.isLate,
      requiresReview: order.requiresAdminReview,
      reviewReason: order.adminReviewReason,
      sequenceNumber: order.sequenceNumber,
      adminDecision: order.adminDecision
    };
  }

  private mapAdminOrderDetail(order: AdminOrderDetailApiResponse): AdminOrderDetail {
    const summary = this.mapAdminOrder(order);

    return {
      ...summary,
      version: order.version,
      source: order.source ?? null,
      externalOrderKey: order.externalOrderKey ?? null,
      deliveryNotes: order.deliveryNotes,
      internalNotes: order.internalNotes,
      salesChannel: order.salesChannelName ?? order.salesChannelType ?? 'Sin canal',
      lines: order.lines.map((line) => ({
        id: line.orderLineId,
        productId: line.productId,
        productName: line.productName,
        unitCode: line.unitCode,
        quantity: line.quantity,
        notes: line.notes,
        machineLabel: line.assignedMachineNumber || line.assignedMachineName
          ? `${line.assignedMachineNumber ? `#${line.assignedMachineNumber}` : ''}${line.assignedMachineNumber && line.assignedMachineName ? ' · ' : ''}${line.assignedMachineName ?? ''}`
          : 'Sin maquina'
      }))
    };
  }

  private mapAuditEvent(event: AdminOrderAuditApiResponse): AdminOrderAuditEvent {
    return {
      id: event.id,
      eventType: event.eventType,
      occurredAt: this.formatDateTime(event.occurredAt),
      actorType: event.actorType,
      summary: event.summary,
      correction: this.parseCorrection(event)
    };
  }

  private parseCorrection(event: AdminOrderAuditApiResponse): AdminOrderAuditEvent['correction'] {
    if (event.eventType !== 'OrderCorrected' || !event.metadataJson) return null;
    try {
      const data = JSON.parse(event.metadataJson);
      if (typeof data?.reason !== 'string' || !Array.isArray(data?.before?.lines) || !Array.isArray(data?.after?.lines)) return null;
      type SnapshotLine = { productId: string; productName: string | null; quantity: number; unitCode: string | null; notes: string | null };
      const validLines = (items: unknown[]): SnapshotLine[] => items.filter((item): item is SnapshotLine => {
        if (!item || typeof item !== 'object') return false;
        const line = item as Record<string, unknown>;
        return typeof line['productId'] === 'string' && typeof line['quantity'] === 'number' && Number.isFinite(line['quantity']) &&
          (typeof line['productName'] === 'string' || line['productName'] === null) &&
          (typeof line['unitCode'] === 'string' || line['unitCode'] === null) &&
          (typeof line['notes'] === 'string' || line['notes'] === null);
      });
      const before = new Map(validLines(data.before.lines).map((line) => [line.productId, line]));
      const after = new Map(validLines(data.after.lines).map((line) => [line.productId, line]));
      const ids = [...new Set([...before.keys(), ...after.keys()])];
      return { reason: data.reason, changes: ids.map((id) => {
        const previous = before.get(id);
        const current = after.get(id);
        return {
          productName: current?.productName || previous?.productName || 'Producto',
          before: previous ? `${previous.quantity} ${unitLabel(previous.unitCode)}` : 'No incluido',
          after: current ? `${current.quantity} ${unitLabel(current.unitCode)}` : 'Retirado',
          beforeNotes: previous?.notes ?? null,
          afterNotes: current?.notes ?? null
        };
      }) };
    } catch { return null; }
  }

  private mapPendingCustomer(customer: PendingCustomerOrderApiResponse): PendingCustomerOrder {
    return {
      id: customer.customerId,
      name: customer.customerName,
      phoneNumber: customer.phoneNumber,
      requestedDelivery: this.formatRequestedDelivery({
        requestedDeliveryTime: customer.preferredDeliveryTime,
        requestedDeliveryWindowStart: customer.preferredDeliveryWindowStart,
        requestedDeliveryWindowEnd: customer.preferredDeliveryWindowEnd
      }),
      deliveryNotes: customer.deliveryNotes,
      frequentProductsCount: customer.frequentProductsCount
    };
  }

  private mapOrderTemplate(template: AdminOrderTemplateApiResponse): AdminOrderTemplate {
    return {
      customerId: template.customerId,
      customerName: template.customerName,
      requestedDeliveryTime: this.toTimeInput(template.preferredDeliveryTime),
      requestedDeliveryWindowStart: this.toTimeInput(template.preferredDeliveryWindowStart),
      requestedDeliveryWindowEnd: this.toTimeInput(template.preferredDeliveryWindowEnd),
      deliveryNotes: template.deliveryNotes,
      products: template.products.map((product) => ({
        id: product.productId,
        name: product.name,
        description: product.description,
        unitCode: product.unitCode,
        suggestedQuantity: product.suggestedQuantity,
        quantity: product.suggestedQuantity,
        notes: null
      }))
    };
  }

  private formatRequestedDelivery(order: {
    requestedDeliveryTime: string | null;
    requestedDeliveryWindowStart: string | null;
    requestedDeliveryWindowEnd: string | null;
  }): string {
    const time = this.formatTime(order.requestedDeliveryTime);
    const windowStart = this.formatTime(order.requestedDeliveryWindowStart);
    const windowEnd = this.formatTime(order.requestedDeliveryWindowEnd);

    if (time) {
      return time;
    }

    if (windowStart && windowEnd) {
      return `${windowStart} - ${windowEnd}`;
    }

    if (windowStart) {
      return `Desde ${windowStart}`;
    }

    if (windowEnd) {
      return `Antes de ${windowEnd}`;
    }

    return 'Sin horario';
  }

  private formatTime(value: string | null): string | null {
    return value ? value.slice(0, 5) : null;
  }

  private toTimeInput(value: string | null): string | null {
    return value ? value.slice(0, 5) : null;
  }

  private formatDateTime(value: string): string {
    return new Intl.DateTimeFormat('es-MX', {
      dateStyle: 'short',
      timeStyle: 'short',
      timeZone: 'America/Mexico_City'
    }).format(new Date(value));
  }
}
