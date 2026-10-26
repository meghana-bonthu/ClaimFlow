import { HttpErrorResponse } from '@angular/common/http';
import { describeError } from './api.service';

describe('describeError', () => {
  it('flattens validation problem details into field messages', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { title: 'Validation failed', errors: { providerNpi: ['NPI must be exactly 10 digits.'], lines: ['Lines must not be empty.'] } }
    });
    const result = describeError(error);
    expect(result.message).toBe('Validation failed');
    expect(result.fields).toEqual(['NPI must be exactly 10 digits.', 'Lines must not be empty.']);
  });

  it('prefers the problem detail for conflicts', () => {
    const error = new HttpErrorResponse({ status: 409, error: { title: 'Conflict', detail: 'Claim is Approved; only pended claims can be reviewed.' } });
    expect(describeError(error).message).toContain('only pended claims');
  });

  it('explains network failures and forbidden actions', () => {
    expect(describeError(new HttpErrorResponse({ status: 0 })).message).toContain('not reachable');
    expect(describeError(new HttpErrorResponse({ status: 403 })).message).toContain('role');
  });
});
