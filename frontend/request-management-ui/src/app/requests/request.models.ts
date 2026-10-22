// Typed mirror of the API contract (see /openapi/v1.json). Enum values travel as strings.

export const REQUEST_STATUSES = ['New', 'InProgress', 'Waiting', 'Completed'] as const;
export type RequestStatus = (typeof REQUEST_STATUSES)[number];

export const REQUEST_PRIORITIES = ['Low', 'Medium', 'High'] as const;
export type RequestPriority = (typeof REQUEST_PRIORITIES)[number];

export type SortField = 'createdAt' | 'updatedAt' | 'title' | 'organizationName' | 'priority';
export type SortDirection = 'asc' | 'desc';

export const PAGE_SIZES = [10, 25, 50, 100] as const;
export const MAX_BULK_ITEMS = 100;

export interface RequestDto {
  id: number;
  title: string;
  organizationName: string;
  status: RequestStatus;
  priority: RequestPriority;
  assignedTo: string | null;
  createdAt: string;
  updatedAt: string;
  /** Opaque concurrency token; send back unchanged as `expectedVersion`. */
  rowVersion: string;
  allowedNextStatuses: RequestStatus[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Filters as edited in the UI. Empty string = no filter. Dates are `yyyy-MM-dd`, interpreted as UTC midnight. */
export interface RequestFilters {
  status: RequestStatus | '';
  priority: RequestPriority | '';
  assignedTo: string;
  organizationName: string;
  createdFrom: string;
  createdBefore: string;
}

export interface RequestListQuery extends RequestFilters {
  q: string;
  sortBy: SortField;
  sortDirection: SortDirection;
  page: number;
  pageSize: number;
}

export interface UpdateStatusBody {
  status: RequestStatus;
  expectedVersion: string;
}

export interface RequestHistoryEntry {
  id: number;
  previousStatus: RequestStatus;
  newStatus: RequestStatus;
  changedAt: string;
  changedBy: string;
}

export interface BulkStatusUpdateItem {
  id: number;
  status: RequestStatus;
  expectedVersion: string;
}

export type BulkOutcome =
  'Updated' | 'NoChange' | 'NotFound' | 'ConcurrencyConflict' | 'InvalidTransition';

export interface BulkStatusUpdateItemResult {
  id: number;
  outcome: BulkOutcome;
  rowVersion: string | null;
  status: RequestStatus | null;
  error: { code: string; message: string } | null;
}

export interface BulkStatusUpdateResponse {
  succeeded: number;
  failed: number;
  results: BulkStatusUpdateItemResult[];
}

export interface RequestSummary {
  totalCount: number;
  byStatus: { status: RequestStatus; count: number }[];
  byPriority: { priority: RequestPriority; count: number }[];
  generatedAt: string;
}

/** RFC 7807 body returned by the API for every error, with our `code` extension. */
export interface ApiProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  currentStatus?: RequestStatus;
  allowedNextStatuses?: RequestStatus[];
}
