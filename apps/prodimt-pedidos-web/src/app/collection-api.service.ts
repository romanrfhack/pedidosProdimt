import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../environments/environment';

export interface CollectionTicketLine {
  orderId: string; orderLineId: string; productName: string; unitCode: string | null; quantity: number; unitPrice: number | null; amount: number | null;
}
export interface CollectionPreview {
  paymentTermCode: string | null; customerId: string; customerName: string; currencyCode: string;
  ticketLines: CollectionTicketLine[]; ticketTotal: number | null; priorBalance: number | null;
  collectedAmount: number; suggestedAmount: number | null; balance: number | null;
  version: string; pricePending: boolean; amountDue: number | null; creditBalance: number | null;
}
export interface TripCollectionCustomer {
  customerId: string; customerName: string; chargeAmount: number | null; paidAmount: number;
  balance: number | null; pricePending: boolean;
}
export interface TripCollections {
  tripId: string; customers: TripCollectionCustomer[]; chargeAmount: number | null;
  paidAmount: number; pendingCashAmount: number; pricePending: boolean;
}

@Injectable({ providedIn: 'root' })
export class CollectionApiService {
  private readonly http = inject(HttpClient);
  private path(management: boolean): string { return environment.apiBaseUrl + (management ? '/api/admin/delivery' : '/api/delivery'); }

  preview(assignmentId: string, quantity: number, management: boolean) {
    return this.http.get<CollectionPreview>(this.path(management) + '/assignments/' + encodeURIComponent(assignmentId) + '/collection-preview',
      { params: new HttpParams().set('quantity', quantity) });
  }
  trip(tripId: string, management: boolean) {
    return this.http.get<TripCollections>(this.path(management) + '/trips/' + encodeURIComponent(tripId) + '/collections');
  }
}

const currency = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN', minimumFractionDigits: 2, maximumFractionDigits: 2 });
export function collectionMoney(amount: number | null | undefined): string {
  return amount == null ? 'Pendiente' : currency.format(amount);
}
export function parseCollectionAmount(value: string): number | null {
  const normalized = value.trim().replace(',', '.');
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  const amount = Number(normalized), cents = Math.round(amount * 100);
  return Number.isFinite(amount) && Number.isSafeInteger(cents) && cents >= 0 ? cents / 100 : null;
}
