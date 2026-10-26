import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'cf-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <a class="skip" href="#main">Skip to content</a>
    <div class="layout" [class.nav-open]="navOpen()">
      <aside class="rail">
        <div class="brand">
          <svg viewBox="0 0 32 32" width="28" height="28" aria-hidden="true">
            <rect width="32" height="32" rx="7" fill="#0F6E6A" />
            <path d="M9 11h14M9 16h9M9 21h14" stroke="#F4F7F5" stroke-width="2.6" stroke-linecap="round" />
            <circle cx="23" cy="16" r="2.4" fill="#F2C14E" />
          </svg>
          <span>ClaimFlow</span>
          <button class="menu" type="button" (click)="navOpen.set(!navOpen())" [attr.aria-expanded]="navOpen()" aria-controls="nav">Menu</button>
        </div>

        <nav id="nav" aria-label="Main">
          <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
          <a routerLink="/claims" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Claims</a>
          @if (auth.canSubmit()) {
            <a routerLink="/claims/new" routerLinkActive="active">New claim</a>
            <a routerLink="/claims/upload" routerLinkActive="active">Upload 837 file</a>
          }
          <a routerLink="/reference" routerLinkActive="active">Members &amp; providers</a>
        </nav>

        @if (auth.user(); as user) {
          <div class="who">
            <div class="who-name">{{ user.displayName }}</div>
            <div class="who-role">{{ user.role }} · {{ user.email }}</div>
            <button type="button" class="signout" (click)="auth.logout()">Sign out</button>
          </div>
        }
      </aside>

      <main id="main" tabindex="-1">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    .skip { position: absolute; left: -999px; top: 0.5rem; background: var(--gold); color: var(--ink); padding: 0.5rem 1rem; z-index: 10; }
    .skip:focus { left: 0.5rem; }
    .layout { display: grid; grid-template-columns: 240px 1fr; min-height: 100vh; background: linear-gradient(to right, var(--rail) 240px, var(--paper) 240px); }
    .rail { background: var(--rail); color: var(--rail-ink); display: flex; flex-direction: column; position: sticky; top: 0; height: 100vh; }
    .brand { display: flex; align-items: center; gap: 0.6rem; padding: 1.25rem 1.25rem 1.5rem; color: #fff; font-weight: 700; font-size: 1.125rem; }
    .menu { display: none; margin-left: auto; font: inherit; font-size: 0.875rem; background: none; color: var(--rail-ink); border: 1px solid #3a5054; border-radius: var(--radius-sm); padding: 0.3rem 0.7rem; }
    nav { display: grid; gap: 2px; padding: 0 0.75rem; }
    nav a { color: var(--rail-ink); text-decoration: none; padding: 0.55rem 0.75rem; border-radius: var(--radius-sm); font-weight: 500; border-left: 3px solid transparent; }
    nav a:hover { background: #20363a; color: #fff; }
    nav a.active { background: #20363a; color: #fff; border-left-color: var(--gold); }
    .who { margin-top: auto; padding: 1rem 1.25rem 1.25rem; border-top: 1px solid #2a3f43; font-size: 0.8125rem; }
    .who-name { color: #fff; font-weight: 600; }
    .who-role { color: #8fa3a0; overflow-wrap: anywhere; }
    .signout { margin-top: 0.6rem; font: inherit; font-weight: 600; background: none; border: 0; color: var(--gold); padding: 0; cursor: pointer; }
    main { min-width: 0; outline: none; }

    @media (max-width: 860px) {
      .layout { grid-template-columns: 1fr; background: var(--paper); }
      .rail { position: static; height: auto; }
      .brand { padding-bottom: 1.25rem; }
      .menu { display: inline-block; }
      nav, .who { display: none; }
      .nav-open nav { display: grid; padding-bottom: 0.75rem; }
      .nav-open .who { display: block; }
    }
  `
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly navOpen = signal(false);

  constructor() {
    inject(Router).events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.navOpen.set(false));
  }
}
