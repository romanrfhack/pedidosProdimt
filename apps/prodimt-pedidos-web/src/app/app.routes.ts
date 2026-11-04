import { Routes } from '@angular/router';
import { deliveryDraftGuard } from './delivery-draft.guard';
import { AdminLoginComponent } from './admin-login.component';
import { AdminPasswordComponent } from './admin-password.component';
import { AdminImportComponent } from './admin-import.component';
import { AdminPendingComponent } from './admin-pending.component';
import { AdminPendingCustomersComponent } from './admin-pending-customers.component';
import { AdminTodayComponent, adminTodayDraftGuard } from './admin-today.component';
import { permissionGuard } from './permission.guard';
import { AdminAccessDeniedComponent } from './admin-access-denied.component';
import { adminAuthGuard } from './admin-auth.guard';
import { AdminDailyImportComponent } from './admin-daily-import.component';

export const routes: Routes = [
  { path: '', redirectTo: 'cliente', pathMatch: 'full' },
  { path: 'cliente', loadComponent: () => import('./customer-today.component').then(m => m.CustomerTodayComponent) },
  { path: 'admin/login', component: AdminLoginComponent },
  { path: 'personal/reparto', loadComponent: () => import('./delivery-onboarding.component').then(m => m.DeliveryOnboardingComponent), canActivate: [adminAuthGuard] },
  { path: 'reparto', loadComponent: () => import('./driver-delivery.component').then(m => m.DriverDeliveryComponent), canActivate: [permissionGuard], canDeactivate: [deliveryDraftGuard], data: { permissions: ['delivery.read.own', 'delivery.operate.own'] } },
  { path: 'admin/reparto', loadComponent: () => import('./admin-delivery.component').then(m => m.AdminDeliveryComponent), canActivate: [permissionGuard], canDeactivate: [deliveryDraftGuard], data: { permissions: ['delivery.manage'], deliveryManagement: true } },
  { path: 'admin/cambiar-contrasena', component: AdminPasswordComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/sin-acceso', component: AdminAccessDeniedComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/horarios', loadComponent: () => import('./admin-ordering-schedules.component').then(m => m.AdminOrderingSchedulesComponent), canActivate: [permissionGuard], data: { permissions: ['customers.manage'] } },
  { path: 'admin/seguridad', loadComponent: () => import('./admin-security.component').then(m => m.AdminSecurityComponent), canActivate: [permissionGuard], data: { permissions: ['security.manage'] } },
  { path: 'admin/pedidos', component: AdminTodayComponent, canActivate: [permissionGuard], canDeactivate: [adminTodayDraftGuard], data: { permissions: ['orders.read'] } },
  { path: 'admin/pendientes', component: AdminPendingComponent, canActivate: [permissionGuard], data: { permissions: ['orders.review'] } },
  { path: 'admin/clientes-pendientes', component: AdminPendingCustomersComponent, canActivate: [permissionGuard], data: { permissions: ['orders.capture'] } },
  { path: 'admin/productos-por-configurar', loadComponent: () => import('./admin-product-setups.component').then(m => m.AdminProductSetupsComponent), canActivate: [permissionGuard], data: { permissions: ['customers.manage','pricing.manage'] } },
  { path: 'admin/catalogos', loadComponent: () => import('./admin-catalogs.component').then(m => m.AdminCatalogsComponent), canActivate: [permissionGuard], data: { permissions: ['customers.read', 'customers.manage', 'products.read', 'products.manage', 'machines.read', 'machines.manage', 'tokens.manage', 'pricing.manage'] } },
  { path: 'admin/clientes/:customerId/configuracion', loadComponent: () => import('./admin-catalogs.component').then(m => m.AdminCatalogsComponent), canActivate: [permissionGuard], data: { permissions: ['customers.manage', 'tokens.manage', 'pricing.manage'] } },
  { path: 'admin/operacion', loadComponent: () => import('./admin-operations.component').then(m => m.AdminOperationsComponent), canActivate: [permissionGuard], data: { permissions: ['operations.read','operations.acknowledge','operations.assign','operations.produce','operations.allocate'] } },
  { path: 'admin/importar-pedidos', component: AdminDailyImportComponent, canActivate: [permissionGuard], data: { permissions: ['imports.manage'] } },
  { path: 'admin/importacion', component: AdminImportComponent, canActivate: [permissionGuard], data: { permissions: ['imports.manage'] } }
];
