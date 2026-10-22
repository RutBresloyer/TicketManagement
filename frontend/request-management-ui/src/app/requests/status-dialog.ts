import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActorService } from '../core/actor.service';
import { UiError, toUiError } from '../core/http-error';
import { UtcDatePipe } from '../core/utc-date.pipe';
import { HistoryList } from './history-list';
import { RequestsApiService } from './requests-api.service';
import { RequestDto, RequestStatus } from './request.models';
import { statusLabel } from './status-label';

/**
 * Details + status change for one request. The version sent is the one this dialog last read; on 409 the user is told
 * that someone else changed the request and must explicitly refresh before trying again. Nothing is retried silently.
 */
@Component({
  selector: 'app-status-dialog',
  imports: [HistoryList, UtcDatePipe],
  template: `
    <dialog #dialog class="dialog" aria-labelledby="dialog-title" (close)="closed.emit()">
      <header class="dialog-header">
        <h2 id="dialog-title">Request #{{ current().id }}</h2>
        <button type="button" class="icon-button" aria-label="Close" (click)="close()">✕</button>
      </header>

      <dl class="details">
        <dt>Title</dt>
        <dd>{{ current().title }}</dd>
        <dt>Organization</dt>
        <dd>{{ current().organizationName }}</dd>
        <dt>Status</dt>
        <dd>
          <span class="badge" [attr.data-status]="current().status" data-testid="current-status">{{
            label(current().status)
          }}</span>
        </dd>
        <dt>Priority</dt>
        <dd>{{ current().priority }}</dd>
        <dt>Assignee</dt>
        <dd>{{ current().assignedTo ?? 'Unassigned' }}</dd>
        <dt>Created</dt>
        <dd>{{ current().createdAt | utcDate }}</dd>
        <dt>Updated</dt>
        <dd>{{ current().updatedAt | utcDate }}</dd>
      </dl>

      <section class="status-form" aria-labelledby="change-heading">
        <h3 id="change-heading">Change status</h3>
        @if (current().allowedNextStatuses.length === 0) {
          <p class="muted">
            '{{ label(current().status) }}' is final; no further changes are allowed.
          </p>
        } @else {
          <fieldset [disabled]="saving() || conflict()">
            <legend class="visually-hidden">Next status</legend>
            @for (s of current().allowedNextStatuses; track s) {
              <label class="radio">
                <input
                  type="radio"
                  name="next-status"
                  [value]="s"
                  [checked]="selected() === s"
                  (change)="selected.set(s)"
                />
                {{ label(s) }}
              </label>
            }
          </fieldset>
          <button type="button" class="primary" (click)="save()" [disabled]="!canSave()">
            {{ saving() ? 'Saving…' : 'Save' }}
          </button>
          @if (!actor.isValid()) {
            <p class="inline-error">Set a valid demo actor in the header first.</p>
          }
        }

        @if (conflict()) {
          <div class="callout callout-warning" role="alert" data-testid="conflict">
            <p>
              <strong>Someone else changed this request after you opened it.</strong> Your change
              was <em>not</em> saved. Refresh to see the current status, then decide whether to
              change it again.
            </p>
            <div>
              <button type="button" class="primary" (click)="refresh()" [disabled]="refreshing()">
                {{ refreshing() ? 'Refreshing…' : 'Refresh' }}
              </button>
            </div>
          </div>
        }
        @if (error(); as e) {
          <div class="callout callout-error" role="alert" data-testid="error">
            {{ e.message }}
            @for (f of e.fieldErrors; track f) {
              <div>{{ f }}</div>
            }
          </div>
        }
      </section>

      <app-history-list [requestId]="current().id" [version]="current().rowVersion" />
    </dialog>
  `,
})
export class StatusDialog {
  private readonly api = inject(RequestsApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly actor = inject(ActorService);

  readonly request = input.required<RequestDto>();
  /** Emitted after a successful change, with the updated request (and its new rowVersion). */
  readonly saved = output<RequestDto>();
  readonly closed = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly refreshed = signal<RequestDto | null>(null);

  /** The version of the request this dialog works with: the input, until the user saves or refreshes. */
  protected readonly current = computed(() => this.refreshed() ?? this.request());
  protected readonly selected = signal<RequestStatus | null>(null);
  protected readonly saving = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly conflict = signal(false);
  protected readonly error = signal<UiError | null>(null);
  protected readonly canSave = computed(
    () => this.selected() !== null && !this.saving() && !this.conflict() && this.actor.isValid(),
  );
  protected readonly label = statusLabel;

  constructor() {
    afterNextRender(() => {
      const element = this.dialog().nativeElement;
      if (typeof element.showModal === 'function') {
        element.showModal();
      } else {
        element.setAttribute('open', ''); // environments without <dialog> support (e.g. older jsdom)
      }
    });
  }

  protected save(): void {
    const status = this.selected();
    if (status === null) {
      return;
    }
    const request = this.current();
    this.saving.set(true);
    this.error.set(null);

    this.api
      .updateStatus(
        request.id,
        { status, expectedVersion: request.rowVersion },
        this.actor.actorId(),
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.saving.set(false);
          this.selected.set(null);
          this.refreshed.set(updated);
          this.saved.emit(updated);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          const uiError = toUiError(err);
          if (uiError.status === 409) {
            this.conflict.set(true);
          } else {
            // 422 (forbidden transition), 400 (validation), 404, 500: show the server's explanation.
            this.error.set(uiError);
          }
        },
      });
  }

  /** Fetches the latest request and token. The user then chooses again; nothing is re-submitted automatically. */
  protected refresh(): void {
    this.refreshing.set(true);
    this.api
      .get(this.current().id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => {
          this.refreshing.set(false);
          this.refreshed.set(fresh);
          this.conflict.set(false);
          this.error.set(null);
          this.selected.set(null);
        },
        error: (err: unknown) => {
          this.refreshing.set(false);
          this.error.set(toUiError(err));
        },
      });
  }

  /** Esc and this button both end in the native 'close' event, which emits `closed` exactly once. */
  protected close(): void {
    const element = this.dialog().nativeElement;
    if (typeof element.close === 'function' && element.open) {
      element.close();
    } else {
      this.closed.emit();
    }
  }
}
