import { CanDeactivateFn } from '@angular/router';

export const productionDraftGuard: CanDeactivateFn<{ canLeave(): boolean }> = component => component.canLeave();
