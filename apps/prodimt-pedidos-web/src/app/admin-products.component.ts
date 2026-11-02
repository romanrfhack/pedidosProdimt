import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminProductCatalogApiResponse } from './admin-catalog-api.service';
import { unitLabel } from './operational-labels';
import { PRODUCT_CATEGORIES, ProductCategory, ProductStateFilter, compareCatalogProducts, productCategory, productMatches, productStateMatches } from './product-catalog-view';

@Component({
  selector: 'app-admin-products',
  imports: [FormsModule],
  templateUrl: './admin-products.component.html',
  styleUrl: './admin-products.component.css'
})
export class AdminProductsComponent {
  @Input({ required: true }) products: AdminProductCatalogApiResponse[] = [];
  @Input() canManage = true;
  @Input() busyIds: ReadonlySet<string> = new Set();
  @Output() createRequested = new EventEmitter<void>();
  @Output() editRequested = new EventEmitter<AdminProductCatalogApiResponse>();
  @Output() toggleRequested = new EventEmitter<AdminProductCatalogApiResponse>();
  protected query = '';
  protected category: ProductCategory | 'all' = 'all';
  protected state: ProductStateFilter = 'all';
  protected showArchived = false;
  protected readonly unitLabel = unitLabel;

  protected get current(): AdminProductCatalogApiResponse[] { return this.products.filter(x => !x.mergedIntoProductId); }
  protected get activeCount(): number { return this.current.filter(x => x.isActive).length; }
  protected get archiveCount(): number { return this.products.filter(x => !!x.mergedIntoProductId).length; }
  protected get categories() {
    return PRODUCT_CATEGORIES.map(group => ({ ...group, count: this.current.filter(x => productCategory(x.name) === group.id).length })).filter(x => x.count > 0);
  }
  protected get groups() {
    return PRODUCT_CATEGORIES.filter(group => this.category === 'all' || this.category === group.id)
      .map(group => ({ ...group, products: this.current.filter(x => productCategory(x.name) === group.id
        && productMatches(x, this.query) && productStateMatches(x, this.state)).sort(compareCatalogProducts) }))
      .filter(group => group.products.length > 0);
  }
  protected get visibleCount(): number { return this.groups.reduce((total, group) => total + group.products.length, 0); }
  protected get archived(): AdminProductCatalogApiResponse[] {
    return this.products.filter(x => !!x.mergedIntoProductId && productMatches(x, this.query)
      && (this.category === 'all' || productCategory(x.name) === this.category)
      && this.state !== 'active').sort(compareCatalogProducts);
  }
  protected resetFilters(): void { this.query = ''; this.category = 'all'; this.state = 'all'; }
  protected sharedName(product: AdminProductCatalogApiResponse): string | null {
    return this.products.find(x => x.id === product.mergedIntoProductId)?.name ?? null;
  }
}
