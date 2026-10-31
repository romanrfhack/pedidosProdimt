import { Routes } from '@angular/router';
import { AdminLoginComponent } from './admin-login.component';
import { AdminPasswordComponent } from './admin-password.component';
import { AdminImportComponent } from './admin-import.component';
import { AdminPendingComponent } from './admin-pending.component';
import { AdminPendingCustomersComponent } from './admin-pending-customers.component';
import { AdminTodayComponent } from './admin-today.component';
import { adminAuthGuard } from './admin-auth.guard';
import { CustomerTodayComponent } from './customer-today.component';
import { AdminOperationsComponent } from './admin-operations.component';
import { AdminDailyImportComponent } from './admin-daily-import.component';

export const routes: Routes = [
  { path: '', redirectTo: 'cliente', pathMatch: 'full' },
  { path: 'cliente', component: CustomerTodayComponent },
  { path: 'admin/login', component: AdminLoginComponent },
  { path: 'admin/cambiar-contrasena', component: AdminPasswordComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/pedidos', component: AdminTodayComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/pendientes', component: AdminPendingComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/clientes-pendientes', component: AdminPendingCustomersComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/catalogos', loadComponent: () => import('./admin-catalogs.component').then(m => m.AdminCatalogsComponent), canActivate: [adminAuthGuard] },
  { path: 'admin/clientes/:customerId/configuracion', loadComponent: () => import('./admin-catalogs.component').then(m => m.AdminCatalogsComponent), canActivate: [adminAuthGuard] },
  { path: 'admin/operacion', component: AdminOperationsComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/importar-pedidos', component: AdminDailyImportComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/importacion', component: AdminImportComponent, canActivate: [adminAuthGuard] }
];
