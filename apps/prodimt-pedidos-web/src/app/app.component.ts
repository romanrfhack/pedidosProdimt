import { Component, DestroyRef, effect, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { ProductSetupApiService } from './product-setup-api.service';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
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
          <a routerLink="/admin/pedidos" routerLinkActive="active">Pedidos</a>
          <a routerLink="/admin/operacion" routerLinkActive="active">Operación</a>
          <a routerLink="/admin/pendientes" routerLinkActive="active">Revision</a>
          <a routerLink="/admin/clientes-pendientes" routerLinkActive="active">Pendientes</a>
          <a routerLink="/admin/productos-por-configurar" routerLinkActive="active" aria-label="Productos por configurar">Configurar productos @if (setups.pendingCount(); as count) { <span class="setup-nav-count">{{ count }}</span> }</a>
          <a routerLink="/admin/catalogos" routerLinkActive="active">Catalogos</a>
          <a routerLink="/admin/importacion" routerLinkActive="active">Importacion</a>
          <a routerLink="/admin/importar-pedidos" routerLinkActive="active">Importar pedidos</a>
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
  private readonly router = inject(Router);

  protected readonly setups = inject(ProductSetupApiService);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  constructor() {
    effect(() => { if (this.auth.isAdminSession()) this.setups.refreshCount(); else this.setups.clearCount(); });
    const refresh = () => { if (this.auth.isAdminSession() && this.document.visibilityState !== 'hidden') this.setups.refreshCount(); };
    const timer = setInterval(refresh, 30000);
    this.document.addEventListener('visibilitychange', refresh);
    this.destroyRef.onDestroy(() => { clearInterval(timer); this.document.removeEventListener('visibilitychange', refresh); this.setups.clearCount(); });
  }

  protected logout(): void {
    this.auth.logout();
    void this.router.navigate(['/cliente']);
  }
}
