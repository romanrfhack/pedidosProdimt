import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { EffectivePermissionsService } from './effective-permissions.service';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <header class="app-shell__header">
      <div>
        <p class="eyebrow">PRODIMT</p>
        <h1>Pedidos</h1>
      </div>
      <nav aria-label="Navegacion principal">
        <a routerLink="/cliente" routerLinkActive="active">Cliente</a>
        @if (auth.isAdminSession()) {
          @if (access.has('orders.read')) { <a routerLink="/admin/pedidos" routerLinkActive="active">Pedidos</a> }
          @if (access.any(['operations.read','operations.acknowledge'])) { <a routerLink="/admin/operacion" routerLinkActive="active">Operación</a> }
          @if (access.has('orders.review')) { <a routerLink="/admin/pendientes" routerLinkActive="active">Revision</a> }
          @if (access.has('orders.capture')) { <a routerLink="/admin/clientes-pendientes" routerLinkActive="active">Pendientes</a> }
          @if (access.any(['customers.read','customers.manage','products.read','products.manage','machines.read','machines.manage','tokens.manage','pricing.manage'])) { <a routerLink="/admin/catalogos" routerLinkActive="active">Catalogos</a> }
          @if (access.has('imports.manage')) { <a routerLink="/admin/importacion" routerLinkActive="active">Importacion</a> }
          @if (access.has('imports.manage')) { <a routerLink="/admin/importar-pedidos" routerLinkActive="active">Importar pedidos</a> }
          @if (access.has('security.manage')) { <a routerLink="/admin/seguridad" routerLinkActive="active">Seguridad</a> }
          <a routerLink="/admin/cambiar-contrasena" routerLinkActive="active">Cambiar contraseña</a>
        }
        @if (auth.session()) {
          <button type="button" class="nav-action" (click)="logout()">Salir</button>
        } @else {
          <a routerLink="/admin/login" routerLinkActive="active">Admin</a>
        }
      </nav>
    </header>

    <main class="app-shell__main">
      <router-outlet />
    </main>
  `
})
export class AppComponent {
  protected readonly auth = inject(AuthService);
  protected readonly access = inject(EffectivePermissionsService);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.logout();
    void this.router.navigate(['/cliente']);
  }
}
