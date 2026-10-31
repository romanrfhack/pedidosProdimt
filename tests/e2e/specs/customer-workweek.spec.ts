import { expect, test, type Page } from '@playwright/test';

const apiBase='http://127.0.0.1:5088';
const id='11111111-1111-1111-1111-111111111111';
const days=['Lunes','Martes','Miércoles','Jueves','Viernes','Sábado','Domingo'];

async function fixture(page: Page, restDays: number[] = [], productCount = 2) {
  let record={id,name:'Cliente de configuración demo',phoneNumber:'5550000000',isActive:true,
    preferredDeliveryTime:'08:30:00',preferredDeliveryWindowStart:null,preferredDeliveryWindowEnd:null,
    deliveryNotes:'Entrega de prueba',routeCode:'DEMO',routeStopOrder:1,hasStoreKey:null,paymentTermCode:null,
    internalDeliveryInstructions:'Instrucción ficticia',restDays,orderingProfileVersion:0,isOrderProfileApproved:false};
  const profileItems=['22222222-2222-2222-2222-222222222201','22222222-2222-2222-2222-222222222202','22222222-2222-2222-2222-222222222203'].slice(0,productCount).map((productId,index)=>({
    productId,productName:['#9 1/2','#10 1/2','#11'][index],unitCode:'kg',productIsActive:true,defaultQuantity:index?10:20,
    sortOrder:index+1,interpretationPosition:index+1,isActive:true,preferredMachineId:null,captureAlias:null
  }));
  await page.route(`**/api/admin/customers/${id}/ordering-profile`,route=>route.fulfill({json:{
    customerId:id,orderingProfileVersion:1,isOrderProfileApproved:true,items:profileItems
  }}));
  const writes:any[]=[];
  await page.route('**/api/admin/customers',route=>route.fulfill({json:[record]}));
  await page.route('**/api/admin/customers/day-status',route=>route.fulfill({json:{date:'2026-10-31',customers:[
    {customerId:id,status:record.restDays.includes(6)?'ScheduledRest':'AwaitingOrder',orderCount:0,requiresReview:false}
  ]}}));
  await page.route(`**/api/admin/customers/${id}`,async route=>{
    const body=route.request().postDataJSON();writes.push(body);record={...record,...body};
    await route.fulfill({json:record});
  });
  await page.goto('/admin/login');
  await page.getByRole('button',{name:'Entrar'}).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto(`/admin/clientes/${id}/configuracion`);
  const config=page.getByTestId('customer-config');
  await expect(config.getByText('Cargando configuración...')).toHaveCount(0);
  await expect(config.getByLabel('Cantidad sugerida').first()).toBeEnabled();
  return {config,week:page.getByTestId('customer-working-week'),writes,
    changeRecord:(changes:Partial<typeof record>)=>{record={...record,...changes};}};
}

test.beforeEach(async ({request})=>{await request.post(`${apiBase}/__test/reset`);});

test('casillas marcadas son trabajo; guardar descansos conserva datos recientes y el borrador de productos',async({page})=>{
  const {config,week,writes,changeRecord}=await fixture(page,[1]);
  await expect(week.getByRole('checkbox',{name:'Lunes',exact:true})).not.toBeChecked();
  await expect(week.getByRole('checkbox',{name:'Domingo',exact:true})).toBeChecked();
  await config.getByLabel('Cantidad sugerida').first().fill('31');
  const profile=await config.locator('.profile-version').textContent();
  changeRecord({phoneNumber:'5550000123',routeCode:'RUTA-RECIENTE'});
  await week.getByRole('checkbox',{name:'Sábado',exact:true}).uncheck();
  await week.getByRole('button',{name:'Guardar días',exact:true}).click();
  await expect(week.getByRole('status')).toHaveText('Días de trabajo guardados.');
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({restDays:[1,6],phoneNumber:'5550000123',routeCode:'RUTA-RECIENTE',preferredDeliveryTime:'08:30:00'});
  await expect(config.getByLabel('Cantidad sugerida').first()).toHaveValue('31');
  await expect(config.locator('.profile-version')).toHaveText(profile!);
  await expect(page.getByTestId('customer-card')).toHaveAttribute('data-day-state','ScheduledRest');
  await page.reload();
  await expect(week.getByRole('checkbox',{name:'Lunes',exact:true})).not.toBeChecked();
  await expect(week.getByRole('checkbox',{name:'Sábado',exact:true})).not.toBeChecked();
  await expect(week.getByRole('checkbox',{name:'Martes',exact:true})).toBeChecked();
});

test('permite siete días de trabajo o siete descansos sin desactivar ni crear pedidos',async({page})=>{
  const orderWrites:string[]=[];
  page.on('request',request=>{if(request.method()!=='GET'&&/\/orders\//.test(request.url()))orderWrites.push(request.url());});
  const {week,writes}=await fixture(page);
  for(const day of days){await expect(week.getByRole('checkbox',{name:day,exact:true})).toBeChecked();await week.getByRole('checkbox',{name:day,exact:true}).uncheck();}
  await week.getByRole('button',{name:'Guardar días',exact:true}).click();
  await expect(week.getByRole('status')).toBeVisible();
  expect(writes[0].restDays).toEqual([1,2,3,4,5,6,7]);
  for(const day of days)await week.getByRole('checkbox',{name:day,exact:true}).check();
  await week.getByRole('button',{name:'Guardar días',exact:true}).click();
  await expect.poll(()=>writes.length).toBe(2);
  expect(writes[1].restDays).toEqual([]);
  expect(writes[1]).not.toHaveProperty('isActive');
  expect(orderWrites).toEqual([]);
});

test('un calendario cambiado en otra pantalla requiere recarga y conserva las cantidades sin guardar',async({page})=>{
  const {config,week,writes,changeRecord}=await fixture(page);
  await config.getByLabel('Cantidad sugerida').first().fill('29');
  await week.getByRole('checkbox',{name:'Domingo',exact:true}).uncheck();
  changeRecord({restDays:[1]});
  await week.getByRole('button',{name:'Guardar días',exact:true}).click();
  await expect(week.getByRole('alert')).toContainText('calendario cambió');
  expect(writes).toHaveLength(0);
  await expect(week.getByRole('checkbox',{name:'Domingo',exact:true})).not.toBeChecked();
  await week.getByRole('button',{name:'Recargar días'}).click();
  await expect(week.getByRole('checkbox',{name:'Lunes',exact:true})).not.toBeChecked();
  await expect(week.getByRole('checkbox',{name:'Domingo',exact:true})).toBeChecked();
  await expect(config.getByLabel('Cantidad sugerida').first()).toHaveValue('29');
});

test('fallo al guardar días conserva la selección para reintentar',async({page})=>{
  const {week,writes}=await fixture(page);
  await page.route(`**/api/admin/customers/${id}`,route=>route.abort('failed'));
  await week.getByRole('checkbox',{name:'Miércoles',exact:true}).uncheck();
  await week.getByRole('button',{name:'Guardar días',exact:true}).click();
  await expect(week.getByRole('alert')).toContainText('No se pudieron guardar');
  await expect(week.getByRole('checkbox',{name:'Miércoles',exact:true})).not.toBeChecked();
  await expect(week.getByRole('button',{name:'Guardar días',exact:true})).toBeEnabled();
  expect(writes).toHaveLength(0);
});

for(const viewport of [{width:1366,height:768},{width:1920,height:1080},{width:768,height:1024},{width:390,height:844}]) {
  test(`configuración utiliza el ancho y mantiene controles accesibles en ${viewport.width}px`,async({page})=>{
    await page.setViewportSize(viewport);
    await page.route(`**/api/admin/customers/${id}/machine-assignments`,route=>route.fulfill({json:[]}));
    const {config,week}=await fixture(page,[],3);
    await expect(config.getByTestId('profile-product')).toHaveCount(3);
    await expect(week.getByRole('checkbox')).toHaveCount(7);
    await expect(config.getByRole('button',{name:'Guardar frecuentes',exact:true})).toBeVisible();
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
    if(viewport.width>=1200) {
      const main=await page.locator('.app-shell__main').boundingBox();
      expect(main!.width).toBeGreaterThan(viewport.width-20);
      expect(await page.evaluate(()=>document.documentElement.scrollHeight<=window.innerHeight+1)).toBe(true);
    }
    await page.screenshot({path:test.info().outputPath(`configuration-${viewport.width}.png`),fullPage:true});
  });
}
