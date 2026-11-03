import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EffectivePermissionsService } from './effective-permissions.service';

@Component({
  selector: 'app-delivery-onboarding',
  imports: [RouterLink],
  styleUrl: './delivery-onboarding.component.css',
  template: `
    <section class="page stack delivery-onboarding" data-testid="delivery-onboarding" aria-label="Cómo habilitar reparto">
      <div class="page-title"><p class="eyebrow">Personal PRODIMT</p><h2>Reparto</h2><p>Consulta la preparación necesaria para conectar los pedidos con el recorrido del chofer.</p></div>
      @if (access.unavailable()) {
        <div class="alert warning" role="alert">No pudimos consultar tus permisos. Vuelve a consultarlos para comprobar tu acceso.</div>
      } @else if (access.loaded() && !deliveryPath) {
        <div class="notice"><strong>Tu cuenta todavía no tiene acceso al reparto.</strong><p>Los accesos para consultar pedidos o catálogos son distintos de los accesos de reparto. Un pedido por revisar también necesita una decisión administrativa antes de asignarse.</p></div>
      }
      <ol class="delivery-onboarding-steps">
        <li><h3>Habilitar a las personas</h3><p>Para operar como chofer, administración puede asignar el rol «Repartidor - Camioneta (sistema)» o los permisos equivalentes. El rol personalizado anterior conserva sus accesos; tener un nombre parecido no habilita la función automáticamente.</p><p>La persona que prepara rutas y vueltas necesita el permiso de administrar reparto. El chofer consulta y opera únicamente sus vueltas asignadas.</p></li>
        <li><h3>Preparar el recorrido del día</h3><p>Coordinación configura la ruta con el código usado por los clientes, elige al chofer y crea una vuelta para la fecha del pedido. Confirma la ubicación antes de asignar las cantidades por partida. Si cambia una ubicación ya asignada, usa «Actualizar indicaciones» en esa asignación abierta para que el chofer reciba la ficha actualizada.</p><p>El código de ruta guardado en un cliente no crea una vuelta automáticamente. Sólo los pedidos registrados o aceptados pueden asignarse; los que indican «No pide» quedan fuera.</p></li>
        <li><h3>Registrar el reparto</h3><p>En «Mi reparto», el chofer confirma las cantidades contadas, cargadas y entregadas. Las diferencias, notas y horas quedan registradas sin cambiar el pedido original.</p></li>
      </ol>
      <div class="delivery-onboarding-actions">
        @if (deliveryPath; as path) { <a class="primary" [routerLink]="path" [queryParams]="date ? { date: date } : {}">{{ access.has('delivery.manage') ? 'Gestionar reparto' : 'Mi reparto' }}</a> }
        <button class="secondary" type="button" (click)="refresh()" [disabled]="loading">{{ loading ? 'Consultando…' : 'Volver a consultar mis permisos' }}</button>
        @if (access.has('security.manage')) { <a class="secondary" routerLink="/admin/seguridad">Seguridad</a> }
        @if (access.has('orders.read')) { <a class="secondary" routerLink="/admin/pedidos" [queryParams]="date ? { date: date } : {}">Ver pedidos</a> }
      </div>
      @if (message) { <p class="notice" role="status">{{ message }}</p> }
      @if (access.loaded() && !deliveryPath && !access.has('security.manage')) { <p>Solicita al responsable de accesos que revise tu cuenta. Después usa «Volver a consultar mis permisos».</p> }
    </section>
  `
})
export class DeliveryOnboardingComponent {
  protected readonly access = inject(EffectivePermissionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly destroy = inject(DestroyRef);
  protected readonly date = this.route.snapshot.queryParamMap.get('date');
  protected loading = false;
  protected message: string | null = null;

  protected get deliveryPath(): string | null {
    if (this.access.has('delivery.manage')) return '/admin/reparto';
    return this.access.any(['delivery.read.own', 'delivery.operate.own']) ? '/reparto' : null;
  }

  protected async refresh(): Promise<void> {
    if (this.loading) return;
    this.loading = true; this.message = null;
    const refreshed = await this.access.refresh();
    if (this.destroy.destroyed) return;
    this.loading = false;
    const path = this.deliveryPath;
    if (refreshed && path) {
      void this.router.navigate([path], { queryParams: this.date ? { date: this.date } : {} });
      return;
    }
    this.message = refreshed ? 'Permisos consultados. Tu cuenta todavía necesita la asignación de acceso a reparto.' : 'No se pudo verificar tu acceso. Reintenta cuando tengas conexión.';
    this.detector.detectChanges();
  }
}
