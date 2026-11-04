import type { AllocateProductionRequest, AssignProductionMachineRequest, RegisterProductionRequest } from './admin-operations-api.service';

type CommandBody =
  | { kind: 'batch'; request: RegisterProductionRequest }
  | { kind: 'allocation'; request: AllocateProductionRequest }
  | { kind: 'machine'; orderId: string; lineId: string; request: AssignProductionMachineRequest }
  | { kind: 'reverse'; allocationId: string; request: { operationId: string; reason: string } };

/** No credentials, client snapshots or arbitrary URLs are stored in a retry. */
export type PendingProductionCommand = CommandBody & { version: 1; userId: string; date: string };

const invalid = 'No se pudo recuperar una operación de producción guardada. No captures otra vez los mismos kilos; conserva este navegador y solicita revisar la referencia pendiente.';
const unavailable = 'No se pudo conservar la referencia de producción en este navegador. No se envió una nueva operación. Habilita el almacenamiento y vuelve a intentar.';
const text = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const quantity = (value: unknown) => typeof value === 'number' && Number.isFinite(value) && value > 0 && Math.abs(value * 100 - Math.round(value * 100)) < 0.000001;
const onlyKeys = (value: Record<string, unknown>, keys: string[]) => Object.keys(value).every(key => keys.includes(key));
const date = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
  && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
const reason = (value: unknown) => typeof value === 'string' && !!value.trim() && value.length <= 500;

function isCommand(value: unknown, userId: string): value is PendingProductionCommand {
  if (!record(value) || value['version'] !== 1 || value['userId'] !== userId
    || !date(value['date']) || !record(value['request'])
    || !onlyKeys(value, ['version', 'userId', 'date', 'kind', 'request', ...(value['kind'] === 'machine' ? ['orderId', 'lineId'] : value['kind'] === 'reverse' ? ['allocationId'] : [])])) return false;
  const request = value['request'];
  if (typeof request['operationId'] !== 'string' || !/^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(request['operationId'])) return false;
  switch (value['kind']) {
    case 'batch': return onlyKeys(request, ['operationId', 'date', 'machineId', 'productId', 'quantity', 'notes']) && request['date'] === value['date'] && text(request['machineId']) && text(request['productId']) && quantity(request['quantity'])
      && (request['notes'] === null || typeof request['notes'] === 'string' && request['notes'].length <= 500);
    case 'allocation': return onlyKeys(request, ['operationId', 'batchId', 'orderId', 'orderLineId', 'quantity', 'expectedOrderVersion']) && text(request['batchId']) && text(request['orderId']) && text(request['orderLineId']) && quantity(request['quantity'])
      && Number.isInteger(request['expectedOrderVersion']) && Number(request['expectedOrderVersion']) > 0;
    case 'machine': return onlyKeys(request, ['operationId', 'machineId', 'expectedOrderVersion', 'reason']) && text(value['orderId']) && text(value['lineId']) && (request['machineId'] === null || text(request['machineId']))
      && Number.isInteger(request['expectedOrderVersion']) && Number(request['expectedOrderVersion']) > 0 && reason(request['reason']);
    case 'reverse': return onlyKeys(request, ['operationId', 'reason']) && text(value['allocationId']) && reason(request['reason']);
    default: return false;
  }
}

/** Small storage boundary: never discard corrupt/uncertain commands or overwrite another operation. */
export class ProductionCommandStore {
  constructor(private readonly storage: () => Storage) {}

  pending(userId: string): PendingProductionCommand[] {
    try {
      const storage = this.storage(), prefix = this.prefix(userId), commands: PendingProductionCommand[] = [];
      for (let index = 0; index < storage.length; index++) {
        const key = storage.key(index);
        if (!key?.startsWith(prefix)) continue;
        const value: unknown = JSON.parse(storage.getItem(key) ?? 'null');
        if (!isCommand(value, userId) || key !== this.key(value)) throw new Error(invalid);
        commands.push(value);
      }
      return commands.sort((a, b) => a.request.operationId.localeCompare(b.request.operationId));
    } catch (error) { throw new Error(error instanceof Error && error.message === invalid ? invalid : unavailable); }
  }

  remember(command: PendingProductionCommand): void {
    if (!isCommand(command, command.userId)) throw new Error(invalid);
    const existing = this.pending(command.userId);
    if (existing.some(value => value.request.operationId !== command.request.operationId)) {
      throw new Error('Hay otra operación de producción sin confirmar. Recupera su resultado antes de registrar otra.');
    }
    try {
      const storage = this.storage(), key = this.key(command), serialized = JSON.stringify(command);
      const previous = storage.getItem(key);
      if (previous !== null && previous !== serialized) throw new Error(invalid);
      storage.setItem(key, serialized);
      if (storage.getItem(key) !== serialized) throw new Error(unavailable);
    } catch (error) { throw new Error(error instanceof Error && error.message === invalid ? invalid : unavailable); }
  }

  /** Only a confirmed result (or definitive initial rejection) may release this exact reference. */
  complete(command: PendingProductionCommand): void {
    try {
      const storage = this.storage(), key = this.key(command), current = storage.getItem(key);
      if (current !== null && current !== JSON.stringify(command)) throw new Error(invalid);
      storage.removeItem(key);
      if (storage.getItem(key) !== null) throw new Error('cleanup');
    } catch (error) {
      throw new Error(error instanceof Error && error.message === invalid ? invalid
        : 'El resultado fue recibido, pero la referencia sigue guardada. Reintenta su confirmación antes de registrar otra operación.');
    }
  }

  private prefix(userId: string): string {
    if (!text(userId)) throw new Error(invalid);
    return `prodimt.production.pending.v1.${encodeURIComponent(userId)}.`;
  }
  private key(command: PendingProductionCommand): string { return this.prefix(command.userId) + command.request.operationId; }
}
