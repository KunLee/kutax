import { Injectable, signal } from '@angular/core';

/** "retro" is the KuTax2 Figma design (docs/figma/Kutax2) and the default; "classic" is the original Kutax1 look. */
export type ThemeName = 'retro' | 'classic';
const key = 'kutax.theme';
const themeColors: Record<ThemeName, string> = { retro: '#20c9c6', classic: '#f8f6ef' };

function stored(): ThemeName {
  try {
    return localStorage.getItem(key) === 'classic' ? 'classic' : 'retro';
  } catch {
    return 'retro'; // Storage can be unavailable (private mode); the default still works.
  }
}

function apply(theme: ThemeName) {
  document.documentElement.dataset['theme'] = theme;
  document.documentElement.classList.toggle('theme-retro', theme === 'retro');
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', themeColors[theme]);
}

/** Called before Angular starts so the first paint already uses the chosen theme. */
export function applyStoredTheme() {
  apply(stored());
}

/** The person's chosen look, remembered on this device. */
@Injectable({ providedIn: 'root' })
export class Theme {
  readonly current = signal<ThemeName>(stored());

  set(theme: ThemeName) {
    this.current.set(theme);
    apply(theme);
    try {
      localStorage.setItem(key, theme);
    } catch {
      // Not remembered on this device, but still applied for this visit.
    }
  }
}
