import { Injectable, signal } from '@angular/core';

export type NotificationKind = 'success' | 'warning' | 'error';

export interface AppNotification {
  id: number;
  kind: NotificationKind;
  text: string;
}

/** Transient status messages, announced through an aria-live region in the app shell. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;
  readonly notifications = signal<AppNotification[]>([]);

  show(kind: NotificationKind, text: string, durationMs = 6000): void {
    const notification = { id: this.nextId++, kind, text };
    this.notifications.update((list) => [...list, notification]);
    setTimeout(() => this.dismiss(notification.id), durationMs);
  }

  dismiss(id: number): void {
    this.notifications.update((list) => list.filter((n) => n.id !== id));
  }
}
