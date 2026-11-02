import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ProductEditorValue } from './product-editor.model';

@Component({
  selector: 'app-admin-product-dialog', imports: [FormsModule],
  templateUrl: './admin-product-dialog.component.html', styleUrl: './admin-product-dialog.component.css'
})
export class AdminProductDialogComponent implements OnInit, AfterViewInit, OnDestroy {
  @Input({ required: true }) initialValue!: ProductEditorValue;
  @Input() saving = false;
  @Input() error: string | null = null;
  @Output() saved = new EventEmitter<ProductEditorValue>();
  @Output() dismissed = new EventEmitter<void>();
  @ViewChild('dialog', { static: true }) private dialog!: ElementRef<HTMLDialogElement>;
  protected value!: ProductEditorValue;
  private returnFocusTo: HTMLElement | null = null;
  ngOnInit(): void { this.value = { ...this.initialValue }; }
  ngAfterViewInit(): void {
    this.returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.dialog.nativeElement.showModal();
  }
  ngOnDestroy(): void {
    this.dialog.nativeElement.close();
    if (this.returnFocusTo?.isConnected) this.returnFocusTo.focus();
  }
  protected submit(): void { if (!this.saving && this.value.name.trim()) this.saved.emit({ ...this.value, name: this.value.name.trim() }); }
  protected dismiss(event?: Event): void { event?.preventDefault(); if (!this.saving) this.dismissed.emit(); }
  protected backdrop(event: MouseEvent): void {
    if (event.target !== this.dialog.nativeElement) return;
    const box = this.dialog.nativeElement.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) this.dismiss();
  }
}
