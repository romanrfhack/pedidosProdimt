import { AdminProductCatalogApiResponse } from './admin-catalog-api.service';
import { unitLabel } from './operational-labels';

export type ProductCategory = 'numbered' | 'flour' | 'parboiled' | 'steamed' | 'other';
export type ProductStateFilter = 'all' | 'active' | 'inactive';
export const PRODUCT_CATEGORIES: ReadonlyArray<{ id: ProductCategory; label: string; icon: string }> = [
  { id: 'numbered', label: 'Por número', icon: '#' },
  { id: 'flour', label: 'Harina', icon: 'H' },
  { id: 'parboiled', label: 'Sancochados', icon: 'S' },
  { id: 'steamed', label: 'Vapor', icon: 'V' },
  { id: 'other', label: 'Otros productos', icon: '+' }
];

export function catalogText(value: string): string {
  return value.normalize('NFKD').replace(/\p{M}/gu, '').toLocaleLowerCase('es-MX').trim();
}

// Presentation only. Explicit preparations take precedence over the mold number;
// neither description, customer, unit nor price imply an ingredient/preparation.
export function productCategory(name: string): ProductCategory {
  const text = catalogText(name);
  if (/\bsancochad[oa]s?\b/.test(text)) return 'parboiled';
  if (/\bvapor\b/.test(text)) return 'steamed';
  if (/\bharina\b/.test(text)) return 'flour';
  return /#\s*\d|^\d+(?:[.,]\d+)?(?:\s|$)/.test(text) ? 'numbered' : 'other';
}

const collator = new Intl.Collator('es-MX', { numeric: true, sensitivity: 'base' });
function moldNumber(name: string): number | null {
  const text = catalogText(name).replaceAll('⁄', '/');
  const match = text.match(/#\s*(\d+(?:[.,]\d+)?)(?:\s+(\d+)\s*\/\s*(\d+))?/)
    ?? text.match(/^(\d+(?:[.,]\d+)?)(?:\s+(\d+)\s*\/\s*(\d+))?/);
  if (!match) return null;
  return Number(match[1].replace(',', '.')) + (match[2] && Number(match[3]) > 0 ? Number(match[2]) / Number(match[3]) : 0);
}

export function compareCatalogProducts(a: AdminProductCatalogApiResponse, b: AdminProductCatalogApiResponse): number {
  if (a.isActive !== b.isActive) return a.isActive ? -1 : 1;
  const first = moldNumber(a.name), second = moldNumber(b.name);
  if (first !== null && second !== null && first !== second) return first - second;
  return collator.compare(a.name, b.name) || a.id.localeCompare(b.id);
}

export function productMatches(product: AdminProductCatalogApiResponse, query: string): boolean {
  const category = PRODUCT_CATEGORIES.find(x => x.id === productCategory(product.name))!;
  const units: Record<string, string> = { kg: 'kilo kilos kilogramo kilogramos', piece: 'pieza piezas', dozen: 'docena docenas', package: 'paquete paquetes' };
  const text = catalogText([product.name, product.description ?? '', category.label, unitLabel(product.unitCode), units[product.unitCode ?? ''] ?? ''].join(' '));
  return catalogText(query).split(/\s+/).filter(Boolean).every(term => text.includes(term));
}

export function productStateMatches(product: AdminProductCatalogApiResponse, state: ProductStateFilter): boolean {
  return state === 'all' || (state === 'active' ? product.isActive : !product.isActive);
}
