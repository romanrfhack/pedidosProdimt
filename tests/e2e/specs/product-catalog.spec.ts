import { expect, test, type Page } from '@playwright/test';

const names = ['#15', '#11 Vapor', '#12 1/2', 'BURRRERA', '#11', '#10.5 Sancochado',
  'Tortilla Harina Natural', '#13 17gr. Grueso', '#11 1/2', '#11 Sancochado', '#14',
  'Flauta', '#12', 'Tortilla Harina Taquera', '#13', '#15 25 gr. Grueso', 'Tortilla Harina Mantequilla'];
const rows = () => names.map((name, i) => ({ id: `synthetic-product-${i}`, name,
  description: i === 7 ? 'Preparación especial de prueba' : null,
  unitCode: name === 'BURRRERA' ? 'package' : 'kg', isActive: true, mergedIntoProductId: null as string | null }));
test.beforeEach(async ({ request }) => { await request.post('http://127.0.0.1:5088/__test/reset'); });

async function open(page: Page, products = rows()) {
  const writes: any[] = [];
  await page.route('**/api/admin/products', async route => {
    if (route.request().method() === 'GET') { await route.fulfill({ json: products }); return; }
    const body = route.request().postDataJSON(); writes.push(body);
    const value = { id: 'synthetic-new', ...body, isActive: true, mergedIntoProductId: null };
    products.push(value); await route.fulfill({ json: value });
  });
  await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto('/admin/catalogos'); await page.getByRole('button', { name: 'Productos', exact: true }).click();
  await expect(page.getByTestId('product-card')).toHaveCount(products.filter(x => !x.mergedIntoProductId).length);
  return { writes, products };
}

test('agrupa los 17 productos por preparación explícita y ordena moldes numéricamente sin cambiar datos', async ({ page }) => {
  const f = await open(page);
  const groups: Record<string, number> = { numbered: 9, flour: 3, parboiled: 2, steamed: 1, other: 2 };
  for (const [category, count] of Object.entries(groups)) {
    await expect(page.locator(`section.product-group[data-category="${category}"]`).getByTestId('product-card')).toHaveCount(count);
  }
  await expect(page.locator('[data-category="numbered"] h5')).toHaveText([
    '#11', '#11 1/2', '#12', '#12 1/2', '#13', '#13 17gr. Grueso', '#14', '#15', '#15 25 gr. Grueso'
  ]);
  await expect(page.getByTestId('product-card').filter({ hasText: 'BURRRERA' })).toContainText('Unidad: paquetes');
  await expect(page.getByTestId('product-card').filter({ hasText: '#13 17gr.' })).toContainText('Preparación especial de prueba');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(f.products.map(x => x.name)).toEqual(names); expect(f.writes).toEqual([]);
});

test('busca sin acentos por nombre descripción tipo y unidad, combina filtros y recupera todos', async ({ page }) => {
  const f = await open(page); const search = page.getByRole('searchbox', { name: 'Buscar producto' });
  await search.fill('preparacion especial'); await expect(page.getByTestId('product-card')).toHaveCount(1);
  await search.fill('paquetes'); await expect(page.getByTestId('product-card')).toHaveCount(1);
  await expect(page.getByTestId('product-card')).toContainText('BURRRERA');
  await page.getByRole('button', { name: 'Limpiar búsqueda de productos' }).click();
  await page.getByRole('button', { name: 'Harina', exact: true }).click();
  await expect(page.getByTestId('product-card')).toHaveCount(3);
  await search.fill('natural'); await expect(page.getByTestId('product-card')).toHaveCount(1);
  await search.fill('sin-coincidencias'); await expect(page.getByText('No encontramos productos con esos filtros')).toBeVisible();
  await page.getByRole('button', { name: 'Quitar filtros' }).click();
  await expect(page.getByTestId('product-card')).toHaveCount(17); expect(f.writes).toEqual([]);
});

for (const [width, height, minColumns] of [[320, 740, 1], [390, 844, 1], [768, 1024, 2], [1366, 768, 4], [1920, 1080, 6]]) {
  test(`tarjetas usan el ancho disponible a ${width}px sin desbordamiento`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height }); await open(page);
    const grid = page.locator('section.product-group[data-category="numbered"] .product-grid');
    const metrics = await grid.evaluate(el => ({ columns: getComputedStyle(el).gridTemplateColumns.split(' ').length,
      width: el.getBoundingClientRect().width, overflow: document.documentElement.scrollWidth > innerWidth }));
    expect(metrics.columns).toBeGreaterThanOrEqual(minColumns); expect(metrics.overflow).toBe(false);
    if (width < 600) expect(metrics.columns).toBe(1);
    if (width >= 1366) expect(metrics.width).toBeGreaterThan(width * .9);
    if (width === 1366 || width === 390) await page.screenshot({ path: testInfo.outputPath(`catalog-${width}.png`), fullPage: true });
    await page.getByRole('button', { name: 'Nuevo producto', exact: true }).click();
    const dialog = page.getByRole('dialog'); await expect(dialog).toBeVisible();
    const bounds = await dialog.boundingBox(); expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width); expect(bounds!.height).toBeLessThanOrEqual(height);
    await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Nuevo producto', exact: true })).toBeFocused();
  });
}

test('modal edita el producto por su id, conserva unidad y muestra errores sin perder borrador', async ({ page }) => {
  const f = await open(page); let attempt = 0;
  const product = f.products.find(x => x.name === 'BURRRERA')!;
  await page.route(`**/api/admin/products/${product.id}`, async route => {
    const body = route.request().postDataJSON(); f.writes.push(body); attempt++;
    if (attempt === 1) { await route.fulfill({ status: 400, json: { error: 'Descripción de prueba rechazada' } }); return; }
    Object.assign(product, body); await route.fulfill({ json: product });
  });
  await page.getByTestId('product-card').filter({ hasText: 'BURRRERA' }).getByRole('button', { name: 'Editar', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Nombre', { exact: true })).toHaveValue('BURRRERA');
  await expect(dialog.getByLabel('Unidad de venta', { exact: true })).toHaveValue('package');
  await dialog.getByLabel('Descripción', { exact: true }).fill('Presentación de prueba');
  await dialog.getByRole('button', { name: 'Guardar producto', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('Descripción de prueba rechazada');
  await expect(dialog.getByLabel('Descripción', { exact: true })).toHaveValue('Presentación de prueba');
  await dialog.getByRole('button', { name: 'Guardar producto', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(f.writes).toEqual(Array(2).fill({ name: 'BURRRERA', description: 'Presentación de prueba', unitCode: 'package' }));
  await expect(page.getByTestId('product-card').filter({ hasText: 'BURRRERA' })).toContainText('Presentación de prueba');
});

test('estado distingue inactivos y archivados, sin permitir editar o reactivar los consolidados', async ({ page }) => {
  const products = rows(); products[0].isActive = false;
  products.push({ ...products[4], id: 'synthetic-archive', name: '#11 (Referencia de prueba)', isActive: false, mergedIntoProductId: products[4].id });
  await open(page, products);
  await expect(page.getByTestId('archived-product-card')).toHaveCount(0);
  await page.getByLabel('Estado', { exact: true }).selectOption('inactive');
  await expect(page.getByTestId('product-card')).toHaveCount(1);
  await expect(page.getByTestId('product-card')).toContainText('Inactivo');
  await page.getByRole('button', { name: 'Mostrar productos archivados por consolidación', exact: true }).click();
  const archive = page.getByTestId('archived-product-card'); await expect(archive).toHaveCount(1);
  await expect(archive).toContainText('Unificado en #11'); await expect(archive.getByRole('button')).toHaveCount(0);
  await page.getByLabel('Estado', { exact: true }).selectOption('active');
  await expect(page.getByTestId('product-card')).toHaveCount(16); await expect(archive).toHaveCount(0);
});

test('enlace desde configuración de cliente abre edición y conserva la unidad pendiente', async ({ page }) => {
  const f = await open(page); f.products[4].unitCode = null as any;
  await page.goto(`/admin/catalogos?productId=${f.products[4].id}`);
  const dialog = page.getByRole('dialog'); await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Nombre', { exact: true })).toHaveValue('#11');
  await expect(dialog.getByLabel('Unidad de venta', { exact: true })).toHaveValue('0: null');
});

test('un guardado pendiente bloquea cierre y envíos repetidos y crea una sola vez', async ({ page }) => {
  await open(page); let writes = 0;
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/admin/products', async route => {
    if (route.request().method() === 'GET') { await route.fulfill({ json: rows() }); return; }
    writes++; await pending;
    await route.fulfill({ json: { id: 'new-synthetic', ...route.request().postDataJSON(), isActive: true } });
  });
  await page.getByRole('button', { name: 'Nuevo producto', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Unidad de venta', { exact: true })).toHaveValue('0: null');
  await dialog.getByLabel('Nombre', { exact: true }).fill('Producto nuevo de prueba');
  await dialog.getByRole('button', { name: 'Crear producto', exact: true }).click();
  await expect.poll(() => writes).toBe(1);
  await expect(dialog.getByRole('button', { name: 'Guardando…', exact: true })).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Cancelar', exact: true })).toBeDisabled();
  await page.keyboard.press('Escape'); await expect(dialog).toBeVisible();
  release(); await expect(dialog).toHaveCount(0); expect(writes).toBe(1);
});

test('activar y desactivar conserva el id del producto y actualiza filtro y contador', async ({ page }) => {
  const f = await open(page); const value = f.products[4]; const methods: string[] = [];
  await page.route(`**/api/admin/products/${value.id}/*`, async route => {
    methods.push(route.request().method());
    value.isActive = route.request().url().endsWith('/activate'); await route.fulfill({ json: value });
  });
  const card = page.getByTestId('product-card').filter({ has: page.getByRole('heading', { name: '#11', exact: true }) });
  await card.getByRole('button', { name: 'Desactivar', exact: true }).click();
  await expect(card).toContainText('Inactivo');
  await page.getByLabel('Estado', { exact: true }).selectOption('inactive'); await expect(page.getByTestId('product-card')).toHaveCount(1);
  await card.getByRole('button', { name: 'Activar', exact: true }).click();
  await expect(page.getByTestId('product-card')).toHaveCount(0);
  await page.getByRole('button', { name: 'Quitar filtros' }).click(); await expect(page.getByTestId('product-card')).toHaveCount(17);
  await expect(card).toContainText('Activo'); expect(methods).toEqual(['PATCH', 'PATCH']);
});
