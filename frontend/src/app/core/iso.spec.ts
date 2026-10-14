import { box, ring } from './iso';

describe('building block chart geometry', () => {
  it('keeps a box exactly as tall as its value, with depth drawn up and to the right', () => {
    const b = box(10, 100, 20, 40, 14, -9);
    expect(b.front).toEqual({ x: 10, y: 60, w: 20, h: 40 });
    expect(b.top).toBe('10,60 24,51 44,51 30,60');
    expect(b.side).toBe('30,60 44,51 44,91 30,100');
  });

  it('splits a ring by value and draws only the walls you can see', () => {
    const slices = ring([{ key: 'a', value: 3, color: 'red' }, { key: 'b', value: 1, color: 'blue' }, { key: 'none', value: 0, color: 'grey' }],
      { cx: 100, cy: 60, rx: 80, ry: 40, irx: 40, iry: 20, depth: 10 });
    expect(slices.map((s) => s.key)).toEqual(['a', 'b']);
    // Three quarters from the top, clockwise: across the whole near side, so one outer wall; part of the far inner wall.
    expect(slices[0].outer.length).toBe(1);
    expect(slices[0].inner.length).toBe(1);
    // The last quarter sits at the back: no outer wall, only the inner wall seen through the hole.
    expect(slices[1].outer.length).toBe(0);
    expect(slices[1].inner.length).toBe(1);
    expect(slices[0].top).toMatch(/^M 100 20 A 80 40 0 0 1/);
  });

  it('draws nothing when there is nothing to show', () => {
    expect(ring([{ key: 'a', value: 0, color: 'red' }], { cx: 0, cy: 0, rx: 1, ry: 1, irx: 0.5, iry: 0.5, depth: 1 })).toEqual([]);
  });
});
