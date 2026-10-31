import { PaymentTermCode } from './operational-labels';

export interface CustomerEditorValue {
  id: string | null;
  name: string;
  phoneNumber: string | null;
  preferredDeliveryTime: string | null;
  preferredDeliveryWindowStart: string | null;
  preferredDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
  routeCode: string | null;
  routeStopOrder: number | null;
  hasStoreKey: boolean | null;
  paymentTermCode: PaymentTermCode | null;
  internalDeliveryInstructions: string | null;
  restDays: number[];
}
