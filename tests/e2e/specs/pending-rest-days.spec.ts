import { expect, test, type Page } from '@playwright/test';

const api='http://127.0.0.1:5088';
const customer='11111111-1111-1111-1111-111111111112';
// The selected service date must win even where UTC noon is the following
// local calendar day. No "today" status is used for another service date.
test.use({timezoneId:'Pacific/Kiritimati'});
test.beforeEach(async({request})=>{await request.post(api+'/__test/reset');});

async function open(page:Page,date:string) {
  await page.goto('/admin/login');
  await page.getByRole('button',{name:'Entrar',exact:true}).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto('/admin/clientes-pendientes');
  await expect(page.getByRole('button',{name:'Consultar clientes'})).toBeEnabled();
  await page.getByLabel('Fecha de servicio').fill(date);
  await page.getByRole('button',{name:'Consultar clientes'}).click();
  await expect(page.getByRole('button',{name:'Consultar clientes'})).toBeEnabled();
}

test('descanso del lunes no se espera; puede mostrarse y capturarse como excepción sin crear NoOrder',async({page})=>{
  await page.route('**/api/admin/customers',route=>route.fulfill({json:[{id:customer,restDays:[1]}]}));
  const writes:string[]=[];
  page.on('request',r=>{if(r.method()==='POST'&&/\/orders\//.test(r.url()))writes.push(r.url());});
  await open(page,'2026-10-26');
  await expect(page.getByText('Cliente Demo 2',{exact:true})).toHaveCount(0);
  const show=page.getByRole('checkbox',{name:/Mostrar también clientes en descanso/});
  await expect(show).not.toBeChecked();
  await show.check();
  const card=page.locator('article').filter({hasText:'Cliente Demo 2'});
  await expect(card).toContainText('Descanso programado');
  await card.getByRole('button',{name:'Capturar pedido',exact:true}).click();
  await expect(page.getByTestId('order-capture')).toBeVisible();
  await expect(show).toBeDisabled();
  expect(writes).toEqual([]);
});

test('domingo se interpreta con la fecha elegida y un día de trabajo vuelve a mostrarlo sin cambiar datos',async({page})=>{
  await page.route('**/api/admin/customers',route=>route.fulfill({json:[{id:customer,restDays:[7]}]}));
  await open(page,'2026-11-01');
  await expect(page.getByText('Cliente Demo 2',{exact:true})).toHaveCount(0);
  await expect(page.getByTestId('pending-rest-filter')).toBeVisible();
  await page.getByLabel('Fecha de servicio').fill('2026-11-02');
  await page.getByRole('button',{name:'Consultar clientes'}).click();
  await expect(page.getByText('Cliente Demo 2',{exact:true})).toBeVisible();
  await expect(page.getByTestId('pending-rest-filter')).toHaveCount(0);
});

test('si no se puede leer el calendario no inventa expectativas y permite consultar otra vez',async({page})=>{
  let fail=true;
  await page.route('**/api/admin/customers',route=>fail?route.abort('failed'):route.fulfill({json:[{id:customer,restDays:[]}]}));
  await open(page,'2026-10-26');
  await expect(page.getByText('No se pudieron cargar los clientes pendientes.')).toBeVisible();
  await expect(page.getByText('Cliente Demo 2',{exact:true})).toHaveCount(0);
  fail=false;
  await page.getByRole('button',{name:'Consultar clientes'}).click();
  await expect(page.getByText('Cliente Demo 2',{exact:true})).toBeVisible();
  await expect(page.getByText('No se pudieron cargar los clientes pendientes.')).toHaveCount(0);
});
