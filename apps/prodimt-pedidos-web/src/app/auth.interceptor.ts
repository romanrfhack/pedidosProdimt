import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authTokenInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.url.includes('/api/auth/')) {
    return next(request);
  }

  const auth = inject(AuthService);
  const router = inject(Router);
  const accessToken = auth.getAccessToken();

  if (!accessToken) {
    return next(request);
  }

  return next(request.clone({
    setHeaders: {
      Authorization: `Bearer ${accessToken}`
    }
  })).pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse && error.status === 401 && auth.isAdminSession()
        && auth.getAccessToken() === accessToken) {
      auth.logout();
      void router.navigate(['/admin/login'], { queryParams: { sessionExpired: '1' } });
    }
    return throwError(() => error);
  }));
};
