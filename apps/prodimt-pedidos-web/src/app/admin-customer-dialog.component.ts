import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
  protected customerForm!: CustomerEditorValue;
  private returnFocusTo: HTMLElement | null = null;

  ngOnInit(): void {
    this.customerForm = { ...this.initialValue, restDays: [...this.initialValue.restDays] };
  }

  ngAfterViewInit(): void {
    this.returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.dialog.nativeElement.showModal();
  }

  ngOnDestroy(): void {
    this.dialog.nativeElement.close();
    if (this.returnFocusTo?.isConnected) this.returnFocusTo.focus();
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
