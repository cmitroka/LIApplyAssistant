import { appendFileSync, existsSync, readFileSync, writeFileSync } from 'node:fs';

const summaryPath = 'coverage/jobgrid/coverage-summary.json';

if (!existsSync(summaryPath)) {
  console.log('No coverage summary was produced.');
  process.exit(0);
}

const total = JSON.parse(readFileSync(summaryPath, 'utf8')).total;

function row(label, key) {
  const metric = total[key];
  return `| ${label} | ${metric.pct}% (${metric.covered}/${metric.total}) |`;
}

const markdown = [
  '<!-- coverage-summary -->',
  '### Unit test coverage',
  '',
  '| Metric | Coverage |',
  '| --- | --- |',
  row('Statements', 'statements'),
  row('Branches', 'branches'),
  row('Functions', 'functions'),
  row('Lines', 'lines'),
  '',
  'The HTML report and `lcov.info` are attached to this run as the `coverage-report` artifact.',
  'Thresholds are enforced in `web/karma.conf.js` (`coverageReporter.check`).',
  '',
].join('\n');

writeFileSync('coverage/summary.md', markdown);
console.log(markdown);

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${markdown}\n`);
}
