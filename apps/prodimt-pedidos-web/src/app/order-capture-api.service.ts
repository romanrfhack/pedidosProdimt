import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { AdminCustomerOrderingProfileApiResponse } from './admin-catalog-api.service';
import { AdminOrderSummaryApiResponse } from './customer-orders-api.service';

export interface CapturePreviewLine { productId: string; productName: string; quantity: number; unitCode: string; interpretationPosition: number; captureAlias: string | null; }
export interface CapturePreview {
  customerId: string; serviceDate: string; text: string; fingerprint: string;
  profile: AdminCustomerOrderingProfileApiResponse; lines: CapturePreviewLine[];
  preferredDeliveryTime: string | null; preferredDeliveryWindowStart: string | null; preferredDeliveryWindowEnd: string | null;
}
export interface CaptureOrderRequest {
  operationId: string; expectedProfileVersion: number; serviceDate: string;
  text?: string; previewFingerprint?: string;
  lines?: Array<{ productId: string; quantity: number; expectedUnitCode: string | null; notes: string | null }>;
  confirmSeparateDelivery: boolean; deliveryNotes: string | null; internalNotes: string | null;
}
@Injectable({ providedIn: 'root' })
export class OrderCaptureApiService {
  private readonly http = inject(HttpClient);
  preview(customerId: string, text: string, serviceDate: string): Observable<CapturePreview> {
    return this.http.post<CapturePreview>(`${environment.apiBaseUrl}/api/admin/customers/${customerId}/orders/preview`, { text, serviceDate });
  }
  submit(customerId: string, request: CaptureOrderRequest): Observable<{ order: AdminOrderSummaryApiResponse; replayed: boolean }> {
    return this.http.post<{ order: AdminOrderSummaryApiResponse; replayed: boolean }>(`${environment.apiBaseUrl}/api/admin/customers/${customerId}/orders/capture`, request);
  }
}
