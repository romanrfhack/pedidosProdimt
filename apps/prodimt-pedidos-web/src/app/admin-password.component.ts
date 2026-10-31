import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-admin-password',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="admin-password">
      <div class="page-title"><p class="eyebrow">Mi cuenta</p><h2>Cambiar contraseña</h2></div>
      <p>Al guardar, se cerrarán tus sesiones. Vuelve a entrar con la nueva contraseña.</p>
      <form class="login-form" (ngSubmit)="save()">
        <label><span>Contraseña actual</span>
          <input name="currentPassword" type="password" autocomplete="current-password" maxlength="1024"
            [(ngModel)]="currentPassword" [disabled]="saving" required></label>
        <label><span>Nueva contraseña</span>
          <input name="newPassword" type="password" autocomplete="new-password" minlength="16" maxlength="128"
            [(ngModel)]="newPassword" [disabled]="saving" required></label>
        <p>Usa entre 16 y 128 caracteres.</p>
        <label><span>Confirmar nueva contraseña</span>
          <input name="confirmPassword" type="password" autocomplete="new-password" maxlength="128"
            [(ngModel)]="confirmPassword" [disabled]="saving" required></label>
        @if (error) { <div class="alert error" role="alert">{{ error }}</div> }
        <button type="submit" class="primary" [disabled]="saving">{{ saving ? 'Guardando…' : 'Guardar contraseña' }}</button>
      </form>
    </section>
  `
})
export class AdminPasswordComponent {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly changeDetector = inject(ChangeDetectorRef);
  protected currentPassword = '';
  protected newPassword = '';
  protected confirmPassword = '';
  protected saving = false;
  protected error: string | null = null;

  protected save(): void {
    if (this.saving) return;
    this.error = null;
    if (this.newPassword.length < 16 || this.newPassword.length > 128) {
      this.error = 'La nueva contraseña debe tener entre 16 y 128 caracteres.';
      return;
    }
    if (this.newPassword !== this.confirmPassword) {
      this.error = 'La nueva contraseña y su confirmación no coinciden.';
      return;
    }
    this.saving = true;
    this.http.post<void>(`${environment.apiBaseUrl}/api/admin/account/change-password`, {
      currentPassword: this.currentPassword, newPassword: this.newPassword, confirmPassword: this.confirmPassword
    }).subscribe({
      next: () => {
        this.currentPassword = this.newPassword = this.confirmPassword = '';
        this.auth.logout();
        void this.router.navigate(['/admin/login'], { queryParams: { passwordChanged: '1' } });
      },
      error: (response: unknown) => {
        this.saving = false;
        this.error = response instanceof HttpErrorResponse && response.error?.error
          ? response.error.error : 'No se pudo cambiar la contraseña. Revisa la conexión antes de reintentar.';
        this.changeDetector.detectChanges();
      }
    });
  }
}
