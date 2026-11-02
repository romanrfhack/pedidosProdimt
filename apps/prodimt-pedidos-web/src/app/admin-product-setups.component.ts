import { ChangeDetectorRef, Component, inject, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ProductSetup, ProductSetupApiService } from './product-setup-api.service';
import { ProductSetupReviewComponent } from './product-setup-review.component';
import { unitLabel } from './operational-labels';

@Component({
  selector: 'app-admin-product-setups', imports: [FormsModule, RouterLink, ProductSetupReviewComponent],
  templateUrl: './admin-product-setups.component.html', styleUrl: './admin-product-setups.component.css'
})
export class AdminProductSetupsComponent implements OnDestroy {
  private readonly api = inject(ProductSetupApiService);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private request: Subscription | null = null;
  protected items: ProductSetup[] = [];
  protected search = '';
  protected loading = false;
  protected error: string | null = null;
  protected selectedId: string | null = this.route.snapshot.queryParamMap.get('requestId');
  protected readonly unitLabel = unitLabel;
  constructor() { this.load(); }
  ngOnDestroy(): void { this.request?.unsubscribe(); }
  protected get visible(): ProductSetup[] {
    const normalize = (v: string) => v.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es-MX');
    const terms = normalize(this.search).split(/\s+/).filter(Boolean);
    return this.items.filter(x => terms.every(term => normalize(`${x.customerName} ${x.productName}`).includes(term)));
  }
  protected load(): void {
    this.request?.unsubscribe(); this.loading = true; this.error = null;
    this.request = this.api.getOpen().subscribe({
      next: rows => { this.items = rows; this.api.pendingCount.set(rows.length); this.loading = false; this.detector.detectChanges(); },
      error: () => { this.error = 'No pudimos consultar los productos por configurar. Intenta de nuevo.'; this.loading = false; this.detector.detectChanges(); }
    });
  }
  protected review(id: string): void {
    this.selectedId = id;
    void this.router.navigate([], { relativeTo: this.route, queryParams: { requestId: id }, replaceUrl: true });
  }
  protected closeReview(): void {
    this.selectedId = null;
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
  }
  protected pendingPrice(item: ProductSetup): boolean { return item.orders.some(x => x.pricePending); }
  protected date(value: string): string { return value.split('-').reverse().join('/'); }
}
