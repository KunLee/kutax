import { wholeCents } from './allocation-editor';

describe('allocation amounts', () => {
  it('accepts two-decimal amounts that floating point can’t store exactly', () => {
    // 71.9 × 100 is 7190.000000000001; these used to be refused as "more than two decimal places".
    for (const amount of [71.9, 103.59, 0.1 + 0.2, 141.97, 5211.14, 1]) expect(wholeCents(amount)).toBe(true);
    for (const amount of [1.005, 0.001, 12.345]) expect(wholeCents(amount)).toBe(false);
  });
});
