/**
 * Geometry for the "building block" (2.5D) charts: oblique boxes and a tilted ring with thickness. Heights stay
 * proportional to the values (no perspective), so the figures read as accurately as flat charts.
 */

export interface Point { x: number; y: number; }
/** One box: its front rectangle, the side and top faces as polygon points. */
export interface Box { front: { x: number; y: number; w: number; h: number }; side: string; top: string; }

const pts = (points: Point[]) => points.map((p) => `${round(p.x)},${round(p.y)}`).join(' ');
const round = (n: number) => Math.round(n * 100) / 100;

/** A box standing on `base`, `h` tall, with its depth drawn up and to the right by (dx, dy). */
export function box(x: number, base: number, w: number, h: number, dx: number, dy: number): Box {
  const top = base - h;
  return {
    front: { x: round(x), y: round(top), w: round(w), h: round(h) },
    side: pts([{ x: x + w, y: top }, { x: x + w + dx, y: top + dy }, { x: x + w + dx, y: base + dy }, { x: x + w, y: base }]),
    top: pts([{ x, y: top }, { x: x + dx, y: top + dy }, { x: x + w + dx, y: top + dy }, { x: x + w, y: top }]),
  };
}

/** The floor under a row of boxes. */
export function floor(x: number, base: number, width: number, dx: number, dy: number) {
  return pts([{ x, y: base }, { x: x + width, y: base }, { x: x + width + dx, y: base + dy }, { x: x + dx, y: base + dy }]);
}

export interface RingSlice { key: string; color: string; top: string; outer: string[]; inner: string[]; /** Middle of the slice's top face, for a tooltip. */ mid: Point; }
export interface Ring { cx: number; cy: number; rx: number; ry: number; irx: number; iry: number; depth: number; }

/**
 * A ring lying tilted, split into slices by value, clockwise from the top. Each slice has its top face, the outer
 * wall facing you and the inner wall seen through the hole (only the parts that are visible).
 */
export function ring(values: { key: string; value: number; color: string }[], r: Ring): RingSlice[] {
  const total = values.reduce((sum, v) => sum + Math.max(0, v.value), 0);
  if (total <= 0) return [];
  const at = (t: number, inner = false, down = 0): Point => ({
    x: r.cx + (inner ? r.irx : r.rx) * Math.cos(t),
    y: r.cy + (inner ? r.iry : r.ry) * Math.sin(t) + down,
  });
  const arc = (to: Point, inner: boolean, clockwise: boolean) =>
    `A ${inner ? r.irx : r.rx} ${inner ? r.iry : r.ry} 0 0 ${clockwise ? 1 : 0} ${round(to.x)} ${round(to.y)}`;
  const move = (p: Point) => `M ${round(p.x)} ${round(p.y)}`;
  const line = (p: Point) => `L ${round(p.x)} ${round(p.y)}`;
  // Pieces no wider than a quarter turn, so every arc is a small arc.
  const pieces = (a: number, b: number) => {
    const n = Math.max(1, Math.ceil((b - a) / (Math.PI / 2)));
    return Array.from({ length: n }, (_, i) => [a + ((b - a) * i) / n, a + ((b - a) * (i + 1)) / n] as const);
  };
  const clip = (a: number, b: number, lo: number, hi: number) => (Math.min(b, hi) > Math.max(a, lo) ? [[Math.max(a, lo), Math.min(b, hi)] as const] : []);
  const wall = (a: number, b: number, inner: boolean) =>
    pieces(a, b).map(([s, e]) => `${move(at(s, inner))} ${arc(at(e, inner), inner, true)} ${line(at(e, inner, r.depth))} ${arc(at(s, inner, r.depth), inner, false)} Z`).join(' ');

  let start = -Math.PI / 2;
  return values.filter((v) => v.value > 0).map((v) => {
    const a = start;
    const b = (start += (v.value / total) * Math.PI * 2);
    const top = pieces(a, b).map(([s, e]) => `${move(at(s))} ${arc(at(e), false, true)} ${line(at(e, true))} ${arc(at(s, true), true, false)} Z`).join(' ');
    // Outer wall shows on the near half (0..π); inner wall on the far half (-π/2..0 and π..3π/2).
    const outer = clip(a, b, 0, Math.PI).map(([s, e]) => wall(s, e, false));
    const inner = [...clip(a, b, -Math.PI / 2, 0), ...clip(a, b, Math.PI, Math.PI * 1.5)].map(([s, e]) => wall(s, e, true));
    const middle = (a + b) / 2;
    const mid = { x: round(r.cx + ((r.rx + r.irx) / 2) * Math.cos(middle)), y: round(r.cy + ((r.ry + r.iry) / 2) * Math.sin(middle)) };
    return { key: v.key, color: v.color, top, outer, inner, mid };
  });
}
