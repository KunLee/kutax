import { Component, ElementRef, OnDestroy, computed, effect, input, signal, viewChild } from '@angular/core';
import type { PDFDocumentLoadingTask } from 'pdfjs-dist';
import { TranslatePipe } from '../core/i18n';

const zoomLevels = [1, 1.5, 2, 3, 4];
// Large canvases exhaust GPU memory (Chrome then leaves areas unpainted); ~8 MP stays sharp and light.
const maxPixels = 8_000_000;

/** Zoomable view of a receipt original: PDFs render with PDF.js (no document scripts), photos as images. */
@Component({
  selector: 'app-document-viewer', imports: [TranslatePipe],
  template: `
    <div class="viewport" #viewport tabindex="0" role="region" [attr.aria-label]="'Original receipt, zoom ' + percent() + '%'">
      @if (isPdf()) {
        <canvas #canvas aria-hidden="true"></canvas>
      } @else {
        <img [src]="src()" alt="Original receipt" [style.width.%]="zoom() * 100" (load)="message.set('')" (error)="message.set('This image could not be displayed. Download the original instead.')" />
      }
    </div>
    <div class="controls">
      @if (isPdf() && pages() > 1) {
        <button class="icon-btn" type="button" (click)="turn(-1)" [disabled]="busy() || page() <= 1" [attr.aria-label]="'Previous page' | t">‹</button>
        <span class="pages">{{ page() }}/{{ pages() }}</span>
        <button class="icon-btn" type="button" (click)="turn(1)" [disabled]="busy() || page() >= pages()" [attr.aria-label]="'Next page' | t">›</button>
        <span class="divider" aria-hidden="true"></span>
      }
      <button class="icon-btn" type="button" (click)="step(-1)" [disabled]="zoomIndex() === 0" [attr.aria-label]="'Zoom out' | t">−</button>
      <button class="zoom" type="button" (click)="zoomIndex.set(0)" [attr.aria-label]="'Fit to width, currently ' + percent() + '%'">{{ percent() }}%</button>
      <button class="icon-btn" type="button" (click)="step(1)" [disabled]="zoomIndex() === levels.length - 1" [attr.aria-label]="'Zoom in' | t">+</button>
    </div>
    @if (message()) { <p class="message" role="status">{{ message() | t }}</p> }
  `,
  styles: `
    :host { display: flex; flex-direction: column; max-width: 100%; min-height: 0; min-width: 0; width: 100%; }
    .viewport { background: #fffdf5; box-sizing: border-box; flex: 1; max-width: 100%; min-width: 0; scrollbar-gutter: stable; width: 100%; max-height: var(--viewer-height, min(70vh, 640px)); min-height: 200px; overflow: auto; overscroll-behavior: contain; }
    .viewport:focus-visible { outline: 3px solid var(--purple); outline-offset: -3px; }
    canvas, img { display: block; margin: 0 auto; max-width: none; }
    img { height: auto; }
    .controls { align-items: center; background: var(--paper); display: flex; flex-wrap: wrap; gap: 6px; justify-content: center; padding: 8px; }
    .icon-btn { font-size: 20px; height: 40px; width: 40px; }
    .zoom { background: var(--white); border: 1px solid var(--line); border-radius: 10px; font-family: var(--mono); font-size: 13px; min-height: 40px; min-width: 64px; }
    .pages { font-size: 13px; min-width: 40px; text-align: center; }
    .divider { background: var(--line); height: 24px; margin: 0 4px; width: 1px; }
    .message { background: var(--paper); font-size: 13px; margin: 0; padding: 0 12px 10px; text-align: center; }
  `,
})
export class DocumentViewer implements OnDestroy {
  readonly documentId = input.required<string>();
  readonly contentType = input.required<string>();
  readonly levels = zoomLevels;
  readonly zoomIndex = signal(0);
  readonly zoom = computed(() => zoomLevels[this.zoomIndex()]);
  readonly percent = computed(() => Math.round(this.zoom() * 100));
  readonly isPdf = computed(() => this.contentType() === 'application/pdf');
  readonly src = computed(() => `/api/documents/${encodeURIComponent(this.documentId())}/content`);
  readonly page = signal(1);
  readonly pages = signal(0);
  readonly busy = signal(false);
  readonly message = signal('');
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly viewport = viewChild<ElementRef<HTMLDivElement>>('viewport');
  private task?: PDFDocumentLoadingTask;
  private generation = 0;
  private resize?: ResizeObserver;
  private lastWidth = 0;

  constructor() {
    effect((onCleanup) => {
      const id = this.documentId();
      if (!this.isPdf() || !this.canvas()) return;
      const generation = ++this.generation;
      this.message.set('Loading PDF…');
      void this.open(id, generation);
      onCleanup(() => {
        ++this.generation;
        void this.task?.destroy();
        this.task = undefined;
      });
    });
    // Re-render at the new zoom so text stays sharp, and when the container is resized (full screen, rotation).
    effect(() => {
      this.zoom();
      if (this.isPdf()) void this.draw();
    });
    effect(() => {
      const element = this.viewport()?.nativeElement;
      this.resize?.disconnect();
      if (!element || typeof ResizeObserver === 'undefined') return;
      this.resize = new ResizeObserver(() => {
        if (Math.abs(element.offsetWidth - this.lastWidth) > 8 && this.isPdf()) void this.draw();
      });
      this.resize.observe(element);
    });
  }

  ngOnDestroy() {
    this.resize?.disconnect();
  }

  /** Back to fit-to-width, e.g. when leaving full screen. */
  reset() {
    this.zoomIndex.set(0);
  }

  step(direction: number) {
    this.zoomIndex.update((index) => Math.min(zoomLevels.length - 1, Math.max(0, index + direction)));
  }

  async turn(direction: number) {
    this.page.update((page) => page + direction);
    await this.draw();
  }

  private async open(id: string, generation: number) {
    try {
      const pdf = await import('pdfjs-dist');
      if (generation !== this.generation) return;
      pdf.GlobalWorkerOptions.workerSrc = '/assets/pdfjs/pdf.worker.min.mjs';
      this.task = pdf.getDocument({ url: `/api/documents/${encodeURIComponent(id)}/content`, wasmUrl: '/assets/pdfjs/wasm/',
        standardFontDataUrl: '/assets/pdfjs/standard_fonts/', cMapUrl: '/assets/pdfjs/cmaps/', cMapPacked: true });
      const document = await this.task.promise;
      if (generation !== this.generation) return;
      if (document.numPages > 100) throw new Error('Page limit');
      this.pages.set(document.numPages);
      this.page.set(1);
      await this.draw();
    } catch {
      if (generation === this.generation) this.message.set('Preview unavailable. Download the original to read it.');
    }
  }

  private async draw() {
    const canvas = this.canvas()?.nativeElement;
    const viewport = this.viewport()?.nativeElement;
    if (!this.task || !canvas || !viewport) return;
    this.busy.set(true);
    try {
      const pdf = await this.task.promise;
      const page = await pdf.getPage(this.page());
      const natural = page.getViewport({ scale: 1 });
      // Track the outer width: it doesn't change when a scrollbar appears, so redraws can't feed back into resizes.
      this.lastWidth = viewport.offsetWidth;
      // Size from the visible box (never wider than the screen) so a zoomed page can't widen its own container.
      const visible = Math.min(viewport.clientWidth, document.documentElement.clientWidth);
      const cssWidth = Math.max(240, visible - 2) * this.zoom();
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      // Cap the bitmap so very high zoom stays within GPU and mobile memory limits.
      const scale = Math.min((cssWidth / natural.width) * ratio, 4096 / natural.width, 4096 / natural.height, Math.sqrt(maxPixels / (natural.width * natural.height)));
      const rendered = page.getViewport({ scale });
      canvas.width = 0; // release the previous bitmap before allocating the next
      canvas.width = Math.ceil(rendered.width);
      canvas.height = Math.ceil(rendered.height);
      canvas.style.width = `${cssWidth}px`;
      canvas.style.height = `${(cssWidth * natural.height) / natural.width}px`;
      await page.render({ canvas, viewport: rendered }).promise;
      this.message.set('');
    } catch {
      this.message.set('This page could not be displayed. Download the original.');
    } finally {
      this.busy.set(false);
    }
  }
}
