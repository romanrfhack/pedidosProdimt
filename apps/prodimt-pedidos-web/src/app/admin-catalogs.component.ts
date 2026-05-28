import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import {
  AdminCatalogApiService,
  AdminCustomerAccessTokenApiResponse,
  AdminCustomerCatalogApiResponse,
  AdminCustomerFrequentProductApiResponse,
  AdminCustomerMachineAssignmentApiResponse,
  AdminMachineCatalogApiResponse,
  AdminProductCatalogApiResponse
} from './admin-catalog-api.service';

type CatalogTab = 'customers' | 'products' | 'machines';

interface CustomerForm {
  id: string | null;
  name: string;
  phoneNumber: string | null;
  preferredDeliveryTime: string | null;
  preferredDeliveryWindowStart: string | null;
  preferredDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
}

interface ProductForm {
  id: string | null;
  name: string;
  description: string | null;
}

interface MachineForm {
  id: string | null;
  number: number | null;
  name: string | null;
}

@Component({
  selector: 'app-admin-catalogs',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="admin-catalogs">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Catalogos</h2>
      </div>

      <div class="tab-bar" aria-label="Catalogos">
        <button type="button" [class.active]="activeTab === 'customers'" (click)="activeTab = 'customers'">Clientes</button>
        <button type="button" [class.active]="activeTab === 'products'" (click)="activeTab = 'products'">Productos</button>
        <button type="button" [class.active]="activeTab === 'machines'" (click)="activeTab = 'machines'">Maquinas</button>
      </div>

      @if (isLoading) {
        <div class="notice">Cargando catalogos...</div>
      }

      @if (resultMessage) {
        <div class="alert success">{{ resultMessage }}</div>
      }

      @if (errorMessage) {
        <div class="alert error">{{ errorMessage }}</div>
      }

      @if (!isLoading) {
        @if (activeTab === 'customers') {
          <section class="stack" data-testid="catalog-customers">
            <form class="detail-panel stack" (ngSubmit)="saveCustomer()">
              <strong>{{ customerForm.id ? 'Editar cliente' : 'Nuevo cliente' }}</strong>
              <label>
                <span>Nombre</span>
                <input name="customerName" [(ngModel)]="customerForm.name">
              </label>
              <label>
                <span>Telefono</span>
                <input name="customerPhone" [(ngModel)]="customerForm.phoneNumber">
              </label>
              <div class="catalog-grid">
                <label>
                  <span>Hora entrega</span>
                  <input name="customerDeliveryTime" type="time" [(ngModel)]="customerForm.preferredDeliveryTime">
                </label>
                <label>
                  <span>Ventana inicio</span>
                  <input name="customerWindowStart" type="time" [(ngModel)]="customerForm.preferredDeliveryWindowStart">
                </label>
                <label>
                  <span>Ventana fin</span>
                  <input name="customerWindowEnd" type="time" [(ngModel)]="customerForm.preferredDeliveryWindowEnd">
                </label>
              </div>
              <label>
                <span>Notas entrega</span>
                <input name="customerDeliveryNotes" [(ngModel)]="customerForm.deliveryNotes">
              </label>
              <div class="review-actions">
                <button type="submit" class="primary compact">{{ customerForm.id ? 'Guardar cliente' : 'Crear cliente' }}</button>
                <button type="button" class="secondary compact" (click)="resetCustomerForm()">Limpiar</button>
              </div>
            </form>

            <div class="admin-list">
              @for (customer of customers; track customer.id) {
                <article class="admin-row">
                  <div>
                    <strong>{{ customer.name }}</strong>
                    <small>Telefono: {{ customer.phoneNumber || 'Sin telefono' }}</small>
                    <small>Entrega: {{ formatCustomerDelivery(customer) }}</small>
                  </div>
                  <div class="badges">
                    <span class="badge" [class.neutral]="customer.isActive" [class.danger]="!customer.isActive">
                      {{ customer.isActive ? 'Activo' : 'Inactivo' }}
                    </span>
                  </div>
                  <div class="review-actions">
                    <button type="button" class="secondary compact" (click)="editCustomer(customer)">Editar</button>
                    <button type="button" class="secondary compact" (click)="openCustomerConfig(customer)">Configurar</button>
                    <button type="button" class="secondary compact" (click)="toggleCustomer(customer)">
                      {{ customer.isActive ? 'Desactivar' : 'Activar' }}
                    </button>
                  </div>

                  @if (selectedCustomer?.id === customer.id) {
                    <section class="detail-panel stack" data-testid="customer-config">
                      <strong>Configuracion de {{ customer.name }}</strong>

                      <div class="stack">
                        <strong>Productos frecuentes</strong>
                        <div class="line-list">
                          @for (row of frequentRows; track row.productId; let i = $index) {
                            <div class="line-row catalog-line">
                              <select [(ngModel)]="row.productId" [name]="'frequentProduct' + i">
                                @for (product of products; track product.id) {
                                  <option [value]="product.id">{{ product.name }}{{ product.isActive ? '' : ' (inactivo)' }}</option>
                                }
                              </select>
                              <input type="number" min="0" step="1" [(ngModel)]="row.defaultQuantity" [name]="'frequentQuantity' + i" aria-label="Cantidad default">
                              <input type="number" min="1" step="1" [(ngModel)]="row.sortOrder" [name]="'frequentSort' + i" aria-label="Orden">
                              <label class="inline-check">
                                <input type="checkbox" [(ngModel)]="row.isActive" [name]="'frequentActive' + i">
                                <span>Activo</span>
                              </label>
                              <button type="button" class="secondary compact" (click)="removeFrequentRow(i)">Quitar</button>
                            </div>
                          }
                        </div>
                        <div class="review-actions">
                          <button type="button" class="secondary compact" (click)="addFrequentRow()">Agregar producto</button>
                          <button type="button" class="primary compact" (click)="saveFrequentProducts()">Guardar frecuentes</button>
                        </div>
                      </div>

                      <div class="stack">
                        <strong>Maquinas asignadas</strong>
                        <div class="line-list">
                          @for (row of machineAssignmentRows; track row.machineId; let i = $index) {
                            <div class="line-row catalog-line">
                              <select [(ngModel)]="row.machineId" [name]="'assignmentMachine' + i">
                                @for (machine of machines; track machine.id) {
                                  <option [value]="machine.id">#{{ machine.number }} {{ machine.name || '' }}{{ machine.isActive ? '' : ' (inactiva)' }}</option>
                                }
                              </select>
                              <label class="inline-check">
                                <input type="checkbox" [(ngModel)]="row.isDefault" [name]="'assignmentDefault' + i" (ngModelChange)="setDefaultAssignment(row)">
                                <span>Default</span>
                              </label>
                              <label class="inline-check">
                                <input type="checkbox" [(ngModel)]="row.isActive" [name]="'assignmentActive' + i">
                                <span>Activa</span>
                              </label>
                              <input [(ngModel)]="row.notes" [name]="'assignmentNotes' + i" aria-label="Notas asignacion">
                              <button type="button" class="secondary compact" (click)="removeMachineAssignmentRow(i)">Quitar</button>
                            </div>
                          }
                        </div>
                        <div class="review-actions">
                          <button type="button" class="secondary compact" (click)="addMachineAssignmentRow()">Agregar maquina</button>
                          <button type="button" class="primary compact" (click)="saveMachineAssignments()">Guardar maquinas</button>
                        </div>
                      </div>

                      <div class="stack">
                        <strong>Tokens de acceso</strong>
                        @if (generatedPlainToken) {
                          <div class="alert warning">
                            Token generado: <code>{{ generatedPlainToken }}</code>
                          </div>
                        }
                        <div class="catalog-grid">
                          <label>
                            <span>Descripcion</span>
                            <input name="newTokenDescription" [(ngModel)]="newTokenDescription">
                          </label>
                          <label>
                            <span>Expira</span>
                            <input name="newTokenExpiresAt" type="datetime-local" [(ngModel)]="newTokenExpiresAt">
                          </label>
                        </div>
                        <div class="review-actions">
                          <button type="button" class="primary compact" (click)="createAccessToken()">Crear token</button>
                        </div>
                        <div class="line-list">
                          @for (token of accessTokens; track token.tokenId) {
                            <div class="line-row">
                              <span>
                                <strong>{{ token.description }}</strong>
                                <small>{{ token.isActive ? 'Activo' : 'Revocado' }} · Creado {{ formatDate(token.createdAt) }}</small>
                              </span>
                              <button type="button" class="secondary compact" [disabled]="!token.isActive" (click)="revokeAccessToken(token)">Revocar</button>
                            </div>
                          }
                        </div>
                      </div>
                    </section>
                  }
                </article>
              }
            </div>
          </section>
        }

        @if (activeTab === 'products') {
          <section class="stack" data-testid="catalog-products">
            <form class="detail-panel stack" (ngSubmit)="saveProduct()">
              <strong>{{ productForm.id ? 'Editar producto' : 'Nuevo producto' }}</strong>
              <label>
                <span>Nombre</span>
                <input name="productName" [(ngModel)]="productForm.name">
              </label>
              <label>
                <span>Descripcion</span>
                <input name="productDescription" [(ngModel)]="productForm.description">
              </label>
              <div class="review-actions">
                <button type="submit" class="primary compact">{{ productForm.id ? 'Guardar producto' : 'Crear producto' }}</button>
                <button type="button" class="secondary compact" (click)="resetProductForm()">Limpiar</button>
              </div>
            </form>

            <div class="admin-list">
              @for (product of products; track product.id) {
                <article class="admin-row">
                  <div>
                    <strong>{{ product.name }}</strong>
                    <small>{{ product.description || 'Sin descripcion' }}</small>
                  </div>
                  <div class="badges">
                    <span class="badge" [class.neutral]="product.isActive" [class.danger]="!product.isActive">
                      {{ product.isActive ? 'Activo' : 'Inactivo' }}
                    </span>
                  </div>
                  <div class="review-actions">
                    <button type="button" class="secondary compact" (click)="editProduct(product)">Editar</button>
                    <button type="button" class="secondary compact" (click)="toggleProduct(product)">
                      {{ product.isActive ? 'Desactivar' : 'Activar' }}
                    </button>
                  </div>
                </article>
              }
            </div>
          </section>
        }

        @if (activeTab === 'machines') {
          <section class="stack" data-testid="catalog-machines">
            <form class="detail-panel stack" (ngSubmit)="saveMachine()">
              <strong>{{ machineForm.id ? 'Editar maquina' : 'Nueva maquina' }}</strong>
              <label>
                <span>Numero</span>
                <input name="machineNumber" type="number" min="1" step="1" [(ngModel)]="machineForm.number">
              </label>
              <label>
                <span>Nombre</span>
                <input name="machineName" [(ngModel)]="machineForm.name">
              </label>
              <div class="review-actions">
                <button type="submit" class="primary compact">{{ machineForm.id ? 'Guardar maquina' : 'Crear maquina' }}</button>
                <button type="button" class="secondary compact" (click)="resetMachineForm()">Limpiar</button>
              </div>
            </form>

            <div class="admin-list">
              @for (machine of machines; track machine.id) {
                <article class="admin-row">
                  <div>
                    <strong>#{{ machine.number }}</strong>
                    <small>{{ machine.name || 'Sin nombre' }}</small>
                  </div>
                  <div class="badges">
                    <span class="badge" [class.neutral]="machine.isActive" [class.danger]="!machine.isActive">
                      {{ machine.isActive ? 'Activa' : 'Inactiva' }}
                    </span>
                  </div>
                  <div class="review-actions">
                    <button type="button" class="secondary compact" (click)="editMachine(machine)">Editar</button>
                    <button type="button" class="secondary compact" (click)="toggleMachine(machine)">
                      {{ machine.isActive ? 'Desactivar' : 'Activar' }}
                    </button>
                  </div>
                </article>
              }
            </div>
          </section>
        }
      }
    </section>
  `
})
export class AdminCatalogsComponent {
  private readonly api = inject(AdminCatalogApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);

  protected activeTab: CatalogTab = 'customers';
  protected customers: AdminCustomerCatalogApiResponse[] = [];
  protected products: AdminProductCatalogApiResponse[] = [];
  protected machines: AdminMachineCatalogApiResponse[] = [];
  protected frequentRows: AdminCustomerFrequentProductApiResponse[] = [];
  protected machineAssignmentRows: AdminCustomerMachineAssignmentApiResponse[] = [];
  protected accessTokens: AdminCustomerAccessTokenApiResponse[] = [];
  protected selectedCustomer: AdminCustomerCatalogApiResponse | null = null;
  protected generatedPlainToken: string | null = null;
  protected newTokenDescription: string | null = null;
  protected newTokenExpiresAt: string | null = null;
  protected isLoading = true;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;
  protected customerForm: CustomerForm = this.emptyCustomerForm();
  protected productForm: ProductForm = this.emptyProductForm();
  protected machineForm: MachineForm = this.emptyMachineForm();

  constructor() {
    this.loadCatalogs();
  }

  protected saveCustomer(): void {
    const request = {
      name: this.customerForm.name,
      phoneNumber: this.emptyToNull(this.customerForm.phoneNumber),
      preferredDeliveryTime: this.emptyToNull(this.customerForm.preferredDeliveryTime),
      preferredDeliveryWindowStart: this.emptyToNull(this.customerForm.preferredDeliveryWindowStart),
      preferredDeliveryWindowEnd: this.emptyToNull(this.customerForm.preferredDeliveryWindowEnd),
      deliveryNotes: this.emptyToNull(this.customerForm.deliveryNotes)
    };
    const operation = this.customerForm.id
      ? this.api.updateCustomer(this.customerForm.id, request)
      : this.api.createCustomer(request);

    operation.subscribe({
      next: () => {
        this.resultMessage = 'Cliente guardado.';
        this.resetCustomerForm();
        this.loadCatalogs(false);
      },
      error: (error: unknown) => this.showError(error, 'No se pudo guardar el cliente.')
    });
  }

  protected editCustomer(customer: AdminCustomerCatalogApiResponse): void {
    this.customerForm = {
      id: customer.id,
      name: customer.name,
      phoneNumber: customer.phoneNumber,
      preferredDeliveryTime: this.toTimeInput(customer.preferredDeliveryTime),
      preferredDeliveryWindowStart: this.toTimeInput(customer.preferredDeliveryWindowStart),
      preferredDeliveryWindowEnd: this.toTimeInput(customer.preferredDeliveryWindowEnd),
      deliveryNotes: customer.deliveryNotes
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
    this.selectedCustomer = customer;
    this.generatedPlainToken = null;
    this.resultMessage = null;
    this.errorMessage = null;

    forkJoin({
      frequentProducts: this.api.getFrequentProducts(customer.id),
      assignments: this.api.getMachineAssignments(customer.id),
      tokens: this.api.getAccessTokens(customer.id)
    }).subscribe({
      next: ({ frequentProducts, assignments, tokens }) => {
        this.frequentRows = frequentProducts;
        this.machineAssignmentRows = assignments;
        this.accessTokens = tokens;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => this.showError(error, 'No se pudo cargar la configuracion del cliente.')
    });
  }

  protected resetCustomerForm(): void {
    this.customerForm = this.emptyCustomerForm();
  }

  protected addFrequentRow(): void {
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
        defaultQuantity: 0,
        sortOrder: this.frequentRows.length + 1,
        isActive: true
      }
    ];
  }

  protected removeFrequentRow(index: number): void {
    this.frequentRows = this.frequentRows.filter((_, currentIndex) => currentIndex !== index);
  }

  protected saveFrequentProducts(): void {
    if (!this.selectedCustomer) {
      return;
    }

    this.api.updateFrequentProducts(this.selectedCustomer.id, {
      items: this.frequentRows.map((row) => ({
        productId: row.productId,
        defaultQuantity: row.defaultQuantity === null ? null : Number(row.defaultQuantity),
        sortOrder: Number(row.sortOrder) || 0,
        isActive: row.isActive
      }))
    }).subscribe({
      next: (rows) => {
        this.frequentRows = rows;
        this.resultMessage = 'Productos frecuentes guardados.';
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => this.showError(error, 'No se pudieron guardar los productos frecuentes.')
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
    if (!this.selectedCustomer) {
      return;
    }

    this.api.createAccessToken(this.selectedCustomer.id, {
      description: this.emptyToNull(this.newTokenDescription),
      expiresAt: this.toIsoDateTime(this.newTokenExpiresAt)
    }).subscribe({
      next: (token) => {
        this.generatedPlainToken = token.plainToken;
        this.newTokenDescription = null;
        this.newTokenExpiresAt = null;
        this.resultMessage = 'Token creado.';
        this.reloadAccessTokens();
      },
      error: (error: unknown) => this.showError(error, 'No se pudo crear el token.')
    });
  }

  protected revokeAccessToken(token: AdminCustomerAccessTokenApiResponse): void {
    if (!this.selectedCustomer) {
      return;
    }

    this.api.revokeAccessToken(this.selectedCustomer.id, token.tokenId).subscribe({
      next: () => {
        this.resultMessage = 'Token revocado.';
        this.generatedPlainToken = null;
        this.reloadAccessTokens();
      },
      error: (error: unknown) => this.showError(error, 'No se pudo revocar el token.')
    });
  }

  protected saveProduct(): void {
    const request = {
      name: this.productForm.name,
      description: this.emptyToNull(this.productForm.description)
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
      description: product.description
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
        this.products = products;
        this.machines = machines;
        this.isLoading = false;
        this.errorMessage = null;
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

    this.api.getAccessTokens(this.selectedCustomer.id).subscribe({
      next: (tokens) => {
        this.accessTokens = tokens;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => this.showError(error, 'No se pudieron cargar los tokens.')
    });
  }

  private emptyCustomerForm(): CustomerForm {
    return {
      id: null,
      name: '',
      phoneNumber: null,
      preferredDeliveryTime: null,
      preferredDeliveryWindowStart: null,
      preferredDeliveryWindowEnd: null,
      deliveryNotes: null
    };
  }

  private emptyProductForm(): ProductForm {
    return {
      id: null,
      name: '',
      description: null
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
}
