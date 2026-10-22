import { provideHttpClient, withFetch } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // fetch backend: unsubscribing (switchMap) aborts the underlying request via AbortController.
    provideHttpClient(withFetch()),
  ],
};
