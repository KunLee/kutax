import { Component, input } from '@angular/core';

export type IconName =
  | 'archive'
  | 'arrow'
  | 'back'
  | 'calendar'
  | 'camera'
  | 'check'
  | 'chevron'
  | 'clock'
  | 'close'
  | 'download'
  | 'file'
  | 'folder'
  | 'help'
  | 'home'
  | 'image'
  | 'logout'
  | 'more'
  | 'plus'
  | 'report'
  | 'settings'
  | 'shield'
  | 'spark'
  | 'upload'
  | 'users'
  | 'warning';

// Stroke paths from the KuTax Figma kit (docs/figma), plus a few app-only glyphs.
@Component({
  selector: 'app-icon',
  host: { style: 'display:inline-flex' },
  template: `<svg class="icon" aria-hidden="true" fill="none" viewBox="0 0 24 24" [attr.width]="size()" [attr.height]="size()">
    @switch (name()) {
      @case ('archive') { <path d="M5 8v10h14V8" /><path d="M3 4h18v4H3zM9 12h6" /> }
      @case ('arrow') { <path d="m9 18 6-6-6-6" /> }
      @case ('back') { <path d="m15 18-6-6 6-6" /> }
      @case ('calendar') { <rect x="4" y="5" width="16" height="15" rx="2" /><path d="M4 10h16M8 3v4m8-4v4" /> }
      @case ('camera') { <path d="M4 8h3l2-3h6l2 3h3v11H4z" /><circle cx="12" cy="13" r="3.5" /> }
      @case ('check') { <path d="m5 12 4 4L19 6" /> }
      @case ('chevron') { <path d="m8 10 4 4 4-4" /> }
      @case ('clock') { <circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /> }
      @case ('close') { <path d="m7 7 10 10M17 7 7 17" /> }
      @case ('download') { <path d="M12 3v12m-5-5 5 5 5-5" /><path d="M5 20h14" /> }
      @case ('file') { <path d="M6 3h8l4 4v14H6z" /><path d="M14 3v5h4M9 13h6M9 17h5" /> }
      @case ('folder') { <path d="M3 6h7l2 2h9v11H3z" /> }
      @case ('help') { <circle cx="12" cy="12" r="9" /><path d="M9.8 9a2.3 2.3 0 1 1 3.2 2.1c-.7.4-1 1-1 1.9M12 17h.01" /> }
      @case ('home') { <path d="M4 11 12 4l8 7v9h-5v-6H9v6H4z" /> }
      @case ('image') { <rect x="3" y="4" width="18" height="16" rx="3" /><circle cx="9" cy="10" r="2" /><path d="m5 18 5-5 3 3 2-2 4 4" /> }
      @case ('logout') { <path d="M14 4h5v16h-5M10 8l-4 4 4 4M6 12h10" /> }
      @case ('more') { <circle cx="5" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="19" cy="12" r="1" fill="currentColor" stroke="none" /> }
      @case ('plus') { <path d="M12 5v14M5 12h14" /> }
      @case ('report') { <path d="M6 20V10m6 10V4m6 16v-7" /><path d="M3 20h18" /> }
      @case ('settings') { <circle cx="12" cy="12" r="3" /><path d="M12 3v2m0 14v2M3 12h2m14 0h2M5.6 5.6 7 7m10 10 1.4 1.4m0-12.8L17 7M7 17l-1.4 1.4" /> }
      @case ('shield') { <path d="M12 3 5 6v6c0 4 3 7 7 9 4-2 7-5 7-9V6z" /><path d="m9 12 2 2 4-4" /> }
      @case ('spark') { <path d="m12 3 1.5 5.5L19 10l-5.5 1.5L12 17l-1.5-5.5L5 10l5.5-1.5z" /> }
      @case ('upload') { <path d="M12 16V5m-5 5 5-5 5 5" /><path d="M5 19h14" /> }
      @case ('users') { <circle cx="9" cy="8" r="3.5" /><path d="M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6M16 4.5a3.5 3.5 0 0 1 0 7M18 14c2 .8 3 2.8 3 6" /> }
      @case ('warning') { <path d="M12 4 2.5 20h19z" /><path d="M12 10v4M12 17h.01" /> }
    }
  </svg>`,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(20);
}
