import { DOCUMENT } from '@angular/common';
import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild, inject } from '@angular/core';

@Component({
  selector: 'app-admin-access-tokens-dialog',
  templateUrl: './admin-access-tokens-dialog.component.html',
  styleUrl: './admin-access-tokens-dialog.component.css'
})
export class AdminAccessTokensDialogComponent implements AfterViewInit, OnDestroy {
  @Input({ required: true }) customerName = '';
  @Input() busy = false;
  @Output() dismissed = new EventEmitter<void>();
  @ViewChild('dialog', { static: true }) private dialog!: ElementRef<HTMLDialogElement>;
  private readonly document = inject(DOCUMENT);
  private returnFocusTo: HTMLElement | null = null;
  private previousOverflow = '';
  private pressedBackdrop = false;

  ngAfterViewInit(): void {
    this.returnFocusTo = this.document.activeElement instanceof HTMLElement ? this.document.activeElement : null;
    this.previousOverflow = this.document.body.style.overflow;
    this.document.body.style.overflow = 'hidden';
    this.dialog.nativeElement.showModal();
  }

  ngOnDestroy(): void {
    this.dialog.nativeElement.close();
    this.document.body.style.overflow = this.previousOverflow;
    if (this.returnFocusTo?.isConnected) this.returnFocusTo.focus({ preventScroll: true });
  }

  protected dismiss(event?: Event): void {
    event?.preventDefault();
    if (!this.busy) this.dismissed.emit();
  }

  protected pressBackdrop(event: PointerEvent): void {
    this.pressedBackdrop = this.isOutside(event);
  }

  protected backdrop(event: MouseEvent): void {
    if (this.pressedBackdrop && this.isOutside(event)) this.dismiss();
    this.pressedBackdrop = false;
  }

  private isOutside(event: MouseEvent): boolean {
    if (event.target !== this.dialog.nativeElement) return false;
    const bounds = this.dialog.nativeElement.getBoundingClientRect();
    return event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom;
  }
}
