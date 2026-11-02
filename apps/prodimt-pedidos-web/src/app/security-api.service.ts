import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../environments/environment';

export interface SecurityPermission { id: string; key: string; name: string; module: string; description: string; isActive: boolean; isSystem: boolean; }
export interface SecurityRole { id: string; name: string; description: string; isActive: boolean; isSystem: boolean; permissionIds: string[]; }
export interface EffectivePermission { id: string; key: string; name: string; module: string; roleNames: string[]; isDirect: boolean; }
export interface SecurityUser { id: string; userName: string; displayName: string; isActive: boolean; roleIds: string[]; directPermissionIds: string[]; effectivePermissions: EffectivePermission[]; createdAt: string; }
export interface SecuritySnapshot { revision: string; users: SecurityUser[]; roles: SecurityRole[]; permissions: SecurityPermission[]; }
export type SecurityCollection = 'users' | 'roles' | 'permissions';

@Injectable({ providedIn: 'root' })
export class SecurityApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/admin/security`;
  read() { return this.http.get<SecuritySnapshot>(this.base); }
  save(collection: SecurityCollection, id: string | null, value: object) {
    return id ? this.http.put<SecuritySnapshot>(`${this.base}/${collection}/${id}`, value)
      : this.http.post<SecuritySnapshot>(`${this.base}/${collection}`, value);
  }
  remove(collection: SecurityCollection, id: string, revision: string) {
    return this.http.delete<SecuritySnapshot>(`${this.base}/${collection}/${id}`, {
      params: new HttpParams().set('expectedRevision', revision)
    });
  }
}
