import { Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { UtcDatePipe } from '../core/utc-date.pipe';
import { UiError } from '../core/http-error';
import { RequestSummary } from './request.models';
import { statusLabel } from './status-label';

@Component({
  selector: 'app-summary-panel',
  imports: [DecimalPipe, UtcDatePipe],
  template: `
    <section class="summary" aria-labelledby="summary-heading">
      <h2 id="summary-heading" class="visually-hidden">Summary</h2>
      @if (error(); as e) {
        <p class="inline-error" role="alert">Summary unavailable: {{ e.message }}</p>
      } @else if (summary(); as s) {
        <div class="summary-card summary-total">
          <span class="summary-value">{{ s.totalCount | number }}</span>
          <span class="summary-label">Total requests</span>
        </div>
        <div class="summary-card">
          <span class="summary-label">By status</span>
          <ul class="summary-list">
            @for (item of s.byStatus; track item.status) {
              <li>
                <span class="badge" [attr.data-status]="item.status">{{ label(item.status) }}</span>
                {{ item.count | number }}
              </li>
            }
          </ul>
        </div>
        <div class="summary-card">
          <span class="summary-label">By priority</span>
          <ul class="summary-list">
            @for (item of s.byPriority; track item.priority) {
              <li>
                <span class="priority" [attr.data-priority]="item.priority">{{
                  item.priority
                }}</span>
                {{ item.count | number }}
              </li>
            }
          </ul>
        </div>
        <p class="muted summary-note">Computed {{ s.generatedAt | utcDate }} · cached up to 30 s</p>
      } @else {
        <p class="muted" aria-live="polite">Loading summary…</p>
      }
    </section>
  `,
})
export class SummaryPanel {
  readonly data = input.required<
    { summary: RequestSummary | null; error: UiError | null } | undefined
  >();
  protected readonly summary = computed(() => this.data()?.summary ?? null);
  protected readonly error = computed(() => this.data()?.error ?? null);
  protected readonly label = statusLabel;
}
