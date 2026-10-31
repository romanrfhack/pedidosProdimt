import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { CustomerOrderApiResponse } from './customer-orders-api.service';

export interface CustomerCaptureProduct {
  productId: string;
  productName: string;
  unitCode: string | null;
  interpretationPosition: number | null;
  captureAlias: string | null;
  sortOrder: number;
}

export interface CustomerCaptureLine {
  productId: string;
  productName: string;
  quantity: number;
  unitCode: string | null;
  notes?: string | null;
}

export interface CustomerNextOrder {
  customerId: string;
  customerName: string;
  deliveryDate: string;
  serverNow: string;
  profileVersion: number;
  isProfileApproved: boolean;
  hasActiveOrder: boolean;
  products: CustomerCaptureProduct[];
  currentOrder: {
    orderId: string;
    deliveryDate: string;
    status: string;
    submittedAt: string;
    deliveryNotes: string | null;
    lines: CustomerCaptureLine[];
  } | null;
  preferredDeliveryTime: string | null;
  preferredDeliveryWindowStart: string | null;
  preferredDeliveryWindowEnd: string | null;
}

export interface CustomerCapturePreview {
  deliveryDate: string;
  serverNow: string;
  text: string;
  fingerprint: string;
  profileVersion: number;
  lines: Array<CustomerCaptureLine & { interpretationPosition: number; captureAlias: string | null }>;
}

export interface CustomerCaptureRequest {
  operationId: string;
  expectedProfileVersion: number;
  expectedDeliveryDate: string;
  text?: string;
  previewFingerprint?: string;
  lines?: Array<{ productId: string; quantity: number; expectedUnitCode: string | null; notes: string | null }>;
  deliveryNotes: string | null;
}

export interface CustomerNoOrderRequest { operationId: string; expectedDeliveryDate: string; }
export interface CustomerCaptureReceipt { order: CustomerOrderApiResponse; replayed: boolean; }

@Injectable({ providedIn: 'root' })
export class CustomerCaptureApiService {
  private readonly http = inject(HttpClient);

  getNext(customerId: string): Observable<CustomerNextOrder> {
    return this.http.get<CustomerNextOrder>(this.url(customerId, 'next'));
  }

  preview(customerId: string, text: string, expectedDeliveryDate: string): Observable<CustomerCapturePreview> {
    return this.http.post<CustomerCapturePreview>(this.url(customerId, 'preview'), { text, expectedDeliveryDate });
  }

  submit(customerId: string, request: CustomerCaptureRequest): Observable<CustomerCaptureReceipt> {
    return this.http.post<CustomerCaptureReceipt>(this.url(customerId, 'capture'), request);
  }

  markNoOrder(customerId: string, request: CustomerNoOrderRequest): Observable<CustomerCaptureReceipt> {
    return this.http.post<CustomerCaptureReceipt>(this.url(customerId, 'next/no-order'), request);
  }

  private url(customerId: string, action: string): string {
    return `${environment.apiBaseUrl}/api/customer-orders/${customerId}/${action}`;
  }
}
