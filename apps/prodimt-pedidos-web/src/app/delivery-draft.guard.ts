import { CanDeactivateFn } from '@angular/router';
export const deliveryDraftGuard: CanDeactivateFn<{ canLeave(): boolean }> = component => component.canLeave();
