import { DOCUMENT } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminCustomerDialogComponent } from './admin-customer-dialog.component';
import { CustomerEditorValue } from './customer-editor.model';
import { CustomerWorkingWeekComponent } from './customer-working-week.component';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, of, switchMap, throwError } from 'rxjs';
import {
  AdminCatalogApiService,
  CustomerDayStatusResponse,
  AdminCustomerAccessTokenApiResponse,
  CreatedCustomerAccessTokenApiResponse,
  AdminCustomerCatalogApiResponse,
  AdminCustomerFrequentProductApiResponse,
  AdminCustomerMachineAssignmentApiResponse,
  AdminMachineCatalogApiResponse,
  AdminProductCatalogApiResponse
} from './admin-catalog-api.service';
import { formatReminderDate, formatReminderExpiration, mexicoDateTimeToIso, normalizeReminderPhone } from './customer-reminder';
import { UnitCode, unitLabel, storeKeyLabel, paymentTermLabel } from './operational-labels';

type CatalogTab = 'customers' | 'products' | 'machines';


interface ProductForm {
  id: string | null;
  name: string;
  description: string | null;
  unitCode: UnitCode | null;
}

interface MachineForm {
  id: string | null;
  number: number | null;
  name: string | null;
}

@Component({
  selector: 'app-admin-catalogs',
  imports: [FormsModule, ReactiveFormsModule, RouterLink, AdminCustomerDialogComponent, CustomerWorkingWeekComponent],
  styleUrls: ['./admin-customer-cards.css', './admin-customer-configuration.css'],
  templateUrl: './admin-catalogs.component.html'
})
export class AdminCatalogsComponent {
  private readonly api = inject(AdminCatalogApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly focusedCustomerId = this.route.snapshot.paramMap.get('customerId');
  protected readonly configurationOnly = this.focusedCustomerId !== null;
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  protected customerDialogOpen = false;
  protected isCustomerSaving = false;
  protected customerDialogError: string | null = null;
  protected customerDayFilter: 'all' | 'AwaitingOrder' | 'HasOrder' | 'off' = 'all';
  protected dayStatuses = new Map<string, CustomerDayStatusResponse>();
  protected dailyDate: string | null = null;
  protected isDayLoading = false;
  protected dayStatusError: string | null = null;
  protected customerSearch = '';
  protected workingRestDays: number[] = [];
  private loadedRestDays: number[] = [];
  protected isWeekSaving = false;
  protected weekError: string | null = null;
  protected weekMessage: string | null = null;
  protected isConfigLoading = false;
  protected isProfileSaving = false;
  protected profileDirty = false;
  protected profileConflict = false;
  protected profileError: string | null = null;
  protected profileMessage: string | null = null;
  private profileReadSequence = 0;

  protected activeTab: CatalogTab = 'customers';
  protected customers: AdminCustomerCatalogApiResponse[] = [];
  protected products: AdminProductCatalogApiResponse[] = [];
  protected machines: AdminMachineCatalogApiResponse[] = [];
  protected frequentRows: AdminCustomerFrequentProductApiResponse[] = [];
  private profileProductSnapshot = new Map<string, AdminProductCatalogApiResponse>();
  protected machineAssignmentRows: AdminCustomerMachineAssignmentApiResponse[] = [];
  protected accessTokens: AdminCustomerAccessTokenApiResponse[] = [];
  protected selectedCustomer: AdminCustomerCatalogApiResponse | null = null;
  protected generatedPlainToken: string | null = null;
  protected newTokenDescription: string | null = 'Recuerda hacer tu próximo pedido';
  protected newTokenExpiresAt: string | null = null;
  protected tokenPhone = '';
  protected isTokenSaving = false;
  protected isSharingToken = false;
  protected tokenError: string | null = null;
  protected tokenMessage: string | null = null;
  protected reminderDate: string | null = null;
  protected generatedAccess: CreatedCustomerAccessTokenApiResponse | null = null;
  private reminderServerNow: string | null = null;
  protected readonly formatReminderDate = formatReminderDate;
  protected readonly formatReminderExpiration = formatReminderExpiration;
  protected isLoading = true;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected customerForm: CustomerEditorValue = this.emptyCustomerForm();
  protected productForm: ProductForm = this.emptyProductForm();
  protected machineForm: MachineForm = this.emptyMachineForm();
  protected readonly profileApproval = new FormControl(false, { nonNullable: true });
  protected readonly unitLabel = unitLabel;
  protected readonly storeKeyLabel = storeKeyLabel;
  protected readonly paymentTermLabel = paymentTermLabel;

  constructor() {
    this.loadCatalogs();
    this.refreshDayStatus();
    const refresh = () => {
      if (this.document.visibilityState !== 'hidden' && this.activeTab === 'customers') this.refreshDayStatus();
    };
    const timer = setInterval(refresh, 30000);
    this.document.addEventListener('visibilitychange', refresh);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      this.document.removeEventListener('visibilitychange', refresh);
    });
  }

  protected get visibleCustomers(): AdminCustomerCatalogApiResponse[] {
    if (this.focusedCustomerId) return this.customers.filter(customer => customer.id === this.focusedCustomerId);
    const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es-MX');
    const search = normalize(this.customerSearch.trim());
    return this.customers.filter(customer =>
      (!search || [customer.name, customer.phoneNumber, customer.routeCode, customer.externalCode].some(value => value && normalize(value).includes(search))) &&
      (this.customerDayFilter === 'all' || (this.customerDayFilter === 'off'
        ? ['NoOrderConfirmed', 'ScheduledRest'].includes(this.dayState(customer)) : this.dayState(customer) === this.customerDayFilter)))
      .slice().sort((a, b) => this.dayRank(a) - this.dayRank(b) || a.name.localeCompare(b.name, 'es-MX'));
  }

  protected dayState(customer: AdminCustomerCatalogApiResponse): string {
    return this.dayStatuses.get(customer.id)?.status ?? 'Unavailable';
  }

  protected dayLabel(customer: AdminCustomerCatalogApiResponse): string {
    const labels: Record<string, string> = {
      AwaitingOrder: 'Por confirmar', HasOrder: 'Con pedido', NoOrderConfirmed: 'Hoy no pide',
      ScheduledRest: 'Descanso programado', Inactive: 'Inactivo', Unavailable: 'Estado sin actualizar'
    };
    return labels[this.dayState(customer)] ?? labels['Unavailable'];
  }

  protected countDayState(state: string): number {
    return this.customers.filter(customer => this.dayState(customer) === state).length;
  }

  protected initials(name: string): string {
    return name.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join('').toLocaleUpperCase('es-MX');
  }

  private dayRank(customer: AdminCustomerCatalogApiResponse): number {
    if (!customer.isActive) return 3;
    return ({ AwaitingOrder: 0, HasOrder: 1, NoOrderConfirmed: 2, ScheduledRest: 2 } as Record<string, number>)[this.dayState(customer)] ?? 0;
  }

  protected refreshDayStatus(): void {
    if (this.isDayLoading) return;
    this.isDayLoading = true;
    this.api.getCustomerDayStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: response => {
        if (!response || !Array.isArray(response.customers) || !/^\d{4}-\d{2}-\d{2}$/.test(response.date)) {
          this.finishDayStatusError();
          return;
        }
        this.dayStatuses = new Map(response.customers.map(row => [row.customerId, row]));
        this.dailyDate = response.date;
        this.dayStatusError = null;
        this.isDayLoading = false;
        this.changeDetector.detectChanges();
      },
      error: () => this.finishDayStatusError()
    });
  }

  private finishDayStatusError(): void {
    this.isDayLoading = false;
    this.dayStatusError = this.dailyDate
      ? 'No pudimos actualizar el estado de hoy. Se conserva la última consulta del ' + this.dailyDate.split('-').reverse().join('/') + '.'
      : 'No pudimos consultar los pedidos de hoy. Los estados se mostrarán cuando se recupere la conexión.';
    this.changeDetector.detectChanges();
  }

  protected newCustomer(): void {
    this.customerForm = this.emptyCustomerForm();
    this.customerDialogError = null;
    this.customerDialogOpen = true;
  }

  protected productFor(row: AdminCustomerFrequentProductApiResponse): AdminProductCatalogApiResponse | undefined {
    return this.profileProductSnapshot.get(row.productId) ?? this.products.find(product => product.id === row.productId);
  }

  protected get profileProducts(): AdminProductCatalogApiResponse[] {
    return this.products.map(product => this.profileProductSnapshot.get(product.id) ?? product);
  }

  protected get profileValidationErrors(): string[] {
    const errors: string[] = [];
    const ids = this.frequentRows.map(row => row.productId);
    if (new Set(ids).size !== ids.length) errors.push('Cada producto debe aparecer una sola vez para este cliente.');
    if (this.frequentRows.some(row => !this.productFor(row))) errors.push('Selecciona un producto existente en cada fila.');
    if (this.frequentRows.some(row => !Number.isInteger(row.sortOrder) || row.sortOrder < 1)) errors.push('El orden visual debe ser un número entero desde 1.');
    if (this.frequentRows.some(row => row.defaultQuantity !== null && (!Number.isFinite(row.defaultQuantity) || row.defaultQuantity < 0))) errors.push('La cantidad sugerida debe ser cero o positiva, o quedar vacía.');
    const aliases = this.frequentRows.map(row => row.captureAlias?.trim().toUpperCase() || '').filter(Boolean);
    if (aliases.some(value => !/^[A-Z]{1,16}$/.test(value))) errors.push('Las abreviaturas deben tener de 1 a 16 letras sin espacios ni números.');
    if (new Set(aliases).size !== aliases.length) errors.push('No repitas abreviaturas dentro de este cliente.');
    const positions = this.frequentRows.filter(row => row.interpretationPosition !== null).map(row => row.interpretationPosition!);
    if (positions.some(position => !Number.isInteger(position) || position < 1)) errors.push('Las posiciones deben ser números enteros desde 1, o quedar vacías.');
    if (new Set(positions).size !== positions.length) errors.push('No repitas posiciones dentro de este cliente.');
    if (this.frequentRows.some(row => row.preferredMachineId && !this.machines.some(machine => machine.id === row.preferredMachineId && machine.isActive))) errors.push('Selecciona una máquina activa o usa la asignación del cliente.');
    if (this.profileApproval.value) {
      const active = this.frequentRows.filter(row => row.isActive);
      if (active.length === 0 || active.some(row => !this.productFor(row)?.isActive || !this.productFor(row)?.unitCode)) errors.push('Para aprobar, todos los productos activos deben tener unidad confirmada y estar activos en el catálogo.');
      const activePositions = active.map(row => row.interpretationPosition).sort((a, b) => (a ?? 0) - (b ?? 0));
      if (activePositions.some((position, index) => position !== index + 1)) errors.push('Para aprobar, asigna posiciones consecutivas 1, 2, 3… a todos los productos activos.');
    }
    return errors;
  }

  protected get profileWarnings(): string[] {
    const warnings: string[] = [];
    if (this.frequentRows.some(row => row.isActive && !this.productFor(row)?.unitCode)) warnings.push('Hay productos sin unidad. Puedes guardar el borrador, pero no capturar cantidades de esos productos ni aprobar el perfil.');
    if (this.frequentRows.some(row => row.isActive && row.interpretationPosition === null)) warnings.push('Hay posiciones sin confirmar. No son necesarias para capturar pedidos indicando producto y cantidad.');
    if (this.frequentRows.some(row => row.isActive && !row.preferredMachineId) && !this.machineAssignmentRows.some(row => row.isActive && row.isDefault)) warnings.push('Hay productos sin máquina propia y no existe una máquina predeterminada del cliente. Su demanda quedará sin asignar.');
    return warnings;
  }

  protected get interpretationPreview(): string {
    const ordered = this.frequentRows.filter(row => row.isActive && row.interpretationPosition !== null)
      .slice().sort((a, b) => a.interpretationPosition! - b.interpretationPosition!);
    if (!ordered.length) return 'Secuencia del cliente: aún no hay posiciones confirmadas.';
    return 'Secuencia del cliente: ' + ordered.map(row => `${row.interpretationPosition} = ${this.productFor(row)?.name ?? row.productName}`).join(' · ');
  }

  protected navigateToCustomerConfig(customer: AdminCustomerCatalogApiResponse): void {
    void this.router.navigate(['/admin/clientes', customer.id, 'configuracion']);
  }

  protected editProductUnit(row: AdminCustomerFrequentProductApiResponse): void {
    if (this.profileDirty) {
      this.profileError = 'Guarda primero los cambios del perfil antes de abrir el producto en el catálogo.';
      return;
    }
    void this.router.navigate(['/admin/catalogos'], { queryParams: { productId: row.productId } });
  }

  protected saveCustomer(value: CustomerEditorValue): void {
    if (this.isCustomerSaving) return;
    this.customerForm = { ...value, restDays: [...value.restDays] };
    this.isCustomerSaving = true;
    this.customerDialogError = null;
    const request = {
      name: this.customerForm.name,
      phoneNumber: this.emptyToNull(this.customerForm.phoneNumber),
      preferredDeliveryTime: this.emptyToNull(this.customerForm.preferredDeliveryTime),
      preferredDeliveryWindowStart: this.emptyToNull(this.customerForm.preferredDeliveryWindowStart),
      preferredDeliveryWindowEnd: this.emptyToNull(this.customerForm.preferredDeliveryWindowEnd),
      deliveryNotes: this.emptyToNull(this.customerForm.deliveryNotes),
      routeCode: this.emptyToNull(this.customerForm.routeCode),
      routeStopOrder: this.customerForm.routeStopOrder,
      hasStoreKey: this.customerForm.hasStoreKey,
      paymentTermCode: this.customerForm.paymentTermCode,
      internalDeliveryInstructions: this.emptyToNull(this.customerForm.internalDeliveryInstructions),
      restDays: [...this.customerForm.restDays]
    };
    const operation = this.customerForm.id
      ? this.api.updateCustomer(this.customerForm.id, request)
      : this.api.createCustomer(request);

    operation.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.resultMessage = 'Cliente guardado.';
        this.isCustomerSaving = false;
        this.resetCustomerForm();
        this.loadCatalogs(false);
      },
      error: (error: unknown) => {
        this.isCustomerSaving = false;
        this.customerDialogError = this.profileErrorText(error, 'No se pudo guardar el cliente.');
        this.changeDetector.detectChanges();
      }
    });
  }

  protected editCustomer(customer: AdminCustomerCatalogApiResponse): void {
    if (this.configurationOnly && (this.isWeekSaving || this.workingWeekDirty)) {
      this.weekError = 'Guarda o recarga los días de trabajo antes de editar los datos del cliente.';
      return;
    }
    this.customerDialogError = null;
    this.customerDialogOpen = true;
    this.customerForm = {
      id: customer.id,
      name: customer.name,
      phoneNumber: customer.phoneNumber,
      preferredDeliveryTime: this.toTimeInput(customer.preferredDeliveryTime),
      preferredDeliveryWindowStart: this.toTimeInput(customer.preferredDeliveryWindowStart),
      preferredDeliveryWindowEnd: this.toTimeInput(customer.preferredDeliveryWindowEnd),
      deliveryNotes: customer.deliveryNotes,
      routeCode: customer.routeCode ?? null,
      routeStopOrder: customer.routeStopOrder ?? null,
      hasStoreKey: customer.hasStoreKey ?? null,
      paymentTermCode: customer.paymentTermCode ?? null,
      internalDeliveryInstructions: customer.internalDeliveryInstructions ?? null,
      restDays: [...(customer.restDays ?? [])]
    };
  }

  protected toggleCustomer(customer: AdminCustomerCatalogApiResponse): void {
    const operation = customer.isActive
      ? this.api.deactivateCustomer(customer.id)
      : this.api.activateCustomer(customer.id);

    operation.subscribe({
      next: () => {
        this.resultMessage = customer.isActive ? 'Cliente desactivado.' : 'Cliente activado.';
        this.loadCatalogs(false);
      },
      error: (error: unknown) => this.showError(error, 'No se pudo cambiar el estado del cliente.')
    });
  }

  protected openCustomerConfig(customer: AdminCustomerCatalogApiResponse): void {
    const sequence = ++this.profileReadSequence;
    this.selectedCustomer = customer;
    this.setWorkingWeek(customer.restDays ?? []);
    this.profileApproval.setValue(false);
    this.generatedPlainToken = null;
    this.generatedAccess = null;
    this.newTokenDescription = 'Recuerda hacer tu próximo pedido';
    this.newTokenExpiresAt = null;
    this.tokenPhone = customer.phoneNumber ?? '';
    this.tokenError = null;
    this.tokenMessage = null;
    this.reminderDate = null;
    this.api.getNextDeliveryDate().subscribe({
      next: metadata => {
        if (this.selectedCustomer?.id !== customer.id) return;
        this.reminderDate = metadata.deliveryDate;
        this.reminderServerNow = metadata.serverNow;
        this.changeDetector.detectChanges();
      },
      error: () => { /* The create action requires a fresh, successful metadata read. */ }
    });
    this.resultMessage = null;
    this.errorMessage = null;
    this.isConfigLoading = true;
    this.frequentRows = [];
    this.profileProductSnapshot.clear();
    this.machineAssignmentRows = [];
    this.accessTokens = [];
    this.profileDirty = false;
    this.profileConflict = false;
    this.profileError = null;

    forkJoin({
      profile: this.api.getOrderingProfile(customer.id),
      assignments: this.api.getMachineAssignments(customer.id),
      tokens: this.api.getAccessTokens(customer.id)
    }).subscribe({
      next: ({ profile, assignments, tokens }) => {
        if (sequence !== this.profileReadSequence || this.selectedCustomer?.id !== customer.id) return;
        this.selectedCustomer.orderingProfileVersion = profile.orderingProfileVersion;
        this.selectedCustomer.isOrderProfileApproved = profile.isOrderProfileApproved;
        this.profileProductSnapshot = new Map(profile.items.map(item => [item.productId, {
          id: item.productId, name: item.productName, description: null,
          unitCode: item.unitCode ?? null, isActive: item.productIsActive ?? false
        }]));
        this.frequentRows = profile.items.slice().sort((a, b) => a.sortOrder - b.sortOrder);
        this.machineAssignmentRows = assignments;
        this.accessTokens = tokens;
        this.isConfigLoading = false;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (sequence !== this.profileReadSequence) return;
        this.isConfigLoading = false;
        this.profileConflict = true;
        this.profileError = this.profileErrorText(error, 'No se pudo cargar la configuración. Recarga antes de guardar.');
        this.changeDetector.detectChanges();
      }
    });
  }

  protected resetCustomerForm(): void {
    this.customerForm = this.emptyCustomerForm();
    this.customerDialogOpen = false;
    this.customerDialogError = null;
  }

  protected get workingWeekDirty(): boolean {
    return !this.sameWeek(this.workingRestDays, this.loadedRestDays);
  }

  protected changeWorkingWeek(restDays: number[]): void {
    if (this.isWeekSaving || this.isCustomerSaving) return;
    this.workingRestDays = [...restDays];
    this.weekError = null;
    this.weekMessage = null;
  }

  protected reloadWorkingWeek(): void {
    if (!this.selectedCustomer || this.isWeekSaving) return;
    const id = this.selectedCustomer.id;
    this.isWeekSaving = true;
    this.weekError = null;
    this.api.getCustomers().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: customers => {
        const customer = customers.find(item => item.id === id);
        if (customer) {
          this.setWorkingWeek(customer.restDays ?? []);
          this.weekMessage = 'Días de trabajo recargados.';
        } else this.weekError = 'No se encontró el cliente.';
        this.isWeekSaving = false;
        this.changeDetector.detectChanges();
      },
      error: error => {
        this.isWeekSaving = false;
        this.weekError = this.profileErrorText(error, 'No se pudieron consultar los días de trabajo.');
        this.changeDetector.detectChanges();
      }
    });
  }

  protected saveWorkingWeek(): void {
    if (!this.selectedCustomer || !this.workingWeekDirty || this.isWeekSaving || this.isCustomerSaving) return;
    const id = this.selectedCustomer.id;
    const restDays = [...this.workingRestDays];
    this.isWeekSaving = true;
    this.weekError = null;
    this.weekMessage = null;
    // The existing PUT also edits delivery/contact fields. Read their latest
    // values immediately before saving the calendar rather than sending the
    // older screen snapshot. This does not change the API's concurrency contract.
    this.api.getCustomers().pipe(
      switchMap(customers => {
        const latest = customers.find(item => item.id === id);
        if (!latest) return throwError(() => new Error('No se encontró el cliente.'));
        if (this.sameWeek(latest.restDays ?? [], restDays)) return of(latest);
        if (!this.sameWeek(latest.restDays ?? [], this.loadedRestDays))
          return throwError(() => new HttpErrorResponse({ status:409, error:{ error:'El calendario cambió en otra pantalla. Recarga los días antes de guardar; tu selección sigue visible.' } }));
        return this.api.updateCustomer(id, {
          name:latest.name, phoneNumber:latest.phoneNumber,
          preferredDeliveryTime:latest.preferredDeliveryTime, preferredDeliveryWindowStart:latest.preferredDeliveryWindowStart,
          preferredDeliveryWindowEnd:latest.preferredDeliveryWindowEnd, deliveryNotes:latest.deliveryNotes,
          routeCode:latest.routeCode, routeStopOrder:latest.routeStopOrder, hasStoreKey:latest.hasStoreKey,
          paymentTermCode:latest.paymentTermCode, internalDeliveryInstructions:latest.internalDeliveryInstructions,
          restDays
        });
      }), takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: customer => {
        this.customers = this.customers.map(item => item.id === id ? customer : item);
        if (this.selectedCustomer?.id === id) this.selectedCustomer = {
          ...customer, orderingProfileVersion:this.selectedCustomer.orderingProfileVersion,
          isOrderProfileApproved:this.selectedCustomer.isOrderProfileApproved
        };
        this.setWorkingWeek(customer.restDays ?? restDays);
        this.weekMessage = 'Días de trabajo guardados.';
        this.isWeekSaving = false;
        this.refreshDayStatus();
        this.changeDetector.detectChanges();
      },
      error: error => {
        this.isWeekSaving = false;
        this.weekError = this.profileErrorText(error, 'No se pudieron guardar los días de trabajo.');
        this.changeDetector.detectChanges();
      }
    });
  }

  private setWorkingWeek(restDays: number[]): void {
    this.loadedRestDays = [...restDays];
    this.workingRestDays = [...restDays];
    this.weekError = null;
    this.weekMessage = null;
  }

  private sameWeek(first: number[], second: number[]): boolean {
    return JSON.stringify([...new Set(first)].sort()) === JSON.stringify([...new Set(second)].sort());
  }

  protected addFrequentRow(): void {
    this.invalidateProfileApproval();
    const used = new Set(this.frequentRows.map((row) => row.productId));
    const product = this.products.find((item) => item.isActive && !used.has(item.id));

    if (!product) {
      this.errorMessage = 'No hay productos activos disponibles para agregar.';
      return;
    }

    this.frequentRows = [
      ...this.frequentRows,
      {
        productId: product.id,
        productName: product.name,
        defaultQuantity: null,
        sortOrder: this.frequentRows.length + 1,
        isActive: true,
        preferredMachineId: null,
        interpretationPosition: null,
        captureAlias: ''
      }
    ];
  }

  protected removeFrequentRow(index: number): void {
    this.invalidateProfileApproval();
    this.frequentRows = this.frequentRows.filter((_, currentIndex) => currentIndex !== index);
    this.frequentRows.forEach((row, currentIndex) => row.sortOrder = currentIndex + 1);
  }

  protected moveFrequentRow(index: number, direction: -1 | 1): void {
    const destination = index + direction;
    if (destination < 0 || destination >= this.frequentRows.length) return;
    this.invalidateProfileApproval();
    const reordered = this.frequentRows.slice();
    [reordered[index], reordered[destination]] = [reordered[destination], reordered[index]];
    reordered.forEach((row, currentIndex) => row.sortOrder = currentIndex + 1);
    this.frequentRows = reordered;
  }

  protected useVisualOrderForPositions(): void {
    this.invalidateProfileApproval();
    let position = 0;
    this.frequentRows.slice().sort((a, b) => a.sortOrder - b.sortOrder).forEach(row => {
      row.interpretationPosition = row.isActive ? ++position : null;
    });
  }

  protected reloadProfile(): void {
    if (this.selectedCustomer) this.openCustomerConfig(this.selectedCustomer);
  }

  protected invalidateProfileApproval(): void {
    this.profileApproval.setValue(false);
    this.profileDirty = true;
    this.profileMessage = null;
    this.profileError = null;
  }

  protected saveFrequentProducts(): void {
    if (!this.selectedCustomer || this.isConfigLoading || this.isProfileSaving || this.profileConflict || this.profileValidationErrors.length > 0) {
      return;
    }
    const customerId = this.selectedCustomer.id;
    this.isProfileSaving = true;
    this.profileError = null;
    this.profileMessage = null;
    this.api.updateFrequentProducts(customerId, {
      approveForInterpretation: this.profileApproval.value,
      expectedProfileVersion: this.selectedCustomer.orderingProfileVersion,
      items: this.frequentRows.map((row) => ({
        productId: row.productId,
        expectedUnitCode: this.productFor(row)?.unitCode ?? null,
        defaultQuantity: row.defaultQuantity === null ? null : Number(row.defaultQuantity),
        sortOrder: Number(row.sortOrder) || 0,
        isActive: row.isActive,
        preferredMachineId: row.preferredMachineId ?? null,
        interpretationPosition: row.interpretationPosition ?? null,
        captureAlias: row.captureAlias?.trim().toUpperCase() ?? ''
      }))
    }).subscribe({
      next: () => {
        this.isProfileSaving = false;
        this.profileApproval.setValue(false);
        this.profileMessage = 'Productos frecuentes guardados.';
        this.reloadProfile();
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.isProfileSaving = false;
        this.profileConflict = error instanceof HttpErrorResponse && error.status === 409;
        this.profileError = this.profileConflict
          ? 'Otro usuario cambió este perfil. Tus cambios siguen en pantalla; recarga la versión vigente antes de volver a editar.'
          : this.profileErrorText(error, 'No se pudieron guardar los productos frecuentes.');
        this.changeDetector.detectChanges();
      }
    });
  }

  protected addMachineAssignmentRow(): void {
    const used = new Set(this.machineAssignmentRows.map((row) => row.machineId));
    const machine = this.machines.find((item) => item.isActive && !used.has(item.id));

    if (!machine) {
      this.errorMessage = 'No hay maquinas activas disponibles para agregar.';
      return;
    }

    this.machineAssignmentRows = [
      ...this.machineAssignmentRows,
      {
        machineId: machine.id,
        machineNumber: machine.number,
        machineName: machine.name,
        isDefault: this.machineAssignmentRows.length === 0,
        isActive: true,
        notes: null
      }
    ];
  }

  protected removeMachineAssignmentRow(index: number): void {
    this.machineAssignmentRows = this.machineAssignmentRows.filter((_, currentIndex) => currentIndex !== index);
  }

  protected setDefaultAssignment(row: AdminCustomerMachineAssignmentApiResponse): void {
    if (!row.isDefault) {
      return;
    }

    this.machineAssignmentRows = this.machineAssignmentRows.map((item) => ({
      ...item,
      isDefault: item.machineId === row.machineId
    }));
  }

  protected saveMachineAssignments(): void {
    if (!this.selectedCustomer) {
      return;
    }

    this.api.updateMachineAssignments(this.selectedCustomer.id, {
      items: this.machineAssignmentRows.map((row) => ({
        machineId: row.machineId,
        isDefault: row.isDefault,
        isActive: row.isActive,
        notes: this.emptyToNull(row.notes)
      }))
    }).subscribe({
      next: (rows) => {
        this.machineAssignmentRows = rows;
        this.resultMessage = 'Asignaciones de maquina guardadas.';
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => this.showError(error, 'No se pudieron guardar las asignaciones de maquina.')
    });
  }

  protected createAccessToken(): void {
    if (!this.selectedCustomer || this.isTokenSaving || this.isSharingToken) return;
    this.tokenError = null;
    this.tokenMessage = null;
    const customer = this.selectedCustomer;
    const phone = normalizeReminderPhone(this.tokenPhone);
    const expiresAt = mexicoDateTimeToIso(this.newTokenExpiresAt);
    if (!phone) {
      this.tokenError = 'Captura un teléfono de México de 10 dígitos o un número internacional con + y código de país.';
      return;
    }
    if (!expiresAt) {
      this.tokenError = 'Selecciona una fecha y hora válidas de vencimiento, en horario de Ciudad de México.';
      return;
    }
    const description = this.newTokenDescription?.trim() || 'Recuerda hacer tu próximo pedido';
    this.isTokenSaving = true;
    this.api.getNextDeliveryDate().pipe(
      switchMap(metadata => {
        this.reminderDate = metadata.deliveryDate;
        this.reminderServerNow = metadata.serverNow;
        if (Date.parse(expiresAt) <= Date.parse(metadata.serverNow))
          return throwError(() => new Error('El vencimiento debe ser posterior a la hora actual de Ciudad de México.'));
        return phone === customer.phoneNumber ? of(undefined) : this.api.updateCustomerPhone(customer.id, phone).pipe(
          switchMap(() => {
            customer.phoneNumber = phone;
            if (this.selectedCustomer?.id === customer.id) this.tokenPhone = phone;
            return of(undefined);
          })
        );
      }),
      switchMap(() => this.api.createAccessToken(customer.id, { description, expiresAt, reminderLink: true })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (token) => {
        this.isTokenSaving = false;
        if (this.selectedCustomer?.id !== customer.id) return;
        this.generatedPlainToken = token.plainToken;
        this.generatedAccess = token;
        this.tokenMessage = 'Enlace creado. Cópialo o compártelo ahora; su valor sólo está disponible en esta pantalla.';
        this.reloadAccessTokens();
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.isTokenSaving = false;
        this.tokenError = this.profileErrorText(error, error instanceof Error ? error.message : 'No se pudo crear el enlace. Si el teléfono se guardó, se conservará.');
        this.changeDetector.detectChanges();
      }
    });
  }

  protected get generatedLink(): string | null {
    return this.generatedAccess && this.generatedPlainToken
      ? `${this.document.location.origin}/cliente#token=${encodeURIComponent(this.generatedPlainToken)}` : null;
  }

  protected get reminderPreview(): string {
    if (!this.selectedCustomer) return '';
    const description = this.generatedAccess?.description || this.newTokenDescription?.trim() || 'Recuerda hacer tu próximo pedido';
    const expiresAt = this.generatedAccess?.expiresAt || mexicoDateTimeToIso(this.newTokenExpiresAt);
    return `Hola, ${this.selectedCustomer.name}. ${description}\n\nEntrega prevista si lo confirmas ahora: ${this.reminderDate ? formatReminderDate(this.reminderDate) : 'se calculará al crear el enlace'}.\n\n${this.generatedLink || '{enlace de acceso}'}\n\nLos pedidos confirmados desde las 6:00 p. m. se entregan al día siguiente. Revisa la fecha antes de confirmar.\n\n${expiresAt ? `Enlace disponible hasta el ${formatReminderExpiration(expiresAt)}, hora de Ciudad de México.` : 'Configura la fecha y hora de vencimiento.'}`;
  }

  protected async copyAccessContent(message: boolean): Promise<void> {
    const customerId = this.selectedCustomer?.id;
    const tokenId = this.generatedAccess?.tokenId;
    if (!customerId || !tokenId) return;
    if (message) {
      try {
        const context = await firstValueFrom(forkJoin({ metadata: this.api.getNextDeliveryDate(), tokens: this.api.getAccessTokens(customerId) })
          .pipe(takeUntilDestroyed(this.destroyRef)));
        if (this.selectedCustomer?.id !== customerId || this.generatedAccess?.tokenId !== tokenId) return;
        if (!context.tokens.some(token => token.tokenId === tokenId && token.isActive)
          || !this.generatedAccess.expiresAt || Date.parse(this.generatedAccess.expiresAt) <= Date.parse(context.metadata.serverNow)) {
          this.generatedAccess = null; this.generatedPlainToken = null;
          this.tokenError = 'El enlace venció o fue revocado. Genera uno nuevo para compartirlo.';
          this.changeDetector.detectChanges(); return;
        }
        this.reminderDate = context.metadata.deliveryDate;
        this.reminderServerNow = context.metadata.serverNow;
      } catch {
        this.tokenError = 'No se pudo actualizar la fecha del mensaje. Vuelve a intentar.';
        this.changeDetector.detectChanges(); return;
      }
    }
    const value = message ? this.reminderPreview : this.generatedLink;
    if (!value) return;
    try {
      await this.document.defaultView?.navigator.clipboard.writeText(value);
      this.tokenMessage = message ? 'Mensaje copiado.' : 'Enlace copiado.';
    } catch {
      this.tokenError = 'No pudimos copiarlo automáticamente. Selecciona el texto y cópialo manualmente.';
    }
    this.changeDetector.detectChanges();
  }

  protected openWhatsApp(): void {
    if (!this.generatedAccess || !this.selectedCustomer || this.isSharingToken) return;
    const phone = normalizeReminderPhone(this.selectedCustomer.phoneNumber ?? '');
    const customerId = this.selectedCustomer.id;
    const tokenId = this.generatedAccess.tokenId;
    if (!phone) { this.tokenError = 'Guarda un teléfono válido y genera un nuevo enlace.'; return; }
    // Reserve the window in the user's click; network validation must not trigger a blocked popup.
    const popup = this.document.defaultView?.open('about:blank', '_blank');
    if (!popup) { this.tokenError = 'Permite abrir ventanas emergentes o copia el mensaje para compartirlo.'; return; }
    popup.opener = null;
    this.isSharingToken = true;
    this.tokenError = null;
    forkJoin({ metadata: this.api.getNextDeliveryDate(), tokens: this.api.getAccessTokens(customerId) })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ metadata, tokens }) => {
        if (this.selectedCustomer?.id !== customerId || this.generatedAccess?.tokenId !== tokenId) {
          popup.close(); this.isSharingToken = false; this.changeDetector.detectChanges(); return;
        }
        this.reminderDate = metadata.deliveryDate;
        this.reminderServerNow = metadata.serverNow;
        this.isSharingToken = false;
        if (!tokens.some(token => token.tokenId === tokenId && token.isActive)
          || !this.generatedAccess?.expiresAt || Date.parse(this.generatedAccess.expiresAt) <= Date.parse(metadata.serverNow)) {
          popup.close(); this.generatedAccess = null; this.generatedPlainToken = null;
          this.tokenError = 'El enlace venció o fue revocado. Genera uno nuevo antes de compartirlo.';
        } else {
          popup.location.href = `https://wa.me/${phone.slice(1)}?text=${encodeURIComponent(this.reminderPreview)}`;
          this.tokenMessage = 'WhatsApp abierto con el mensaje preparado. Confirma el envío desde WhatsApp.';
        }
        this.changeDetector.detectChanges();
      },
      error: () => {
        popup.close(); this.isSharingToken = false;
        this.tokenError = 'No se pudo verificar la fecha de entrega. Vuelve a intentar o copia el enlace.';
        this.changeDetector.detectChanges();
      }
    });
  }

  protected revokeAccessToken(token: AdminCustomerAccessTokenApiResponse): void {
    if (!this.selectedCustomer) {
      return;
    }

    this.api.revokeAccessToken(this.selectedCustomer.id, token.tokenId).subscribe({
      next: () => {
        this.resultMessage = 'Token revocado.';
        if (this.generatedAccess?.tokenId === token.tokenId) {
          this.generatedPlainToken = null;
          this.generatedAccess = null;
        }
        this.reloadAccessTokens();
      },
      error: (error: unknown) => this.showError(error, 'No se pudo revocar el token.')
    });
  }

  protected saveProduct(): void {
    const request = {
      name: this.productForm.name,
      description: this.emptyToNull(this.productForm.description),
      unitCode: this.productForm.unitCode
    };
    const operation = this.productForm.id
      ? this.api.updateProduct(this.productForm.id, request)
      : this.api.createProduct(request);

    operation.subscribe({
      next: () => {
        this.resultMessage = 'Producto guardado.';
        this.resetProductForm();
        this.loadCatalogs(false);
      },
      error: (error: unknown) => this.showError(error, 'No se pudo guardar el producto.')
    });
  }

  protected editProduct(product: AdminProductCatalogApiResponse): void {
    this.productForm = {
      id: product.id,
      name: product.name,
      description: product.description,
      unitCode: product.unitCode ?? null
    };
  }

  protected toggleProduct(product: AdminProductCatalogApiResponse): void {
    const operation = product.isActive
      ? this.api.deactivateProduct(product.id)
      : this.api.activateProduct(product.id);

    operation.subscribe({
      next: () => {
        this.resultMessage = product.isActive ? 'Producto desactivado.' : 'Producto activado.';
        this.loadCatalogs(false);
      },
      error: (error: unknown) => this.showError(error, 'No se pudo cambiar el estado del producto.')
    });
  }

  protected resetProductForm(): void {
    this.productForm = this.emptyProductForm();
  }

  protected saveMachine(): void {
    const request = {
      number: Number(this.machineForm.number),
      name: this.emptyToNull(this.machineForm.name)
    };
    const operation = this.machineForm.id
      ? this.api.updateMachine(this.machineForm.id, request)
      : this.api.createMachine(request);

    operation.subscribe({
      next: () => {
        this.resultMessage = 'Maquina guardada.';
        this.resetMachineForm();
        this.loadCatalogs(false);
      },
      error: (error: unknown) => this.showError(error, 'No se pudo guardar la maquina.')
    });
  }

  protected editMachine(machine: AdminMachineCatalogApiResponse): void {
    this.machineForm = {
      id: machine.id,
      number: machine.number,
      name: machine.name
    };
  }

  protected toggleMachine(machine: AdminMachineCatalogApiResponse): void {
    const operation = machine.isActive
      ? this.api.deactivateMachine(machine.id)
      : this.api.activateMachine(machine.id);

    operation.subscribe({
      next: () => {
        this.resultMessage = machine.isActive ? 'Maquina desactivada.' : 'Maquina activada.';
        this.loadCatalogs(false);
      },
      error: (error: unknown) => this.showError(error, 'No se pudo cambiar el estado de la maquina.')
    });
  }

  protected resetMachineForm(): void {
    this.machineForm = this.emptyMachineForm();
  }

  protected formatCustomerDelivery(customer: AdminCustomerCatalogApiResponse): string {
    const time = this.toTimeInput(customer.preferredDeliveryTime);
    const start = this.toTimeInput(customer.preferredDeliveryWindowStart);
    const end = this.toTimeInput(customer.preferredDeliveryWindowEnd);

    if (time) {
      return time;
    }

    if (start && end) {
      return `${start} - ${end}`;
    }

    return 'Sin horario';
  }

  protected formatDate(value: string): string {
    return new Date(value).toLocaleString('es-MX', {
      dateStyle: 'short',
      timeStyle: 'short'
    });
  }

  private loadCatalogs(showLoading = true): void {
    if (showLoading) {
      this.isLoading = true;
    }

    forkJoin({
      customers: this.api.getCustomers(),
      products: this.api.getProducts(),
      machines: this.api.getMachines()
    }).subscribe({
      next: ({ customers, products, machines }) => {
        this.customers = customers;
        // The profile version belongs to the loaded snapshot, never to a separate catalog refresh.
        if (this.selectedCustomer) {
          const details = customers.find(customer => customer.id === this.selectedCustomer?.id);
          if (details) {
            this.selectedCustomer = { ...details, orderingProfileVersion: this.selectedCustomer.orderingProfileVersion, isOrderProfileApproved: this.selectedCustomer.isOrderProfileApproved };
            if (!this.workingWeekDirty && !this.isWeekSaving) this.setWorkingWeek(details.restDays ?? []);
          }
        }
        this.products = products;
        this.machines = machines;
        this.refreshDayStatus();
        this.isLoading = false;
        this.errorMessage = null;
        if (this.focusedCustomerId && !this.selectedCustomer) {
          const focused = customers.find(customer => customer.id === this.focusedCustomerId);
          if (focused) this.openCustomerConfig(focused);
          else this.errorMessage = 'No se encontró el cliente solicitado.';
        }
        const productId = this.route.snapshot.queryParamMap.get('productId');
        if (productId && !this.configurationOnly && showLoading) {
          const product = products.find(item => item.id === productId);
          if (product) { this.activeTab = 'products'; this.editProduct(product); }
        }
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.isLoading = false;
        this.showError(error, 'No se pudieron cargar los catalogos.');
      }
    });
  }

  private reloadAccessTokens(): void {
    if (!this.selectedCustomer) {
      return;
    }

    const customerId = this.selectedCustomer.id;
    const sequence = this.profileReadSequence;
    this.api.getAccessTokens(customerId).subscribe({
      next: (tokens) => {
        if (this.selectedCustomer?.id !== customerId || this.profileReadSequence !== sequence) return;
        this.accessTokens = tokens;
        if (this.generatedAccess && !tokens.some(token => token.tokenId === this.generatedAccess?.tokenId && token.isActive)) {
          this.generatedAccess = null; this.generatedPlainToken = null;
        }
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (this.selectedCustomer?.id === customerId && this.profileReadSequence === sequence)
          this.showError(error, 'No se pudieron cargar los tokens.');
      }
    });
  }

  private emptyCustomerForm(): CustomerEditorValue {
    return {
      id: null,
      name: '',
      phoneNumber: null,
      preferredDeliveryTime: null,
      preferredDeliveryWindowStart: null,
      preferredDeliveryWindowEnd: null,
      deliveryNotes: null,
      routeCode: null,
      routeStopOrder: null,
      hasStoreKey: null,
      paymentTermCode: null,
      internalDeliveryInstructions: null,
      restDays: []
    };
  }

  private emptyProductForm(): ProductForm {
    return {
      id: null,
      name: '',
      description: null,
      unitCode: null
    };
  }

  private emptyMachineForm(): MachineForm {
    return {
      id: null,
      number: null,
      name: null
    };
  }

  private emptyToNull(value: string | null): string | null {
    return value && value.trim() ? value.trim() : null;
  }

  private toTimeInput(value: string | null): string | null {
    return value ? value.slice(0, 5) : null;
  }

  private toIsoDateTime(value: string | null): string | null {
    return value ? new Date(value).toISOString() : null;
  }

  private showError(error: unknown, fallback: string): void {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      this.errorMessage = error.error.error;
    } else {
      this.errorMessage = fallback;
    }

    this.changeDetector.detectChanges();
  }

  private profileErrorText(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && typeof error.error?.error === 'string' ? error.error.error : fallback;
  }
}
