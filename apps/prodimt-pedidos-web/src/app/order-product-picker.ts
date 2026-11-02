import { Subscription } from 'rxjs';
import { AdditionalProduct, ProductSetupApiService } from './product-setup-api.service';

// Per-capture state; never writes the catalog/profile when a draft is opened.
export class OrderProductPicker {
  isOpen = false;
  loading = false;
  error: string | null = null;
  products: AdditionalProduct[] = [];
  private request: Subscription | null = null;
  private customerId = '';
  private admin = false;
  private excluded = new Set<string>();
  constructor(private readonly api: ProductSetupApiService, private readonly changed: () => void) {}
  show(customerId: string, admin: boolean, excludedIds: string[]): void {
    this.customerId = customerId; this.admin = admin; this.excluded = new Set(excludedIds);
    this.products = []; this.isOpen = true; this.reload();
  }
  reload(): void {
    this.request?.unsubscribe(); this.loading = true; this.error = null;
    this.request = this.api.getAdditional(this.customerId, this.admin).subscribe({
      next: rows => { this.products = rows.filter(x => !this.excluded.has(x.productId)); this.loading = false; this.changed(); },
      error: () => { this.loading = false; this.error = 'No pudimos consultar los productos. Intenta de nuevo.'; this.changed(); }
    });
  }
  close(): void { this.request?.unsubscribe(); this.request = null; this.isOpen = false; this.loading = false; }
}
