import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface OperationsLine {
  orderLineId: string;
  productId: string;
  productName: string;
  unitCode: string | null;
  quantity: number;
  assignedMachineId: string | null;
  assignedMachineNumber: number | null;
  assignedMachineName: string | null;
  notes: string | null;
  requiresMachine?: boolean;
}

export interface OperationsOrder {
  orderId: string;
  orderVersion: number;
  customerId: string | null;
  customerName: string;
  sequenceNumber: number;
  status: string;
  routeCode: string | null;
  routeStopOrder: number | null;
  hasStoreKey: boolean | null;
  paymentTermCode: string | null;
  internalDeliveryInstructions: string | null;
  requestedDeliveryTime: string | null;
  requestedDeliveryWindowStart: string | null;
  requestedDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
  orderingProfileVersion: number | null;
  lines: OperationsLine[];
}

export interface OperationsDemand {
  machineId: string | null;
  machineNumber: number | null;
  machineName: string | null;
  productId: string;
  productName: string;
  unitCode: string;
  quantity: number;
}

export interface OperationsPlan {
  date: string;
  routeCode: string | null;
  demandLabel: string;
  preparationLabel: string;
  orders: OperationsOrder[];
  machineDemand: OperationsDemand[];
  pendingReview: {
    orderId: string;
    customerId: string | null;
    customerName: string;
    sequenceNumber: number;
    reviewReason: string | null;
  }[];
  warnings: { code: string; message: string; orderId: string | null; orderLineId: string | null }[];
}

export interface OperationsChange {
  id: string;
  orderId: string;
  orderVersion: number;
  version: number;
  kind: 'correction' | 'review';
  customerId: string | null;
  customerName: string;
  routeCode: string | null;
  machineId: string | null;
  machineNumber: number | null;
  machineName: string | null;
  reason: string;
  actorDisplayName: string | null;
  occurredAt: string;
  acknowledgedAt: string | null;
  acknowledgedBy: string | null;
  lines: Array<{
    productId: string;
    productName: string;
    unitCode: string | null;
    beforeQuantity: number;
    afterQuantity: number;
    deltaQuantity: number;
    beforeNotes: string | null;
    afterNotes: string | null;
  }>;
}

export interface OperationsChanges {
  date: string;
  pendingCount: number;
  changes: OperationsChange[];
}

export interface AcknowledgeChangeRequest {
  operationKey: string;
  expectedVersion: number;
}

export interface ProductionMachine {
  machineId: string; machineNumber: number; machineName: string;
  isActive: boolean;
  assignedQuantity: number; producedQuantity: number; pendingQuantity: number;
  allocatedQuantity: number; availableQuantity: number;
}
export interface ProductionBatch {
  id: string; date: string; machineId: string; machineNumber: number | null; machineName: string | null;
  productId: string; productName: string; unitCode: string; quantity: number;
  allocatedQuantity: number; availableQuantity: number; createdAt: string;
}
export interface ProductionLine extends OperationsLine {
  allocatedQuantity: number; pendingAllocationQuantity: number;
}
export interface ProductionOrder {
  orderId: string; customerId: string | null; customerName: string; sequenceNumber: number;
  routeCode: string | null; routeStopOrder: number | null; orderVersion: number;
  internalDeliveryInstructions: string | null; requestedDeliveryTime: string | null;
  requestedDeliveryWindowStart: string | null; requestedDeliveryWindowEnd: string | null;
  lines: ProductionLine[];
}
export interface ProductionAllocation {
  id: string; batchId: string; orderId: string; orderLineId: string; customerName: string;
  quantity: number; createdAt: string; reversedAt: string | null; reverseReason: string | null;
}
export interface ProductionRelease {
  id: string; machineId: string; machineNumber: number; machineName: string;
  orderId: string; orderLineId: string; customerName: string; productName: string;
  quantity: number; createdAt: string;
}
export interface ProductionDashboard {
  date: string; routeCode: string | null; summaryScope: string;
  machines: ProductionMachine[]; batches: ProductionBatch[];
  orders: ProductionOrder[]; allocations: ProductionAllocation[]; releases?: ProductionRelease[];
}
export interface ProductionCommandResponse { id: string; orderVersion: number | null; replayed: boolean; }
export interface RegisterProductionRequest {
  operationId: string; date: string; machineId: string; productId: string; quantity: number; notes: string | null;
}
export interface AllocateProductionRequest {
  operationId: string; batchId: string; orderId: string; orderLineId: string; quantity: number; expectedOrderVersion: number;
}
export interface AssignProductionMachineRequest {
  operationId: string; machineId: string | null; expectedOrderVersion: number; reason: string;
}

@Injectable({ providedIn: 'root' })
export class AdminOperationsApiService {
  private readonly http = inject(HttpClient);

  getPlan(date: string, routeCode: string): Observable<OperationsPlan> {
    let params = new HttpParams().set('date', date);
    if (routeCode.trim()) params = params.set('routeCode', routeCode.trim());
    return this.http.get<OperationsPlan>(`${environment.apiBaseUrl}/api/admin/operations/plan`, { params });
  }

  getChanges(date: string, routeCode: string): Observable<OperationsChanges> {
    let params = new HttpParams().set('date', date).set('pendingOnly', false);
    if (routeCode.trim()) params = params.set('routeCode', routeCode.trim());
    return this.http.get<OperationsChanges>(`${environment.apiBaseUrl}/api/admin/operations/changes`, { params });
  }

  acknowledge(changeId: string, request: AcknowledgeChangeRequest): Observable<OperationsChange> {
    return this.http.post<OperationsChange>(`${environment.apiBaseUrl}/api/admin/operations/changes/${changeId}/acknowledge`, request);
  }

  getProduction(date: string, routeCode: string): Observable<ProductionDashboard> {
    let params = new HttpParams().set('date', date);
    if (routeCode.trim()) params = params.set('routeCode', routeCode.trim());
    return this.http.get<ProductionDashboard>(`${environment.apiBaseUrl}/api/admin/operations/production`, { params });
  }
  registerProduction(request: RegisterProductionRequest): Observable<ProductionCommandResponse> {
    return this.http.post<ProductionCommandResponse>(`${environment.apiBaseUrl}/api/admin/operations/production/batches`, request);
  }
  allocateProduction(request: AllocateProductionRequest): Observable<ProductionCommandResponse> {
    return this.http.post<ProductionCommandResponse>(`${environment.apiBaseUrl}/api/admin/operations/production/allocations`, request);
  }
  reverseAllocation(id: string, request: { operationId: string; reason: string }): Observable<ProductionCommandResponse> {
    return this.http.post<ProductionCommandResponse>(`${environment.apiBaseUrl}/api/admin/operations/production/allocations/${id}/reverse`, request);
  }
  assignMachine(orderId: string, lineId: string, request: AssignProductionMachineRequest): Observable<ProductionCommandResponse> {
    return this.http.post<ProductionCommandResponse>(`${environment.apiBaseUrl}/api/admin/operations/orders/${orderId}/lines/${lineId}/machine`, request);
  }
}
