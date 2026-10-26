import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { ApiService } from '../core/api.service';
import { FeeScheduleDto, MemberDto, ProviderDto } from '../core/models';
import { UsdPipe } from '../shared/ui';

type Tab = 'members' | 'providers' | 'fees';

@Component({
  selector: 'cf-reference',
  standalone: true,
  imports: [DatePipe, UsdPipe],
  template: `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Members &amp; providers</h1>
          <p>The synthetic reference data the rules engine checks claims against.</p>
        </div>
      </header>

      <div class="tabs" role="tablist" aria-label="Reference data">
        <button role="tab" [attr.aria-selected]="tab() === 'members'" (click)="tab.set('members')">Members</button>
        <button role="tab" [attr.aria-selected]="tab() === 'providers'" (click)="tab.set('providers')">Providers</button>
        <button role="tab" [attr.aria-selected]="tab() === 'fees'" (click)="tab.set('fees')">Fee schedule</button>
      </div>

      <section class="panel" role="tabpanel">
        <div class="table-wrap">
          @switch (tab()) {
            @case ('members') {
              <table>
                <thead><tr><th scope="col">Member</th><th scope="col">Plan</th><th scope="col">Coverage</th><th scope="col">Deductible met</th><th scope="col">Out-of-pocket met</th></tr></thead>
                <tbody>
                  @for (m of members(); track m.id) {
                    <tr>
                      <td>{{ m.fullName }}<div class="muted small code">{{ m.memberNumber }}</div></td>
                      <td>{{ m.planName }}</td>
                      <td>
                        {{ m.coverageStart | date: 'MMM d, y' }} to {{ m.coverageEnd ? (m.coverageEnd | date: 'MMM d, y') : 'present' }}
                        @if (m.coverageEnd) { <div class="small ended">Coverage ended</div> }
                      </td>
                      <td>
                        <div class="meter" [attr.aria-label]="m.deductibleMet + ' of ' + m.annualDeductible"><span [style.width.%]="pct(m.deductibleMet, m.annualDeductible)"></span></div>
                        <div class="small muted">{{ m.deductibleMet | usd }} of {{ m.annualDeductible | usd }}</div>
                      </td>
                      <td>
                        <div class="meter" [attr.aria-label]="m.outOfPocketMet + ' of ' + m.outOfPocketMax"><span [style.width.%]="pct(m.outOfPocketMet, m.outOfPocketMax)"></span></div>
                        <div class="small muted">{{ m.outOfPocketMet | usd }} of {{ m.outOfPocketMax | usd }}</div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
            @case ('providers') {
              <table>
                <thead><tr><th scope="col">Provider</th><th scope="col">NPI</th><th scope="col">Specialty</th><th scope="col">Network</th><th scope="col">Status</th></tr></thead>
                <tbody>
                  @for (p of providers(); track p.id) {
                    <tr>
                      <td>{{ p.name }}</td>
                      <td class="code">{{ p.npi }}</td>
                      <td>{{ p.specialty }}</td>
                      <td [class.warn]="!p.isInNetwork">{{ p.isInNetwork ? 'In network' : 'Out of network' }}</td>
                      <td [class.ended]="!p.isActive">{{ p.isActive ? 'Active' : 'Inactive' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
            @case ('fees') {
              <table>
                <thead><tr><th scope="col">Code</th><th scope="col">Description</th><th scope="col" class="num">Allowed</th><th scope="col">Pricing</th><th scope="col">Prior auth</th></tr></thead>
                <tbody>
                  @for (f of fees(); track f.procedureCode) {
                    <tr>
                      <td class="code">{{ f.procedureCode }}</td>
                      <td>{{ f.description }}</td>
                      <td class="num">{{ f.allowedAmount | usd }}</td>
                      <td>{{ f.isOfficeVisit ? 'Copay' : 'Deductible + coinsurance' }}</td>
                      <td [class.warn]="f.requiresPriorAuthorization">{{ f.requiresPriorAuthorization ? 'Required' : 'Not required' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          }
        </div>
      </section>
    </div>
  `,
  styles: `
    .tabs { display: flex; gap: 0.25rem; border-bottom: 1px solid var(--line); margin-bottom: 1rem; }
    .tabs button { font: inherit; font-weight: 600; background: none; border: 0; border-bottom: 3px solid transparent; padding: 0.6rem 0.9rem; color: var(--ink-soft); cursor: pointer; margin-bottom: -1px; }
    .tabs button[aria-selected='true'] { color: var(--ink); border-bottom-color: var(--teal); }
    .small { font-size: 0.8125rem; }
    .ended { color: var(--red); }
    .warn { color: var(--amber); font-weight: 600; }
    .meter { width: 9rem; height: 0.4rem; background: var(--line); border-radius: 999px; overflow: hidden; margin: 0.45rem 0 0.25rem; }
    .meter span { display: block; height: 100%; background: var(--teal); }
  `
})
export class ReferenceComponent {
  private readonly api = inject(ApiService);
  protected readonly tab = signal<Tab>('members');
  protected readonly members = toSignal(this.api.members(), { initialValue: [] as MemberDto[] });
  protected readonly providers = toSignal(this.api.providers(), { initialValue: [] as ProviderDto[] });
  protected readonly fees = toSignal(this.api.feeSchedule(), { initialValue: [] as FeeScheduleDto[] });

  protected pct(value: number, max: number): number {
    return max ? Math.min(100, (value / max) * 100) : 0;
  }
}
