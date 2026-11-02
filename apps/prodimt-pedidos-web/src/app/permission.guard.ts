import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { EffectivePermissionsService } from './effective-permissions.service';

export const permissionGuard: CanActivateFn = async route => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const permissions = inject(EffectivePermissionsService);
  if (!auth.isAdmin()) return router.createUrlTree(['/admin/login']);
  await permissions.refresh();
  const keys = (route.data['permissions'] ?? []) as string[];
  return permissions.any(keys) ? true : router.createUrlTree(['/admin/sin-acceso']);
};
