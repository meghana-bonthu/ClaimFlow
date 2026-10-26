import { Component, inject, input, signal, effect, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService, describeError } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { CLAIM_STATUSES, ClaimListItem, ClaimStatus, PagedResult } from '../core/models';
import { StatusComponent, StatusLabelPipe, UsdPipe } from '../shared/ui';

@Component({
  selector: 'cf-claims-list',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe, StatusComponent, StatusLabelPipe, UsdPipe],
  template: `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Claims</h1>
          <p>Search by claim number, member ID, last name, provider NPI or patient control number.</p>
        </div>
        @if (auth.canSubmit()) {
          <div class="actions">
            <a class="btn btn-secondary" routerLink="/claims/upload">Upload 837 file</a>
            <a class="btn btn-primary" routerLink="/claims/new">New claim</a>
          </div>
        }
      </header>

      <div class="panel">
        <div class="filters">
          <div class="field search">
            <label for="q">Search</label>
            <input id="q" type="search" placeholder="e.g. CLM-, M100001, Smith" [ngModel]="search()" (ngModelChange)="onSearch($event)" />
          </div>
          <div class="chips" role="group" aria-label="Filter by status">
            <button type="button" [class.on]="!statusFilter()" (click)="setStatus(null)">All</button>
            @for (s of statuses; track s) {
              <button type="button" [class.on]="statusFilter() === s" (click)="setStatus(s)" [attr.aria-pressed]="statusFilter() === s">{{ s | statusLabel }}</button>
            }
          </div>
        </div>

        @if (error()) {
          <div class="alert alert-error" role="alert">{{ error() }}</div>
        }

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Claim</th>
                <th scope="col">Member</th>
                <th scope="col">Provider</th>
                <th scope="col">Service date</th>
                <th scope="col">Status</th>
                <th scope="col" class="num">Billed</th>
                <th scope="col" class="num">Plan paid</th>
                <th scope="col">Received</th>
              </tr>
            </thead>
            <tbody>
              @for (c of result()?.items ?? []; track c.id) {
                <tr class="clickable" (click)="open(c)">
                  <td><a class="code nowrap" [routerLink]="['/claims', c.id]" (click)="$event.stopPropagation()">{{ c.claimNumber }}</a></td>
                  <td>{{ c.memberName }}<div class="muted small">{{ c.memberNumber }}</div></td>
                  <td>{{ c.providerName }}</td>
                  <td class="nowrap">{{ c.serviceDateFrom | date: 'MMM d, y' }}</td>
                  <td><cf-status [status]="c.status" /></td>
                  <td class="num">{{ c.totalBilled | usd }}</td>
                  <td class="num">{{ c.planPaid | usd }}</td>
                  <td class="nowrap">{{ c.submittedAt | date: 'MMM d, h:mm a' }}<div class="muted small">{{ sourceLabel(c.source) }}</div></td>
                </tr>
              } @empty {
                <tr><td colspan="8" class="empty">{{ loading() ? 'Loading claims…' : 'No claims match these filters.' }}</td></tr>
              }
            </tbody>
          </table>
        </div>

        @if (result(); as r) {
          @if (r.totalPages > 1) {
            <nav class="pager" aria-label="Pagination">
              <span class="muted">{{ r.totalCount }} claims · page {{ r.page }} of {{ r.totalPages }}</span>
              <button class="btn btn-secondary" type="button" [disabled]="r.page <= 1" (click)="go(r.page - 1)">Previous</button>
              <button class="btn btn-secondary" type="button" [disabled]="r.page >= r.totalPages" (click)="go(r.page + 1)">Next</button>
            </nav>
          }
        }
      </div>
    </div>
  `,
  styles: `
    .actions { display: flex; gap: 0.5rem; }
    .filters { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 1rem 1.5rem; padding: 1rem 1.25rem; border-bottom: 1px solid var(--line); }
    .search { flex: 1 1 260px; max-width: 360px; }
    .chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
    .chips button { font: inherit; font-size: 0.8125rem; font-weight: 500; padding: 0.35rem 0.75rem; border-radius: 999px; border: 1px solid var(--line-strong); background: var(--surface); cursor: pointer; }
    .chips button.on { background: var(--ink); border-color: var(--ink); color: #fff; }
    .alert { margin: 1rem 1.25rem; }
    .small { font-size: 0.8125rem; }
    .pager { display: flex; align-items: center; justify-content: flex-end; gap: 0.5rem; padding: 0.75rem 1.25rem; border-top: 1px solid var(--line); }
    .pager .muted { margin-right: auto; font-size: 0.875rem; }
  `
})
export class ClaimsListComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  /** Bound from the ?status= query parameter (dashboard links here). */
  readonly status = input<ClaimStatus | undefined>();
  /** Bound from ?q= so searches can be linked to. */
  readonly q = input<string | undefined>();

  protected readonly statuses = CLAIM_STATUSES;
  protected readonly statusFilter = signal<ClaimStatus | null>(null);
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly result = signal<PagedResult<ClaimListItem> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly search$ = new Subject<string>();

  constructor() {
    effect(() => {
      const fromQuery = this.status() ?? null;
      const text = this.q();
      untracked(() => {
        this.statusFilter.set(fromQuery);
        if (text !== undefined) this.search.set(text);
        this.page.set(1);
        this.load();
      });
    });

    this.search$.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => {
      this.page.set(1);
      this.load();
    });
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.search$.next(value);
  }

  protected setStatus(status: ClaimStatus | null): void {
    this.router.navigate([], { queryParams: { status: status ?? null }, queryParamsHandling: 'merge' });
  }

  protected go(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected open(claim: ClaimListItem): void {
    this.router.navigate(['/claims', claim.id]);
  }

  protected sourceLabel(source: string): string {
    return source === 'X12Edi' ? 'X12 837 file' : source === 'Portal' ? 'Portal' : 'API';
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.searchClaims({ status: this.statusFilter(), search: this.search(), page: this.page() }).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(describeError(e).message);
        this.loading.set(false);
      }
    });
  }
}
