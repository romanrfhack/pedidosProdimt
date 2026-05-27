import { Injectable } from '@angular/core';

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
  readonly customerName = 'Cliente de ejemplo';

  readonly frequentProducts: FrequentProduct[] = [
    { id: '22222222-2222-2222-2222-222222222201', name: '#9.5', suggestedQuantity: 12, quantity: 12 },
    { id: '22222222-2222-2222-2222-222222222202', name: '#10', suggestedQuantity: 8, quantity: 8 },
    { id: '22222222-2222-2222-2222-222222222203', name: 'Flauta', suggestedQuantity: 6, quantity: 6 }
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
}
