# request-management-ui

Angular 22 front end of the Request Management take-home. See the [root README](../../README.md) for setup, architecture and tests.

```bash
npm ci
npm start                    # http://localhost:4200 (API expected on http://localhost:5080)
npm run build                # production build -> dist/
npm test -- --watch=false    # Vitest unit tests
```

Where to look: `src/app/requests/request-list.store.ts` (RxJS query pipeline: debounce → distinctUntilChanged → switchMap),
`requests-api.service.ts` (all HTTP calls), `status-dialog.ts` (409 conflict UX), `bulk-update-panel.ts` (per-item outcomes).
