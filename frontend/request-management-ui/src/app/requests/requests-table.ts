import { Component, input, output } from '@angular/core';
import { UtcDatePipe } from '../core/utc-date.pipe';
import { RequestDto, SortDirection, SortField } from './request.models';
import { statusLabel } from './status-label';

interface Column {
  label: string;
  sortField?: SortField;
}

@Component({
  selector: 'app-requests-table',
  imports: [UtcDatePipe],
  template: `
    <div class="table-wrap" [class.is-loading]="loading()">
      <table>
        <caption class="visually-hidden">
          Requests, sorted by
          {{
            sortBy()
          }}
          {{
            sortDirection()
          }}
        </caption>
        <thead>
          <tr>
            <th scope="col" class="col-select">
              <input
                type="checkbox"
                aria-label="Select all rows on this page"
                [checked]="allSelected()"
                [indeterminate]="someSelected()"
                (change)="toggleAll.emit(checked($event))"
              />
            </th>
            @for (col of columns; track col.label) {
              <th scope="col" [attr.aria-sort]="ariaSort(col)">
                @if (col.sortField; as field) {
                  <button type="button" class="sort-button" (click)="sort.emit(field)">
                    {{ col.label }} <span aria-hidden="true">{{ sortIndicator(field) }}</span>
                  </button>
                } @else {
                  {{ col.label }}
                }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @for (r of items(); track r.id) {
            <tr [class.selected]="selectedIds().has(r.id)">
              <td class="col-select">
                <input
                  type="checkbox"
                  [attr.aria-label]="'Select request ' + r.id"
                  [checked]="selectedIds().has(r.id)"
                  (change)="toggle.emit({ request: r, selected: checked($event) })"
                />
              </td>
              <td>
                <button type="button" class="link-button" (click)="open.emit(r)">
                  {{ r.title }}
                </button>
                <span class="muted id-hint">#{{ r.id }}</span>
              </td>
              <td>{{ r.organizationName }}</td>
              <td>
                <span class="badge" [attr.data-status]="r.status">{{ label(r.status) }}</span>
              </td>
              <td>
                <span class="priority" [attr.data-priority]="r.priority">{{ r.priority }}</span>
              </td>
              <td>{{ r.assignedTo ?? '—' }}</td>
              <td class="nowrap">{{ r.createdAt | utcDate }}</td>
              <td class="nowrap">{{ r.updatedAt | utcDate }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class RequestsTable {
  readonly items = input.required<RequestDto[]>();
  readonly loading = input(false);
  readonly sortBy = input.required<SortField>();
  readonly sortDirection = input.required<SortDirection>();
  readonly selectedIds = input.required<ReadonlySet<number>>();

  readonly sort = output<SortField>();
  readonly open = output<RequestDto>();
  readonly toggle = output<{ request: RequestDto; selected: boolean }>();
  readonly toggleAll = output<boolean>();

  protected readonly label = statusLabel;
  protected readonly columns: Column[] = [
    { label: 'Title', sortField: 'title' },
    { label: 'Organization', sortField: 'organizationName' },
    { label: 'Status' },
    { label: 'Priority', sortField: 'priority' },
    { label: 'Assignee' },
    { label: 'Created', sortField: 'createdAt' },
    { label: 'Updated', sortField: 'updatedAt' },
  ];

  protected allSelected(): boolean {
    return this.items().length > 0 && this.items().every((r) => this.selectedIds().has(r.id));
  }

  protected someSelected(): boolean {
    return !this.allSelected() && this.items().some((r) => this.selectedIds().has(r.id));
  }

  protected ariaSort(col: Column): string | null {
    if (!col.sortField) {
      return null;
    }
    if (col.sortField !== this.sortBy()) {
      return 'none';
    }
    return this.sortDirection() === 'asc' ? 'ascending' : 'descending';
  }

  protected sortIndicator(field: SortField): string {
    if (field !== this.sortBy()) {
      return '↕';
    }
    return this.sortDirection() === 'asc' ? '▲' : '▼';
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }
}
