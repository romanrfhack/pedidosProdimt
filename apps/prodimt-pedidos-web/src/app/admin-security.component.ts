import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { EffectivePermissionsService } from './effective-permissions.service';
import { SecurityApiService, SecurityCollection, SecurityPermission, SecurityRole, SecuritySnapshot, SecurityUser } from './security-api.service';

type Tab = SecurityCollection;
interface UserDraft { id: string | null; userName: string; displayName: string; password: string; isActive: boolean; roleIds: string[]; directPermissionIds: string[]; }
interface RoleDraft { id: string | null; name: string; description: string; isActive: boolean; isSystem: boolean; permissionIds: string[]; }
interface PermissionDraft { id: string | null; key: string; name: string; module: string; description: string; isActive: boolean; isSystem: boolean; }

@Component({
  selector: 'app-admin-security',
  imports: [FormsModule],
  templateUrl: './admin-security.component.html',
  styleUrls: ['./admin-security.component.css', './admin-security-responsive.css']
})
export class AdminSecurityComponent {
  private readonly api = inject(SecurityApiService);
  protected readonly access = inject(EffectivePermissionsService);
  private readonly detector = inject(ChangeDetectorRef);
  private readonly destroy = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  protected snapshot: SecuritySnapshot | null = null;
  protected tab: Tab = 'users';
  protected query = '';
  protected permissionQuery = '';
  protected state = 'all';
  protected loading = true;
  protected saving = false;
  protected conflict = false;
  protected error: string | null = null;
  protected success: string | null = null;
  protected user: UserDraft | null = null;
  protected role: RoleDraft | null = null;
  protected permission: PermissionDraft | null = null;
  protected deleting: { collection: SecurityCollection; id: string; name: string } | null = null;
  private revision = '';
  private previousFocus: HTMLElement | null = null;

  constructor() { this.reload(); this.destroy.onDestroy(() => { if (this.user) this.user.password = ''; }); }

  protected get editorOpen(): boolean { return !!(this.user || this.role || this.permission); }
  protected get users() { return (this.snapshot?.users ?? []).filter(x => this.matches([x.userName, x.displayName, ...this.roleNames(x.roleIds)]) && this.stateMatches(x.isActive)); }
  protected get roles() { return (this.snapshot?.roles ?? []).filter(x => this.matches([x.name, x.description]) && this.stateMatches(x.isActive)); }
  protected get permissions() { return (this.snapshot?.permissions ?? []).filter(x => this.matches([x.name, x.key, x.module, x.description]) && this.stateMatches(x.isActive)); }
  protected get availableRoles() { return this.snapshot?.roles ?? []; }
  protected get groups() {
    const rows = (this.snapshot?.permissions ?? []).filter(x => this.normalize([x.name, x.key, x.module].join(' ')).includes(this.normalize(this.permissionQuery)));
    return [...new Set(rows.map(x => x.module))].sort((a, b) => a.localeCompare(b, 'es-MX'))
      .map(module => ({ module, permissions: rows.filter(x => x.module === module).sort((a, b) => a.name.localeCompare(b.name, 'es-MX')) }));
  }
  protected get effectiveCount() { return (this.snapshot?.permissions ?? []).filter(x => this.effective(x)).length; }
  protected effective(permission: SecurityPermission): boolean { return permission.isActive && !!this.user?.isActive && (this.user.directPermissionIds.includes(permission.id) || this.inherited(permission).length > 0); }
  protected inherited(permission: SecurityPermission): string[] {
    return (this.snapshot?.roles ?? []).filter(x => x.isActive && this.user?.roleIds.includes(x.id) && x.permissionIds.includes(permission.id)).map(x => x.name);
  }
  protected roleNames(ids: string[]): string[] { return (this.snapshot?.roles ?? []).filter(x => ids.includes(x.id)).map(x => x.name); }
  protected countActive(rows: { isActive: boolean }[]): number { return rows.filter(x => x.isActive).length; }
  protected roleMembers(id: string): number { return (this.snapshot?.users ?? []).filter(x => x.roleIds.includes(id)).length; }
  protected permissionAssignments(id: string): number { return (this.snapshot?.roles ?? []).filter(x => x.permissionIds.includes(id)).length + (this.snapshot?.users ?? []).filter(x => x.directPermissionIds.includes(id)).length; }

  protected switchTab(tab: Tab): void { if (this.editorOpen || this.deleting || this.saving) return; this.tab = tab; this.query = ''; this.state = 'all'; this.error = null; this.success = null; }
  protected reload(): void {
    if (this.saving) return;
    this.loading = true; this.error = null;
    this.api.read().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: value => { this.snapshot = value; this.loading = false; this.conflict = false; this.revision = value.revision; this.detector.detectChanges(); },
      error: (error: unknown) => { this.loading = false; this.error = this.formatError(error, 'No se pudo consultar la administración de accesos.'); this.detector.detectChanges(); }
    });
  }
  protected discardAndReload(): void { this.close(); this.reload(); }
  protected editUser(value?: SecurityUser, event?: Event): void {
    this.prepare(event); this.user = value ? { id: value.id, userName: value.userName, displayName: value.displayName, password: '', isActive: value.isActive, roleIds: [...value.roleIds], directPermissionIds: [...value.directPermissionIds] }
      : { id: null, userName: '', displayName: '', password: '', isActive: true, roleIds: [], directPermissionIds: [] };
    this.focusEditor();
  }
  protected editRole(value?: SecurityRole, event?: Event): void {
    this.prepare(event); this.role = value ? { ...value, permissionIds: [...value.permissionIds] }
      : { id: null, name: '', description: '', isActive: true, isSystem: false, permissionIds: [] };
    this.focusEditor();
  }
  protected editPermission(value?: SecurityPermission, event?: Event): void {
    this.prepare(event); this.permission = value ? { ...value } : { id: null, key: '', name: '', module: '', description: '', isActive: true, isSystem: false };
    this.focusEditor();
  }
  protected toggle(ids: string[], id: string, selected: boolean): void { const index = ids.indexOf(id); if (selected && index < 0) ids.push(id); if (!selected && index >= 0) ids.splice(index, 1); }
  protected close(): void {
    if (this.saving) return;
    if (this.user) this.user.password = '';
    this.user = null; this.role = null; this.permission = null; this.deleting = null; this.conflict = false; this.permissionQuery = ''; this.error = null;
    this.previousFocus?.focus(); this.previousFocus = null;
  }
  protected askDelete(collection: SecurityCollection, id: string, name: string, event: Event): void {
    this.prepare(event); this.deleting = { collection, id, name };
  }
  protected async confirmDelete(): Promise<void> {
    if (!this.deleting || this.saving || this.conflict) return;
    const { collection, id } = this.deleting;
    await this.mutate(() => firstValueFrom(this.api.remove(collection, id, this.revision)), collection === 'users' ? 'Usuario eliminado. Se conserva su historial.' : 'Registro eliminado.');
  }
  protected async saveUser(): Promise<void> {
    if (!this.user || this.saving || this.conflict) return;
    const draft = this.user;
    if (!draft.userName.trim() || !draft.displayName.trim() || (!draft.id && !draft.password)) { this.error = 'Completa el usuario, nombre visible y contraseña inicial.'; return; }
    if (draft.password && (draft.password.length < 16 || draft.password.length > 128)) { this.error = 'La contraseña debe contener entre 16 y 128 caracteres.'; return; }
    await this.mutate(() => firstValueFrom(this.api.save('users', draft.id, { expectedRevision: this.revision, userName: draft.userName.trim(), displayName: draft.displayName.trim(), password: draft.password || null, isActive: draft.isActive, roleIds: [...draft.roleIds], directPermissionIds: [...draft.directPermissionIds] })), 'Usuario y asignaciones guardados.');
  }
  protected async saveRole(): Promise<void> {
    if (!this.role || this.saving || this.conflict) return;
    const draft = this.role;
    if (!draft.name.trim()) { this.error = 'Escribe el nombre del rol.'; return; }
    await this.mutate(() => firstValueFrom(this.api.save('roles', draft.id, { expectedRevision: this.revision, name: draft.name.trim(), description: draft.description.trim(), isActive: draft.isActive, permissionIds: [...draft.permissionIds] })), 'Rol y permisos guardados.');
  }
  protected async savePermission(): Promise<void> {
    if (!this.permission || this.saving || this.conflict) return;
    const draft = this.permission;
    if (!draft.key.trim() || !draft.name.trim() || !draft.module.trim()) { this.error = 'Completa la clave, el nombre y el módulo del permiso.'; return; }
    await this.mutate(() => firstValueFrom(this.api.save('permissions', draft.id, { expectedRevision: this.revision, key: draft.key.trim(), name: draft.name.trim(), module: draft.module.trim(), description: draft.description.trim(), isActive: draft.isActive })), 'Permiso guardado.');
  }

  private async mutate(operation: () => Promise<SecuritySnapshot>, message: string): Promise<void> {
    this.saving = true; this.error = null; this.success = null;
    try {
      const value = await operation();
      this.snapshot = value; this.revision = value.revision;
      this.saving = false; this.close(); this.success = message;
      await this.access.refresh();
    } catch (error) {
      this.saving = false;
      this.conflict = error instanceof HttpErrorResponse && error.status === 409;
      this.error = this.formatError(error, 'No pudimos confirmar el cambio. Consulta el estado antes de volver a guardar.');
    }
    this.detector.detectChanges();
  }
  private prepare(event?: Event): void {
    this.close(); this.previousFocus = event?.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    this.revision = this.snapshot?.revision ?? ''; this.error = null; this.success = null; this.permissionQuery = '';
  }
  private focusEditor(): void {
    setTimeout(() => { const editor = this.document.querySelector<HTMLElement>('.security-editor h3'); editor?.focus(); editor?.scrollIntoView({ block: 'nearest' }); });
  }
  private normalize(value: string) { return value.trim().normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es-MX'); }
  private matches(values: (string | null | undefined)[]) { return this.normalize(values.filter(Boolean).join(' ')).includes(this.normalize(this.query)); }
  private stateMatches(active: boolean) { return this.state === 'all' || (this.state === 'active') === active; }
  private formatError(error: unknown, fallback: string) { return error instanceof HttpErrorResponse && typeof error.error?.error === 'string' ? error.error.error : fallback; }
}
