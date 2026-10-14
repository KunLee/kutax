import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { DateField, parseDate } from './date-field';

@Component({ imports: [DateField, FormsModule], template: `<app-date-field inputId="d" name="d" [(ngModel)]="date" max="2026-12-31" />` })
class Host { date = '2026-08-26'; }

describe('date field', () => {
  beforeEach(() => localStorage.setItem('kutax.language', 'en'));

  it('reads the ways people type dates and rejects impossible ones', () => {
    expect(parseDate('26/08/2026')).toBe('2026-08-26');
    expect(parseDate('1-8-2026')).toBe('2026-08-01');
    expect(parseDate('2026/8/1')).toBe('2026-08-01');
    expect(parseDate('2026年8月1日')).toBe('2026-08-01');
    expect(parseDate('31/02/2026')).toBeNull();
    expect(parseDate('next week')).toBeNull();
  });

  it('shows the date, picks a new one from the calendar and blocks later dates', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.querySelector<HTMLInputElement>('#d')!.value).toBe('26/08/2026');
    page.querySelector<HTMLButtonElement>('button.open')!.click();
    fixture.detectChanges();
    expect(page.querySelector('.month strong')!.textContent).toBe('August 2026');
    // Weeks start on Monday: 1 August 2026 is a Saturday, so the grid opens on Monday 27 July.
    expect(page.querySelector<HTMLElement>('.day')!.dataset['date']).toBe('2026-07-27');
    page.querySelector<HTMLButtonElement>('.day[data-date="2026-08-03"]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.date).toBe('2026-08-03');
    expect(page.querySelector('.calendar')).toBeNull();
    page.querySelector<HTMLButtonElement>('button.open')!.click();
    fixture.detectChanges();
    page.querySelectorAll<HTMLButtonElement>('.step')[1].click();
    page.querySelectorAll<HTMLButtonElement>('.step')[1].click();
    page.querySelectorAll<HTMLButtonElement>('.step')[1].click();
    page.querySelectorAll<HTMLButtonElement>('.step')[1].click();
    page.querySelectorAll<HTMLButtonElement>('.step')[1].click();
    fixture.detectChanges();
    expect(page.querySelector<HTMLButtonElement>('.day[data-date="2027-01-04"]')!.disabled).toBe(true);
  });
});
