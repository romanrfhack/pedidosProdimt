import { HttpClient } from '@angular/common/http';
import { Injectable, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';

interface MyPermissions { userId: string; permissions: string[]; }

@Injectable({ providedIn: 'root' })
export class EffectivePermissionsService {
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly values = signal<ReadonlySet<string>>(new Set());
  private currentToken: string | null = null;
  private request: { token: string; promise: Promise<boolean> } | null = null;
  readonly loaded = signal(false);
  readonly unavailable = signal(false);
  readonly userId = signal<string | null>(null);

  constructor() {
    effect(() => {
      const session = this.auth.session();
      const token = session?.actorType === 'Admin' ? session.accessToken : null;
      if (token !== this.currentToken) {
        this.currentToken = token;
        this.values.set(new Set()); this.loaded.set(false); this.unavailable.set(false); this.userId.set(null);
        if (token) void this.refresh();
      }
    });
  }

  has(key: string): boolean { return this.loaded() && this.auth.isAdmin() && this.values().has(key); }
  any(keys: readonly string[]): boolean { return keys.some(key => this.has(key)); }

  async refresh(): Promise<boolean> {
    const token = this.auth.isAdmin() ? this.auth.getAccessToken() : null;
    if (!token) { this.values.set(new Set()); this.loaded.set(false); this.userId.set(null); return false; }
    if (this.request?.token === token) return this.request.promise;
    this.currentToken = token;
    const promise = firstValueFrom(this.http.get<MyPermissions>(`${environment.apiBaseUrl}/api/admin/security/me`))
      .then(value => {
        if (this.auth.getAccessToken() !== token) return false;
        if (!Array.isArray(value.permissions) || !value.permissions.every(key => typeof key === 'string') || typeof value.userId !== 'string') throw new Error('Invalid permissions response');
        this.values.set(new Set(value.permissions)); this.loaded.set(true); this.unavailable.set(false); this.userId.set(value.userId);
        return true;
      }).catch(() => {
        if (this.auth.getAccessToken() === token) {
          this.values.set(new Set()); this.loaded.set(false); this.unavailable.set(true); this.userId.set(null);
        }
        return false;
      }).finally(() => { if (this.request?.token === token) this.request = null; });
    this.request = { token, promise };
    return promise;
  }

  landing(): string {
    const routes: [string[], string][] = [
      [['orders.read'], '/admin/pedidos'], [['orders.capture'], '/admin/clientes-pendientes'],
      [['orders.review'], '/admin/pendientes'], [['customers.read', 'products.read', 'machines.read', 'customers.manage', 'products.manage', 'machines.manage','tokens.manage','pricing.manage'], '/admin/catalogos'],
      [['operations.read','operations.acknowledge'], '/admin/operacion'], [['security.manage'], '/admin/seguridad'], [['imports.manage'], '/admin/importacion']
    ];
    return routes.find(([keys]) => this.any(keys))?.[1] ?? '/admin/sin-acceso';
  }
}
