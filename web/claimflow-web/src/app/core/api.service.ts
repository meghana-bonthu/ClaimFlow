import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import {
  AuditEntry,
  ClaimDetail,
  ClaimListItem,
  ClaimStatus,
  ClaimSubmitted,
  DashboardSummary,
  FeeScheduleDto,
  MemberDto,
  PagedResult,
  PaymentRun,
  ProblemDetails,
  ProviderDto,
  SubmitClaimRequest,
  X12BatchResult
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  searchClaims(options: { status?: ClaimStatus | null; search?: string; page?: number; pageSize?: number }) {
    let params = new HttpParams().set('page', options.page ?? 1).set('pageSize', options.pageSize ?? 20);
    if (options.status) params = params.set('status', options.status);
    if (options.search?.trim()) params = params.set('search', options.search.trim());
    return this.http.get<PagedResult<ClaimListItem>>('/api/claims', { params });
  }

  getClaim(id: string) {
    return this.http.get<ClaimDetail>(`/api/claims/${id}`);
  }

  getAudit(id: string) {
    return this.http.get<AuditEntry[]>(`/api/claims/${id}/audit`);
  }

  submitClaim(request: SubmitClaimRequest) {
    return this.http.post<ClaimSubmitted>('/api/claims', request);
  }

  uploadX12(file: File) {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<X12BatchResult>('/api/claims/x12', form);
  }

  review(id: string, decision: 'Approve' | 'Deny', note: string, denialReasonCode: string | null) {
    return this.http.post<{ id: string; status: ClaimStatus }>(`/api/claims/${id}/review`, { decision, note, denialReasonCode });
  }

  downloadRemittance(id: string) {
    return this.http.get(`/api/claims/${id}/remittance`, { responseType: 'blob' });
  }

  releasePayments() {
    return this.http.post<PaymentRun>('/api/payments/release', {});
  }

  dashboard() {
    return this.http.get<DashboardSummary>('/api/dashboard');
  }

  members(search = '') {
    return this.http.get<MemberDto[]>('/api/members', { params: search ? { search } : {} });
  }

  providers(search = '') {
    return this.http.get<ProviderDto[]>('/api/providers', { params: search ? { search } : {} });
  }

  feeSchedule() {
    return this.http.get<FeeScheduleDto[]>('/api/fee-schedule');
  }
}

/** Turns an API error into one readable sentence plus any field errors. */
export function describeError(error: unknown): { message: string; fields: string[] } {
  if (error instanceof HttpErrorResponse) {
    const problem = (error.error ?? {}) as ProblemDetails;
    const fields = Object.values(problem.errors ?? {}).flat();
    if (error.status === 0) return { message: 'The ClaimFlow API is not reachable. Check that it is running.', fields: [] };
    if (error.status === 403) return { message: 'Your role does not allow this action.', fields: [] };
    return { message: problem.detail ?? problem.title ?? `Request failed (${error.status}).`, fields };
  }
  return { message: 'Something went wrong. Try again.', fields: [] };
}
