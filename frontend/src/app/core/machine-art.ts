import { Component } from '@angular/core';
import { Icon } from './icon';

/** Decorative KuTax receipt machine from the Figma kit. Hidden from assistive technology. */
@Component({
  selector: 'app-machine-art',
  imports: [Icon],
  host: { 'aria-hidden': 'true' },
  template: `
    <div class="orbit orbit--one"></div>
    <div class="orbit orbit--two"></div>
    <div class="machine">
      <span class="light"></span><span class="slot"></span>
      <div class="screen"><app-icon name="upload" [size]="32" /></div>
      <div class="controls"><span></span><span></span><span></span></div>
    </div>
    <div class="paper paper--left"><app-icon name="file" /><span></span><span></span></div>
    <div class="paper paper--right"><app-icon name="image" /><span></span></div>
    <span class="spark spark--one">✦</span><span class="spark spark--two">✦</span>
  `,
  styles: `
    :host { display: block; height: 280px; position: relative; }
    .machine { background: var(--sky); border: 3px solid var(--ink); border-radius: 35px 35px 27px 27px;
      box-shadow: 9px 10px 0 rgba(37, 36, 58, 0.94); height: 190px; left: 50%; padding: 26px 25px; position: absolute;
      top: 46px; transform: translateX(-50%) rotate(-2deg); width: 196px; z-index: 3; }
    .light { background: var(--orange); border: 2px solid var(--ink); border-radius: 50%; height: 13px; position: absolute; right: 18px; top: 14px; width: 13px; }
    .slot { background: var(--ink); border-radius: 5px; height: 7px; left: 27px; position: absolute; top: 22px; width: 92px; }
    .screen { align-items: center; background: #fbfaf3; border: 2px solid var(--ink); border-radius: 17px; color: var(--purple);
      display: flex; height: 92px; justify-content: center; }
    .screen app-icon { animation: bounce 2s ease-in-out infinite; }
    .controls { display: flex; gap: 9px; margin-top: 13px; }
    .controls span { background: var(--purple); border: 2px solid var(--ink); border-radius: 50%; height: 12px; width: 12px; }
    .controls span:nth-child(2) { background: var(--yellow); }
    .controls span:nth-child(3) { background: var(--orange); border-radius: 6px; margin-left: auto; width: 28px; }
    .paper { background: var(--white); border: 2px solid var(--ink); border-radius: 10px; box-shadow: 4px 5px 0 var(--ink);
      color: var(--purple); height: 92px; padding: 13px; position: absolute; width: 70px; z-index: 4; }
    .paper span { background: var(--line); display: block; height: 3px; margin-top: 7px; }
    .paper--left { left: 6%; top: 35px; transform: rotate(-12deg); }
    .paper--right { background: var(--yellow); right: 6%; top: 143px; transform: rotate(9deg); }
    .orbit { border: 1.5px dashed rgba(52, 42, 92, 0.45); border-radius: 50%; left: 50%; position: absolute; transform: translateX(-50%); }
    .orbit--one { height: 245px; top: 16px; width: min(310px, 90%); }
    .orbit--two { height: 175px; top: 52px; width: min(350px, 100%); }
    .spark { color: var(--orange); position: absolute; }
    .spark--one { font-size: 22px; right: 7%; top: 48px; }
    .spark--two { bottom: 15px; color: var(--purple); font-size: 15px; left: 12%; }
    @keyframes bounce { 0%, 100% { transform: translateY(2px); } 50% { transform: translateY(-4px); } }
    @media (max-width: 700px) {
      :host { transform: scale(0.72); transform-origin: top center; margin-bottom: -78px; }
    }
  `,
})
export class MachineArt {}
