import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../environments/environment';
import {
  AdminDecisionApiValue,
  AdminOrderAuditApiResponse,
  AdminOrderSummaryApiResponse,
  CustomerCurrentOrderSummaryApiResponse,
  CustomerOrderApiResponse,
  CustomerOrdersApiService,
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

export interface AdminOrderAuditEvent {
  id: string;
  eventType: string;
  occurredAt: string;
  actorType: string;
  summary: string;
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

  reviewOrder(orderId: string, decision: AdminDecisionApiValue): Observable<AdminOrder> {
    return this.customerOrdersApi.reviewOrder(orderId, {
      decision,
      internalNotes: null
    }).pipe(map((order) => this.mapAdminOrder(order)));
  }

  loadOrderAudit(orderId: string): Observable<AdminOrderAuditEvent[]> {
    return this.customerOrdersApi.getOrderAudit(orderId).pipe(
      map((events) => events.map((event) => this.mapAuditEvent(event)))
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

  private mapAuditEvent(event: AdminOrderAuditApiResponse): AdminOrderAuditEvent {
    return {
      id: event.id,
      eventType: event.eventType,
      occurredAt: this.formatDateTime(event.occurredAt),
      actorType: event.actorType,
      summary: event.summary
    };
  }

  private formatRequestedDelivery(order: AdminOrderSummaryApiResponse): string {
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

  private formatDateTime(value: string): string {
    return new Intl.DateTimeFormat('es-MX', {
      dateStyle: 'short',
      timeStyle: 'short'
    }).format(new Date(value));
  }
}
