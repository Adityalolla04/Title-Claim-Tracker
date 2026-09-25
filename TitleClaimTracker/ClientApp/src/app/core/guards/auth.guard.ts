import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { catchError, map, of } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (_route, routeState) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.ensureSession().pipe(
    map(account => account
      ? true
      : router.createUrlTree(['/login'], { queryParams: { returnUrl: routeState.url } })),
    catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: routeState.url } }))),
  );
};
