import { TestBed } from '@angular/core/testing';
import { applyStoredTheme, Theme } from './theme';

describe('theme', () => {
  beforeEach(() => localStorage.clear());

  it('defaults to the retro look and remembers a switch to classic', () => {
    applyStoredTheme();
    expect(document.documentElement.dataset['theme']).toBe('retro');
    const theme = TestBed.inject(Theme);
    expect(theme.current()).toBe('retro');
    theme.set('classic');
    expect(document.documentElement.dataset['theme']).toBe('classic');
    expect(document.documentElement.classList.contains('theme-retro')).toBe(false);
    expect(localStorage.getItem('kutax.theme')).toBe('classic');
    applyStoredTheme();
    expect(document.documentElement.dataset['theme']).toBe('classic');
  });
});
