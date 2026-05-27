import { Injectable } from '@angular/core';
import { Observable, catchError, map, of } from 'rxjs';
import { CustomerOrdersApiService } from './customer-orders-api.service';

export interface FrequentProduct {
  id: string;
  name: string;
  suggestedQuantity: number;
  quantity: number;
}

export interface AdminOrder {
  id: string;
  customerName: string;
  status: string;
  requestedDelivery: string;
  isLate: boolean;
  requiresReview: boolean;
}

@Injectable({ providedIn: 'root' })
export class OrderDataService {
  constructor(private readonly customerOrdersApi: CustomerOrdersApiService) {}

  readonly customerName = 'Cliente de ejemplo';

  readonly frequentProducts: FrequentProduct[] = [
    { id: '22222222-2222-2222-2222-222222222201', name: '#9 1/2', suggestedQuantity: 20, quantity: 20 },
    { id: '22222222-2222-2222-2222-222222222202', name: '#10 1/2', suggestedQuantity: 10, quantity: 10 },
    { id: '22222222-2222-2222-2222-222222222203', name: '#11', suggestedQuantity: 6, quantity: 6 }
  ];

  readonly todayOrders: AdminOrder[] = [
    {
      id: 'ORD-001',
      customerName: 'Cliente de ejemplo',
      status: 'Submitted',
      requestedDelivery: '12:00 - 14:00',
      isLate: false,
      requiresReview: false
    },
    {
      id: 'ORD-002',
      customerName: 'Cliente con pedido tardio',
      status: 'PendingAdminReview',
      requestedDelivery: '14:00',
      isLate: true,
      requiresReview: true
    }
  ];

  readonly pendingReviewOrders: AdminOrder[] = this.todayOrders.filter((order) => order.requiresReview);

  loadCustomerToday(): Observable<{ customerName: string; frequentProducts: FrequentProduct[] }> {
    return this.customerOrdersApi.getToday().pipe(
      map((response) => ({
        customerName: response.customerName,
        frequentProducts: response.products.map((product) => ({
          id: product.productId,
          name: product.name,
          suggestedQuantity: product.suggestedQuantity,
          quantity: product.suggestedQuantity
        }))
      })),
      catchError(() => of({
        customerName: this.customerName,
        frequentProducts: this.frequentProducts.map((product) => ({ ...product }))
      }))
    );
  }
}
