import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  BehaviorSubject,
  Observable,
  Subject,
  catchError,
  combineLatest,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  scan,
  startWith,
  switchMap,
} from 'rxjs';
import { UiError, toUiError } from '../core/http-error';
import { RequestsApiService } from './requests-api.service';
import {
  PagedResult,
  RequestDto,
  RequestFilters,
  RequestListQuery,
  RequestSummary,
  SortField,
} from './request.models';

export const SEARCH_DEBOUNCE_MS = 300;

interface Criteria extends RequestFilters {
  sortBy: SortField;
  sortDirection: 'asc' | 'desc';
  pageSize: number;
}

export const EMPTY_FILTERS: RequestFilters = {
  status: '',
  priority: '',
  assignedTo: '',
  organizationName: '',
  createdFrom: '',
  createdBefore: '',
};

const DEFAULT_CRITERIA: Criteria = {
  ...EMPTY_FILTERS,
  sortBy: 'createdAt',
  sortDirection: 'desc',
  pageSize: 25,
};

export interface ListState {
  query: RequestListQuery | null;
  result: PagedResult<RequestDto> | null;
  loading: boolean;
  error: UiError | null;
}

type ListEvent =
  | { type: 'loading'; query: RequestListQuery }
  | { type: 'loaded'; query: RequestListQuery; result: PagedResult<RequestDto> }
  | { type: 'failed'; query: RequestListQuery; error: UiError };

/**
 * State of the requests page. Every user action becomes a stream event; one pipeline turns them into HTTP calls:
 *
 *   search text ─debounce 300ms─distinctUntilChanged─┐
 *   filters / sort / page size ──────────────────────┴─► filters ─switchMap─► page (reset to 1 on every filter change)
 *                                                                                 └─ + reload ─switchMap─► GET /api/requests
 *
 * switchMap unsubscribes from the previous HTTP observable when a newer query arrives, which aborts the in-flight
 * request, so a slow, stale response can never overwrite the results of a newer query.
 * Provided per page component; toSignal() ties every subscription to that component's lifetime.
 */
@Injectable()
export class RequestListStore {
  private readonly api = inject(RequestsApiService);

  private readonly searchInput$ = new Subject<string>();
  private readonly criteria$ = new BehaviorSubject<Criteria>(DEFAULT_CRITERIA);
  private readonly pageChange$ = new Subject<number>();
  private readonly reload$ = new BehaviorSubject<void>(undefined);
  private readonly summaryReload$ = new BehaviorSubject<void>(undefined);

  /** Raw text as typed (bound to the input); the server only sees its debounced value. */
  readonly searchText = signal('');
  readonly criteria = toSignal(this.criteria$, { requireSync: true });

  private readonly debouncedSearch$ = this.searchInput$.pipe(
    map((text) => text.trim()),
    debounceTime(SEARCH_DEBOUNCE_MS),
    startWith(''),
    distinctUntilChanged(),
  );

  private readonly query$: Observable<RequestListQuery> = combineLatest([
    this.debouncedSearch$,
    this.criteria$,
  ]).pipe(
    // A new filter/sort/page-size combination always starts at page 1; a page change alone keeps the filters.
    switchMap(([q, criteria]) =>
      this.pageChange$.pipe(
        startWith(1),
        map((page) => ({ ...criteria, q, page })),
      ),
    ),
  );

  private readonly state$ = combineLatest([this.query$, this.reload$]).pipe(
    switchMap(([query]) =>
      this.api.list(query).pipe(
        map((result): ListEvent => ({ type: 'loaded', query, result })),
        catchError((error: unknown) =>
          of<ListEvent>({ type: 'failed', query, error: toUiError(error) }),
        ),
        startWith<ListEvent>({ type: 'loading', query }),
      ),
    ),
    scan(reduceListState, { query: null, result: null, loading: true, error: null } as ListState),
  );

  readonly state = toSignal(this.state$, {
    initialValue: { query: null, result: null, loading: true, error: null } satisfies ListState,
  });

  readonly items = computed(() => this.state().result?.items ?? []);
  readonly hasActiveFilters = computed(() => {
    const c = this.criteria();
    return (
      this.searchText().trim() !== '' ||
      (Object.keys(EMPTY_FILTERS) as (keyof RequestFilters)[]).some((k) => c[k] !== '')
    );
  });

  readonly summary = toSignal(
    this.summaryReload$.pipe(
      switchMap(() =>
        this.api.summary().pipe(
          map((summary) => ({ summary, error: null as UiError | null })),
          catchError((error: unknown) =>
            of({ summary: null as RequestSummary | null, error: toUiError(error) }),
          ),
        ),
      ),
    ),
  );

  setSearch(text: string): void {
    this.searchText.set(text);
    this.searchInput$.next(text);
  }

  updateFilters(changes: Partial<RequestFilters>): void {
    this.criteria$.next({ ...this.criteria$.value, ...changes });
  }

  setPageSize(pageSize: number): void {
    this.criteria$.next({ ...this.criteria$.value, pageSize });
  }

  /** Clicking the active column flips the direction; a new column starts descending. */
  toggleSort(field: SortField): void {
    const current = this.criteria$.value;
    const sortDirection =
      current.sortBy === field && current.sortDirection === 'desc' ? 'asc' : 'desc';
    this.criteria$.next({ ...current, sortBy: field, sortDirection });
  }

  goToPage(page: number): void {
    this.pageChange$.next(page);
  }

  clearFilters(): void {
    this.setSearch('');
    this.criteria$.next({ ...this.criteria$.value, ...EMPTY_FILTERS });
  }

  /** Re-runs the current query (same filters, sort and page), e.g. after a successful status change. */
  reload(): void {
    this.reload$.next();
  }

  reloadSummary(): void {
    this.summaryReload$.next();
  }
}

function reduceListState(state: ListState, event: ListEvent): ListState {
  switch (event.type) {
    case 'loading':
      // Keep showing the previous rows while the next page loads; the template dims them.
      return { ...state, query: event.query, loading: true, error: null };
    case 'loaded':
      return { query: event.query, result: event.result, loading: false, error: null };
    case 'failed':
      return { query: event.query, result: null, loading: false, error: event.error };
  }
}
