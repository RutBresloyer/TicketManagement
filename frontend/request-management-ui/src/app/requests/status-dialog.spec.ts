import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { StatusDialog } from './status-dialog';
import { requestDto } from './test-data';

describe('StatusDialog', () => {
  let fixture: ComponentFixture<StatusDialog>;
  let http: HttpTestingController;
  let element: HTMLElement;

  const isPatch = (r: { method: string }) => r.method === 'PATCH';
  const flushHistory = () => http.expectOne((r) => r.url.endsWith('/history')).flush([]);

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [StatusDialog],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StatusDialog);
    fixture.componentRef.setInput(
      'request',
      requestDto({ id: 7, status: 'New', rowVersion: 'AAAAAAAAAAE=' }),
    );
    element = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    flushHistory();
  });

  afterEach(() => http.verify());

  async function chooseAndSave(status: string): Promise<void> {
    element.querySelector<HTMLInputElement>(`input[type=radio][value="${status}"]`)!.click();
    await fixture.whenStable();
    buttonByText('Save').click();
    await fixture.whenStable();
  }

  function buttonByText(text: string): HTMLButtonElement {
    const button = [...element.querySelectorAll('button')].find(
      (b) => b.textContent?.trim() === text,
    );
    if (!button) throw new Error(`No button "${text}"`);
    return button;
  }

  it('sends the version it read and the demo actor header', async () => {
    await chooseAndSave('InProgress');

    const patch = http.expectOne(isPatch);
    expect(patch.request.url).toContain('/api/requests/7/status');
    expect(patch.request.body).toEqual({ status: 'InProgress', expectedVersion: 'AAAAAAAAAAE=' });
    expect(patch.request.headers.get('X-Actor-Id')).toBe('demo-user-1');

    patch.flush(
      requestDto({
        id: 7,
        status: 'InProgress',
        rowVersion: 'AAAAAAAAAAI=',
        allowedNextStatuses: ['Waiting', 'Completed'],
      }),
    );
    await fixture.whenStable();
    flushHistory(); // new version -> history re-fetched
    expect(element.querySelector('[data-testid=current-status]')?.textContent).toContain(
      'In progress',
    );
  });

  it('on 409 explains the conflict, never retries by itself, and Refresh shows the current state', async () => {
    await chooseAndSave('InProgress');
    http.expectOne(isPatch).flush(
      {
        title: 'The request was changed by someone else.',
        status: 409,
        code: 'ConcurrencyConflict',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();

    expect(element.querySelector('[data-testid=conflict]')?.textContent).toContain(
      'Someone else changed this request',
    );
    http.expectNone(isPatch); // no silent retry / overwrite
    expect(buttonByText('Save').disabled).toBe(true);

    buttonByText('Refresh').click();
    const get = http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/api/requests/7'));
    get.flush(
      requestDto({
        id: 7,
        status: 'Waiting',
        rowVersion: 'AAAAAAAAAAk=',
        allowedNextStatuses: ['InProgress', 'Completed'],
      }),
    );
    await fixture.whenStable();
    flushHistory();

    expect(element.querySelector('[data-testid=conflict]')).toBeNull();
    expect(element.querySelector('[data-testid=current-status]')?.textContent).toContain('Waiting');
    const options = [...element.querySelectorAll<HTMLInputElement>('input[type=radio]')].map(
      (r) => r.value,
    );
    expect(options).toEqual(['InProgress', 'Completed']);

    // A deliberate second attempt uses the refreshed token.
    await chooseAndSave('Completed');
    const retry = http.expectOne(isPatch);
    expect(retry.request.body).toEqual({ status: 'Completed', expectedVersion: 'AAAAAAAAAAk=' });
    retry.flush(
      requestDto({
        id: 7,
        status: 'Completed',
        rowVersion: 'AAAAAAAAAAo=',
        allowedNextStatuses: [],
      }),
    );
    await fixture.whenStable();
    flushHistory();
  });

  it('shows the business-rule message on 422', async () => {
    await chooseAndSave('Waiting');
    http.expectOne(isPatch).flush(
      {
        title: 'This status transition is not allowed.',
        status: 422,
        code: 'InvalidStatusTransition',
        detail:
          "Cannot change status from 'Completed' to 'Waiting': 'Completed' is a terminal status.",
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await fixture.whenStable();

    expect(element.querySelector('[data-testid=error]')?.textContent).toContain('terminal status');
  });
});
