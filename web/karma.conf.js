// Karma configuration file, see link for more information
// https://karma-runner.github.io/1.0/config/configuration-file.html
//
// Coverage gates live in coverageReporter.check. Raise them as new tests land;
// do not lower them to get a red build green. See README "Engineering practices".

module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine', '@angular-devkit/build-angular'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
      require('@angular-devkit/build-angular/plugins/karma'),
    ],
    client: {
      jasmine: {},
      clearContext: false,
    },
    jasmineHtmlReporter: {
      suppressAll: true,
    },
    coverageReporter: {
      dir: require('path').join(__dirname, './coverage/jobgrid'),
      subdir: '.',
      reporters: [
        { type: 'html' },
        { type: 'text-summary' },
        { type: 'text' },
        { type: 'lcovonly' },
        { type: 'json-summary' },
      ],
      // Measured baseline with the starter specs (Nov 2026):
      // statements 99.18%, branches 90.32%, functions 100%, lines 99%.
      // Gates sit under that so a dropped test fails CI without flaking on one branch.
      check: {
        global: {
          statements: 90,
          branches: 80,
          functions: 95,
          lines: 90,
        },
      },
    },
    reporters: ['progress', 'kjhtml'],
    browsers: ['Chrome'],
    customLaunchers: {
      ChromeHeadlessCI: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },
    restartOnFileChange: true,
    singleRun: false,
  });
};
