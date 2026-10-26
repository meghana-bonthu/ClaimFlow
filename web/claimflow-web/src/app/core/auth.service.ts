import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, tap, throwError } from 'rxjs';
import { LoginResponse, Role } from './models';

const STORAGE_KEY = 'claimflow.session';

/**
 * Holds the JWT session. sessionStorage (not localStorage) so the token is dropped when the tab closes —
 * a sensible default for an app that displays PHI.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<LoginResponse | null>(this.restore());

  readonly user = this.session.asReadonly();
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly role = computed(() => this.session()?.role ?? null);
  readonly canSubmit = computed(() => this.hasRole('Submitter', 'Admin'));
  readonly canReview = computed(() => this.hasRole('Adjudicator', 'Admin'));
  readonly canPay = computed(() => this.hasRole('Admin'));
  readonly canAudit = computed(() => this.hasRole('Adjudicator', 'Admin'));

  get token(): string | null {
    return this.session()?.token ?? null;
  }

  login(email: string, password: string) {
    return this.http.post<LoginResponse>('/api/auth/login', { email, password }).pipe(
      tap((response) => {
        sessionStorage.setItem(STORAGE_KEY, JSON.stringify(response));
        this.session.set(response);
      })
    );
  }

  logout(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
    this.router.navigate(['/login']);
  }

  hasRole(...roles: Role[]): boolean {
    const role = this.session()?.role;
    return !!role && roles.includes(role);
  }

  private restore(): LoginResponse | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as LoginResponse;
      return new Date(session.expiresAt) > new Date() ? session : null;
    } catch {
      return null;
    }
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token;
  const request = token && req.url.startsWith('/api') ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !req.url.endsWith('/auth/login')) auth.logout();
      return throwError(() => error);
    })
  );
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/login']);
};

export const roleGuard =
  (...roles: Role[]): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    return auth.hasRole(...roles) ? true : inject(Router).createUrlTree(['/dashboard']);
  };
