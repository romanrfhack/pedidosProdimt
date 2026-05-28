import { Routes } from '@angular/router';
import { AdminLoginComponent } from './admin-login.component';
import { AdminPendingComponent } from './admin-pending.component';
import { AdminPendingCustomersComponent } from './admin-pending-customers.component';
import { AdminTodayComponent } from './admin-today.component';
import { adminAuthGuard } from './admin-auth.guard';
import { CustomerTodayComponent } from './customer-today.component';

export const routes: Routes = [
  { path: '', redirectTo: 'cliente', pathMatch: 'full' },
  { path: 'cliente', component: CustomerTodayComponent },
  { path: 'admin/login', component: AdminLoginComponent },
  { path: 'admin/pedidos', component: AdminTodayComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/pendientes', component: AdminPendingComponent, canActivate: [adminAuthGuard] },
  { path: 'admin/clientes-pendientes', component: AdminPendingCustomersComponent, canActivate: [adminAuthGuard] }
];
