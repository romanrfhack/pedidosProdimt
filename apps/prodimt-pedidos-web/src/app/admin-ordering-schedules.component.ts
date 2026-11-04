import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, inject } from '@angular/core';
import { Subject, Subscription, takeUntil } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { environment } from '../environments/environment';
interface RouteSchedule { routeCode: string; cutoff: string | null; version: number; }
interface CustomerSchedule { customerId: string; customerName: string; routeCode: string | null; cutoff: string | null; version: number; }
interface Schedules { routes: RouteSchedule[]; customers: CustomerSchedule[]; }
type Schedule = RouteSchedule | CustomerSchedule;
interface SavedSchedule { key: string; cutoff: string | null; expectedVersion: number; }
@Component({
 selector: 'app-ordering-schedules', imports: [FormsModule],
 template: `
 <section class="page stack schedules-page" data-testid="ordering-schedules">
 <div class="page-title"><p class="eyebrow">Configuración</p><h2>Horarios de pedidos</h2></div>
 <button type="button" class="secondary" (click)="refresh()" [disabled]="loading || saving">Actualizar horarios</button>
 <p>La hora del cliente tiene prioridad. Si se deja vacía, se aplica la de su ruta; si tampoco está configurada, se usan las 10:00 a. m.</p>
 <p class="notice">Recibimos solicitudes para el mismo día antes de las 18:00. Después se ofrece el siguiente día hábil. PRODIMT descansa los martes. Hora de Ciudad de México.</p>
 @if (error) { <p class="alert error" role="alert">{{ error }}</p> }
 @if (notice) { <p class="alert success" role="status">{{ notice }}</p> }
 @if (loading) { <p role="status">Consultando horarios…</p> }
 @if (data; as d) {
 <section class="stack"><h3>Por ruta</h3><div class="schedule-grid">
 @for (route of d.routes; track route.routeCode) {
 <form class="schedule-card" [attr.data-testid]="'schedule-route-'+route.routeCode" (ngSubmit)="saveRoute(route)">
 <strong>Ruta {{ route.routeCode }}</strong><label><span>Hora límite de confirmación automática</span>
 <input type="time" step="60" max="17:59" name="cutoff" [(ngModel)]="route.cutoff" [disabled]="saving || loading"></label>
 <small>Vacío: usar las 10:00 a. m.</small>
 @if (changed(route)) { <small>Cambios sin guardar</small> }
 @if (hasConflict(route)) {
 <p class="notice" role="status">{{ conflictMessage(route) }}</p>
 <button type="button" class="secondary" (click)="useCurrent(route)" [disabled]="saving || loading">{{ available(route) ? 'Usar horario actual' : 'Descartar borrador' }}</button>
 @if (available(route)) { <button type="button" class="secondary" (click)="keepDraft(route)" [disabled]="saving || loading">Conservar mi cambio sobre la versión actual</button> }
 }
 <button class="primary" [disabled]="saving || loading || needsRefresh || hasConflict(route)">Guardar ruta</button>
 </form> }</div></section>
 <section class="stack"><h3>Excepciones por cliente</h3>
 <label><span>Buscar cliente o ruta</span><input name="search" type="search" [(ngModel)]="search" placeholder="Nombre o ruta"></label>
 <div class="schedule-grid">@for (customer of visibleCustomers; track customer.customerId) {
 <form class="schedule-card" [attr.data-testid]="'schedule-customer-'+customer.customerId" (ngSubmit)="saveCustomer(customer)">
 <strong>{{ customer.customerName }}</strong><small>Ruta {{ customer.routeCode || 'sin asignar' }}</small>
 <label><span>Hora particular del cliente</span><input type="time" step="60" max="17:59" name="cutoff" [(ngModel)]="customer.cutoff" [disabled]="saving || loading"></label>
 <small>Sin horario particular: {{ inherited(customer) }}. Borrar la hora restaura esta herencia.</small>
 @if (changed(customer)) { <small>Cambios sin guardar</small> }
 @if (hasConflict(customer)) {
 <p class="notice" role="status">{{ conflictMessage(customer) }}</p>
 <button type="button" class="secondary" (click)="useCurrent(customer)" [disabled]="saving || loading">{{ available(customer) ? 'Usar horario actual' : 'Descartar borrador' }}</button>
 @if (available(customer)) { <button type="button" class="secondary" (click)="keepDraft(customer)" [disabled]="saving || loading">Conservar mi cambio sobre la versión actual</button> }
 }
 <button class="primary" [disabled]="saving || loading || needsRefresh || hasConflict(customer)">Guardar cliente</button>
 </form> }</div></section> }
 </section>`,
 styles: [`.schedules-page{max-width:1100px}.schedule-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,280px),1fr));gap:16px}.schedule-card{display:grid;gap:12px;border:1px solid #dce4e5;border-radius:12px;padding:18px}.schedule-card small{color:#52616a}.schedule-card input{width:100%;min-height:44px}`]
})
export class AdminOrderingSchedulesComponent implements OnDestroy {
 private readonly http = inject(HttpClient);
 private readonly cdr = inject(ChangeDetectorRef);
 private readonly url = environment.apiBaseUrl + '/api/admin/ordering-schedules';
 private readonly destroyed = new Subject<void>();
 private readonly snapshots = new Map<string, Schedule>();
 private readonly conflicts = new Set<string>();
 private readSubscription?: Subscription;
 private readGeneration = 0;
 protected data: Schedules | null = null;
 protected search = '';
 protected error: string | null = null;
 protected notice: string | null = null;
 protected loading = true;
 protected saving = false;
 protected needsRefresh = false;

 constructor() { this.load(); }
 ngOnDestroy(): void {
   this.readGeneration++;
   this.destroyed.next();
   this.destroyed.complete();
 }
 protected get visibleCustomers(): CustomerSchedule[] {
   const q = this.search.trim().toLocaleLowerCase('es');
   return this.data?.customers.filter(x => !q || (x.customerName+' '+x.routeCode).toLocaleLowerCase('es').includes(q)) ?? [];
 }
 protected inherited(c: CustomerSchedule): string {
   // A route draft is not the effective inheritance until it has been saved.
   const route = this.snapshots.get('route:' + (c.routeCode?.trim().toUpperCase() ?? ''));
   return route?.cutoff?.slice(0, 5) ?? '10:00';
 }
 protected refresh(): void {
   if (this.saving || this.loading) return;
   this.error = null;
   this.load();
 }
 protected changed(row: Schedule): boolean {
   const current = this.snapshots.get(this.key(row));
   return !current || row.version !== current.version || this.time(row.cutoff) !== this.time(current.cutoff);
 }
 protected hasConflict(row: Schedule): boolean { return this.conflicts.has(this.key(row)); }
 protected available(row: Schedule): boolean { return this.snapshots.has(this.key(row)); }
 protected conflictMessage(row: Schedule): string {
   const current = this.snapshots.get(this.key(row));
   if (!current) return 'Este registro ya no está disponible. Conservamos tu borrador; revisa antes de descartarlo.';
   const time = current.cutoff?.slice(0, 5) ?? ('customerId' in row ? 'heredar de la ruta' : '10:00');
   return 'El horario cambió en otra sesión. Actual: ' + time + '. Tu borrador se conserva. Elige qué versión usar antes de guardar.';
 }
 protected useCurrent(row: Schedule): void {
   if (this.saving || this.loading) return;
   const key = this.key(row);
   const current = this.snapshots.get(key);
   if (current) Object.assign(row, current);
   else if (this.data) {
     this.data.routes = this.data.routes.filter(x => this.key(x) !== key);
     this.data.customers = this.data.customers.filter(x => this.key(x) !== key);
   }
   this.conflicts.delete(key);
 }
 protected keepDraft(row: Schedule): void {
   if (this.saving || this.loading) return;
   const current = this.snapshots.get(this.key(row));
   if (!current) return;
   // The user explicitly chose to save this draft over the version shown above.
   row.version = current.version;
   this.conflicts.delete(this.key(row));
 }
 protected saveRoute(route: RouteSchedule): void {
   this.save(route, '/routes', { routeCode: route.routeCode, cutoff: this.time(route.cutoff), expectedVersion: route.version });
 }
 protected saveCustomer(customer: CustomerSchedule): void {
   this.save(customer, '/customers/'+customer.customerId, { cutoff: this.time(customer.cutoff), expectedVersion: customer.version });
 }
 private key(row: Schedule): string {
   return 'customerId' in row ? 'customer:'+row.customerId : 'route:'+row.routeCode.trim().toUpperCase();
 }
 private time(value: string | null): string | null { return !value ? null : value.length === 5 ? value + ':00' : value; }
 private save(row: Schedule, path: string, body: unknown): void {
   if (this.saving || this.loading || this.needsRefresh || this.hasConflict(row)) return;
   const saved: SavedSchedule = { key: this.key(row), cutoff: this.time(row.cutoff), expectedVersion: row.version };
   this.saving = true;
   this.error = null;
   this.notice = null;
   this.http.put(this.url+path, body).pipe(takeUntil(this.destroyed)).subscribe({
     next: () => {
       this.notice = 'Horario guardado.';
       // Keep saving true until the refreshed versions have arrived.
       this.load(saved);
     },
     error: (e: unknown) => {
       this.error = this.message(e);
       if (e instanceof HttpErrorResponse && e.status === 409) {
         // Refresh server versions, but never overwrite the local draft on conflict.
         this.load();
       } else {
         this.saving = false;
         this.cdr.detectChanges();
       }
     }
   });
 }
 private load(saved?: SavedSchedule): void {
   const generation = ++this.readGeneration;
   this.readSubscription?.unsubscribe();
   this.loading = true;
   this.readSubscription = this.http.get<Schedules>(this.url).pipe(takeUntil(this.destroyed)).subscribe({
     next: data => {
       if (generation !== this.readGeneration) return;
       this.reconcile(data, saved);
       this.loading = false;
       this.saving = false;
       this.needsRefresh = false;
       this.cdr.detectChanges();
     },
     error: (e: unknown) => {
       if (generation !== this.readGeneration) return;
       this.error = this.message(e) + ' Tus cambios se conservan. Actualiza los horarios antes de volver a guardar.';
       this.loading = false;
       this.saving = false;
       this.needsRefresh = true;
       this.cdr.detectChanges();
     }
   });
 }
 private reconcile(incoming: Schedules, saved?: SavedSchedule): void {
   const previous = this.data;
   const merge = <T extends Schedule>(rows: T[], drafts: T[]): T[] => {
     const byKey = new Map(drafts.map(row => [this.key(row), row]));
     const merged = rows.map(row => {
       const key = this.key(row);
       const draft = byKey.get(key);
       byKey.delete(key);
       const isSaved = saved?.key === key;
       if (isSaved && row.version === saved.expectedVersion + 1 && this.time(row.cutoff) === saved.cutoff) {
         this.conflicts.delete(key);
         return { ...row };
       }
       if (draft && (this.changed(draft) || this.hasConflict(draft) || isSaved)) {
         if (row.version !== draft.version || isSaved) this.conflicts.add(key);
         return { ...row, cutoff: draft.cutoff, version: draft.version };
       }
       this.conflicts.delete(key);
       return { ...row };
     });
     for (const [key, draft] of byKey) {
       if (this.changed(draft) || this.hasConflict(draft) || saved?.key === key) {
         this.conflicts.add(key);
         merged.push(draft);
       }
     }
     return merged;
   };
   this.data = {
     routes: merge(incoming.routes, previous?.routes ?? []),
     customers: merge(incoming.customers, previous?.customers ?? [])
   };
   this.snapshots.clear();
   for (const row of [...incoming.routes, ...incoming.customers]) this.snapshots.set(this.key(row), { ...row });
 }
 private message(e: unknown): string {
   return e instanceof HttpErrorResponse ? e.error?.error || 'No pudimos consultar o guardar el horario.' : 'No pudimos guardar el horario.';
 }
}
