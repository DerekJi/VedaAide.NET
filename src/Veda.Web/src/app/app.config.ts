import { APP_INITIALIZER, ApplicationConfig, importProvidersFrom, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withViewTransitions } from '@angular/router';
import { HTTP_INTERCEPTORS, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import {
  MSAL_GUARD_CONFIG, MSAL_INSTANCE, MSAL_INTERCEPTOR_CONFIG,
  MsalBroadcastService, MsalGuard, MsalInterceptor, MsalModule, MsalService
} from '@azure/msal-angular';
import {
  IPublicClientApplication, InteractionType, LogLevel,
  PublicClientApplication
} from '@azure/msal-browser';
import { routes } from './app.routes';
import { environment } from '../environments/environment';

export function msalInstanceFactory(): IPublicClientApplication {
  return new PublicClientApplication({
    auth: {
      ...environment.msalConfig.auth,
      knownAuthorities: environment.msalConfig.auth.knownAuthorities ?? []
    },
    cache: { cacheLocation: 'localStorage', storeAuthStateInCookie: false },
    system: { loggerOptions: { logLevel: LogLevel.Warning } }
  });
}

export function msalInitializer(msalInstance: IPublicClientApplication): () => Promise<void> {
  return () =>
    msalInstance.initialize()
      .then(() => msalInstance.handleRedirectPromise())
      .then((result) => {
        if (result?.account) {
          msalInstance.setActiveAccount(result.account);
        } else {
          const accounts = msalInstance.getAllAccounts();
          if (accounts.length > 0) {
            msalInstance.setActiveAccount(accounts[0]);
          }
        }
      })
      .catch(() => {});
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withViewTransitions()),
    provideHttpClient(withInterceptorsFromDi()),
    provideAnimationsAsync(),
    importProvidersFrom(MsalModule),
    { provide: MSAL_INSTANCE, useFactory: msalInstanceFactory },
    {
      provide: APP_INITIALIZER,
      useFactory: msalInitializer,
      deps: [MSAL_INSTANCE],
      multi: true
    },
    MsalService,
    MsalGuard,
    MsalBroadcastService,
    {
      provide: MSAL_GUARD_CONFIG,
      useValue: {
        interactionType: InteractionType.Redirect,
        authRequest: { scopes: [environment.apiScope] }
      }
    },
    {
      provide: MSAL_INTERCEPTOR_CONFIG,
      useValue: {
        interactionType: InteractionType.Redirect,
        protectedResourceMap: new Map<string, string[]>([
          ['/api', [environment.apiScope]]
        ])
      }
    },
    { provide: HTTP_INTERCEPTORS, useClass: MsalInterceptor, multi: true }
  ]
};
