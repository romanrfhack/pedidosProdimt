import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface DeliveryRoute {
  code: string; name: string; defaultDriverUserId: string | null; isActive: boolean; version: number;
  dayDriverUserId?: string | null; dayDriverVersion?: number;
}
export interface DeliveryDriver { id: string; displayName: string; userName: string; }
export interface DeliveryLine {
  orderLineId: string; productId: string; productName: string; unitCode: string | null;
  requestedQuantity: number; deliveredQuantity: number; reservedQuantity: number; availableQuantity: number;
}
export interface DeliveryInstructions {
  mapsUrl: string | null; address: string | null; requestedDeliveryTime: string | null;
  requestedDeliveryWindowStart: string | null; requestedDeliveryWindowEnd: string | null;
  hasStoreKey: boolean | null; paymentTermCode: string | null;
  deliveryNotes: string | null; internalDeliveryInstructions: string | null;
}
export interface DeliveryOrder extends DeliveryInstructions {
  orderId: string; customerId: string; customerName: string; sequenceNumber: number;
  routeCode: string | null; routeStopOrder: number | null; orderVersion: number; locationVersion: number;
  lines: DeliveryLine[];
}
export interface DeliveryAssignment extends DeliveryInstructions {
  id: string; orderId: string; orderLineId: string; customerId: string; customerName: string;
  sequenceNumber: number; routeStopOrder: number | null; productName: string; unitCode: string;
  requestedQuantity: number; assignedQuantity: number; releasedQuantity: number;
  countedQuantity: number | null; loadedQuantity: number | null; deliveredQuantity: number;
  returnedQuantity: number; transferredOutQuantity: number; transferredInQuantity: number;
  custodyQuantity: number; pendingQuantity: number; status: string; version: number;
  currentOrderVersion: number; hasOrderChanges: boolean; orderIsComplete: boolean;
}
export interface DeliveryEvent {
  id: string; assignmentId: string | null; type: string; quantity: number | null;
  previousQuantity: number | null; reason: string | null; notes: string | null;
  actorName: string | null; occurredAt: string;
}
export interface DeliveryTrip {
  id: string; date: string; routeCode: string; routeName: string; tripNumber: number;
  driverUserId: string; driverName: string; status: string; version: number;
  createdAt: string; closedAt: string | null; assignments: DeliveryAssignment[]; events: DeliveryEvent[];
  departedAt?: string | null; plannedOrders?: DeliveryOrder[]; canChangeDriver?: boolean;
}
export interface DeliveryDashboard {
  date: string; routes: DeliveryRoute[]; drivers: DeliveryDriver[]; trips: DeliveryTrip[]; orders: DeliveryOrder[];
  canPrepare?: boolean; operationalDate?: boolean;
}
export interface DeliveryCommandResult { id: string; version: number | null; replayed: boolean; }
export interface DeliveryPendingCommand {
  operationId: string; path: string; method: 'POST' | 'PUT'; body: Record<string, unknown>;
  title: string; management: boolean; tripId: string | null; summary: string;
}

export function safeDeliveryMapsUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.username || url.password || url.port) return null;
    const host = url.hostname.toLowerCase();
    const allowed = host === 'maps.app.goo.gl' || host === 'maps.google.com' || host === 'maps.google.com.mx'
      || ((host === 'www.google.com' || host === 'google.com' || host === 'google.com.mx' || host === 'www.google.com.mx') && /^\/maps(?:\/|$)/.test(url.pathname))
      || (host === 'goo.gl' && url.pathname.startsWith('/maps/'));
    return allowed ? url.href : null;
  } catch { return null; }
}

@Injectable({ providedIn: 'root' })
export class DeliveryApiService {
  private readonly http = inject(HttpClient);
  getDashboard(date: string | null, management: boolean): Observable<DeliveryDashboard> {
    const params = date ? new HttpParams().set('date', date) : new HttpParams();
    return this.http.get<DeliveryDashboard>(`${environment.apiBaseUrl}${management ? '/api/admin/delivery' : '/api/delivery'}`, { params });
  }
  getTrip(id: string, management: boolean): Observable<DeliveryTrip> {
    return this.http.get<DeliveryTrip>(`${environment.apiBaseUrl}${management ? '/api/admin/delivery' : '/api/delivery'}/trips/${encodeURIComponent(id)}`);
  }
  execute(command: DeliveryPendingCommand): Observable<DeliveryCommandResult> {
    // Replays only commands from this module, without persisting tokens or accepting arbitrary URLs.
    if (!/^\/api\/(?:admin\/)?delivery(?:\/|$)/.test(command.path)) throw new Error('Operación de reparto no válida.');
    return this.http.request<DeliveryCommandResult>(command.method, `${environment.apiBaseUrl}${command.path}`, { body: command.body });
  }
  remember(userId: string, command: DeliveryPendingCommand): void {
    sessionStorage.setItem(this.storageKey(userId), JSON.stringify(command));
  }
  pending(userId: string): DeliveryPendingCommand | null {
    try {
      const command = JSON.parse(sessionStorage.getItem(this.storageKey(userId)) ?? 'null') as DeliveryPendingCommand | null;
      if (!command || !['POST', 'PUT'].includes(command.method) || !command.body || typeof command.body !== 'object'
        || command.operationId !== command.body['operationId'] || !/^\/api\/(?:admin\/)?delivery(?:\/|$)/.test(command.path)) return null;
      return command;
    } catch { return null; }
  }
  clear(userId: string): void { try { sessionStorage.removeItem(this.storageKey(userId)); } catch { /* A retained receipt can be safely replayed. */ } }
  private storageKey(userId: string): string { return `prodimt.delivery.pending.v1.${userId}`; }
}
