import { RequestStatus } from './request.models';

const LABELS: Record<RequestStatus, string> = {
  New: 'New',
  InProgress: 'In progress',
  Waiting: 'Waiting',
  Completed: 'Completed',
};

export function statusLabel(status: RequestStatus): string {
  return LABELS[status];
}
