import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { NotificationService } from '../core/notification.service';
import { BulkUpdatePanel } from './bulk-update-panel';
import { FilterBar } from './filter-bar';
import { Pagination } from './pagination';
import { RequestListStore } from './request-list.store';
import { BulkStatusUpdateResponse, RequestDto } from './request.models';
import { RequestsTable } from './requests-table';
import { StatusDialog } from './status-dialog';
import { SummaryPanel } from './summary-panel';

@Component({
  selector: 'app-requests-page',
  imports: [SummaryPanel, FilterBar, RequestsTable, Pagination, StatusDialog, BulkUpdatePanel],
  providers: [RequestListStore],
  templateUrl: './requests-page.html',
})
export class RequestsPage {
  protected readonly store = inject(RequestListStore);
  private readonly notifications = inject(NotificationService);

  protected readonly state = this.store.state;
  protected readonly openRequest = signal<RequestDto | null>(null);

  /** Selection = rows of the currently displayed page, captured with the rowVersion they were read with. */
  private readonly selection = signal<ReadonlyMap<number, RequestDto>>(new Map());
  protected readonly selectedIds = computed(
    () => new Set(this.selection().keys()) as ReadonlySet<number>,
  );
  protected readonly selectedRows = computed(() => [...this.selection().values()]);

  constructor() {
    // A different query (filters, sort, page) shows different rows: drop the selection so that a bulk change can only
    // ever target rows the user can see. A plain reload of the same query refreshes the selected rows' versions instead.
    let previousKey = '';
    effect(() => {
      const { query, result } = this.state();
      const key = JSON.stringify(query);
      untracked(() => {
        if (key !== previousKey) {
          previousKey = key;
          this.selection.set(new Map());
          return;
        }
        const current = this.selection();
        if (result && current.size > 0) {
          // Same query re-read: keep the selected IDs but take the rows (and rowVersions) just displayed.
          this.selection.set(
            new Map(result.items.filter((r) => current.has(r.id)).map((r) => [r.id, r])),
          );
        }
      });
    });
  }

  protected toggle(request: RequestDto, selected: boolean): void {
    this.selection.update((current) => {
      const next = new Map(current);
      if (selected) {
        next.set(request.id, request);
      } else {
        next.delete(request.id);
      }
      return next;
    });
  }

  protected toggleAll(selected: boolean): void {
    this.selection.set(selected ? new Map(this.store.items().map((r) => [r.id, r])) : new Map());
  }

  protected reselect(ids: number[]): void {
    const wanted = new Set(ids);
    const rows = this.store.items().filter((r) => wanted.has(r.id));
    this.selection.set(new Map(rows.map((r) => [r.id, r])));
    if (rows.length < ids.length) {
      this.notifications.show(
        'warning',
        `${ids.length - rows.length} failed request(s) are no longer on this page.`,
      );
    }
  }

  protected onSaved(updated: RequestDto): void {
    this.notifications.show('success', `Request #${updated.id} is now ${updated.status}.`);
    this.store.reload();
    this.store.reloadSummary();
  }

  protected onBulkCompleted(response: BulkStatusUpdateResponse): void {
    if (response.failed === 0) {
      this.notifications.show(
        'success',
        `Bulk change applied to all ${response.succeeded} request(s).`,
      );
    } else {
      this.notifications.show(
        'warning',
        `Partial success: ${response.succeeded} succeeded, ${response.failed} failed. See details.`,
      );
    }
    this.selection.set(new Map());
    if (response.results.some((r) => r.outcome === 'Updated')) {
      this.store.reload();
      this.store.reloadSummary();
    }
  }
}
