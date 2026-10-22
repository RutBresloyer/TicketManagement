import { Injectable, computed, signal } from '@angular/core';

/** Same rule as the API: 3–64 chars, letters/digits/'.'/'_'/'-', starting with a letter or digit. */
export const ACTOR_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$/;

/**
 * Development-only demo identity sent as the X-Actor-Id header. This is NOT authentication:
 * in a real deployment the actor comes from the authenticated user's token on the server.
 */
@Injectable({ providedIn: 'root' })
export class ActorService {
  readonly actorId = signal('demo-user-1');
  readonly isValid = computed(() => ACTOR_PATTERN.test(this.actorId()));
}
