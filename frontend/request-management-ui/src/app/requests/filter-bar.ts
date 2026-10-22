import { Component, inject } from '@angular/core';
import { RequestListStore } from './request-list.store';
import { REQUEST_PRIORITIES, REQUEST_STATUSES, RequestFilters } from './request.models';
import { statusLabel } from './status-label';

@Component({
  selector: 'app-filter-bar',
  template: `
    <form
      class="filter-bar"
      role="search"
      aria-label="Filter requests"
      (submit)="$event.preventDefault()"
    >
      <div class="field field-wide">
        <label for="f-q">Search title or organization</label>
        <input
          id="f-q"
          type="search"
          maxlength="100"
          placeholder="e.g. payment"
          autocomplete="off"
          [value]="store.searchText()"
          (input)="store.setSearch(value($event))"
        />
      </div>
      <div class="field">
        <label for="f-status">Status</label>
        <select id="f-status" (change)="set('status', value($event))">
          <option value="" [selected]="store.criteria().status === ''">Any</option>
          @for (s of statuses; track s) {
            <option [value]="s" [selected]="store.criteria().status === s">{{ label(s) }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label for="f-priority">Priority</label>
        <select id="f-priority" (change)="set('priority', value($event))">
          <option value="" [selected]="store.criteria().priority === ''">Any</option>
          @for (p of priorities; track p) {
            <option [value]="p" [selected]="store.criteria().priority === p">{{ p }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label for="f-assignee">Assignee (exact)</label>
        <input
          id="f-assignee"
          maxlength="100"
          placeholder="agent-07"
          [value]="store.criteria().assignedTo"
          (change)="set('assignedTo', value($event))"
        />
      </div>
      <div class="field">
        <label for="f-org">Organization (exact)</label>
        <input
          id="f-org"
          maxlength="150"
          placeholder="Blue Harbor Logistics"
          [value]="store.criteria().organizationName"
          (change)="set('organizationName', value($event))"
        />
      </div>
      <div class="field">
        <label for="f-from">Created on/after (UTC)</label>
        <input
          id="f-from"
          type="date"
          [value]="store.criteria().createdFrom"
          (change)="set('createdFrom', value($event))"
        />
      </div>
      <div class="field">
        <label for="f-before">Created before (UTC)</label>
        <input
          id="f-before"
          type="date"
          [value]="store.criteria().createdBefore"
          (change)="set('createdBefore', value($event))"
        />
      </div>
      <div class="field field-action">
        <button
          type="button"
          class="secondary"
          (click)="store.clearFilters()"
          [disabled]="!store.hasActiveFilters()"
        >
          Clear filters
        </button>
      </div>
    </form>
  `,
})
export class FilterBar {
  protected readonly store = inject(RequestListStore);
  protected readonly statuses = REQUEST_STATUSES;
  protected readonly priorities = REQUEST_PRIORITIES;
  protected readonly label = statusLabel;

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected set<K extends keyof RequestFilters>(key: K, value: string): void {
    this.store.updateFilters({ [key]: value.trim() } as Partial<RequestFilters>);
  }
}
