import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { AdminOrderDetailApiResponse, SubmitCustomerOrderLineApiRequest } from './customer-orders-api.service';

export interface DailyOrdersRequest {
  content: string;
  fileName: string | null;
  source: string;
  batchKey: string;
}

export interface DailyOrdersResult {
  batchKey: string;
  canApply: boolean;
  applied: boolean;
  replayed: boolean;
  orders: Array<{
    externalOrderKey: string;
    customerId: string;
    customerName: string;
    serviceDate: string;
    action: 'order' | 'no_order';
    disposition: 'create' | 'existing';
    orderId: string | null;
    lines: Array<{ productId: string; productName: string; quantity: number; unitCode: string; notes: string | null }>;
  }>;
  errors: Array<{ rowNumber: number | null; message: string }>;
  warnings: string[];
}

export interface CorrectOrderRequest {
  operationKey: string;
  expectedVersion: number;
  reason: string;
  lines: SubmitCustomerOrderLineApiRequest[];
}

@Injectable({ providedIn: 'root' })
export class AdminDailyOrdersApiService {
  private readonly http = inject(HttpClient);

  validate(request: DailyOrdersRequest): Observable<DailyOrdersResult> {
    return this.http.post<DailyOrdersResult>(`${environment.apiBaseUrl}/api/admin/daily-orders/validate`, request);
  }

  apply(request: DailyOrdersRequest): Observable<DailyOrdersResult> {
    return this.http.post<DailyOrdersResult>(`${environment.apiBaseUrl}/api/admin/daily-orders/apply`, request);
  }

  correct(orderId: string, request: CorrectOrderRequest): Observable<AdminOrderDetailApiResponse> {
    return this.http.post<AdminOrderDetailApiResponse>(`${environment.apiBaseUrl}/api/admin/orders/${orderId}/corrections`, request);
  }
}
