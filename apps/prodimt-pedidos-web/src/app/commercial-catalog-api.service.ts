import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';

export interface PriceRevision {
  id: string; version: number; unitPrice: number | null; currencyCode: string; unitCode: string;
  productName: string; effectiveFrom: string; effectiveUntil: string | null; recordedAt: string;
  actorId: string; actorDisplayName: string | null; reason: string; operationId: string; supersededForDate?: boolean;
}
export interface CustomerProductPrice {
  customerId: string; productId: string; productName: string; unitCode: string | null; version: number;
  asOfDate: string; unitPrice: number | null; currencyCode: string; appliedRevisionId: string | null;
  history: PriceRevision[]; replayed?: boolean; unchanged?: boolean;
}
export interface PriceChange {
  unitPrice: number | null; effectiveFrom: string; reason: string; expectedVersion: number;
  operationId: string; expectedUnitCode: string;
}
export interface ConsolidationProduct { id: string; name: string; unitCode: string | null; description?: string | null; }
export interface DuplicateGroup { suggestedName: string; suggestedTargetId: string; products: ConsolidationProduct[]; }
export interface ConsolidationSelection {
  targetProductId: string; sourceProductIds: string[]; sharedName: string; description: string | null;
}
export interface ConsolidationPreview {
  targetProductId: string; sharedName: string; unitCode: string | null; sources: ConsolidationProduct[];
  customers: { customerId: string; name: string; profileVersion: number }[];
  priceAssignments: number; fingerprint: string; errors: string[]; canApply: boolean;
}
export interface ConsolidationRequest extends ConsolidationSelection { fingerprint: string; reason: string; operationId: string; }
export interface ConsolidationResult {
  operationId: string; targetProductId: string; sharedName: string; archivedProducts: number;
  updatedProfiles: number; priceAssignments: number; replayed: boolean;
}

@Injectable({ providedIn: 'root' })
export class CommercialCatalogApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl.replace(/\/$/, '');
  getPrices(customerId: string) { return this.http.get<CustomerProductPrice[]>(`${this.base}/api/admin/customers/${customerId}/prices`); }
  changePrice(customerId: string, productId: string, request: PriceChange) {
    return this.http.post<CustomerProductPrice>(`${this.base}/api/admin/customers/${customerId}/products/${productId}/prices`, request);
  }
  getDuplicateGroups() { return this.http.get<DuplicateGroup[]>(`${this.base}/api/admin/products/consolidation/candidates`); }
  previewConsolidation(request: ConsolidationSelection) {
    return this.http.post<ConsolidationPreview>(`${this.base}/api/admin/products/consolidation/preview`, request);
  }
  consolidate(request: ConsolidationRequest) {
    return this.http.post<ConsolidationResult>(`${this.base}/api/admin/products/consolidation/apply`, request);
  }
}

export function priceText(value: number | null | undefined): string {
  return value === null || value === undefined ? 'Sin precio' : new Intl.NumberFormat('es-MX',
    { style: 'currency', currency: 'MXN', minimumFractionDigits: 2, maximumFractionDigits: 4 }).format(value);
}
export function priceDate(value: string): string { return value.slice(0, 10).split('-').reverse().join('/'); }
export function priceRecordedAt(value: string): string {
  return new Intl.DateTimeFormat('es-MX', { timeZone: 'America/Mexico_City', day: '2-digit', month: '2-digit',
    year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(new Date(value));
}
