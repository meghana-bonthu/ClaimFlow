import { IcdPipe, StatusLabelPipe, UsdPipe, statusLabel } from './ui';

describe('display pipes', () => {
  it('formats ICD-10-CM codes with a decimal point after the category', () => {
    const pipe = new IcdPipe();
    expect(pipe.transform('J069')).toBe('J06.9');
    expect(pipe.transform('S72001A')).toBe('S72.001A');
    expect(pipe.transform('R51')).toBe('R51');
  });

  it('formats money as US dollars', () => {
    expect(new UsdPipe().transform(1234.5)).toBe('$1,234.50');
    expect(new UsdPipe().transform(null)).toBe('$0.00');
  });

  it('uses readable status labels', () => {
    expect(statusLabel('InAdjudication')).toBe('In adjudication');
    expect(new StatusLabelPipe().transform(null)).toBe('New');
  });
});
