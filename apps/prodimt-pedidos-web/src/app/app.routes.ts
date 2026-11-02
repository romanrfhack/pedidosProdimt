import { Routes } from '@angular/router';
import { AdminLoginComponent } from './admin-login.component';
import { AdminPasswordComponent } from './admin-password.component';
import { AdminImportComponent } from './admin-import.component';
import { AdminPendingComponent } from './admin-pending.component';
import { AdminPendingCustomersComponent } from './admin-pending-customers.component';
import { AdminTodayComponent } from './admin-today.component';
import { permissionGuard } from './permission.guard';
import { AdminAccessDeniedComponent } from './admin-access-denied.component';
import { adminAuthGuard } from './admin-auth.guard';
import { CustomerTodayComponent } from './customer-today.component';
import { AdminOperationsComponent } from './admin-operations.component';
import { AdminDailyImportComponent } from './admin-daily-import.component';

export const routes: Routes = [
  { path: '', redirectTo: 'cliente', pathMatch: 'full' },
  { path: 'cliente', component: CustomerTodayComponent },
  { path: 'admin/login', component: AdminLoginComponent },
  { path: 'admin/cambiar-contrasena', component: AdminPasswordComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/sin-acceso', component: AdminAccessDeniedComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/seguridad', loadComponent: () => import('./admin-security.component').then(m => m.AdminSecurityComponent), canActivate: [permissionGuard], data: { permissions: ['security.manage'] } },
  { path: 'admin/pedidos', component: AdminTodayComponent, canActivate: [permissionGuard], data: { permissions: ['orders.read'] } },
  { path: 'admin/pendientes', component: AdminPendingComponent, canActivate: [permissionGuard], data: { permissions: ['orders.review'] } },
  { path: 'admin/clientes-pendientes', component: AdminPendingCustomersComponent, canActivate: [permissionGuard], data: { permissions: ['orders.capture'] } },
  { path: 'admin/catalogos', loadComponent: () => import('./admin-catalogs.component').then(m => m.AdminCatalogsComponent), canActivate: [permissionGuard], data: { permissions: ['customers.read', 'customers.manage', 'products.read', 'products.manage', 'machines.read', 'machines.manage', 'tokens.manage', 'pricing.manage'] } },
  { path: 'admin/clientes/:customerId/configuracion', loadComponent: () => import('./admin-catalogs.component').then(m => m.AdminCatalogsComponent), canActivate: [permissionGuard], data: { permissions: ['customers.manage', 'tokens.manage', 'pricing.manage'] } },
  { path: 'admin/operacion', component: AdminOperationsComponent, canActivate: [permissionGuard], data: { permissions: ['operations.read','operations.acknowledge'] } },
  { path: 'admin/importar-pedidos', component: AdminDailyImportComponent, canActivate: [permissionGuard], data: { permissions: ['imports.manage'] } },
  { path: 'admin/importacion', component: AdminImportComponent, canActivate: [permissionGuard], data: { permissions: ['imports.manage'] } }
];
