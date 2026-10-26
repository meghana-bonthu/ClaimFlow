import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { LoginResponse } from './models';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  const session = (role: LoginResponse['role']): LoginResponse => ({
    token: 'jwt',
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    email: `${role.toLowerCase()}@claimflow.dev`,
    displayName: role,
    role
  });

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts signed out', () => {
    expect(auth.isAuthenticated()).toBeFalse();
    expect(auth.canSubmit()).toBeFalse();
  });

  it('stores the session and derives permissions from the role', () => {
    auth.login('adjudicator@claimflow.dev', 'x').subscribe();
    http.expectOne('/api/auth/login').flush(session('Adjudicator'));

    expect(auth.isAuthenticated()).toBeTrue();
    expect(auth.canReview()).toBeTrue();
    expect(auth.canAudit()).toBeTrue();
    expect(auth.canSubmit()).toBeFalse();
    expect(auth.canPay()).toBeFalse();
    expect(sessionStorage.getItem('claimflow.session')).toContain('Adjudicator');
  });

  it('gives admins every permission and clears the session on logout', () => {
    auth.login('admin@claimflow.dev', 'x').subscribe();
    http.expectOne('/api/auth/login').flush(session('Admin'));
    expect(auth.canPay() && auth.canSubmit() && auth.canReview()).toBeTrue();

    auth.logout();
    expect(auth.isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem('claimflow.session')).toBeNull();
  });
});
