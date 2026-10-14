import { inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { Session } from './core/session';
import { InboxPage } from './pages/inbox-page';

// The shell only renders routes for a signed-in account; the API still enforces every permission.
const administrator = async () => {
  const session = inject(Session);
  const home = inject(Router).parseUrl('/');
  await session.ready;
  return session.account()?.isAdministrator || home;
};
const review = () => import('./pages/review-page').then((m) => m.ReviewPage);

export const routes: Routes = [
  { path: '', component: InboxPage, title: 'Inbox · KuTax' },
  { path: 'review', loadComponent: review, title: 'Review · KuTax' },
  { path: 'review/quick', loadComponent: () => import('./pages/quick-review-page').then((m) => m.QuickReviewPage), title: 'Quick review · KuTax' },
  { path: 'review/:id', loadComponent: review, title: 'Review receipt · KuTax' },
  { path: 'summary', loadComponent: () => import('./pages/summary-page').then((m) => m.SummaryPage), title: 'Tax · KuTax' },
  { path: 'history', loadComponent: () => import('./pages/history-page').then((m) => m.HistoryPage), title: 'History · KuTax' },
  { path: 'history/:id', loadComponent: () => import('./pages/history-detail-page').then((m) => m.HistoryDetailPage), title: 'Tax pack · KuTax' },
  { path: 'reports', loadComponent: () => import('./pages/reports-page').then((m) => m.ReportsPage), title: 'Reports · KuTax' },
  { path: 'help', loadComponent: () => import('./pages/help-page').then((m) => m.HelpPage), title: 'Help · KuTax' },
  { path: 'invite/:token', loadComponent: () => import('./pages/invite-page').then((m) => m.InvitePage), title: 'Invitation · KuTax' },
  { path: 'settings', loadComponent: () => import('./pages/settings-page').then((m) => m.SettingsPage), title: 'Settings · KuTax' },
  { path: 'rules', loadComponent: () => import('./management/rule-packs-page').then((m) => m.RulePacksPage), canActivate: [administrator], title: 'Tax rules · KuTax' },
  { path: 'rules/:id', loadComponent: () => import('./management/rule-packs-page').then((m) => m.RulePacksPage), canActivate: [administrator], title: 'Rule update · KuTax' },
  {
    path: 'users',
    loadComponent: () => import('./management/user-management').then((m) => m.UserManagement),
    canActivate: [administrator],
    title: 'Users · KuTax',
  },
  { path: '**', redirectTo: '' },
];
