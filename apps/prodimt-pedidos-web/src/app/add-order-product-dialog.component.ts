import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdditionalProduct } from './product-setup-api.service';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-add-order-product-dialog', imports: [FormsModule],
  templateUrl: './add-order-product-dialog.component.html', styleUrl: './add-order-product-dialog.component.css'
})
export class AddOrderProductDialogComponent implements AfterViewInit, OnDestroy {
  @Input() products: AdditionalProduct[] = [];
  @Input() loading = false;
  @Input() error: string | null = null;
  @Output() added = new EventEmitter<{ product: AdditionalProduct; quantity: number }>();
  @Output() dismissed = new EventEmitter<void>();
  @Output() retried = new EventEmitter<void>();
  @ViewChild('dialog', { static: true }) private dialog!: ElementRef<HTMLDialogElement>;
  protected query = '';
  protected selectedId = '';
  protected quantity: number | null = null;
  protected readonly unitLabel = unitLabel;
  private focusBefore: HTMLElement | null = null;
  protected get filtered(): AdditionalProduct[] {
    const fold = (s: string) => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es-MX');
    const terms = fold(this.query.trim()).split(/\s+/).filter(Boolean);
    return this.products.filter(x => terms.every(term => fold(`${x.productName} ${unitLabel(x.unitCode)}`).includes(term)));
  }
  protected get selected(): AdditionalProduct | undefined { return this.products.find(x => x.productId === this.selectedId); }
  protected get valid(): boolean {
    const quantity = Number(this.quantity);
    return !this.loading && !this.error && !!this.selected && Number.isFinite(quantity) && quantity > 0
      && quantity <= 9999999999999999 && Math.abs(quantity * 100 - Math.round(quantity * 100)) < .0001;
  }
  ngAfterViewInit(): void {
    this.focusBefore = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.dialog.nativeElement.showModal();
  }
  ngOnDestroy(): void {
    this.dialog.nativeElement.close();
    if (this.focusBefore?.isConnected) this.focusBefore.focus();
  }
  protected add(): void {
    if (this.valid && this.selected) this.added.emit({ product: this.selected, quantity: Number(this.quantity) });
  }
  protected searchChanged(): void {
    if (this.selectedId && !this.filtered.some(x => x.productId === this.selectedId)) { this.selectedId = ''; this.quantity = null; }
  }
  protected productChanged(): void { this.quantity = null; }
  protected close(event?: Event): void { event?.preventDefault(); this.dismissed.emit(); }
  protected backdrop(event: MouseEvent): void {
    if (event.target !== this.dialog.nativeElement) return;
    const bounds = this.dialog.nativeElement.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) this.close();
  }
}
