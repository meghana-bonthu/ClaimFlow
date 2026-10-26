import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ApiService, describeError } from '../core/api.service';
import { FeeScheduleDto, MemberDto, ProviderDto } from '../core/models';

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

@Component({
  selector: 'cf-submit-claim',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe],
  template: `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>New professional claim</h1>
          <p>The claim is accepted right away, then checked by the rules engine and priced against the member's plan in the background.</p>
        </div>
      </header>

      <form [formGroup]="form" (ngSubmit)="submit()" class="layout" novalidate>
        <section class="panel">
          <div class="panel-head"><h2>Patient and provider</h2></div>
          <div class="panel-body grid2">
            <div class="field">
              <label for="member">Member</label>
              <select id="member" formControlName="memberNumber" required>
                <option value="" disabled>Choose a member</option>
                @for (m of members(); track m.id) {
                  <option [value]="m.memberNumber">{{ m.memberNumber }} · {{ m.fullName }} ({{ m.planName }})</option>
                }
              </select>
            </div>
            <div class="field">
              <label for="provider">Billing provider</label>
              <select id="provider" formControlName="providerNpi" required>
                <option value="" disabled>Choose a provider</option>
                @for (p of providers(); track p.id) {
                  <option [value]="p.npi">{{ p.name }} · NPI {{ p.npi }}{{ p.isActive ? '' : ' (inactive)' }}{{ p.isInNetwork ? '' : ' (out of network)' }}</option>
                }
              </select>
            </div>
            <div class="field">
              <label for="dx">Diagnosis codes</label>
              <input id="dx" formControlName="diagnosisCodes" placeholder="J06.9, R50.9" required />
              <span class="hint">ICD-10-CM, separated by commas. Up to 12.</span>
            </div>
            <div class="field">
              <label for="pcn">Patient control number</label>
              <input id="pcn" formControlName="patientControlNumber" maxlength="38" />
              <span class="hint">Your own reference. Reusing one is rejected as a duplicate submission.</span>
            </div>
          </div>
        </section>

        <section class="panel">
          <div class="panel-head">
            <h2>Service lines</h2>
            <button class="btn btn-secondary" type="button" (click)="addLine()" [disabled]="lines.length >= 50">Add line</button>
          </div>
          <div class="table-wrap">
            <table formArrayName="lines">
              <thead>
                <tr>
                  <th scope="col">Line</th>
                  <th scope="col">Procedure</th>
                  <th scope="col">Modifier</th>
                  <th scope="col">Service date</th>
                  <th scope="col">Units</th>
                  <th scope="col">Charge</th>
                  <th scope="col"><span class="sr">Remove</span></th>
                </tr>
              </thead>
              <tbody>
                @for (line of lines.controls; track line; let i = $index) {
                  <tr [formGroupName]="i">
                    <td class="n">{{ i + 1 }}</td>
                    <td>
                      <input formControlName="procedureCode" list="cpt" maxlength="5" [attr.aria-label]="'Procedure code, line ' + (i + 1)" (change)="suggestCharge(i)" />
                    </td>
                    <td><input formControlName="modifier" maxlength="2" class="short" [attr.aria-label]="'Modifier, line ' + (i + 1)" /></td>
                    <td><input type="date" formControlName="serviceDate" [max]="maxDate" [attr.aria-label]="'Service date, line ' + (i + 1)" /></td>
                    <td><input type="number" formControlName="units" min="1" max="999" class="short" [attr.aria-label]="'Units, line ' + (i + 1)" /></td>
                    <td><input type="number" formControlName="billedAmount" min="0.01" step="0.01" [attr.aria-label]="'Charge, line ' + (i + 1)" /></td>
                    <td>
                      <button class="btn btn-ghost" type="button" (click)="removeLine(i)" [disabled]="lines.length === 1">Remove</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
            <datalist id="cpt">
              @for (f of fees(); track f.procedureCode) {
                <option [value]="f.procedureCode">{{ f.description }}</option>
              }
            </datalist>
          </div>
          <div class="total">
            <span class="muted">Total charge</span>
            <strong>{{ total() | currency: 'USD' }}</strong>
          </div>
        </section>

        @if (error()) {
          <div class="alert alert-error" role="alert">
            {{ error() }}
            @if (fieldErrors().length) {
              <ul>
                @for (f of fieldErrors(); track f) { <li>{{ f }}</li> }
              </ul>
            }
          </div>
        }

        <div class="submit">
          <a class="btn btn-secondary" routerLink="/claims">Cancel</a>
          <button class="btn btn-primary" type="submit" [disabled]="busy()">{{ busy() ? 'Submitting…' : 'Submit claim' }}</button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .layout { display: grid; gap: 1.5rem; max-width: 1000px; }
    .grid2 { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.25rem; }
    td { vertical-align: middle; }
    td.n { color: var(--ink-soft); }
    input.short { max-width: 5.5rem; }
    .total { display: flex; justify-content: flex-end; gap: 1rem; align-items: baseline; padding: 1rem 1.25rem; border-top: 1px solid var(--line); font-size: 1.125rem; }
    .submit { display: flex; justify-content: flex-end; gap: 0.5rem; }
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    @media (max-width: 700px) { .grid2 { grid-template-columns: 1fr; } }
  `
})
export class SubmitClaimComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly members = toSignal(this.api.members(), { initialValue: [] as MemberDto[] });
  protected readonly providers = toSignal(this.api.providers(), { initialValue: [] as ProviderDto[] });
  protected readonly fees = toSignal(this.api.feeSchedule(), { initialValue: [] as FeeScheduleDto[] });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<string[]>([]);
  protected readonly maxDate = today();

  protected readonly form = this.fb.nonNullable.group({
    memberNumber: ['', Validators.required],
    providerNpi: ['', Validators.required],
    diagnosisCodes: ['', Validators.required],
    patientControlNumber: [''],
    lines: this.fb.array([this.newLine()])
  });

  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly total = computed(() => (this.formValue().lines ?? []).reduce((sum, l) => sum + (Number(l?.billedAmount) || 0), 0));

  get lines(): FormArray {
    return this.form.controls.lines;
  }

  private newLine() {
    return this.fb.nonNullable.group({
      procedureCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9]{5}$/)]],
      modifier: ['', Validators.pattern(/^[A-Za-z0-9]{2}$/)],
      serviceDate: [today(), Validators.required],
      units: [1, [Validators.required, Validators.min(1), Validators.max(999)]],
      billedAmount: [0, [Validators.required, Validators.min(0.01)]]
    });
  }

  addLine(): void {
    const last = this.lines.at(this.lines.length - 1)?.value;
    const line = this.newLine();
    if (last?.serviceDate) line.patchValue({ serviceDate: last.serviceDate });
    this.lines.push(line);
  }

  removeLine(index: number): void {
    this.lines.removeAt(index);
  }

  /** Pre-fill a typical charge (fee schedule × 1.6) so the demo is quick to use; providers bill above allowed. */
  suggestCharge(index: number): void {
    const group = this.lines.at(index);
    const fee = this.fees().find((f) => f.procedureCode === String(group.value.procedureCode).toUpperCase());
    if (fee && !Number(group.value.billedAmount)) group.patchValue({ billedAmount: Math.round(fee.allowedAmount * 1.6) });
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.error.set('Check the highlighted fields: every line needs a 5-character procedure code, a date and a charge.');
      this.fieldErrors.set([]);
      return;
    }

    const v = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.api
      .submitClaim({
        memberNumber: v.memberNumber,
        providerNpi: v.providerNpi,
        patientControlNumber: v.patientControlNumber.trim() || null,
        diagnosisCodes: v.diagnosisCodes.split(/[,\s]+/).map((d) => d.trim()).filter(Boolean),
        lines: v.lines.map((l) => ({
          procedureCode: l.procedureCode.toUpperCase(),
          modifier: l.modifier.trim() ? l.modifier.toUpperCase() : null,
          serviceDate: l.serviceDate,
          units: Number(l.units),
          billedAmount: Number(l.billedAmount)
        }))
      })
      .subscribe({
        next: (r) => this.router.navigate(['/claims', r.id]),
        error: (e) => {
          const { message, fields } = describeError(e);
          this.error.set(message);
          this.fieldErrors.set(fields);
          this.busy.set(false);
        }
      });
  }
}
