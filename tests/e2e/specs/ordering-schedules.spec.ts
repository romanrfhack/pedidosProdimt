import { expect, test, type Page } from '@playwright/test';
const customerId='11111111-1111-1111-1111-111111111111';
const productId='22222222-2222-2222-2222-222222222201';
const headers={'access-control-allow-origin':'*','access-control-allow-headers':'authorization,content-type','access-control-allow-methods':'GET,POST,PUT,OPTIONS'};
async function portal(page: Page, future=false, invalid=false) {
 const next:any={customerId,customerName:'Cliente de prueba',deliveryDate:future?'2026-11-04':'2026-11-02',serverNow:'2026-11-02T11:00:00-06:00',profileVersion:1,isProfileApproved:false,hasActiveOrder:false,
 products:[{productId,productName:'Tortillas #11',unitCode:'kg',interpretationPosition:1,captureAlias:null,sortOrder:1}],currentOrder:null,
 preferredDeliveryTime:null,preferredDeliveryWindowStart:null,preferredDeliveryWindowEnd:null,
 orderingWindow:{automaticConfirmationCutoff:'09:00:00',cutoffSource:'customer',requiresConfirmation:!future,requiresDateAcceptance:future,deliveryDate:future?'2026-11-04':'2026-11-02',today:'2026-11-02',requestOnly:!future}};
 const captures:any[]=[];
 await page.route(/\/api\/auth\/customer-token$/, async route=>{
  if(route.request().method()==='OPTIONS')return route.fulfill({status:204,headers});
  await route.fulfill({status:invalid?401:200,headers,json:invalid?{error:'Token de cliente invalido.'}:{accessToken:'customer-schedule-demo',tokenType:'Bearer',expiresAt:'2099-01-01T00:00:00Z',customerId,customerName:next.customerName}});
 });
 await page.route(new RegExp('/api/customer-orders/'+customerId+'/'), async route=>{
  const request=route.request();if(request.method()==='OPTIONS')return route.fulfill({status:204,headers});
  if(request.method()==='GET')return route.fulfill({headers,json:next});
  const body=request.postDataJSON();captures.push(body);
  next.hasActiveOrder=true;next.currentOrder={orderId:'order-demo',deliveryDate:body.expectedDeliveryDate,status:future?'Submitted':'PendingAdminReview',submittedAt:next.serverNow,deliveryNotes:null,lines:[{productId,productName:'Tortillas #11',unitCode:'kg',quantity:body.lines[0].quantity,notes:null}]};
  await route.fulfill({headers,json:{replayed:false,order:{orderId:'order-demo',customerId,orderDate:body.expectedDeliveryDate,status:next.currentOrder.status,sequenceNumber:1,submittedAt:next.serverNow,isLate:!future,requiresAdminReview:!future,adminReviewReason:future?null:'LateSubmission'}}});
 });
 await page.goto('/cliente#token=synthetic-schedule-link');return captures;
}
test('enlace identificable recibe solicitud pendiente sin prometer entrega',async({page})=>{
 const captures=await portal(page);await expect(page.getByTestId('late-order-notice')).toContainText('09:00');
 await expect(page.getByRole('button',{name:'No pedir para esta entrega'})).toHaveCount(0);
 await page.getByLabel('Cantidad de Tortillas #11').fill('25');await page.getByRole('button',{name:'Enviar solicitud de pedido'}).click();
 await expect(page.getByTestId('customer-current-order')).toContainText('Pendiente de confirmación');await expect(page.getByTestId('customer-current-order')).toContainText('aún no está confirmada');
 await expect(page.getByText(/Recibimos tu solicitud para/)).toBeVisible();expect(captures).toHaveLength(1);
});
test('lunes a las 18 ofrece miércoles y requiere aceptar fecha',async({page})=>{
 const captures=await portal(page,true);await expect(page.getByTestId('next-business-day-offer')).toContainText('miércoles');await expect(page.getByTestId('customer-capture')).toHaveCount(0);
 await page.getByRole('button',{name:'Sí, pedir para esta fecha'}).click();await page.getByLabel('Cantidad de Tortillas #11').fill('12');await page.getByRole('button',{name:'Confirmar pedido',exact:true}).click();
 await expect.poll(()=>captures.length).toBe(1);expect(captures[0].expectedDeliveryDate).toBe('2026-11-04');
});
test('enlace no identificable muestra tarjeta amigable móvil',async({page})=>{
 await page.setViewportSize({width:375,height:812});await portal(page,false,true);
 await expect(page.getByRole('heading',{name:'Este enlace ya no está disponible'})).toBeVisible();await expect(page.getByLabel('Token de acceso')).toHaveCount(0);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);await page.getByRole('button',{name:'Usar otro enlace'}).click();await expect(page.getByLabel('Token de acceso')).toBeVisible();
});
test('borrar horario cliente restaura herencia ruta',async({page})=>{
 const updates:any[]=[];const schedules={routes:[{routeCode:'R1',cutoff:'10:00:00',version:1}],customers:[{customerId,customerName:'Cliente volumen',routeCode:'R1',cutoff:'09:00:00',version:1}]};
 await page.route(/\/api\/admin\/ordering-schedules/,async route=>{
  if(route.request().method()==='OPTIONS')return route.fulfill({status:204,headers});if(route.request().method()==='GET')return route.fulfill({headers,json:schedules});
  const body=route.request().postDataJSON();updates.push(body);schedules.customers[0].cutoff=body.cutoff;schedules.customers[0].version++;await route.fulfill({status:204,headers});
 });
 await page.goto('/admin/login');await page.getByRole('button',{name:'Entrar'}).click();await expect(page.getByRole('heading',{name:'Pedidos de hoy'})).toBeVisible();
 await page.getByRole('link',{name:'Horarios',exact:true}).click();await expect(page.getByRole('heading',{name:'Horarios de pedidos'})).toBeVisible();await page.getByLabel('Hora particular del cliente').fill('');await page.getByRole('button',{name:'Guardar cliente'}).click();
 await expect(page.getByRole('status')).toContainText('Horario guardado');expect(updates[0]).toEqual({cutoff:null,expectedVersion:1});await expect(page.getByText(/Sin horario particular: 10:00/)).toBeVisible();
});
