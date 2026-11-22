import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../environments/environment';
import type { TripCollections } from './collection-api.service';

export interface CashReceipt {
  id: string; createdAt: string; receiverName: string; expectedAmount: number;
  receivedAmount: number; difference: number; reason: string | null;
}
export interface CashTrip {
  tripId: string; date: string; routeCode: string; routeName: string; tripNumber: number;
  driverUserId: string; driverName: string; departedAt: string | null; status: string; version: string;
  expectedAmount: number; accountedAmount: number; receivedAmount: number; pendingAmount: number;
  difference: number; receipts: CashReceipt[];
}
export interface CashCustomer {
  customerId: string; customerName: string; dayChargeAmount: number | null;
  dayPaidAmount: number; balance: number | null; creditBalance: number | null; pricePending: boolean;
}
export interface CustomerAccount {
  customerId: string; customerName: string; chargesTotal: number | null; paidTotal: number;
  balance: number | null; creditBalance: number | null; pricePending: boolean; allocationPolicy: string;
  charges: { id: string; date: string; orderId: string; orderLineId: string; productName: string;
    unitCode: string; requestedQuantity: number; quantity: number; isCredit: boolean; unitPrice: number | null;
    amount: number | null; routeCode: string; tripNumber: number; driverName: string; recorded: boolean }[];
  payments: { id: string; customerId: string; customerName: string; amount: number; createdAt: string;
    driverName: string; actorId: string | null; actorName: string | null; serviceDate: string; tripId: string; routeCode: string; tripNumber: number }[];
}
export interface CashDashboard {
  date: string; routeCode: string | null; trips: CashTrip[]; expectedAmount: number;
  receivedAmount: number; pendingAmount: number; difference: number; myReceivedAmount: number; customers: CashCustomer[];
}
export interface ReceiveCashRequest {
  operationId: string; tripId: string; expectedVersion: string; declaredAmount: number | null;
  receivedAmount: number; reason: string | null;
}
export interface PendingCashReceipt { userId: string; date: string; body: ReceiveCashRequest; }

@Injectable({ providedIn: 'root' })
export class CashRegisterApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl + '/api/admin/collections';
  dashboard(date: string | null, routeCode: string) {
    let params = new HttpParams();
    if (date) params = params.set('date', date);
    if (routeCode.trim()) params = params.set('routeCode', routeCode.trim());
    return this.http.get<CashDashboard>(this.base + '/cashier', { params });
  }
  trip(tripId: string) {
    return this.http.get<TripCollections>(this.base + '/trips/' + encodeURIComponent(tripId));
  }
  account(customerId: string) {
    return this.http.get<CustomerAccount>(this.base + '/customers/' + encodeURIComponent(customerId));
  }
  receive(body: ReceiveCashRequest) {
    return this.http.post<{ id: string; replayed?: boolean }>(this.base + '/receipts', body);
  }
}
