import { AfterViewInit, Component, DestroyRef, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AdminCatalogApiService, AdminCustomerRouteApiResponse } from './admin-catalog-api.service';
import { CustomerWorkingWeekComponent } from './customer-working-week.component';
import { CustomerEditorValue } from './customer-editor.model';

@Component({
  selector: 'app-admin-customer-dialog',
  imports: [FormsModule, CustomerWorkingWeekComponent],
  templateUrl: './admin-customer-dialog.component.html',
  styleUrl: './admin-customer-dialog.component.css'
})
export class AdminCustomerDialogComponent implements OnInit, AfterViewInit, OnDestroy {
  @Input({ required: true }) initialValue!: CustomerEditorValue;
  @Input() saving = false;
  @Input() error: string | null = null;
  @Output() saved = new EventEmitter<CustomerEditorValue>();
  @Output() dismissed = new EventEmitter<void>();
  @ViewChild('dialog', { static: true }) private dialog!: ElementRef<HTMLDialogElement>;
  private readonly api = inject(AdminCatalogApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly activeRoutes = signal<AdminCustomerRouteApiResponse[]>([]);
  protected readonly routeCatalog = signal<'loading' | 'ready' | 'error'>('loading');
  protected customerForm!: CustomerEditorValue;
  private originalRouteCode: string | null = null;
  private returnFocusTo: HTMLElement | null = null;

  ngOnInit(): void {
    this.customerForm = { ...this.initialValue, restDays: [...this.initialValue.restDays] };
    this.originalRouteCode = this.initialValue.routeCode;
    this.loadRoutes();
  }

  ngAfterViewInit(): void {
    this.returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.dialog.nativeElement.showModal();
  }

  ngOnDestroy(): void {
    this.dialog.nativeElement.close();
    if (this.returnFocusTo?.isConnected) this.returnFocusTo.focus();
  }

  protected get storedRouteCode(): string | null {
    const code = this.originalRouteCode;
    return code && !this.activeRoutes().some(route => route.code === code) ? code : null;
  }

  protected loadRoutes(): void {
    this.routeCatalog.set('loading');
    this.api.getCustomerRoutes().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: routes => {
        this.activeRoutes.set(routes);
        this.routeCatalog.set('ready');
      },
      error: () => this.routeCatalog.set('error')
    });
  }

  protected selectRoute(code: string): void {
    const nextCode = code || null;
    if (this.customerForm.routeCode === nextCode) return;
    this.customerForm.routeCode = nextCode;
    this.customerForm.routeStopOrder = null;
  }

  protected submit(): void {
    if (!this.saving && this.customerForm.name.trim()) {
      this.saved.emit({ ...this.customerForm, restDays: [...this.customerForm.restDays] });
    }
  }

  protected dismiss(event?: Event): void {
    event?.preventDefault();
    if (!this.saving) this.dismissed.emit();
  }

  protected backdrop(event: MouseEvent): void {
    if (event.target !== this.dialog.nativeElement) return;
    const bounds = this.dialog.nativeElement.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) this.dismiss();
  }

}
