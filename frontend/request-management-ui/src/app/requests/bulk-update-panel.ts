import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActorService } from '../core/actor.service';
import { UiError, toUiError } from '../core/http-error';
import { RequestsApiService } from './requests-api.service';
import {
  BulkStatusUpdateResponse,
  MAX_BULK_ITEMS,
  REQUEST_STATUSES,
  RequestDto,
  RequestStatus,
} from './request.models';
import { statusLabel } from './status-label';

/**
 * Bulk status change for the rows selected on the current page (max 100 = max page size). Each item is sent with the
 * rowVersion as it was read into the table; the API applies each item independently (partial success).
 */
@Component({
  selector: 'app-bulk-update-panel',
  template: `
    <section class="bulk-panel" aria-labelledby="bulk-heading">
      <h2 id="bulk-heading" class="bulk-title">Bulk status change</h2>
      <span>{{ selected().length }} selected on this page</span>
      <label for="bulk-status">New status</label>
      <select id="bulk-status" (change)="onStatus($event)">
        @for (s of statuses; track s) {
          <option [value]="s" [selected]="s === status()">{{ label(s) }}</option>
        }
      </select>
      <button type="button" class="primary" (click)="apply()" [disabled]="!canApply()">
        {{ submitting() ? 'Applying…' : 'Apply to selected' }}
      </button>
      <button
        type="button"
        class="secondary"
        (click)="clearSelection.emit()"
        [disabled]="selected().length === 0"
      >
        Clear selection
      </button>
      @if (selected().length > maxItems) {
        <p class="inline-error">At most {{ maxItems }} requests per bulk change.</p>
      }
    </section>

    @if (error(); as e) {
      <div class="callout callout-error" role="alert">
        {{ e.message }}
        @for (f of e.fieldErrors; track f) {
          <div>{{ f }}</div>
        }
      </div>
    }

    @if (result(); as r) {
      <section
        class="callout"
        [class.callout-warning]="r.failed > 0"
        role="status"
        aria-labelledby="bulk-result-heading"
      >
        <h3 id="bulk-result-heading">
          Bulk result: {{ r.succeeded }} succeeded, {{ r.failed }} failed
        </h3>
        <table class="outcome-table">
          <thead>
            <tr>
              <th scope="col">Request</th>
              <th scope="col">Outcome</th>
              <th scope="col">Details</th>
            </tr>
          </thead>
          <tbody>
            @for (item of r.results; track item.id) {
              <tr [attr.data-outcome]="item.outcome">
                <td>#{{ item.id }}</td>
                <td>{{ item.outcome }}</td>
                <td>
                  {{ item.error?.message ?? (item.status ? 'Now ' + label(item.status) : '') }}
                </td>
              </tr>
            }
          </tbody>
        </table>
        @if (r.failed > 0) {
          <p class="muted">
            The list has been refreshed, so its rows now carry current versions. Failed requests
            that are on this page can be selected again to retry deliberately.
          </p>
          <button type="button" class="secondary" (click)="reselectFailed.emit(failedIds())">
            Select failed requests again
          </button>
        }
        <button type="button" class="secondary" (click)="result.set(null)">Dismiss</button>
      </section>
    }
  `,
})
export class BulkUpdatePanel {
  private readonly api = inject(RequestsApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly actor = inject(ActorService);

  readonly selected = input.required<RequestDto[]>();
  readonly completed = output<BulkStatusUpdateResponse>();
  readonly clearSelection = output<void>();
  readonly reselectFailed = output<number[]>();

  protected readonly statuses = REQUEST_STATUSES;
  protected readonly maxItems = MAX_BULK_ITEMS;
  protected readonly label = statusLabel;
  protected readonly status = signal<RequestStatus>('InProgress');
  protected readonly submitting = signal(false);
  protected readonly result = signal<BulkStatusUpdateResponse | null>(null);
  protected readonly error = signal<UiError | null>(null);
  protected readonly failedIds = computed(() =>
    (this.result()?.results ?? []).filter((r) => r.error !== null).map((r) => r.id),
  );
  protected readonly canApply = computed(
    () =>
      this.selected().length > 0 &&
      this.selected().length <= MAX_BULK_ITEMS &&
      !this.submitting() &&
      this.actor.isValid(),
  );

  protected onStatus(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value as RequestStatus);
  }

  protected apply(): void {
    const items = this.selected().map((r) => ({
      id: r.id,
      status: this.status(),
      expectedVersion: r.rowVersion,
    }));
    this.submitting.set(true);
    this.error.set(null);
    this.api
      .bulkUpdateStatus(items, this.actor.actorId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.submitting.set(false);
          this.result.set(response);
          this.completed.emit(response);
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          this.error.set(toUiError(err));
        },
      });
  }
}
