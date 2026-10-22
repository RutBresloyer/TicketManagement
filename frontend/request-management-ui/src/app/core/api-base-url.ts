import { InjectionToken } from '@angular/core';

/** Origin of the API. The API's CORS policy allows exactly http://localhost:4200 in Development. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  factory: () => 'http://localhost:5080',
});
