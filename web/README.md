# Jobgrid

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 19.2.27.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

Unit tests use Karma and Jasmine. `npm test` watches in Chrome. CI and local headless runs:

```bash
npm run test:ci
```

That command uses Chrome headless, writes `coverage/`, and fails if coverage drops below the thresholds in `karma.conf.js`.

## Linting and formatting

```bash
npm run lint
npm run format:check
npm run format
```

ESLint is [angular-eslint](https://github.com/angular-eslint/angular-eslint). Prettier owns formatting. A Husky pre-commit hook runs both on staged files after `npm install` (from this directory).

## Running end-to-end tests

Playwright smoke tests run against a production build served as a single-page app:

```bash
npm run e2e:install   # once per machine
npm run build -- --configuration=production
npm run e2e
```

The pipeline, required status checks, and Azure deploy are documented in the repository README under **Engineering practices**.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
