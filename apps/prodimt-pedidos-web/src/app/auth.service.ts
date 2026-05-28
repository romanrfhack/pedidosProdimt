import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../environments/environment';

export type AuthActorType = 'Customer' | 'Admin';

export interface AuthSession {
  accessToken: string;
  tokenType: 'Bearer';
  expiresAt: string;
  actorType: AuthActorType;
  customerId: string | null;
  customerName: string | null;
  displayName: string | null;
}

interface CustomerTokenLoginApiResponse {
  accessToken: string;
  tokenType: 'Bearer';
  expiresAt: string;
  customerId: string;
  customerName: string;
}

interface AdminLoginApiResponse {
  accessToken: string;
  tokenType: 'Bearer';
  expiresAt: string;
  displayName: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly storageKey = 'prodimt.auth.session.v1';
  private readonly http = inject(HttpClient);
  private readonly sessionSignal = signal<AuthSession | null>(this.readStoredSession());

  readonly session = this.sessionSignal.asReadonly();
  readonly isAdminSession = computed(() => this.sessionSignal()?.actorType === 'Admin');
  readonly isCustomerSession = computed(() => this.sessionSignal()?.actorType === 'Customer');

  loginCustomerWithToken(token: string): Observable<AuthSession> {
    return this.http.post<CustomerTokenLoginApiResponse>(
      `${environment.apiBaseUrl}/api/auth/customer-token`,
      { token }
    ).pipe(
      map((response) => ({
        accessToken: response.accessToken,
        tokenType: response.tokenType,
        expiresAt: response.expiresAt,
        actorType: 'Customer' as const,
        customerId: response.customerId,
        customerName: response.customerName,
        displayName: null
      })),
      tap((session) => this.storeSession(session))
    );
  }

  loginAdmin(userName: string, password: string): Observable<AuthSession> {
    return this.http.post<AdminLoginApiResponse>(
      `${environment.apiBaseUrl}/api/auth/admin/login`,
      { userName, password }
    ).pipe(
      map((response) => ({
        accessToken: response.accessToken,
        tokenType: response.tokenType,
        expiresAt: response.expiresAt,
        actorType: 'Admin' as const,
        customerId: null,
        customerName: null,
        displayName: response.displayName
      })),
      tap((session) => this.storeSession(session))
    );
  }

  logout(): void {
    localStorage.removeItem(this.storageKey);
    this.sessionSignal.set(null);
  }

  getAccessToken(): string | null {
    const session = this.sessionSignal();

    if (!session) {
      return null;
    }

    if (new Date(session.expiresAt).getTime() <= Date.now()) {
      this.logout();
      return null;
    }

    return session.accessToken;
  }

  getCustomerId(): string | null {
    const session = this.sessionSignal();
    return session?.actorType === 'Customer' ? session.customerId : null;
  }

  isAdmin(): boolean {
    return this.getAccessToken() !== null && this.isAdminSession();
  }

  isCustomer(): boolean {
    return this.getAccessToken() !== null && this.isCustomerSession();
  }

  private storeSession(session: AuthSession): void {
    localStorage.setItem(this.storageKey, JSON.stringify(session));
    this.sessionSignal.set(session);
  }

  private readStoredSession(): AuthSession | null {
    const raw = localStorage.getItem(this.storageKey);

    if (!raw) {
      return null;
    }

    try {
      const session = JSON.parse(raw) as AuthSession;

      if (!session.accessToken || new Date(session.expiresAt).getTime() <= Date.now()) {
        localStorage.removeItem(this.storageKey);
        return null;
      }

      return session;
    } catch {
      localStorage.removeItem(this.storageKey);
      return null;
    }
  }
}
