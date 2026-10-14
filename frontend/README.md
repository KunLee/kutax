# KuTax frontend

Angular standalone SPA. Follow the [repository quick start](../README.md) to configure the API and dependencies.

Use Node.js 26.11.1 (`.nvmrc`) and npm 12.2.0 (`packageManager`), then `npm ci` and `npm start`. The dev server is `http://localhost:4200` and proxies `/api` to `http://localhost:5080`. `npm run build` writes production assets to `dist/kutax/browser`; the application Dockerfile copies these into ASP.NET Core's `wwwroot`. See the [version baseline](../docs/architecture/DEPENDENCIES.md) for the Angular/TypeScript compatibility constraint.

`npm test` runs the component/API-service tests once. The browser uses same-origin Identity cookies, Angular's built-in XSRF cookie/header support, and refreshes its antiforgery token whenever the authentication identity changes. No credentials or service keys are bundled in the SPA.
