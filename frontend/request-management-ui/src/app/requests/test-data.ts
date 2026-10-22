import { PagedResult, RequestDto } from './request.models';

export function requestDto(overrides: Partial<RequestDto> = {}): RequestDto {
  return {
    id: 1,
    title: 'Investigate payment provider timeout (REF-000001)',
    organizationName: 'Blue Harbor Logistics',
    status: 'New',
    priority: 'High',
    assignedTo: 'agent-01',
    createdAt: '2026-03-01T12:00:00+00:00',
    updatedAt: '2026-03-01T12:00:00+00:00',
    rowVersion: 'AAAAAAAAAAE=',
    allowedNextStatuses: ['InProgress', 'Waiting'],
    ...overrides,
  };
}

export function page(
  items: RequestDto[],
  page = 1,
  pageSize = 25,
  totalCount = items.length,
): PagedResult<RequestDto> {
  return { items, page, pageSize, totalCount, totalPages: Math.ceil(totalCount / pageSize) };
}
