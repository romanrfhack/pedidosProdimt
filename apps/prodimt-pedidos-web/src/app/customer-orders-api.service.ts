import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export type AdminDecisionApiValue = 'Accepted' | 'Rejected' | 'AcceptedWithChanges';

export interface CustomerOrderTodayApiResponse {
  customerId: string;
  customerName: string;
  orderDate: string;
  preferredDeliveryTime: string | null;
  preferredDeliveryWindowStart: string | null;
  preferredDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
  currentOrder: CustomerCurrentOrderSummaryApiResponse | null;
  products: CustomerOrderProductApiResponse[];
}

export interface CustomerCurrentOrderSummaryApiResponse {
  orderId: string;
  status: string;
  sequenceNumber: number;
  submittedAt: string;
  isLate: boolean;
  requiresAdminReview: boolean;
  adminReviewReason: string | null;
}

export interface CustomerOrderProductApiResponse {
  productId: string;
  name: string;
  description: string | null;
  suggestedQuantity: number;
}

export interface SubmitCustomerOrderApiRequest {
  lines: SubmitCustomerOrderLineApiRequest[];
}

export interface SubmitCustomerOrderLineApiRequest {
  productId: string;
  quantity: number;
  notes: string | null;
}

export interface CustomerOrderApiResponse extends CustomerCurrentOrderSummaryApiResponse {
  customerId: string;
  orderDate: string;
}

export interface AdminOrderSummaryApiResponse {
  orderId: string;
  customerId: string | null;
  customerName: string;
  orderDate: string;
  submittedAt: string;
  status: string;
  sequenceNumber: number;
  isLate: boolean;
  requiresAdminReview: boolean;
  adminReviewReason: string | null;
  requestedDeliveryTime: string | null;
  requestedDeliveryWindowStart: string | null;
  requestedDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
  adminDecision: string | null;
}

export interface AdminOrderAuditApiResponse {
  id: string;
  orderId: string;
  customerId: string | null;
  eventType: string;
  occurredAt: string;
  actorType: string;
  actorId: string | null;
  actorDisplayName: string | null;
  orderStatus: string | null;
  adminReviewReason: string | null;
  adminDecision: string | null;
  summary: string;
  metadataJson: string | null;
}

export interface ReviewOrderApiRequest {
  decision: AdminDecisionApiValue;
  internalNotes: string | null;
}

@Injectable({ providedIn: 'root' })
export class CustomerOrdersApiService {
  private readonly http = inject(HttpClient);

  getToday(customerId = environment.demoCustomerId): Observable<CustomerOrderTodayApiResponse> {
    return this.http.get<CustomerOrderTodayApiResponse>(
      `${environment.apiBaseUrl}/api/customer-orders/${customerId}/today`
    );
  }

  submitOrder(
    customerId: string,
    request: SubmitCustomerOrderApiRequest
  ): Observable<CustomerOrderApiResponse> {
    return this.http.post<CustomerOrderApiResponse>(
      `${environment.apiBaseUrl}/api/customer-orders/${customerId}/submit`,
      request
    );
  }

  markNoOrder(customerId = environment.demoCustomerId): Observable<CustomerOrderApiResponse> {
    return this.http.post<CustomerOrderApiResponse>(
      `${environment.apiBaseUrl}/api/customer-orders/${customerId}/no-order`,
      {}
    );
  }

  getTodayOrders(): Observable<AdminOrderSummaryApiResponse[]> {
    return this.http.get<AdminOrderSummaryApiResponse[]>(
      `${environment.apiBaseUrl}/api/admin/orders/today`
    );
  }

  getPendingReviewOrders(): Observable<AdminOrderSummaryApiResponse[]> {
    return this.http.get<AdminOrderSummaryApiResponse[]>(
      `${environment.apiBaseUrl}/api/admin/orders/pending-review`
    );
  }

  reviewOrder(orderId: string, request: ReviewOrderApiRequest): Observable<AdminOrderSummaryApiResponse> {
    return this.http.post<AdminOrderSummaryApiResponse>(
      `${environment.apiBaseUrl}/api/admin/orders/${orderId}/review`,
      request
    );
  }

  getOrderAudit(orderId: string): Observable<AdminOrderAuditApiResponse[]> {
    return this.http.get<AdminOrderAuditApiResponse[]>(
      `${environment.apiBaseUrl}/api/admin/orders/${orderId}/audit`
    );
  }
}
