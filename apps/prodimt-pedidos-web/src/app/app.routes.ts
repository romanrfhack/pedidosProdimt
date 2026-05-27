import { Routes } from '@angular/router';
import { AdminPendingComponent } from './admin-pending.component';
import { AdminTodayComponent } from './admin-today.component';
import { CustomerTodayComponent } from './customer-today.component';

export const routes: Routes = [
  { path: '', redirectTo: 'cliente', pathMatch: 'full' },
  { path: 'cliente', component: CustomerTodayComponent },
  { path: 'admin/pedidos', component: AdminTodayComponent },
  { path: 'admin/pendientes', component: AdminPendingComponent }
];
