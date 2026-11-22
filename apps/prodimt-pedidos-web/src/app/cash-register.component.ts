import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, ElementRef, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, timeout } from 'rxjs';
import { unitLabel } from './operational-labels';
import { priceText } from './commercial-catalog-api.service';
import { AuthService } from './auth.service';
import { EffectivePermissionsService } from './effective-permissions.service';
import { collectionMoney, parseCollectionAmount, type TripCollections } from './collection-api.service';
import { CashRegisterApiService, type CashDashboard, type CashTrip, type PendingCashReceipt, type CashCustomer, type CustomerAccount, type CashReceipt } from './cash-register-api.service';

type CashDialog = { kind: 'account'; customer: CashCustomer } | { kind: 'receipt'; trip: CashTrip } | { kind: 'history'; trip: CashTrip } | { kind: 'collections'; trip: CashTrip };
const dateTime = new Intl.DateTimeFormat('es-MX', { timeZone: 'America/Mexico_City', dateStyle: 'short', timeStyle: 'short' });

@Component({
  selector: 'app-cash-register',
  imports: [FormsModule],
  templateUrl: './cash-register.component.html',
  styleUrl: './cash-register.component.css'
})
export class CashRegisterComponent {
  private readonly api = inject(CashRegisterApiService);
  private readonly access = inject(EffectivePermissionsService);
  private readonly auth = inject(AuthService);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly destroy = inject(DestroyRef);
  @ViewChild('cashDialog', { static: true }) private element!: ElementRef<HTMLDialogElement>;
  protected readonly money = collectionMoney;
  protected readonly price = priceText;
  protected readonly unitLabel = unitLabel;
  protected date = '';
  protected routeCode = '';
  protected dashboard: CashDashboard | null = null;
  protected loading = false;
  protected saving = false;
  protected error = '';
  protected storageError = '';
  protected dialogError = '';
  protected success = '';
  protected dialog: CashDialog | null = null;
  protected declared = '';
  protected received = '';
  protected reason = '';
  protected receivedEdited = false;
  protected pending: PendingCashReceipt | null = null;
  protected tripCollections: TripCollections | null = null;
  protected account: CustomerAccount | null = null;
  protected section: 'cash' | 'accounts' = 'cash';
  protected detailLoading = false;
  private generation = 0;
  private appliedRouteCode = '';
  private detailGeneration = 0;
  private readonly beforeUnload = (event: BeforeUnloadEvent) => {
    if (this.saving || this.pending || this.dialog?.kind === 'receipt') { event.preventDefault(); event.returnValue = ''; }
  };
  private readonly storageChanged = (event: StorageEvent) => {
    if (event.key === null || event.key.startsWith(this.storagePrefix)) { this.restorePending(); this.render(); }
  };

  constructor() {
    this.restorePending();
    void this.load();
    const refresh = () => { if (!document.hidden && this.dashboard && !this.loading && !this.dialog && !this.saving) void this.load(true); };
    const interval = window.setInterval(refresh, 15_000);
    window.addEventListener('beforeunload', this.beforeUnload);
    window.addEventListener('storage', this.storageChanged);
    document.addEventListener('visibilitychange', refresh);
    this.destroy.onDestroy(() => {
      this.generation++; this.detailGeneration++;
      clearInterval(interval);
      window.removeEventListener('beforeunload', this.beforeUnload);
      window.removeEventListener('storage', this.storageChanged);
      document.removeEventListener('visibilitychange', refresh);
    });
  }

  protected get canReceive(): boolean { return this.access.has('operations.collections.receive'); }
  protected get canWrite(): boolean { return this.canReceive && !this.saving && !this.loading && !this.pending && !this.storageError; }
  protected get receiptDifference(): number | null {
    const expected = parseCollectionAmount(this.declared), actual = parseCollectionAmount(this.received);
    return expected === null || actual === null ? null : Math.round((expected - actual) * 100) / 100;
  }
  protected get customerTotals(): { charged: number | null; paid: number; balance: number | null; credit: number | null } {
    const customers = this.dashboard?.customers ?? [];
    const total = (field: 'dayChargeAmount' | 'dayPaidAmount' | 'balance' | 'creditBalance'): number | null =>
      customers.some(customer => customer[field] === null) ? null
        : customers.reduce((sum, customer) => sum + Math.round((customer[field] ?? 0) * 100), 0) / 100;
    return { charged: total('dayChargeAmount'), paid: total('dayPaidAmount') ?? 0, balance: total('balance'), credit: total('creditBalance') };
  }
  protected quantity(value: number): string { return new Intl.NumberFormat('es-MX', { maximumFractionDigits: 3 }).format(value); }
  protected differenceLabel(value: number | null): string { return value === null || value === 0 ? 'Diferencia' : value > 0 ? 'Faltante' : 'Sobrante'; }
  protected differenceMoney(value: number | null): string { return this.money(value === null ? null : Math.abs(value)); }
  protected latestReceipt(trip: CashTrip): CashReceipt | null {
    return trip.receipts.reduce<CashReceipt | null>((last, receipt) => !last || receipt.createdAt > last.createdAt ? receipt : last, null);
  }
  protected receiptTime(value: string): string {
    return new Intl.DateTimeFormat('es-MX', { timeZone: 'America/Mexico_City', hour: '2-digit', minute: '2-digit' }).format(new Date(value));
  }
  protected timestamp(value: string): string { return dateTime.format(new Date(value)); }

  canLeave(): boolean {
    if (!this.auth.isAdmin()) return true;
    if (this.saving) return false;
    return (!this.pending && this.dialog?.kind !== 'receipt') || window.confirm('¿Salir de Caja? Las referencias sin confirmar se conservarán para esta cuenta.');
  }

  protected async load(background = false): Promise<void> {
    if (this.loading || this.saving) return;
    this.loading = true;
    this.error = '';
    const generation = ++this.generation;
    const date = background ? this.dashboard?.date ?? this.date : this.date;
    try {
      const result = await firstValueFrom(this.api.dashboard(date || null, background ? this.appliedRouteCode : this.routeCode).pipe(timeout(15_000)));
      if (generation !== this.generation || this.destroy.destroyed) return;
      this.dashboard = result;
      this.date = result.date;
      this.appliedRouteCode = result.routeCode ?? '';
    } catch (error) {
      if (generation === this.generation) this.error = this.message(error, 'No se pudo consultar la recepción de efectivo.');
    } finally {
      if (generation === this.generation) { this.loading = false; this.render(); }
    }
  }

  protected queryChanged(): void { this.generation++; this.loading = false; this.dashboard = null; }

  protected receive(trip: CashTrip): void {
    if (!this.canWrite || trip.pendingAmount <= 0) return;
    this.declared = String(trip.pendingAmount);
    this.received = this.declared;
    this.receivedEdited = false;
    this.reason = '';
    this.open({ kind: 'receipt', trip });
  }

  protected declaredChanged(): void { if (!this.receivedEdited) this.received = this.declared; }
  protected receivedChanged(): void { this.receivedEdited = true; }
  protected reasonChanged(event: Event): void { this.reason = (event.target as HTMLInputElement).value; }
  protected history(trip: CashTrip): void { this.open({ kind: 'history', trip }); }

  protected async showCollections(trip: CashTrip): Promise<void> {
    this.tripCollections = null;
    this.detailLoading = true;
    this.open({ kind: 'collections', trip });
    const generation = ++this.detailGeneration;
    try {
      const value = await firstValueFrom(this.api.trip(trip.tripId).pipe(timeout(15_000)));
      if (generation === this.detailGeneration && !this.destroy.destroyed) this.tripCollections = value;
    } catch (error) {
      if (generation === this.detailGeneration) this.dialogError = this.message(error, 'No se pudo consultar el detalle de los cobros.');
    } finally {
      if (generation === this.detailGeneration) { this.detailLoading = false; this.render(); }
    }
  }

  protected async showAccount(customer: CashCustomer): Promise<void> {
    this.account = null;
    this.detailLoading = true;
    this.open({ kind: 'account', customer });
    const generation = ++this.detailGeneration;
    try {
      const account = await firstValueFrom(this.api.account(customer.customerId).pipe(timeout(15_000)));
      if (generation === this.detailGeneration && !this.destroy.destroyed) this.account = account;
    } catch (error) {
      if (generation === this.detailGeneration) this.dialogError = this.message(error, 'No se pudo consultar la cuenta del cliente.');
    } finally {
      if (generation === this.detailGeneration) { this.detailLoading = false; this.render(); }
    }
  }

  protected saveReceipt(trip: CashTrip): void {
    if (!this.canWrite) return;
    const declaredAmount = parseCollectionAmount(this.declared), receivedAmount = parseCollectionAmount(this.received);
    if (declaredAmount === null || declaredAmount <= 0 || declaredAmount > trip.pendingAmount || receivedAmount === null) {
      this.dialogError = 'Captura importes válidos con máximo dos decimales. El importe declarado debe ser mayor a cero y no superar lo pendiente.';
      return;
    }
    if (this.receiptDifference !== 0 && !this.reason.trim()) { this.dialogError = 'Indica el motivo de la diferencia entre lo declarado y lo recibido.'; return; }
    const userId = this.access.userId();
    if (!userId) return;
    const pending: PendingCashReceipt = { userId, date: this.dashboard!.date, body: {
      operationId: crypto.randomUUID(), tripId: trip.tripId, expectedVersion: trip.version,
      declaredAmount, receivedAmount, reason: this.reason.trim() || null
    } };
    this.restorePending();
    if (this.pending || this.storageError) return;
    try {
      const key = this.storagePrefix + pending.body.operationId, serialized = JSON.stringify(pending);
      localStorage.setItem(key, serialized);
      if (localStorage.getItem(key) !== serialized) throw new Error();
      this.restorePending();
      if (this.storageError) throw new Error();
    } catch {
      this.storageError = 'No se pudo guardar la referencia en este navegador. La recepción sigue sin enviarse.';
      return;
    }
    void this.send(pending, true);
  }

  protected retry(): void { if (this.pending && !this.saving && !this.storageError) void this.send(this.pending, false); }

  private async send(pending: PendingCashReceipt, first: boolean): Promise<void> {
    if (this.saving || pending.userId !== this.access.userId() || !this.canReceive) return;
    this.saving = true;
    this.dialogError = '';
    this.error = '';
    try {
      await firstValueFrom(this.api.receive(pending.body).pipe(timeout(20_000)));
      this.clearPending(pending);
      this.saving = false;
      this.close();
      this.success = 'Efectivo recibido. La hora y el receptor quedaron registrados.';
      await this.load(true);
    } catch (error) {
      const definitive = error instanceof HttpErrorResponse &&
        (error.status === 409 && error.error?.retrySameOperation === false || first && [400, 403, 404].includes(error.status));
      if (definitive) {
        try { this.clearPending(pending); }
        catch { this.storageError = 'La referencia recibida sigue pendiente de retirar. Reintenta para comprobar el resultado.'; }
      }
      const text = this.message(error, definitive ? 'No se pudo guardar la recepción.' : 'No se recibió confirmación. Reintenta con la referencia guardada.');
      if (this.dialog) this.dialogError = text; else this.error = text;
      if (error instanceof HttpErrorResponse && error.status === 403) void this.access.refresh();
      if (definitive && error instanceof HttpErrorResponse && [404, 409].includes(error.status)) {
        this.saving = false;
        this.close();
        await this.load(true);
        this.error = text;
      }
    } finally { this.saving = false; this.render(); }
  }

  protected close(event?: Event): void {
    if (this.saving) { event?.preventDefault(); return; }
    this.detailGeneration++;
    this.element.nativeElement.close();
    this.dialog = null;
    this.dialogError = '';
    this.detailLoading = false;
  }

  private open(dialog: CashDialog): void {
    this.dialog = dialog;
    this.dialogError = '';
    this.render();
    if (!this.element.nativeElement.open) this.element.nativeElement.showModal();
    this.element.nativeElement.querySelector<HTMLInputElement>('input:not([disabled])')?.focus();
  }
  private get storagePrefix(): string { return 'prodimt.collections.cash.pending.v1.' + this.access.userId() + '.'; }
  private restorePending(): void {
    this.storageError = '';
    this.pending = null;
    try {
      const userId = this.access.userId();
      if (!userId) throw new Error();
      const rows: PendingCashReceipt[] = [];
      for (let index = 0; index < localStorage.length; index++) {
        const key = localStorage.key(index);
        if (!key?.startsWith(this.storagePrefix)) continue;
        const command = JSON.parse(localStorage.getItem(key) ?? 'null') as PendingCashReceipt;
        if (!command || command.userId !== userId || !command.body || !/^[a-f0-9-]{36}$/i.test(command.body.operationId) ||
          key !== this.storagePrefix + command.body.operationId) throw new Error();
        rows.push(command);
      }
      this.pending = rows.sort((a, b) => a.body.operationId.localeCompare(b.body.operationId))[0] ?? null;
    } catch { this.storageError = 'No se pudo recuperar una referencia de recepción. Revisa el almacenamiento antes de volver a registrar ese efectivo.'; }
  }
  private clearPending(pending: PendingCashReceipt): void {
    const key = this.storagePrefix + pending.body.operationId;
    const value = localStorage.getItem(key);
    if (value !== null && value !== JSON.stringify(pending)) throw new Error();
    localStorage.removeItem(key);
    if (localStorage.getItem(key) !== null) throw new Error();
    this.restorePending();
  }
  private message(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && typeof error.error?.error === 'string' ? error.error.error : fallback;
  }
  private render(): void { if (!this.destroy.destroyed) this.detector.detectChanges(); }
}
