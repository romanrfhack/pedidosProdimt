import { HttpClient } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { Injectable, inject } from '@angular/core';
import { environment } from '../environments/environment';
import type { ProductionCommandResponse } from './admin-operations-api.service';

export interface WorkspaceMachine { id: string; number: number; name: string; }
export interface WorkspaceLine {
  orderLineId: string; productId: string; productName: string;
  quantity: number; allocatedQuantity: number; pendingQuantity: number; releasedQuantity: number;
}
export interface WorkspaceOrder {
  orderId: string; customerId: string | null; customerName: string; sequenceNumber: number;
  routeCode: string | null; routeStopOrder: number | null; orderVersion: number; lines: WorkspaceLine[];
}
export interface WorkspaceLot {
  id: string; productId: string; productName: string; quantity: number;
  allocatedQuantity: number; availableQuantity: number; handedOffQuantity: number; createdAt: string;
}
export interface MachineWorkspace {
  date: string;
  machine: WorkspaceMachine;
  totals: { producedQuantity: number; allocatedQuantity: number; pendingQuantity: number; availableQuantity: number; };
  orders: WorkspaceOrder[];
  batches: WorkspaceLot[];
  products: { id: string; name: string }[];
  notices: { id: string; orderId: string; orderLineId: string; customerName: string; productName: string; quantity: number; createdAt: string }[];
}

export type TeamPosition = 1 | 2 | 3 | 4;
export type TeamAction = 'absence' | 'cover' | 'move' | 'reset';
export interface TeamSlot {
  position: TeamPosition; label: string; regularWorkerId: string | null; regularWorkerName: string | null;
  workerId: string | null; workerName: string | null;
  status: 'regular' | 'absent' | 'covered' | 'moved-out' | 'moved-in' | 'vacant';
}
export interface MachineTeam {
  machineId: string; date: string; today: string; version: string; effectiveDate: string | null; slots: TeamSlot[];
  workers: { id: string; name: string; isActive: boolean }[];
  destinations: WorkspaceMachine[]; canManageWorkers: boolean; canEditDaily: boolean;
  history: { position: TeamPosition; label: string; action: string; workerName: string | null;
    otherMachineId: string | null; createdAt: string; actorName: string | null }[];
}
export interface MachineOperators { users: { id: string; displayName: string }[]; version: string; userIds: string[]; }

type Operation = { operationId: string };
type OrderLine = { orderId: string; orderLineId: string; expectedOrderVersion: number };
export type MachineWrite =
  | { action: 'capture'; body: Operation & OrderLine & { quantity: number } }
  | { action: 'lots'; body: Operation & { productId: string; quantity: number; date: string } }
  | { action: 'allocate'; body: Operation & OrderLine & { batchId: string; quantity: number } }
  | { action: 'release'; body: Operation & { orderId: string; orderLineId: string | null; quantity: number | null; expectedOrderVersion: number } }
  | { action: 'handoff'; body: Operation & { batchId: string; quantity: number } }
  | { action: 'team/daily'; body: Operation & { date: string; expectedVersion: string; position: TeamPosition; action: TeamAction;
      workerId: string | null; otherMachineId: string | null; otherPosition: TeamPosition | null } }
  | { action: 'team/regular'; body: Operation & { effectiveDate: string; expectedVersion: string; slots: { position: TeamPosition; workerId: string | null }[] } }
  | { action: 'workers'; body: Operation & { name: string } }
  | { action: 'operators'; body: Operation & { userIds: string[]; expectedVersion: string } };
export type PendingMachineWrite = MachineWrite & { machineId: string; userId: string };
export const machineWriteActions: ReadonlySet<string> = new Set(['capture', 'lots', 'allocate', 'release', 'handoff', 'team/daily', 'team/regular', 'workers', 'operators']);

@Injectable({ providedIn: 'root' })
export class MachineWorkspaceApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/admin/machine-workspace`;

  machines() { return this.http.get<WorkspaceMachine[]>(`${this.base}/machines`); }
  workspace(machineId: string) { return this.http.get<MachineWorkspace>(`${this.base}/${machineId}`); }
  team(machineId: string, date?: string) { return this.http.get<MachineTeam>(`${this.base}/${machineId}/team`, { params: date ? { date } : {} }); }
  operators(machineId: string) { return this.http.get<MachineOperators>(`${this.base}/machines/${machineId}/operators`); }
  write(command: PendingMachineWrite): Observable<ProductionCommandResponse | MachineOperators> {
    if (command.action === 'operators') return this.http.put<MachineOperators>(this.base + '/machines/' + command.machineId + '/operators', command.body);
    const path = command.action === 'workers' ? '/workers' : `/${command.machineId}/${command.action}`;
    return this.http.post<ProductionCommandResponse>(this.base + path, command.body);
  }
}
