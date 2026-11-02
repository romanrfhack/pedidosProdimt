import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../environments/environment';
interface RouteSchedule { routeCode: string; cutoff: string | null; version: number; }
interface CustomerSchedule { customerId: string; customerName: string; routeCode: string | null; cutoff: string | null; version: number; }
interface Schedules { routes: RouteSchedule[]; customers: CustomerSchedule[]; }
@Component({
 selector: 'app-ordering-schedules', imports: [FormsModule],
 template: `
 <section class="page stack schedules-page" data-testid="ordering-schedules">
 <div class="page-title"><p class="eyebrow">Configuración</p><h2>Horarios de pedidos</h2></div>
 <p>La hora del cliente tiene prioridad. Si se deja vacía, se aplica la de su ruta; si tampoco está configurada, se usan las 10:00 a. m.</p>
 <p class="notice">Recibimos solicitudes para el mismo día antes de las 18:00. Después se ofrece el siguiente día hábil. PRODIMT descansa los martes. Hora de Ciudad de México.</p>
 @if (error) { <p class="alert error" role="alert">{{ error }}</p> }
 @if (notice) { <p class="alert success" role="status">{{ notice }}</p> }
 @if (loading) { <p role="status">Consultando horarios…</p> }
 @if (data; as d) {
 <section class="stack"><h3>Por ruta</h3><div class="schedule-grid">
 @for (route of d.routes; track route.routeCode) {
 <form class="schedule-card" (ngSubmit)="saveRoute(route)">
 <strong>Ruta {{ route.routeCode }}</strong><label><span>Hora límite de confirmación automática</span>
 <input type="time" step="60" max="17:59" name="cutoff" [(ngModel)]="route.cutoff" [disabled]="saving"></label>
 <small>Vacío: usar las 10:00 a. m.</small><button class="primary" [disabled]="saving">Guardar ruta</button>
 </form> }</div></section>
 <section class="stack"><h3>Excepciones por cliente</h3>
 <label><span>Buscar cliente o ruta</span><input name="search" type="search" [(ngModel)]="search" placeholder="Nombre o ruta"></label>
 <div class="schedule-grid">@for (customer of visibleCustomers; track customer.customerId) {
 <form class="schedule-card" (ngSubmit)="saveCustomer(customer)">
 <strong>{{ customer.customerName }}</strong><small>Ruta {{ customer.routeCode || 'sin asignar' }}</small>
 <label><span>Hora particular del cliente</span><input type="time" step="60" max="17:59" name="cutoff" [(ngModel)]="customer.cutoff" [disabled]="saving"></label>
 <small>Sin horario particular: {{ inherited(customer) }}. Borrar la hora restaura esta herencia.</small>
 <button class="primary" [disabled]="saving">Guardar cliente</button>
 </form> }</div></section> }
 </section>`,
 styles: [`.schedules-page{max-width:1100px}.schedule-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,280px),1fr));gap:16px}.schedule-card{display:grid;gap:12px;border:1px solid #dce4e5;border-radius:12px;padding:18px}.schedule-card small{color:#52616a}.schedule-card input{width:100%;min-height:44px}`]
})
export class AdminOrderingSchedulesComponent {
 private readonly http = inject(HttpClient); private readonly cdr = inject(ChangeDetectorRef);
 private readonly url = environment.apiBaseUrl + '/api/admin/ordering-schedules';
 protected data: Schedules | null = null; protected search = ''; protected error: string | null = null;
 protected notice: string | null = null; protected loading = true; protected saving = false;
 constructor() { this.load(); }
 protected get visibleCustomers(): CustomerSchedule[] { const q = this.search.trim().toLocaleLowerCase('es'); return this.data?.customers.filter(x => !q || (x.customerName+' '+x.routeCode).toLocaleLowerCase('es').includes(q)) ?? []; }
 protected inherited(c: CustomerSchedule): string { return this.data?.routes.find(x => x.routeCode.toUpperCase() === c.routeCode?.trim().toUpperCase())?.cutoff?.slice(0,5) ?? '10:00'; }
 protected saveRoute(route: RouteSchedule): void { this.save('/routes', { routeCode: route.routeCode, cutoff: this.time(route.cutoff), expectedVersion: route.version }); }
 protected saveCustomer(c: CustomerSchedule): void { this.save('/customers/'+c.customerId, { cutoff: this.time(c.cutoff), expectedVersion: c.version }); }
 private time(value: string | null): string | null { return !value ? null : value.length === 5 ? value + ':00' : value; }
 private save(path: string, body: unknown): void {
   if (this.saving) return; this.saving = true; this.error = null; this.notice = null;
   this.http.put(this.url+path, body).subscribe({ next: () => { this.notice='Horario guardado.'; this.saving=false; this.load(); }, error: e => { this.error=this.message(e); this.saving=false; this.cdr.detectChanges(); } });
 }
 private load(): void { this.loading=true; this.http.get<Schedules>(this.url).subscribe({ next:d=>{ this.data=d; this.loading=false; this.cdr.detectChanges(); },error:e=>{this.error=this.message(e);this.loading=false;this.cdr.detectChanges();} }); }
 private message(e: unknown): string { return e instanceof HttpErrorResponse ? e.error?.error || 'No pudimos consultar o guardar el horario.' : 'No pudimos guardar el horario.'; }
}
