import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { Subscription } from 'rxjs';
import { environment } from '../environments/environment';

export interface AdditionalProduct { productId: string; productName: string; unitCode: string; }
export interface ProductSetupOrder {
  orderId: string; serviceDate: string; quantity: number | null; unitCode: string | null;
  source: string; requestedAt: string; requestedBy: string | null; isActive: boolean;
  unitPrice: number | null; pricePending: boolean;
}
export interface ProductSetup {
  id: string; version: number; customerId: string; customerName: string;
  productId: string; productName: string; unitCode: string; isResolved: boolean;
  isConfigured: boolean; createdAt: string; lastRequestedAt: string; orders: ProductSetupOrder[];
}
export interface ProductSetupPreview {
  id: string; version: number; fingerprint: string; canComplete: boolean; blockers: string[];
  lines: Array<{ orderId: string; orderVersion: number; orderLineId: string | null;
    serviceDate: string; quantity: number | null; unitCode: string | null; isActive: boolean;
    currentPrice: number | null; proposedPrice: number | null; revisionId: string | null;
    effectiveFrom: string | null; applyPrice: boolean }>;
}
export interface CompleteProductSetup {
  operationId: string; expectedVersion: number; previewFingerprint: string; reason: string;
}
export interface ProductSetupCompletion { id: string; version: number; resolvedAt: string; pricedLines: number; replayed: boolean; }

@Injectable({ providedIn: 'root' })
export class ProductSetupApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/admin/product-setups`;
  readonly pendingCount = signal<number | null>(null);
  private countRequest: Subscription | null = null;
  getAdditional(customerId: string, admin: boolean): Observable<AdditionalProduct[]> {
    const path = admin ? `admin/customers/${customerId}` : `customer-orders/${customerId}`;
    return this.http.get<AdditionalProduct[]>(`${environment.apiBaseUrl}/api/${path}/additional-products`);
  }
  getOpen(): Observable<ProductSetup[]> { return this.http.get<ProductSetup[]>(this.base); }
  get(id: string): Observable<ProductSetup> { return this.http.get<ProductSetup>(`${this.base}/${id}`); }
  preview(id: string): Observable<ProductSetupPreview> { return this.http.get<ProductSetupPreview>(`${this.base}/${id}/resolution-preview`); }
  complete(id: string, value: CompleteProductSetup): Observable<ProductSetupCompletion> {
    return this.http.post<ProductSetupCompletion>(`${this.base}/${id}/complete`, value);
  }
  refreshCount(): void {
    this.countRequest?.unsubscribe();
    this.countRequest = this.http.get<{ pendingCount: number }>(`${this.base}/count`).subscribe({
      next: result => this.pendingCount.set(result.pendingCount), error: () => this.pendingCount.set(null)
    });
  }
  clearCount(): void { this.countRequest?.unsubscribe(); this.pendingCount.set(null); }
}
