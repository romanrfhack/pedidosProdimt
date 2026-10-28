import { DOCUMENT } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminCustomerDialogComponent } from './admin-customer-dialog.component';
import { CustomerEditorValue } from './customer-editor.model';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import {
  AdminCatalogApiService,
  CustomerDayStatusResponse,
  AdminCustomerAccessTokenApiResponse,
  AdminCustomerCatalogApiResponse,
  AdminCustomerFrequentProductApiResponse,
  AdminCustomerMachineAssignmentApiResponse,
  AdminMachineCatalogApiResponse,
  AdminProductCatalogApiResponse
} from './admin-catalog-api.service';
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
  imports: [FormsModule, ReactiveFormsModule, RouterLink, AdminCustomerDialogComponent],
  styleUrl: './admin-customer-cards.css',
  styles: [`
    .profile-summary, .profile-product-heading { display: flex; gap: 12px; flex-wrap: wrap; align-items: center; justify-content: space-between; }
    .profile-product-card { border: 1px solid #d6e0e7; padding: 14px; border-radius: 12px; }
    .profile-product-heading { grid-column: 1 / -1; }
    .profile-actions { display: flex; flex-wrap: wrap; gap: 8px; grid-column: 1 / -1; }
    .profile-errors { margin: 0; padding-left: 20px; }
    .profile-help { margin: 0; color: #526478; }
    .profile-unit { display: grid; gap: 6px; }
    fieldset { border: 0; margin: 0; padding: 0; min-width: 0; }
    .profile-search { max-width: 540px; }
    .profile-search input { width: 100%; }
    .profile-status { margin: 0; }
  `],
  template: `
    <section class="page stack" data-testid="admin-catalogs">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>{{ configurationOnly ? 'Configuración del cliente' : 'Catalogos' }}</h2>
      </div>

      @if (configurationOnly) {
        <a routerLink="/admin/catalogos">← Volver a clientes y catálogos</a>
      } @else {
      <div class="tab-bar" aria-label="Catalogos">
        <button type="button" [class.active]="activeTab === 'customers'" (click)="activeTab = 'customers'">Clientes</button>
        <button type="button" [class.active]="activeTab === 'products'" (click)="activeTab = 'products'">Productos</button>
        <button type="button" [class.active]="activeTab === 'machines'" (click)="activeTab = 'machines'">Maquinas</button>
      </div>
      }

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
            @if (!configurationOnly) {
              <div class="customer-toolbar">
                <label class="customer-search">
                  <span>Buscar cliente</span>
                  <input name="customerSearch" type="search" [(ngModel)]="customerSearch" placeholder="Nombre, teléfono o ruta" autocomplete="off">
                </label>
                <button type="button" class="primary compact new-customer" (click)="newCustomer()"><span aria-hidden="true">+</span> Nuevo cliente</button>
              </div>
              <div class="customer-day-heading">
                <span>{{ dailyDate ? (dayStatusError ? 'Última consulta · ' : 'Hoy · ') + dailyDate.split('-').reverse().join('/') : 'Estado de hoy' }} · {{ customers.length }} clientes</span>
                <button type="button" class="refresh-day" [disabled]="isDayLoading" (click)="refreshDayStatus()">{{ isDayLoading ? 'Actualizando…' : 'Actualizar estados' }}</button>
              </div>
              <div class="customer-filters" aria-label="Estado de hoy">
                <button type="button" [attr.aria-pressed]="customerDayFilter === 'all'" (click)="customerDayFilter = 'all'">Todos <strong>{{ customers.length }}</strong></button>
                <button type="button" [attr.aria-pressed]="customerDayFilter === 'AwaitingOrder'" (click)="customerDayFilter = 'AwaitingOrder'">Por confirmar <strong>{{ countDayState('AwaitingOrder') }}</strong></button>
                <button type="button" [attr.aria-pressed]="customerDayFilter === 'HasOrder'" (click)="customerDayFilter = 'HasOrder'">Con pedido <strong>{{ countDayState('HasOrder') }}</strong></button>
                <button type="button" [attr.aria-pressed]="customerDayFilter === 'off'" (click)="customerDayFilter = 'off'">Sin pedido hoy <strong>{{ countDayState('NoOrderConfirmed') + countDayState('ScheduledRest') }}</strong></button>
              </div>
            }
            @if (dayStatusError) {
              <div class="alert warning" role="status">{{ dayStatusError }} <button type="button" class="refresh-day" [disabled]="isDayLoading" (click)="refreshDayStatus()">Reintentar</button></div>
            }
            <div class="admin-list customer-list">
              @for (customer of visibleCustomers; track customer.id) {
                <article class="admin-row customer-card" data-testid="customer-card" [attr.data-customer-id]="customer.id" [attr.data-day-state]="dayState(customer)"
                  [class.customer-awaiting]="dayState(customer) === 'AwaitingOrder'"
                  [class.customer-ordered]="dayState(customer) === 'HasOrder'"
                  [class.customer-off]="dayState(customer) === 'NoOrderConfirmed' || dayState(customer) === 'ScheduledRest'"
                  [class.customer-inactive]="!customer.isActive">
                  <div class="customer-card-top">
                    <div class="customer-identity">
                      <span class="customer-avatar" aria-hidden="true">{{ initials(customer.name) }}</span>
                      <div><h3>{{ customer.name }}</h3><small class="customer-route">{{ customer.routeCode || 'Ruta por confirmar' }}@if (customer.routeStopOrder) { · Visita {{ customer.routeStopOrder }} }</small></div>
                    </div>
                    <span class="day-label">{{ dayLabel(customer) }}</span>
                  </div>
                  <div class="customer-meta"><span>{{ customer.phoneNumber || 'Sin teléfono' }}</span><span>Entrega: {{ formatCustomerDelivery(customer) }}</span></div>
                  <div class="customer-tags">
                    <span>{{ storeKeyLabel(customer.hasStoreKey) }}</span><span>{{ paymentTermLabel(customer.paymentTermCode) }}</span>
                    @if (dayStatuses.get(customer.id)?.requiresReview) { <span class="needs-review">Pedido pendiente de revisión</span> }
                    @if (!customer.isActive) { <span>Cliente inactivo</span> }
                  </div>
                  <div class="review-actions">
                    @if (!configurationOnly) { <button type="button" class="primary compact" (click)="navigateToCustomerConfig(customer)">Configurar</button> }
                    <button type="button" class="secondary compact" (click)="editCustomer(customer)">{{ configurationOnly ? 'Editar datos y entrega' : 'Editar' }}</button>
                    <button type="button" class="secondary compact quiet-action" (click)="toggleCustomer(customer)">{{ customer.isActive ? 'Desactivar' : 'Activar' }}</button>
                  </div>

                  @if (selectedCustomer?.id === customer.id) {
                    <section class="detail-panel stack" data-testid="customer-config">
                      <div class="profile-summary">
                        <strong>Configuración de {{ customer.name }}</strong>
                        @if (profileDirty) { <span class="badge warning">Cambios sin guardar</span> }
                      </div>
                      @if (isConfigLoading) { <div class="notice">Cargando configuración...</div> }
                      @if (profileMessage) { <div class="alert success" role="status">{{ profileMessage }}</div> }
                      @if (profileError) { <div class="alert error" role="alert">{{ profileError }}</div> }
                      @if (profileConflict) {
                        <button type="button" class="secondary compact" (click)="reloadProfile()">Recargar perfil y descartar mis cambios</button>
                      }

                      <fieldset class="stack" [disabled]="isConfigLoading || isProfileSaving">
                      <div class="stack">
                        <strong>Productos frecuentes</strong>
                        <p class="notice">Perfil versión {{ selectedCustomer?.orderingProfileVersion ?? 0 }} · {{ selectedCustomer?.isOrderProfileApproved ? 'Aprobado para interpretar posiciones' : 'Pendiente de aprobación' }}. El orden visual y la posición en mensajes como “15 + 20” se configuran por separado.</p>
                        <p class="profile-help">Cada fila es un producto de este cliente. Subir o bajar cambia el orden en pantalla; la posición del mensaje se mantiene hasta que la edites. Las posiciones de cada cliente comienzan en 1.</p>
                        @if (frequentRows.length === 0) { <div class="notice">Este cliente aún no tiene productos configurados. Agrega los que suele pedir.</div> }
                        <div class="line-list">
                          @for (row of frequentRows; track row; let i = $index) {
                            <div class="line-row frequent-line profile-product-card" data-testid="profile-product">
                              <div class="profile-product-heading">
                                <strong>{{ i + 1 }}. {{ productFor(row)?.name || row.productName }}</strong>
                                @if (!productFor(row)?.unitCode) { <span class="badge warning">Falta unidad</span> }
                              </div>
                              <label><span>Producto</span><select [(ngModel)]="row.productId" [name]="'frequentProduct' + i" (ngModelChange)="invalidateProfileApproval()">
                                @for (product of profileProducts; track product.id) {
                                  <option [value]="product.id">{{ product.name }} · {{ unitLabel(product.unitCode) }}{{ product.isActive ? '' : ' (inactivo)' }}</option>
                                }
                              </select></label>
                              <div class="profile-unit">
                                <span>Unidad de venta: <strong>{{ unitLabel(productFor(row)?.unitCode) }}</strong></span>
                                <button type="button" class="secondary compact" (click)="editProductUnit(row)">Ver producto y unidad en catálogo</button>
                              </div>
                              <label><span>Cantidad sugerida</span><input type="number" min="0" step="0.01" [(ngModel)]="row.defaultQuantity" [name]="'frequentQuantity' + i" (ngModelChange)="invalidateProfileApproval()"></label>
                              <label><span>Orden visual</span><input type="number" min="1" step="1" [(ngModel)]="row.sortOrder" [name]="'frequentSort' + i" (ngModelChange)="invalidateProfileApproval()"></label>
                              <label><span>Posición en mensaje</span><input type="number" min="1" step="1" [(ngModel)]="row.interpretationPosition" [name]="'frequentPosition' + i" (ngModelChange)="invalidateProfileApproval()" placeholder="Sin confirmar"></label>
                              <label><span>Máquina para este producto</span><select [(ngModel)]="row.preferredMachineId" [name]="'frequentMachine' + i" (ngModelChange)="invalidateProfileApproval()">
                                <option [ngValue]="null">Usar asignación del cliente</option>
                                @for (machine of machines; track machine.id) {
                                  <option [value]="machine.id">#{{ machine.number }} {{ machine.name || '' }}{{ machine.isActive ? '' : ' (inactiva)' }}</option>
                                }
                              </select></label>
                              <label class="inline-check">
                                <input type="checkbox" [(ngModel)]="row.isActive" [name]="'frequentActive' + i" (ngModelChange)="invalidateProfileApproval()">
                                <span>Activo</span>
                              </label>
                              <div class="profile-actions">
                                <button type="button" class="secondary compact" [disabled]="i === 0" (click)="moveFrequentRow(i, -1)">Subir</button>
                                <button type="button" class="secondary compact" [disabled]="i === frequentRows.length - 1" (click)="moveFrequentRow(i, 1)">Bajar</button>
                                <button type="button" class="secondary compact" (click)="removeFrequentRow(i)">Quitar</button>
                              </div>
                            </div>
                          }
                        </div>
                        <small>La unidad pertenece al producto del catálogo. Su cambio aplica a futuras capturas de los clientes que lo usan; los pedidos existentes conservan su unidad registrada.</small>
                        @if (profileValidationErrors.length > 0) {
                          <div class="alert error" data-testid="profile-validation"><ul class="profile-errors">
                            @for (message of profileValidationErrors; track message) { <li>{{ message }}</li> }
                          </ul></div>
                        }
                        @if (profileWarnings.length > 0) {
                          <div class="alert warning"><ul class="profile-errors">
                            @for (message of profileWarnings; track message) { <li>{{ message }}</li> }
                          </ul></div>
                        }
                        <p class="profile-help" data-testid="position-preview">{{ interpretationPreview }}</p>
                        <button type="button" class="secondary compact" [disabled]="frequentRows.length === 0" (click)="useVisualOrderForPositions()">Usar orden visual como posiciones 1, 2, 3...</button>
                        <label class="inline-check">
                          <input type="checkbox" name="approveProfile" [formControl]="profileApproval">
                          <span>He verificado los productos y sus posiciones; aprobar este perfil</span>
                        </label>
                        <small>Guardar sin esta confirmación deja el perfil pendiente de aprobación. Todavía no se interpretan mensajes automáticamente.</small>
                        <div class="review-actions">
                          <button type="button" class="secondary compact" (click)="addFrequentRow()">Agregar producto</button>
                          <button type="button" class="primary compact" [disabled]="profileValidationErrors.length > 0 || profileConflict" (click)="saveFrequentProducts()">{{ isProfileSaving ? 'Guardando...' : 'Guardar frecuentes' }}</button>
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
                      </fieldset>
                    </section>
                  }
                </article>
              } @empty {
                <div class="empty-customers">{{ customers.length ? 'No hay clientes que coincidan con la búsqueda o el estado elegido.' : 'Agrega tu primer cliente para comenzar.' }}</div>
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
              <label>
                <span>Unidad de venta</span>
                <select name="productUnit" [(ngModel)]="productForm.unitCode">
                  <option [ngValue]="null">Por confirmar</option>
                  <option value="kg">Kilogramos</option>
                  <option value="piece">Piezas</option>
                  <option value="dozen">Docenas</option>
                  <option value="package">Paquetes</option>
                </select>
                <small>Confirma la unidad antes de capturar pedidos. Un paquete no se convierte automáticamente a kilos.</small>
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
                    <small>Unidad: {{ unitLabel(product.unitCode) }}</small>
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
    @defer (when customerDialogOpen) {
      @if (customerDialogOpen) {
        <app-admin-customer-dialog [initialValue]="customerForm" [saving]="isCustomerSaving" [error]="customerDialogError"
          (saved)="saveCustomer($event)" (dismissed)="resetCustomerForm()" />
      }
    } @loading { <p class="notice">Abriendo formulario…</p> }
  `
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
  protected newTokenDescription: string | null = null;
  protected newTokenExpiresAt: string | null = null;
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
    this.profileApproval.setValue(false);
    this.generatedPlainToken = null;
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
        interpretationPosition: null
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
        interpretationPosition: row.interpretationPosition ?? null
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
          if (details) this.selectedCustomer = { ...details, orderingProfileVersion: this.selectedCustomer.orderingProfileVersion, isOrderProfileApproved: this.selectedCustomer.isOrderProfileApproved };
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

    this.api.getAccessTokens(this.selectedCustomer.id).subscribe({
      next: (tokens) => {
        this.accessTokens = tokens;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => this.showError(error, 'No se pudieron cargar los tokens.')
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
