import { expect, test, type Page, type APIRequestContext } from '@playwright/test';

const api='http://127.0.0.1:5088';
const customer='11111111-1111-1111-1111-111111111112';
const ids=['22222222-2222-2222-2222-222222222201','22222222-2222-2222-2222-222222222202','22222222-2222-2222-2222-222222222203'];
async function profile(request:APIRequestContext, approved=true, multiple=false) {
  await request.put(`${api}/api/admin/customers/${customer}/frequent-products`,{headers:{Authorization:'Bearer mock-admin-jwt'},data:{
    approveForInterpretation:approved,expectedProfileVersion:0,
    items:(multiple?ids:[ids[2]]).map((productId,i)=>({productId,defaultQuantity:null,sortOrder:i+1,isActive:true,preferredMachineId:null,
      interpretationPosition:i+1,captureAlias:multiple&&i===2?'HN':null}))}});
}
async function open(page:Page) {
  await page.goto('/admin/login');await page.getByRole('button',{name:'Entrar',exact:true}).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto('/admin/clientes-pendientes');await page.getByRole('button',{name:'Capturar pedido'}).first().click();
  await expect(page.getByTestId('order-capture')).toBeVisible();
}
async function enter(page:Page,text:string) { await page.getByLabel('Pedido del cliente',{exact:true}).fill(text);await page.getByLabel('Pedido del cliente',{exact:true}).press('Tab'); }
test.beforeEach(async({request})=>{await request.post(api+'/__test/reset');});

test('captura rápida desglosa al perder foco, confirma perfil una vez y mantiene horario informativo',async({page,request})=>{
  await open(page);
  await expect(page.locator('input[type=time]')).toHaveCount(0);
  await expect(page.getByText('13:30',{exact:true})).toBeVisible();
  await expect(page.getByRole('spinbutton')).toHaveCount(0);
  await enter(page,'16');
  await expect(page.getByTestId('capture-breakdown')).toContainText('16 kg');
  await expect(page.getByTestId('capture-breakdown')).toContainText('#11');
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeDisabled();
  expect((await (await request.get(api+'/__test/state')).json()).adminSubmitCalls).toBe(0);
  await page.screenshot({path:test.info().outputPath('quick-capture-desktop.png'),fullPage:true});
  await page.getByRole('button',{name:'Confirmar orden y productos',exact:true}).click();
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeEnabled();
  const saved=page.waitForRequest(r=>r.url().endsWith('/orders/capture')&&r.method()==='POST');
  await page.getByRole('button',{name:'Guardar pedido',exact:true}).click();
  expect((await saved).postDataJSON()).toMatchObject({text:'16',expectedProfileVersion:1});
  await expect(page.getByText('Pedido capturado por administracion.')).toBeVisible();
  expect((await (await request.get(api+'/__test/state')).json()).adminSubmitCalls).toBe(1);
});

test('13+7 y 1+5+2HN muestran productos y unidades explícitos sin guardar al pulsar Enter',async({page,request})=>{
  await profile(request,true,true);await open(page);
  await enter(page,'13+7');await expect(page.getByTestId('capture-breakdown').locator('p')).toHaveCount(2);
  await expect(page.getByTestId('capture-breakdown')).toContainText('13 kg de #9 1/2');
  await expect(page.getByTestId('capture-breakdown')).toContainText('7 kg de #10 1/2');
  await page.getByLabel('Pedido del cliente',{exact:true}).fill('1+5+2HN');
  await expect(page.getByTestId('capture-breakdown')).toHaveCount(0);
  await page.getByLabel('Pedido del cliente',{exact:true}).press('Enter');
  await expect(page.getByTestId('capture-breakdown').locator('p')).toHaveCount(3);
  await expect(page.getByTestId('capture-breakdown')).toContainText('2 kg de #11');
  expect((await (await request.get(api+'/__test/state')).json()).adminSubmitCalls).toBe(0);
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeEnabled();
});

test('abreviatura desconocida retira desglose y permite continuar con captura manual explícita',async({page,request})=>{
  await profile(request,true,true);await open(page);
  await enter(page,'13+7');await expect(page.getByTestId('capture-breakdown')).toBeVisible();
  await enter(page,'2DESCONOCIDA');await expect(page.getByRole('alert')).toContainText('abreviatura DESCONOCIDA');
  await expect(page.getByTestId('capture-breakdown')).toHaveCount(0);
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'Captura manual',exact:true}).click();
  await expect(page.getByText('Revisa las cantidades manuales.',{exact:false})).toBeVisible();
  const quantities=page.getByRole('spinbutton');await expect(quantities.nth(0)).toHaveValue('13');
  await quantities.nth(0).fill('4');await quantities.nth(1).fill('0');
  const saved=page.waitForRequest(r=>r.url().endsWith('/orders/capture')&&r.method()==='POST');
  await page.getByRole('button',{name:'Guardar pedido',exact:true}).click();
  const body=(await saved).postDataJSON();expect(body.text).toBeUndefined();
  expect(body.lines).toEqual([{productId:ids[0],quantity:4,expectedUnitCode:'kg',notes:null}]);
});

test('una respuesta vieja de interpretación no sustituye la entrada más reciente',async({page,request})=>{
  await profile(request);await open(page);
  let release!:()=>void;let captured!:()=>void;
  const pending=new Promise<void>(r=>captured=r),wait=new Promise<void>(r=>release=r);
  await page.route('**/orders/preview',async route=>{
    if(route.request().postDataJSON().text==='16') { const response=await route.fetch();captured();await wait;try{await route.fulfill({response});}catch{} }
    else await route.continue();
  });
  await enter(page,'16');await pending;await enter(page,'17');
  await expect(page.getByTestId('capture-breakdown')).toContainText('17 kg');release();
  await expect(page.getByTestId('capture-breakdown')).not.toContainText('16 kg');
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeEnabled();
});

test('respuesta de guardado perdida reintenta la misma referencia y recupera un único pedido',async({page,request})=>{
  await profile(request);await open(page);await enter(page,'16');
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeEnabled();
  const attempts:any[]=[];
  await page.route('**/orders/capture',async route=>{
    attempts.push(route.request().postDataJSON());
    if(attempts.length===1) { await route.fetch();await route.fulfill({status:503,json:{error:'Respuesta de prueba perdida'}}); }
    else await route.continue();
  });
  await page.getByRole('button',{name:'Guardar pedido',exact:true}).click();
  await expect(page.getByTestId('capture-retry')).toBeVisible();
  await expect(page.getByLabel('Pedido del cliente',{exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'Cancelar',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'Captura manual',exact:true})).toBeDisabled();
  await page.getByTestId('capture-retry').click();
  await expect(page.getByText('Pedido capturado por administracion.')).toBeVisible();
  expect(attempts).toHaveLength(2);expect(attempts[0]).toEqual(attempts[1]);
  expect((await (await request.get(api+'/__test/state')).json()).adminSubmitCalls).toBe(1);
});

test('cambio de perfil al guardar exige renovar desglose y conserva el texto',async({page,request})=>{
  await profile(request);await open(page);await enter(page,'16');
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeEnabled();
  await page.route('**/orders/capture',route=>route.fulfill({status:409,json:{error:'El perfil del cliente cambió. Actualiza la configuración.'}}));
  await page.getByRole('button',{name:'Guardar pedido',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('El perfil del cliente cambió');
  await expect(page.getByLabel('Pedido del cliente',{exact:true})).toHaveValue('16');
  await expect(page.getByTestId('capture-breakdown')).toHaveCount(0);
  await expect(page.getByRole('button',{name:'Guardar pedido',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'Actualizar configuración',exact:true}).click();
  await expect(page.getByTestId('capture-breakdown')).toContainText('16 kg');
});

test('móvil mantiene un campo principal y alternativa manual sin desbordar',async({page,request})=>{
  await page.setViewportSize({width:390,height:844});await profile(request,true,true);await open(page);
  await enter(page,'1+5+2HN');await expect(page.getByTestId('capture-breakdown').locator('p')).toHaveCount(3);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.screenshot({path:test.info().outputPath('quick-capture-mobile.png'),fullPage:true});
  await page.getByRole('button',{name:'Captura manual',exact:true}).click();
  await expect(page.getByRole('spinbutton')).toHaveCount(3);
  await expect(page.getByRole('spinbutton').nth(2)).toHaveValue('2');
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});

test('configuración guarda abreviatura por producto y rechaza repetidas dentro del cliente',async({page})=>{
  await open(page);await page.goto('/admin/clientes/11111111-1111-1111-1111-111111111111/configuracion');
  const aliases=page.getByLabel('Abreviatura para captura',{exact:true});await expect(aliases).toHaveCount(2);
  await aliases.nth(0).fill('HN');await aliases.nth(1).fill('hn');
  await expect(page.getByTestId('profile-validation')).toContainText('No repitas abreviaturas');
  await expect(page.getByRole('button',{name:'Guardar frecuentes',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'Usar orden visual como posiciones 1, 2, 3...',exact:true}).click();
  await aliases.nth(1).fill('ES');await page.getByRole('button',{name:'Guardar frecuentes',exact:true}).click();
  await expect(page.getByText('Productos frecuentes guardados.',{exact:true})).toBeVisible();await page.reload();
  await expect(page.getByLabel('Abreviatura para captura',{exact:true}).nth(0)).toHaveValue('HN');
  await expect(page.getByLabel('Abreviatura para captura',{exact:true}).nth(1)).toHaveValue('ES');
});
