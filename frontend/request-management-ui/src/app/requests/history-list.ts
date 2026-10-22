import { Component, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { UiError, toUiError } from '../core/http-error';
import { UtcDatePipe } from '../core/utc-date.pipe';
import { RequestsApiService } from './requests-api.service';
import { RequestHistoryEntry } from './request.models';
import { statusLabel } from './status-label';

interface HistoryState {
  loading: boolean;
  entries: RequestHistoryEntry[];
  error: UiError | null;
}

@Component({
  selector: 'app-history-list',
  imports: [UtcDatePipe],
  template: `
    <section aria-labelledby="history-heading">
      <h3 id="history-heading">Status history</h3>
      @if (state().loading) {
        <p class="muted" aria-live="polite">Loading history…</p>
      } @else if (state().error; as e) {
        <p class="inline-error" role="alert">{{ e.message }}</p>
      } @else if (state().entries.length === 0) {
        <p class="muted">No status changes yet.</p>
      } @else {
        <table class="history-table">
          <thead>
            <tr>
              <th scope="col">Changed at</th>
              <th scope="col">By</th>
              <th scope="col">From</th>
              <th scope="col">To</th>
            </tr>
          </thead>
          <tbody>
            @for (h of state().entries; track h.id) {
              <tr>
                <td class="nowrap">{{ h.changedAt | utcDate }}</td>
                <td>{{ h.changedBy }}</td>
                <td>
                  <span class="badge" [attr.data-status]="h.previousStatus">{{
                    label(h.previousStatus)
                  }}</span>
                </td>
                <td>
                  <span class="badge" [attr.data-status]="h.newStatus">{{
                    label(h.newStatus)
                  }}</span>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
})
export class HistoryList {
  private readonly api = inject(RequestsApiService);

  readonly requestId = input.required<number>();
  /** The request's rowVersion: history is re-fetched whenever the request changes. */
  readonly version = input.required<string>();

  protected readonly label = statusLabel;
  protected readonly state = toSignal(
    toObservable(this.version).pipe(
      switchMap(() =>
        this.api.history(this.requestId()).pipe(
          map((entries): HistoryState => ({ loading: false, entries, error: null })),
          catchError((error: unknown) =>
            of<HistoryState>({ loading: false, entries: [], error: toUiError(error) }),
          ),
          startWith<HistoryState>({ loading: true, entries: [], error: null }),
        ),
      ),
    ),
    { initialValue: { loading: true, entries: [], error: null } },
  );
}
