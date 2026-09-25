// File: TitleClaimTracker/ClientApp/src/main.ts
import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AppShellComponent } from './app/app-shell.component';
import { routes } from './app/app.routes';
import { sessionInterceptor } from './app/core/interceptors/session.interceptor';

bootstrapApplication(AppShellComponent, {
  providers: [
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([sessionInterceptor]),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    ),
  ],
}).catch((error: unknown) => console.error(error));
