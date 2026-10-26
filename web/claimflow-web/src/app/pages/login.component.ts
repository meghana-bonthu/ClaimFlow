import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { describeError } from '../core/api.service';
import { AuthService } from '../core/auth.service';

interface DemoAccount {
  role: string;
  email: string;
  password: string;
  can: string;
}

@Component({
  selector: 'cf-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="wrap">
      <section class="intro">
        <div class="brand">
          <svg viewBox="0 0 32 32" width="36" height="36" aria-hidden="true">
            <rect width="32" height="32" rx="7" fill="#0F6E6A" />
            <path d="M9 11h14M9 16h9M9 21h14" stroke="#F4F7F5" stroke-width="2.6" stroke-linecap="round" />
            <circle cx="23" cy="16" r="2.4" fill="#F2C14E" />
          </svg>
          ClaimFlow
        </div>
        <h1>From 837 to 835, with every decision explained.</h1>
        <p>
          Submit professional claims or X12 837 files, let the rules engine check eligibility, coverage, prior
          authorization and duplicates, then price each line against the member's benefits and release payment.
        </p>
        <p class="fine">Demo environment. All members, providers and claims are synthetic.</p>
      </section>

      <section class="card" aria-labelledby="signin">
        <h2 id="signin">Sign in</h2>
        <form (ngSubmit)="submit()" #f="ngForm">
          <div class="field">
            <label for="email">Email</label>
            <input id="email" name="email" type="email" autocomplete="username" required [(ngModel)]="email" />
          </div>
          <div class="field">
            <label for="password">Password</label>
            <input id="password" name="password" type="password" autocomplete="current-password" required [(ngModel)]="password" />
          </div>
          @if (error()) {
            <div class="alert alert-error" role="alert">{{ error() }}</div>
          }
          <button class="btn btn-primary" type="submit" [disabled]="busy() || !f.valid">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
        </form>

        <div class="demo">
          <h3>Demo accounts</h3>
          <p class="muted">Pick a role to fill in its credentials.</p>
          <ul>
            @for (account of accounts; track account.email) {
              <li>
                <button type="button" (click)="use(account)">
                  <span class="role">{{ account.role }}</span>
                  <span class="can">{{ account.can }}</span>
                </button>
              </li>
            }
          </ul>
        </div>
      </section>
    </div>
  `,
  styles: `
    .wrap { min-height: 100vh; display: grid; grid-template-columns: minmax(0, 1.1fr) minmax(0, 440px); gap: clamp(2rem, 6vw, 6rem); align-items: center; padding: clamp(1.5rem, 6vw, 5rem); background: var(--rail); }
    .intro { color: var(--rail-ink); max-width: 34rem; }
    .brand { display: flex; align-items: center; gap: 0.7rem; color: #fff; font-weight: 700; font-size: 1.25rem; margin-bottom: 3rem; }
    h1 { color: #fff; font-size: clamp(2rem, 4vw, 3rem); line-height: 1.1; letter-spacing: -0.025em; margin-bottom: 1.25rem; }
    .intro p { font-size: 1.0625rem; max-width: 52ch; }
    .fine { margin-top: 1.5rem; font-size: 0.875rem !important; color: #8fa3a0; }
    .card { background: var(--surface); border-radius: var(--radius); padding: 2rem; }
    h2 { font-size: 1.375rem; margin-bottom: 1.25rem; }
    form { display: grid; gap: 1rem; }
    form .btn { padding-block: 0.7rem; }
    .demo { margin-top: 1.75rem; padding-top: 1.25rem; border-top: 1px solid var(--line); }
    .demo .muted { font-size: 0.8125rem; margin: 0.15rem 0 0.75rem; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.5rem; }
    li button { width: 100%; text-align: left; font: inherit; background: var(--paper); border: 1px solid var(--line); border-radius: var(--radius-sm); padding: 0.6rem 0.8rem; cursor: pointer; display: grid; gap: 0.1rem; }
    li button:hover { border-color: var(--teal); }
    .role { font-weight: 600; }
    .can { font-size: 0.8125rem; color: var(--ink-soft); }
    @media (max-width: 860px) { .wrap { grid-template-columns: 1fr; } .brand { margin-bottom: 1.5rem; } }
  `
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly accounts: DemoAccount[] = [
    { role: 'Submitter', email: 'submitter@claimflow.dev', password: 'Submit#2026', can: 'Submits claims and 837 files' },
    { role: 'Adjudicator', email: 'adjudicator@claimflow.dev', password: 'Review#2026', can: 'Reviews pended claims, sees audit trails' },
    { role: 'Admin', email: 'admin@claimflow.dev', password: 'Admin#2026', can: 'Everything, including payment runs' }
  ];

  constructor() {
    if (this.auth.isAuthenticated()) this.router.navigate(['/dashboard']);
  }

  use(account: DemoAccount): void {
    this.email = account.email;
    this.password = account.password;
    this.error.set(null);
  }

  submit(): void {
    this.busy.set(true);
    this.error.set(null);
    this.auth.login(this.email, this.password).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: (e) => {
        this.error.set(describeError(e).message);
        this.busy.set(false);
      }
    });
  }
}
