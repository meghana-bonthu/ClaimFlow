import { Component, computed, input, Pipe, PipeTransform } from '@angular/core';
import { ClaimStatus, FindingOutcome } from '../core/models';

const STATUS_LABELS: Record<ClaimStatus, string> = {
  Received: 'Received',
  InAdjudication: 'In adjudication',
  Pended: 'Pended',
  Approved: 'Approved',
  Denied: 'Denied',
  Paid: 'Paid'
};

export function statusLabel(status: ClaimStatus): string {
  return STATUS_LABELS[status] ?? status;
}

@Pipe({ name: 'statusLabel', standalone: true })
export class StatusLabelPipe implements PipeTransform {
  transform(status: ClaimStatus | null): string {
    return status ? statusLabel(status) : 'New';
  }
}

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

@Pipe({ name: 'usd', standalone: true })
export class UsdPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return usd.format(value ?? 0);
  }
}

/** ICD-10-CM codes are stored without the dot (X12 format); show them the way clinicians read them. */
@Pipe({ name: 'icd', standalone: true })
export class IcdPipe implements PipeTransform {
  transform(code: string): string {
    return code.length > 3 ? `${code.slice(0, 3)}.${code.slice(3)}` : code;
  }
}

/** Status shown with both a shape and a color so it never relies on color alone. */
@Component({
  selector: 'cf-status',
  standalone: true,
  template: `<span class="status" [attr.data-status]="status()"><span class="mark" aria-hidden="true"></span>{{ label() }}</span>`,
  styles: `
    .status { display: inline-flex; align-items: center; gap: 0.4rem; font-weight: 600; font-size: 0.8125rem; white-space: nowrap; color: var(--c); }
    .mark { width: 0.6rem; height: 0.6rem; background: var(--c); }
    [data-status='Received'] { --c: var(--ink-soft); } [data-status='Received'] .mark { border-radius: 50%; background: none; box-shadow: inset 0 0 0 2px var(--c); }
    [data-status='InAdjudication'] { --c: var(--teal); } [data-status='InAdjudication'] .mark { border-radius: 50%; }
    [data-status='Pended'] { --c: var(--amber); } [data-status='Pended'] .mark { transform: rotate(45deg); width: 0.5rem; height: 0.5rem; }
    [data-status='Approved'] { --c: var(--green); } [data-status='Approved'] .mark { border-radius: 50%; }
    [data-status='Denied'] { --c: var(--red); } [data-status='Denied'] .mark { border-radius: 1px; }
    [data-status='Paid'] { --c: var(--indigo); } [data-status='Paid'] .mark { clip-path: polygon(0 0, 100% 50%, 0 100%); }
  `
})
export class StatusComponent {
  readonly status = input.required<ClaimStatus>();
  readonly label = computed(() => statusLabel(this.status()));
}

@Component({
  selector: 'cf-outcome',
  standalone: true,
  template: `<span class="outcome" [attr.data-outcome]="outcome()" [attr.aria-label]="outcome()">{{ glyph() }}</span>`,
  styles: `
    .outcome { display: inline-grid; place-items: center; width: 1.5rem; height: 1.5rem; border-radius: 50%; font-weight: 700; font-size: 0.8rem; color: #fff; }
    [data-outcome='Pass'] { background: var(--green); }
    [data-outcome='Pend'] { background: var(--amber); }
    [data-outcome='Deny'] { background: var(--red); }
  `
})
export class OutcomeComponent {
  readonly outcome = input.required<FindingOutcome>();
  readonly glyph = computed(() => ({ Pass: '✓', Pend: '!', Deny: '✕' })[this.outcome()]);
}
