import { Component, inject } from '@angular/core';
import { ACTOR_PATTERN, ActorService } from './core/actor.service';
import { NotificationService } from './core/notification.service';
import { RequestsPage } from './requests/requests-page';

@Component({
  selector: 'app-root',
  imports: [RequestsPage],
  templateUrl: './app.html',
})
export class App {
  protected readonly actor = inject(ActorService);
  protected readonly notifications = inject(NotificationService);
  protected readonly actorPattern = ACTOR_PATTERN.source;

  protected setActor(event: Event): void {
    this.actor.actorId.set((event.target as HTMLInputElement).value.trim());
  }
}
