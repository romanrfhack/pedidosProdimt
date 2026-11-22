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
    if (!this.validCommand(command)) throw new Error('La operación guardada no es válida.');
    const pending = this.pendingCommands(userId);
    const serialized = JSON.stringify(command);
    const previousCommand = pending.find(value => value.operationId === command.operationId);
    if (previousCommand && JSON.stringify(previousCommand) !== serialized) throw new Error('La referencia guardada tiene datos diferentes.');
    if (!previousCommand && pending.length) throw new Error('Hay otra operación sin confirmar. Recupera su resultado antes de registrar más entregas.');
    if (Object.hasOwn(command.body, 'collectedAmount')) {
      const key = this.financialPrefix(userId) + command.operationId;
      const previous = localStorage.getItem(key);
      if (previous !== null && previous !== serialized) throw new Error('La referencia guardada cambió. Conserva este navegador para revisar el resultado.');
      localStorage.setItem(key, serialized);
      if (localStorage.getItem(key) !== serialized) throw new Error('No se pudo guardar la referencia de cobro.');
    }
    sessionStorage.setItem(this.storageKey(userId), serialized);
  }
  pending(userId: string): DeliveryPendingCommand | null {
    return this.pendingCommands(userId)[0] ?? null;
  }
  clear(userId: string, command?: DeliveryPendingCommand): void {
    const legacyKey = this.storageKey(userId), previous = sessionStorage.getItem(legacyKey);
    if (!command) { sessionStorage.removeItem(legacyKey); return; }
    const serialized = JSON.stringify(command);
    if (previous === serialized) sessionStorage.removeItem(legacyKey);
    if (Object.hasOwn(command.body, 'collectedAmount')) {
      const key = this.financialPrefix(userId) + command.operationId;
      const stored = localStorage.getItem(key);
      if (stored !== null && stored !== serialized) throw new Error('La referencia de cobro cambió.');
      localStorage.removeItem(key);
      if (localStorage.getItem(key) !== null) throw new Error('No se pudo retirar la referencia confirmada.');
    }
  }
  private pendingCommands(userId: string): DeliveryPendingCommand[] {
    const commands = new Map<string, DeliveryPendingCommand>();
    const add = (raw: string, key?: string) => {
      const command = JSON.parse(raw) as DeliveryPendingCommand;
      if (!this.validCommand(command) || key && key !== this.financialPrefix(userId) + command.operationId) throw new Error('No se pudo recuperar una referencia de reparto.');
      const previous = commands.get(command.operationId);
      if (previous && JSON.stringify(previous) !== JSON.stringify(command)) throw new Error('La referencia de reparto tiene datos diferentes.');
      commands.set(command.operationId, command);
    };
    const session = sessionStorage.getItem(this.storageKey(userId));
    if (session) add(session);
    for (let index = 0; index < localStorage.length; index++) {
      const key = localStorage.key(index);
      if (key?.startsWith(this.financialPrefix(userId))) add(localStorage.getItem(key) ?? 'null', key);
    }
    return [...commands.values()].sort((first, second) => first.operationId.localeCompare(second.operationId));
  }
  private validCommand(command: DeliveryPendingCommand): boolean {
    return !!command && ['POST', 'PUT'].includes(command.method) && !!command.body && typeof command.body === 'object'
      && command.operationId === command.body['operationId'] && /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(command.operationId)
      && /^\/api\/(?:admin\/)?delivery(?:\/|$)/.test(command.path);
  }
  private financialPrefix(userId: string): string { return `prodimt.delivery.collection.pending.v1.${userId}.`; }
  private storageKey(userId: string): string { return `prodimt.delivery.pending.v1.${userId}`; }
}
