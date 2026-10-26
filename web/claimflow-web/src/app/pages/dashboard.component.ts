import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService, describeError } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { DashboardSummary, PaymentRun } from '../core/models';
import { StatusComponent, UsdPipe } from '../shared/ui';

@Component({
  selector: 'cf-dashboard',
  standalone: true,
  imports: [RouterLink, DecimalPipe, StatusComponent, UsdPipe],
  template: `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Claims overview</h1>
          <p>How claims are moving through intake, adjudication and payment.</p>
        </div>
        @if (auth.canPay()) {
          <button class="btn btn-primary" type="button" (click)="release()" [disabled]="paying() || approvedCount() === 0">
            {{ paying() ? 'Releasing…' : 'Release payments for ' + approvedCount() + ' approved' }}
          </button>
        }
      </header>

      @if (paymentRun(); as run) {
        <div class="alert alert-ok" role="status">
          Payment run {{ run.runReference }} paid {{ run.claimsPaid }} claims for {{ run.totalPaid | usd }}.
        </div>
      }
      @if (error()) {
        <div class="alert alert-error" role="alert">{{ error() }}</div>
      }

      @if (data(); as d) {
        <section class="kpis" aria-label="Key figures">
          <div class="kpi">
            <div class="kpi-value">{{ d.totalClaims | number }}</div>
            <div class="kpi-label">claims received</div>
          </div>
          <div class="kpi">
            <div class="kpi-value">{{ d.autoAdjudicationRate | number: '1.0-1' }}%</div>
            <div class="kpi-label">finalized without a human touch</div>
          </div>
          <div class="kpi">
            <div class="kpi-value">{{ d.totalBilled | usd }}</div>
            <div class="kpi-label">billed by providers</div>
          </div>
          <div class="kpi">
            <div class="kpi-value">{{ d.totalPaid | usd }}</div>
            <div class="kpi-label">paid out to providers</div>
          </div>
        </section>

        <section class="panel" aria-labelledby="pipeline">
          <div class="panel-head"><h2 id="pipeline">Where claims are now</h2></div>
          <div class="pipeline">
            @for (s of d.byStatus; track s.status) {
              <a class="stage" [routerLink]="['/claims']" [queryParams]="{ status: s.status }" [class.attention]="s.status === 'Pended' && s.count > 0">
                <cf-status [status]="s.status" />
                <span class="stage-count">{{ s.count }}</span>
                <span class="stage-billed muted">{{ s.billed | usd }} billed</span>
              </a>
            }
          </div>
        </section>

        <div class="grid">
          <section class="panel" aria-labelledby="volume">
            <div class="panel-head">
              <h2 id="volume">Last 14 days</h2>
              <div class="legend">
                <span><i class="sw recv"></i>Received</span>
                <span><i class="sw fin"></i>Finalized</span>
              </div>
            </div>
            <div class="panel-body">
              <div class="chart" role="img" [attr.aria-label]="chartLabel()">
                @for (day of d.dailyVolume; track day.date) {
                  <div class="day" [title]="day.date + ': ' + day.received + ' received, ' + day.finalized + ' finalized'">
                    <div class="bars">
                      <div class="bar recv" [style.height.%]="pct(day.received)"></div>
                      <div class="bar fin" [style.height.%]="pct(day.finalized)"></div>
                    </div>
                    <div class="tick">{{ day.date.slice(8) }}</div>
                  </div>
                }
              </div>
              <p class="muted note">Average time from receipt to decision: {{ d.averageDaysToAdjudicate | number: '1.0-2' }} days.</p>
            </div>
          </section>

          <section class="panel" aria-labelledby="denials">
            <div class="panel-head"><h2 id="denials">Top denial reasons</h2></div>
            @if (d.topDenialReasons.length === 0) {
              <p class="empty">No denied service lines yet.</p>
            } @else {
              <ol class="reasons">
                @for (r of d.topDenialReasons; track r.reasonCode) {
                  <li>
                    <span class="carc">CARC {{ r.reasonCode }}</span>
                    <span class="desc">{{ r.description }}</span>
                    <span class="count">{{ r.count }}</span>
                  </li>
                }
              </ol>
            }
          </section>
        </div>
      } @else if (!error()) {
        <p class="muted">Loading…</p>
      }
    </div>
  `,
  styles: `
    .alert { margin-bottom: 1.25rem; }
    .kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 1px; background: var(--line); border: 1px solid var(--line); border-radius: var(--radius); overflow: hidden; margin-bottom: 1.5rem; }
    .kpi { background: var(--surface); padding: 1.25rem clamp(1rem, 2.5vw, 1.5rem); min-width: 0; }
    .kpi-value { font-size: clamp(1.2rem, 4.5vw, 1.75rem); overflow-wrap: anywhere; font-weight: 700; letter-spacing: -0.02em; }
    .kpi-label { color: var(--ink-soft); font-size: 0.875rem; }
    .pipeline { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); }
    .stage { display: grid; gap: 0.2rem; padding: 1rem 1.25rem; text-decoration: none; color: inherit; border-right: 1px solid var(--line); }
    .stage:last-child { border-right: 0; }
    .stage:hover { background: #f8fbfa; }
    .stage.attention { background: var(--amber-wash); }
    .stage-count { font-size: 1.5rem; font-weight: 700; }
    .stage-billed { font-size: 0.8125rem; }
    .grid { display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: 1.5rem; margin-top: 1.5rem; }
    .legend { display: flex; gap: 1rem; font-size: 0.8125rem; color: var(--ink-soft); }
    .legend span { display: inline-flex; align-items: center; gap: 0.35rem; }
    .sw { width: 0.7rem; height: 0.7rem; display: inline-block; border-radius: 2px; }
    .recv { background: var(--line-strong); }
    .fin { background: var(--teal); }
    .chart { display: grid; grid-template-columns: repeat(14, minmax(0, 1fr)); gap: 0.35rem; height: 180px; align-items: end; }
    .day { display: grid; grid-template-rows: 1fr auto; height: 100%; gap: 0.3rem; }
    .bars { display: flex; gap: 2px; align-items: flex-end; height: 100%; }
    .bar { flex: 1; min-height: 2px; border-radius: 2px 2px 0 0; }
    .tick { font-size: 0.75rem; color: var(--ink-soft); text-align: center; }
    .note { margin-top: 1rem; font-size: 0.875rem; }
    .reasons { list-style: none; margin: 0; padding: 0; }
    .reasons li { display: grid; grid-template-columns: auto 1fr auto; gap: 0.75rem; align-items: baseline; padding: 0.8rem 1.25rem; border-bottom: 1px solid var(--line); }
    .reasons li:last-child { border-bottom: 0; }
    .carc { font-weight: 700; color: var(--red); white-space: nowrap; }
    .desc { font-size: 0.875rem; }
    .count { font-weight: 700; }
    @media (max-width: 1100px) { .pipeline { grid-template-columns: repeat(3, minmax(0, 1fr)); } .stage:nth-child(3) { border-right: 0; } .stage:nth-child(-n + 3) { border-bottom: 1px solid var(--line); } .grid { grid-template-columns: 1fr; } }
    @media (max-width: 700px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } .pipeline { grid-template-columns: repeat(2, minmax(0, 1fr)); } .stage { border-right: 0; border-bottom: 1px solid var(--line); } }
  `
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);

  protected readonly data = signal<DashboardSummary | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly paying = signal(false);
  protected readonly paymentRun = signal<PaymentRun | null>(null);

  protected readonly approvedCount = computed(() => this.data()?.byStatus.find((s) => s.status === 'Approved')?.count ?? 0);
  private readonly maxDaily = computed(() => Math.max(1, ...(this.data()?.dailyVolume.flatMap((d) => [d.received, d.finalized]) ?? [1])));
  protected readonly chartLabel = computed(() => {
    const days = this.data()?.dailyVolume ?? [];
    const received = days.reduce((sum, d) => sum + d.received, 0);
    const finalized = days.reduce((sum, d) => sum + d.finalized, 0);
    return `Over the last 14 days, ${received} claims were received and ${finalized} were finalized.`;
  });

  constructor() {
    this.load();
  }

  protected pct(value: number): number {
    return (value / this.maxDaily()) * 100;
  }

  protected release(): void {
    this.paying.set(true);
    this.api.releasePayments().subscribe({
      next: (run) => {
        this.paymentRun.set(run);
        this.paying.set(false);
        this.load();
      },
      error: (e) => {
        this.error.set(describeError(e).message);
        this.paying.set(false);
      }
    });
  }

  private load(): void {
    this.api.dashboard().subscribe({
      next: (d) => this.data.set(d),
      error: (e) => this.error.set(describeError(e).message)
    });
  }
}
