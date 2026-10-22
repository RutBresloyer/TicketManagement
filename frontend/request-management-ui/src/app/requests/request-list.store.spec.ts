import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RequestListStore, SEARCH_DEBOUNCE_MS } from './request-list.store';
import { page, requestDto } from './test-data';

describe('RequestListStore', () => {
  let store: RequestListStore;
  let http: HttpTestingController;

  const isList = (r: { url: string; method: string }) =>
    r.method === 'GET' && r.url.endsWith('/api/requests');
  const expectList = (): TestRequest => http.expectOne(isList);

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [RequestListStore, provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(RequestListStore);
    http.match((r) => r.url.endsWith('/summary')).forEach((r) => r.flush(null));
    expectList().flush(page([requestDto()])); // initial load: page 1, default sort, no filters
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('debounces typing and sends only the final search term', () => {
    store.setSearch('p');
    vi.advanceTimersByTime(100);
    store.setSearch('pa');
    vi.advanceTimersByTime(100);
    store.setSearch('pay');
    http.expectNone(isList); // nothing is sent while the user is still typing

    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    const request = expectList();
    expect(request.request.params.get('q')).toBe('pay');
    request.flush(page([]));

    store.setSearch('pay  '); // same term after trimming: distinctUntilChanged suppresses a duplicate call
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    http.expectNone(isList);
  });

  it('cancels an in-flight request when a newer query starts, so stale results never overwrite fresh ones', () => {
    store.setSearch('payment');
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    const slow = expectList();

    store.updateFilters({ status: 'New' });
    const fresh = expectList();

    expect(slow.cancelled).toBe(true); // switchMap unsubscribed -> HTTP request aborted
    expect(fresh.request.params.get('q')).toBe('payment');
    expect(fresh.request.params.get('status')).toBe('New');

    fresh.flush(page([requestDto({ id: 42 })]));
    expect(store.items().map((r) => r.id)).toEqual([42]);
    expect(store.state().loading).toBe(false);
  });

  it('resets to page 1 when filters or sort change, and keeps filters when only the page changes', () => {
    store.goToPage(3);
    let request = expectList();
    expect(request.request.params.get('page')).toBe('3');
    request.flush(page([], 3, 25, 100));

    store.updateFilters({ priority: 'High' });
    request = expectList();
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('priority')).toBe('High');
    request.flush(page([], 1, 25, 100));

    store.goToPage(2);
    request = expectList();
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('priority')).toBe('High');
    request.flush(page([], 2, 25, 100));

    store.reload(); // e.g. after a successful status change: same filters, same page
    request = expectList();
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('priority')).toBe('High');
    request.flush(page([], 2, 25, 100));

    store.toggleSort('title');
    request = expectList();
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('sortBy')).toBe('title');
    expect(request.request.params.get('sortDirection')).toBe('desc');
    request.flush(page([]));
  });

  it('sends date filters as UTC bounds: inclusive from, exclusive before', () => {
    store.updateFilters({ createdFrom: '2026-01-01', createdBefore: '2026-02-01' });
    const request = expectList();
    expect(request.request.params.get('createdFrom')).toBe('2026-01-01T00:00:00Z');
    expect(request.request.params.get('createdTo')).toBe('2026-02-01T00:00:00Z');
    request.flush(page([]));
  });

  it('exposes validation problems from the API as an error state', () => {
    store.updateFilters({ createdFrom: '2026-02-01', createdBefore: '2026-01-01' });
    expectList().flush(
      {
        title: 'One or more validation errors occurred.',
        status: 400,
        detail: "See 'errors' for the invalid fields.",
        code: 'ValidationFailed',
        errors: { createdFrom: ['createdFrom must be earlier than createdTo.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    const state = store.state();
    expect(state.loading).toBe(false);
    expect(state.error?.status).toBe(400);
    expect(state.error?.fieldErrors).toEqual([
      'createdFrom: createdFrom must be earlier than createdTo.',
    ]);
  });
});
