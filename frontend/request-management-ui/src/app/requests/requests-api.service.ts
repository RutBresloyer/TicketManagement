import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../core/api-base-url';
import {
  BulkStatusUpdateItem,
  BulkStatusUpdateResponse,
  PagedResult,
  RequestDto,
  RequestHistoryEntry,
  RequestListQuery,
  RequestSummary,
  UpdateStatusBody,
} from './request.models';

/** The only place that knows the API's URLs. All filtering, sorting and paging happen on the server. */
@Injectable({ providedIn: 'root' })
export class RequestsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${inject(API_BASE_URL)}/api/requests`;

  list(query: RequestListQuery): Observable<PagedResult<RequestDto>> {
    return this.http.get<PagedResult<RequestDto>>(this.baseUrl, { params: toListParams(query) });
  }

  get(id: number): Observable<RequestDto> {
    return this.http.get<RequestDto>(`${this.baseUrl}/${id}`);
  }

  history(id: number): Observable<RequestHistoryEntry[]> {
    return this.http.get<RequestHistoryEntry[]>(`${this.baseUrl}/${id}/history`);
  }

  summary(): Observable<RequestSummary> {
    return this.http.get<RequestSummary>(`${this.baseUrl}/summary`);
  }

  updateStatus(id: number, body: UpdateStatusBody, actorId: string): Observable<RequestDto> {
    return this.http.patch<RequestDto>(`${this.baseUrl}/${id}/status`, body, {
      headers: actorHeader(actorId),
    });
  }

  bulkUpdateStatus(
    items: BulkStatusUpdateItem[],
    actorId: string,
  ): Observable<BulkStatusUpdateResponse> {
    return this.http.patch<BulkStatusUpdateResponse>(
      `${this.baseUrl}/bulk-status`,
      { items },
      { headers: actorHeader(actorId) },
    );
  }
}

function actorHeader(actorId: string): HttpHeaders {
  return new HttpHeaders({ 'X-Actor-Id': actorId });
}

function toListParams(query: RequestListQuery): HttpParams {
  let params = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sortBy', query.sortBy)
    .set('sortDirection', query.sortDirection);

  const optional: Record<string, string> = {
    q: query.q,
    status: query.status,
    priority: query.priority,
    assignedTo: query.assignedTo.trim(),
    organizationName: query.organizationName.trim(),
    // Date inputs give yyyy-MM-dd; the API's bounds are createdFrom (inclusive) and createdTo (exclusive), in UTC.
    createdFrom: query.createdFrom ? `${query.createdFrom}T00:00:00Z` : '',
    createdTo: query.createdBefore ? `${query.createdBefore}T00:00:00Z` : '',
  };
  for (const [key, value] of Object.entries(optional)) {
    if (value) {
      params = params.set(key, value);
    }
  }
  return params;
}
