import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblem } from '../requests/request.models';

/** What the UI needs to know about a failed call, extracted from the API's ProblemDetails. */
export interface UiError {
  status: number;
  code: string | null;
  message: string;
  fieldErrors: string[];
  problem: ApiProblem | null;
}

export function toUiError(error: unknown): UiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, code: null, message: 'Unexpected error.', fieldErrors: [], problem: null };
  }

  if (error.status === 0) {
    return {
      status: 0,
      code: null,
      message: 'The API is not reachable. Is it running on http://localhost:5080?',
      fieldErrors: [],
      problem: null,
    };
  }

  const problem = isProblem(error.error) ? error.error : null;
  const fieldErrors = Object.entries(problem?.errors ?? {}).flatMap(([field, messages]) =>
    messages.map((m) => `${field}: ${m}`),
  );
  const message = problem?.detail ?? problem?.title ?? `Request failed (HTTP ${error.status}).`;
  return { status: error.status, code: problem?.code ?? null, message, fieldErrors, problem };
}

function isProblem(body: unknown): body is ApiProblem {
  return typeof body === 'object' && body !== null && ('title' in body || 'status' in body);
}
