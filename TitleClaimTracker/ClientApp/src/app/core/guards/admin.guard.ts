import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { catchError, map, of } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const adminGuard: CanActivateFn = (_route, routeState) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.ensureSession().pipe(
    map(account => {
      if (!account) {
        return router.createUrlTree(['/login'], { queryParams: { returnUrl: routeState.url } });
      }

      return account.roles.includes('Admin')
        ? true
        : router.createUrlTree(['/forbidden']);
    }),
    catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: routeState.url } }))),
  );
};
