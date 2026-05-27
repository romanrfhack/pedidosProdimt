import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface CustomerOrderTodayApiResponse {
  customerId: string;
  customerName: string;
  orderDate: string;
  preferredDeliveryTime: string | null;
  preferredDeliveryWindowStart: string | null;
  preferredDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
  products: CustomerOrderProductApiResponse[];
}

export interface CustomerOrderProductApiResponse {
  productId: string;
  name: string;
  description: string | null;
  suggestedQuantity: number;
}

@Injectable({ providedIn: 'root' })
export class CustomerOrdersApiService {
  private readonly http = inject(HttpClient);

  getToday(customerId = environment.demoCustomerId): Observable<CustomerOrderTodayApiResponse> {
    return this.http.get<CustomerOrderTodayApiResponse>(
      `${environment.apiBaseUrl}/api/customer-orders/${customerId}/today`
    );
  }
}
