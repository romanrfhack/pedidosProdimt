import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

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
        <a routerLink="/admin/pedidos" routerLinkActive="active">Pedidos</a>
        <a routerLink="/admin/pendientes" routerLinkActive="active">Revision</a>
      </nav>
    </header>

    <main class="app-shell__main">
      <router-outlet />
    </main>
  `
})
export class AppComponent {}
