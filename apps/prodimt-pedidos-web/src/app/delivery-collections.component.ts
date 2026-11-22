import { ChangeDetectorRef, Component, DestroyRef, ElementRef, Input, ViewChild, inject } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { CollectionApiService, collectionMoney, type TripCollections } from './collection-api.service';

@Component({
  selector: 'app-delivery-collections',
  templateUrl: './delivery-collections.component.html',
  styleUrls: ['./delivery-dialog.component.css', './delivery-collections.component.css']
})
export class DeliveryCollectionsComponent {
  private readonly api = inject(CollectionApiService);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly destroy = inject(DestroyRef);
  private generation = 0;
  @Input({ required: true }) tripId = '';
  @Input() management = false;
  @Input() tripLabel = '';
  @Input() blocked = false;
  @ViewChild('summaryDialog', { static: true }) private dialog!: ElementRef<HTMLDialogElement>;
  protected state: TripCollections | null = null;
  protected loading = false;
  protected error = '';
  protected readonly money = collectionMoney;

  protected async show(): Promise<void> {
    if (!this.tripId || this.blocked || this.loading) return;
    this.state = null;
    this.error = '';
    this.loading = true;
    const tripId = this.tripId, generation = ++this.generation;
    if (!this.dialog.nativeElement.open) this.dialog.nativeElement.showModal();
    try {
      const summary = await firstValueFrom(this.api.trip(tripId, this.management).pipe(timeout(15_000)));
      if (this.tripId === tripId && this.generation === generation && !this.destroy.destroyed) this.state = summary;
    } catch (error) {
      if (this.generation !== generation || this.destroy.destroyed) return;
      this.error = error instanceof HttpErrorResponse && typeof error.error?.error === 'string'
        ? error.error.error : 'No se pudo consultar la cobranza de esta vuelta.';
    } finally {
      if (this.generation === generation && !this.destroy.destroyed) { this.loading = false; this.detector.detectChanges(); }
    }
  }

  protected close(): void { this.generation++; this.loading = false; this.dialog.nativeElement.close(); }
}
