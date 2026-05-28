import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../environments/environment';
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
  productName: string;
  quantity: number;
  notes: string | null;
  machineLabel: string;
}

export interface AdminOrderDetail extends AdminOrder {
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
}

export interface PendingCustomerOrder {
  id: string;
  name: string;
  phoneNumber: string;
  requestedDelivery: string;
  deliveryNotes: string | null;
  frequentProductsCount: number;
}

export interface AdminOrderTemplateProduct {
  id: string;
  name: string;
  description: string | null;
  suggestedQuantity: number;
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
  constructor(private readonly customerOrdersApi: CustomerOrdersApiService) {}

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

  loadTodayOrders(): Observable<AdminOrder[]> {
    return this.customerOrdersApi.getTodayOrders().pipe(
      map((orders) => orders.map((order) => this.mapAdminOrder(order)))
    );
  }

  loadPendingReviewOrders(): Observable<AdminOrder[]> {
    return this.customerOrdersApi.getPendingReviewOrders().pipe(
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

  loadPendingCustomers(): Observable<PendingCustomerOrder[]> {
    return this.customerOrdersApi.getPendingCustomers().pipe(
      map((customers) => customers.map((customer) => this.mapPendingCustomer(customer)))
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

  markAdminNoOrder(customerId: string, internalNotes: string | null): Observable<AdminOrder> {
    return this.customerOrdersApi.markAdminCustomerNoOrder(customerId, { internalNotes }).pipe(
      map((order) => this.mapAdminOrder(order))
    );
  }

  private mapAdminOrder(order: AdminOrderSummaryApiResponse): AdminOrder {
    return {
      id: order.orderId,
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
      deliveryNotes: order.deliveryNotes,
      internalNotes: order.internalNotes,
      salesChannel: order.salesChannelName ?? order.salesChannelType ?? 'Sin canal',
      lines: order.lines.map((line) => ({
        id: line.orderLineId,
        productName: line.productName,
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
      summary: event.summary
    };
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
      timeStyle: 'short'
    }).format(new Date(value));
  }
}
