import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { EffectivePermissionsService } from './effective-permissions.service';

@Component({
  selector: 'app-admin-access-denied',
  template: `<section class="page stack" data-testid="admin-access-denied"><div class="page-title"><p class="eyebrow">Administración</p><h2>Acceso no disponible</h2></div>
    <p>{{ permissions.unavailable() ? 'No pudimos consultar tus permisos. Reintenta para verificar tu acceso.' : 'Tu cuenta no tiene el permiso requerido para esta pantalla. Solicita la asignación a un administrador.' }}</p>
    <button class="secondary" type="button" [disabled]="loading" (click)="retry()">{{ loading ? 'Consultando…' : 'Volver a consultar mis permisos' }}</button></section>`
})
export class AdminAccessDeniedComponent {
  protected readonly permissions = inject(EffectivePermissionsService);
  private readonly router = inject(Router);
  protected loading = false;
  protected async retry() { this.loading = true; await this.permissions.refresh(); this.loading = false; const route = this.permissions.landing(); if (route !== '/admin/sin-acceso') void this.router.navigateByUrl(route); }
}
