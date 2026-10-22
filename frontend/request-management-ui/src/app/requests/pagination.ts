import { DecimalPipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { PAGE_SIZES } from './request.models';

@Component({
  selector: 'app-pagination',
  imports: [DecimalPipe],
  template: `
    <nav class="pagination" aria-label="Pagination">
      <span class="muted" aria-live="polite">
        @if (totalCount() > 0) {
          {{ firstItem() | number }}–{{ lastItem() | number }} of {{ totalCount() | number }} · page
          {{ page() | number }} of {{ totalPages() | number }}
        } @else {
          No results
        }
      </span>
      <span class="pagination-controls">
        <button
          type="button"
          class="secondary"
          (click)="pageChange.emit(1)"
          [disabled]="page() <= 1"
        >
          « First
        </button>
        <button
          type="button"
          class="secondary"
          (click)="pageChange.emit(page() - 1)"
          [disabled]="page() <= 1"
        >
          ‹ Prev
        </button>
        <button
          type="button"
          class="secondary"
          (click)="pageChange.emit(page() + 1)"
          [disabled]="page() >= totalPages()"
        >
          Next ›
        </button>
        <button
          type="button"
          class="secondary"
          (click)="pageChange.emit(totalPages())"
          [disabled]="page() >= totalPages()"
        >
          Last »
        </button>
        <label for="page-size">Rows</label>
        <select id="page-size" (change)="onPageSize($event)">
          @for (size of sizes; track size) {
            <option [value]="size" [selected]="size === pageSize()">{{ size }}</option>
          }
        </select>
      </span>
    </nav>
  `,
})
export class Pagination {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly totalPages = input.required<number>();

  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  protected readonly sizes = PAGE_SIZES;
  protected readonly firstItem = computed(() => (this.page() - 1) * this.pageSize() + 1);
  protected readonly lastItem = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()),
  );

  protected onPageSize(event: Event): void {
    this.pageSizeChange.emit(Number((event.target as HTMLSelectElement).value));
  }
}
