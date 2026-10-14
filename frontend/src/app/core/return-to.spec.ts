import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ReturnTo } from './return-to';

@Component({ template: '' })
class Blank {}

describe('return to', () => {
  it('goes back to the page that led into review, not the previous receipt', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: '**', component: Blank }])] });
    const returnTo = TestBed.inject(ReturnTo);
    const router = TestBed.inject(Router);
    expect(returnTo.label()).toBe('Back to queue');
    await router.navigateByUrl('/summary');
    await router.navigateByUrl('/review/a');
    expect(returnTo.label()).toBe('Back to tax summary');
    await router.navigateByUrl('/review/b');
    returnTo.back();
    await new Promise((resolve) => setTimeout(resolve));
    expect(router.url).toBe('/summary');
  });
});
