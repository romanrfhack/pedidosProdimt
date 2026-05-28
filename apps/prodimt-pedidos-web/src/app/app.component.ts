import { Component, inject } from '@angular/core';
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
          <a routerLink="/admin/pendientes" routerLinkActive="active">Revision</a>
          <a routerLink="/admin/clientes-pendientes" routerLinkActive="active">Pendientes</a>
          <a routerLink="/admin/catalogos" routerLinkActive="active">Catalogos</a>
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

  protected logout(): void {
    this.auth.logout();
    void this.router.navigate(['/cliente']);
  }
}
