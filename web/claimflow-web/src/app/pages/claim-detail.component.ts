import { Component, computed, DestroyRef, inject, input, signal, effect, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, describeError } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { AuditEntry, ClaimDetail } from '../core/models';
import { IcdPipe, OutcomeComponent, StatusComponent, StatusLabelPipe, UsdPipe } from '../shared/ui';

const RULE_LABELS: Record<string, string> = {
  DiagnosisCodes: 'Diagnosis codes are valid ICD-10-CM',
  MemberEligibility: 'Member was covered on each service date',
  ProviderEligibility: 'Billing provider is active and in network',
  TimelyFiling: 'Received within the timely filing limit',
  ProcedureCoverage: 'Procedures are valid and covered',
  PriorAuthorization: 'Prior authorization on file where required',
  DuplicateService: 'Not a duplicate of an earlier service',
  HighDollarReview: 'Below the high-dollar review threshold',
  ManualReview: 'Reviewer decision'
};

const DENIAL_REASONS = [
  { code: '50', label: '50 · Not medically necessary' },
  { code: '96', label: '96 · Non-covered charge' },
  { code: '16', label: '16 · Missing or invalid information' },
  { code: '242', label: '242 · Out-of-network provider' },
  { code: '197', label: '197 · Authorization absent' }
];

@Component({
  selector: 'cf-claim-detail',
  standalone: true,
  imports: [RouterLink, DatePipe, FormsModule, StatusComponent, StatusLabelPipe, OutcomeComponent, UsdPipe, IcdPipe],
  template: `
    <div class="page">
      <a routerLink="/claims" class="back">All claims</a>

      @if (error()) {
        <div class="alert alert-error" role="alert">{{ error() }}</div>
      }

      @if (claim(); as c) {
        <header class="head">
          <div>
            <h1 class="code">{{ c.claimNumber }}</h1>
            <p class="muted">
              {{ c.member.fullName }} ({{ c.member.memberNumber }}, {{ c.member.planName }}) seen by {{ c.provider.name }}
            </p>
          </div>
          <div class="head-actions">
            <cf-status [status]="c.status" />
            @if (canDownload()) {
              <button class="btn btn-secondary" type="button" (click)="download()">Download 835 remittance</button>
            }
          </div>
        </header>

        @if (c.status === 'Received' || c.status === 'InAdjudication') {
          <div class="alert alert-ok" role="status">The rules engine is adjudicating this claim. This page updates on its own.</div>
        }

        @if (c.status === 'Pended') {
          <section class="panel review" aria-labelledby="review-title">
            <div class="panel-body">
              <h2 id="review-title">Held for review</h2>
              <p class="pend-reason">{{ c.pendReason }}</p>
              @if (auth.canReview()) {
                <form class="review-form" (ngSubmit)="review('Approve')">
                  <div class="field note">
                    <label for="note">Review note</label>
                    <textarea id="note" name="note" rows="2" required maxlength="500" [(ngModel)]="note" placeholder="What did you check, and why is this the right decision?"></textarea>
                  </div>
                  <div class="field">
                    <label for="reason">Reason if denying</label>
                    <select id="reason" name="reason" [(ngModel)]="denialCode">
                      @for (r of denialReasons; track r.code) {
                        <option [value]="r.code">{{ r.label }}</option>
                      }
                    </select>
                  </div>
                  <div class="review-actions">
                    <button class="btn btn-primary" type="submit" [disabled]="reviewing() || !note.trim()">Approve and price</button>
                    <button class="btn btn-danger" type="button" [disabled]="reviewing() || !note.trim()" (click)="review('Deny')">Deny claim</button>
                  </div>
                </form>
              } @else {
                <p class="muted">An adjudicator will review this claim.</p>
              }
            </div>
          </section>
        }

        <!-- Where the money went: billed → write-offs → member → plan -->
        <section class="panel money" aria-labelledby="money-title">
          <div class="panel-head">
            <h2 id="money-title">Where the billed amount went</h2>
            <span class="muted">{{ c.totalBilled | usd }} billed</span>
          </div>
          <div class="panel-body">
            @if (!decided()) {
              <p class="muted">Allowed amounts, member cost share and plan payment are calculated once this claim is decided.</p>
            } @else {
            <div class="waterfall" role="img" [attr.aria-label]="moneyLabel()">
              @for (seg of segments(); track seg.key) {
                @if (seg.amount > 0) {
                  <div class="seg" [attr.data-kind]="seg.key" [style.flex-grow]="seg.amount"></div>
                }
              }
            </div>
            <dl class="money-legend">
              @for (seg of segments(); track seg.key) {
                <div [attr.data-kind]="seg.key">
                  <dt><i class="sw"></i>{{ seg.label }}</dt>
                  <dd>{{ seg.amount | usd }}</dd>
                </div>
              }
            </dl>
            }
          </div>
        </section>

        <div class="cols">
          <section class="panel" aria-labelledby="lines-title">
            <div class="panel-head"><h2 id="lines-title">Service lines</h2></div>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">Line</th>
                    <th scope="col">Procedure</th>
                    <th scope="col">Date</th>
                    <th scope="col" class="num">Billed</th>
                    <th scope="col" class="num">Allowed</th>
                    <th scope="col" class="num">Member</th>
                    <th scope="col" class="num">Plan paid</th>
                  </tr>
                </thead>
                <tbody>
                  @for (l of c.lines; track l.lineNumber) {
                    <tr [class.denied]="l.status === 'Denied'">
                      <td>{{ l.lineNumber }}</td>
                      <td>
                        <span class="code nowrap">{{ l.procedureCode }}{{ l.modifier ? '-' + l.modifier : '' }}</span>
                        <span class="muted nowrap"> × {{ l.units }}</span>
                        @if (l.denialCode) {
                          <div class="denial">Denied, CARC {{ l.denialCode }}: {{ l.denialDescription }}</div>
                        }
                      </td>
                      <td class="nowrap">{{ l.serviceDate | date: 'MMM d, y' }}</td>
                      <td class="num">{{ l.billedAmount | usd }}</td>
                      <td class="num">{{ l.allowedAmount | usd }}</td>
                      <td class="num">
                        {{ l.deductibleAmount + l.copayAmount + l.coinsuranceAmount | usd }}
                        @if (l.status === 'Approved') {
                          <div class="split muted">{{ memberSplit(l) }}</div>
                        }
                      </td>
                      <td class="num strong">{{ l.planPaid | usd }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <dl class="facts">
              <div><dt>Diagnoses</dt><dd>@for (dx of c.diagnosisCodes; track dx; let last = $last) {<span class="code">{{ dx | icd }}</span>{{ last ? '' : ', ' }}}</dd></div>
              <div><dt>Provider NPI</dt><dd class="code">{{ c.provider.npi }} <span class="muted">· {{ c.provider.isInNetwork ? 'In network' : 'Out of network' }}</span></dd></div>
              <div><dt>Patient control #</dt><dd>{{ c.patientControlNumber ?? 'None' }}</dd></div>
              <div><dt>Payment</dt><dd>{{ c.paymentReference ?? 'Not paid yet' }}</dd></div>
            </dl>
          </section>

          <section class="panel" aria-labelledby="trace-title">
            <div class="panel-head"><h2 id="trace-title">Adjudication trace</h2></div>
            @if (c.findings.length === 0) {
              <p class="empty">Rules have not run yet.</p>
            } @else {
              <ol class="trace">
                @for (f of c.findings; track $index) {
                  <li [attr.data-outcome]="f.outcome">
                    <cf-outcome [outcome]="f.outcome" />
                    <div>
                      <div class="rule">{{ ruleLabel(f.ruleName) }}</div>
                      @if (f.outcome !== 'Pass' || f.ruleName === 'ManualReview') {
                        <div class="msg">
                          @if (f.lineNumber) { <span>Line {{ f.lineNumber }}: </span> }
                          {{ f.message }}
                          @if (f.reasonCode) { <span class="muted"> (CARC {{ f.reasonCode }})</span> }
                        </div>
                      }
                    </div>
                  </li>
                }
              </ol>
            }
          </section>
        </div>

        <div class="cols">
          <section class="panel" aria-labelledby="history-title">
            <div class="panel-head"><h2 id="history-title">Status history</h2></div>
            <ol class="timeline">
              @for (h of c.history; track $index) {
                <li>
                  <div class="when">{{ h.changedAt | date: 'MMM d, y, h:mm:ss a' }}</div>
                  <div><strong>{{ h.to | statusLabel }}</strong><span class="muted"> by {{ h.actor }}</span></div>
                  @if (h.note) { <div class="muted note-text">{{ h.note }}</div> }
                </li>
              }
            </ol>
          </section>

          @if (auth.canAudit()) {
            <section class="panel" aria-labelledby="audit-title">
              <div class="panel-head">
                <h2 id="audit-title">HIPAA access log</h2>
                <button class="btn btn-ghost" type="button" (click)="loadAudit()">{{ audit() ? 'Refresh' : 'Show' }}</button>
              </div>
              @if (audit(); as entries) {
                <div class="table-wrap">
                  <table>
                    <thead><tr><th scope="col">When</th><th scope="col">Action</th><th scope="col">By</th></tr></thead>
                    <tbody>
                      @for (a of entries; track $index) {
                        <tr><td class="nowrap">{{ a.timestamp | date: 'MMM d, h:mm a' }}</td><td>{{ a.action }}</td><td>{{ a.actor }}</td></tr>
                      }
                    </tbody>
                  </table>
                </div>
              } @else {
                <p class="empty">Every view, decision and remittance download for this claim is recorded.</p>
              }
            </section>
          }
        </div>
      } @else if (!error()) {
        <p class="muted">Loading claim…</p>
      }
    </div>
  `,
  styles: `
    .back { display: inline-block; margin-bottom: 1rem; font-weight: 600; }
    .back::before { content: '‹ '; }
    .alert { margin-bottom: 1.25rem; }
    .head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1.5rem; }
    .head h1 { font-size: 1.75rem; letter-spacing: 0; }
    .head p { margin-top: 0.3rem; max-width: 70ch; }
    .head-actions { display: flex; align-items: center; gap: 1rem; }
    .head-actions cf-status { font-size: 1rem; }

    .review { border-color: var(--amber); border-left: 4px solid var(--amber); margin-bottom: 1.5rem; background: #fffdf8; }
    .pend-reason { margin: 0.35rem 0 1rem; }
    .review-form { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); gap: 1rem; align-items: end; }
    .review-actions { grid-column: 1 / -1; display: flex; gap: 0.5rem; }

    .money { margin-bottom: 1.5rem; }
    .waterfall { display: flex; height: 2.75rem; border-radius: var(--radius-sm); overflow: hidden; gap: 2px; background: var(--paper); }
    .seg { flex-basis: 0; min-width: 4px; }
    [data-kind='contractual'] { --k: #c3cdc9; }
    [data-kind='denied'] { --k: var(--red); }
    [data-kind='member'] { --k: var(--amber); }
    [data-kind='plan'] { --k: var(--teal); }
    .seg { background: var(--k); }
    .money-legend { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 1rem; margin: 1rem 0 0; }
    .money-legend div { border-top: 3px solid var(--k); padding-top: 0.5rem; }
    .money-legend dt { font-size: 0.8125rem; color: var(--ink-soft); }
    .money-legend dd { margin: 0.15rem 0 0; font-size: 1.25rem; font-weight: 700; }
    .sw { display: none; }

    .cols { display: grid; grid-template-columns: minmax(0, 7fr) minmax(0, 4fr); gap: 1.5rem; margin-bottom: 1.5rem; align-items: start; }
    .cols th, .cols td { padding-inline: 0.7rem; }
    .cols th:first-child, .cols td:first-child { padding-left: 1.25rem; }
    tr.denied td { background: #fdf6f5; }
    .denial { color: var(--red); font-size: 0.8125rem; margin-top: 0.2rem; }
    .split { font-size: 0.75rem; }
    .strong { font-weight: 700; }
    .facts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem 1.5rem; margin: 0; padding: 1rem 1.25rem; border-top: 1px solid var(--line); }
    .facts dt { font-size: 0.8125rem; color: var(--ink-soft); }
    .facts dd { margin: 0; }

    .trace { list-style: none; margin: 0; padding: 0.5rem 0; }
    .trace li { display: grid; grid-template-columns: auto 1fr; gap: 0.75rem; padding: 0.55rem 1.25rem; position: relative; }
    .trace li:not(:last-child)::after { content: ''; position: absolute; left: calc(1.25rem + 0.75rem - 1px); top: 2.1rem; bottom: -0.5rem; width: 2px; background: var(--line); }
    .trace li[data-outcome='Pend'] { background: var(--amber-wash); }
    .trace li[data-outcome='Deny'] { background: var(--red-wash); }
    .rule { font-weight: 600; font-size: 0.875rem; padding-top: 0.15rem; }
    .msg { font-size: 0.8125rem; margin-top: 0.15rem; }

    .timeline { list-style: none; margin: 0; padding: 1rem 1.25rem; display: grid; gap: 1rem; }
    .timeline li { border-left: 2px solid var(--teal); padding-left: 0.9rem; }
    .when { font-size: 0.8125rem; color: var(--ink-soft); }
    .note-text { font-size: 0.875rem; }

    @media (max-width: 1000px) { .cols { grid-template-columns: 1fr; } }
    @media (max-width: 640px) { .money-legend, .facts, .review-form { grid-template-columns: 1fr 1fr; } .review-form .note { grid-column: 1 / -1; } }
  `
})
export class ClaimDetailComponent {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly audit = signal<AuditEntry[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly reviewing = signal(false);
  protected readonly denialReasons = DENIAL_REASONS;
  protected note = '';
  protected denialCode = '50';
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private polls = 0;

  protected readonly decided = computed(() => ['Approved', 'Denied', 'Paid'].includes(this.claim()?.status ?? ''));
  protected readonly canDownload = computed(() => ['Approved', 'Denied', 'Paid'].includes(this.claim()?.status ?? ''));

  protected readonly segments = computed(() => {
    const c = this.claim();
    if (!c) return [];
    const denied = c.lines.filter((l) => l.status === 'Denied').reduce((s, l) => s + l.billedAmount, 0);
    const adjudicated = c.status !== 'Received' && c.status !== 'InAdjudication' && c.status !== 'Pended';
    const contractual = adjudicated ? Math.max(0, c.totalBilled - c.totalAllowed - denied) : 0;
    return [
      { key: 'contractual', label: 'Contractual write-off', amount: round(contractual) },
      { key: 'denied', label: 'Denied', amount: round(denied) },
      { key: 'member', label: 'Member owes', amount: c.memberResponsibility },
      { key: 'plan', label: 'Plan pays', amount: c.planPaid }
    ];
  });

  protected readonly moneyLabel = computed(() =>
    this.segments()
      .map((s) => `${s.label}: $${s.amount.toFixed(2)}`)
      .join('; ')
  );

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => {
        this.polls = 0;
        this.audit.set(null);
        this.load(id);
      });
    });
    inject(DestroyRef).onDestroy(() => this.pollTimer && clearTimeout(this.pollTimer));
  }

  protected ruleLabel(rule: string): string {
    return RULE_LABELS[rule] ?? rule;
  }

  protected memberSplit(l: { deductibleAmount: number; copayAmount: number; coinsuranceAmount: number }): string {
    const parts: string[] = [];
    if (l.deductibleAmount) parts.push(`$${l.deductibleAmount.toFixed(2)} deductible`);
    if (l.copayAmount) parts.push(`$${l.copayAmount.toFixed(2)} copay`);
    if (l.coinsuranceAmount) parts.push(`$${l.coinsuranceAmount.toFixed(2)} coinsurance`);
    return parts.join(', ');
  }

  protected review(decision: 'Approve' | 'Deny'): void {
    const c = this.claim();
    if (!c || !this.note.trim()) return;
    this.reviewing.set(true);
    this.api.review(c.id, decision, this.note.trim(), decision === 'Deny' ? this.denialCode : null).subscribe({
      next: () => {
        this.reviewing.set(false);
        this.note = '';
        this.load(c.id);
      },
      error: (e) => {
        this.error.set(describeError(e).message);
        this.reviewing.set(false);
      }
    });
  }

  protected download(): void {
    const c = this.claim();
    if (!c) return;
    this.api.downloadRemittance(c.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${c.claimNumber}-835.txt`;
        a.click();
        URL.revokeObjectURL(url);
        if (this.audit()) this.loadAudit();
      },
      error: (e) => this.error.set(describeError(e).message)
    });
  }

  protected loadAudit(): void {
    const c = this.claim();
    if (!c) return;
    this.api.getAudit(c.id).subscribe({ next: (a) => this.audit.set(a), error: (e) => this.error.set(describeError(e).message) });
  }

  private load(id: string): void {
    this.error.set(null);
    this.api.getClaim(id).subscribe({
      next: (c) => {
        this.claim.set(c);
        // The worker usually finishes in well under a second; poll briefly while it does.
        if ((c.status === 'Received' || c.status === 'InAdjudication') && this.polls++ < 15) {
          this.pollTimer = setTimeout(() => this.load(id), 1000);
        }
      },
      error: (e) => this.error.set(describeError(e).message)
    });
  }
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}
