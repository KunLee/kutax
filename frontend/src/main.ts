import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { applyStoredTheme } from './app/core/theme';
import { applyStoredLanguage } from './app/core/i18n';

applyStoredTheme();
applyStoredLanguage().finally(() => bootstrapApplication(App, appConfig).catch((err) => console.error(err)));
