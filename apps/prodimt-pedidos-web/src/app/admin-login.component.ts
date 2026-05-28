import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-admin-login',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="admin-login">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Entrar</h2>
      </div>

      <form class="login-form" (ngSubmit)="login()">
        <label>
          <span>Usuario</span>
          <input name="userName" autocomplete="username" [(ngModel)]="userName">
        </label>

        <label>
          <span>Contrasena</span>
          <input name="password" type="password" autocomplete="current-password" [(ngModel)]="password">
        </label>

        @if (errorMessage) {
          <div class="alert error">{{ errorMessage }}</div>
        }

        <button type="submit" class="primary" [disabled]="isSubmitting">Entrar</button>
      </form>
    </section>
  `
})
export class AdminLoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly changeDetector = inject(ChangeDetectorRef);

  protected userName = environment.demoAdminUserName;
  protected password = environment.demoAdminPassword;
  protected isSubmitting = false;
  protected errorMessage: string | null = null;

  constructor() {
    if (this.auth.isAdmin()) {
      void this.router.navigate(['/admin/pedidos'], { replaceUrl: true });
    }
  }

  protected login(): void {
    this.errorMessage = null;
    this.isSubmitting = true;

    this.auth.loginAdmin(this.userName, this.password).subscribe({
      next: () => {
        void this.router.navigate(['/admin/pedidos']);
      },
      error: (error: unknown) => {
        this.errorMessage = this.formatError(error, 'No se pudo iniciar sesion.');
        this.isSubmitting = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  private formatError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      return error.error.error;
    }

    return fallback;
  }
}
