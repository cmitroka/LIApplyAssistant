import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { JobDto } from '../../models/job';
import { JobGridComponent } from './job-grid.component';

describe('JobGridComponent', () => {
  let fixture: ComponentFixture<JobGridComponent>;
  let component: JobGridComponent;
  let http: HttpTestingController;

  const sampleJobs: JobDto[] = [
    {
      jobKey: 'JOB1',
      jobTitle: 'Director - AI-First Development',
      company: 'Las Vegas Sands Corp.',
      location: 'United States (Remote)',
      viewed: false,
      isTopApplicant: true,
      applyUrl: 'https://www.linkedin.com/jobs/view/4494672646/',
      postedDateText: '2 hours ago',
      compensationText: null,
      applied: false,
      ignored: false,
      createdAt: new Date(Date.now() - 90 * 60_000).toISOString(),
    },
    {
      jobKey: 'JOB2',
      jobTitle: 'Applied Role',
      company: 'Acme',
      location: 'Remote',
      viewed: true,
      isTopApplicant: false,
      applyUrl: null,
      postedDateText: '1 day ago',
      compensationText: '$100K/yr',
      applied: true,
      appliedAt: '2026-11-20T12:00:00.000Z',
      ignored: false,
      createdAt: new Date(Date.now() - 3 * 24 * 60 * 60_000).toISOString(),
    },
    {
      jobKey: 'JOB3',
      jobTitle: 'Ignored Role',
      company: 'Other Co',
      location: 'Austin, TX',
      viewed: false,
      isTopApplicant: false,
      applyUrl: 'https://www.linkedin.com/jobs/view/1/',
      postedDateText: null,
      compensationText: null,
      applied: false,
      ignored: true,
      ignoredAt: '2026-11-21T12:00:00.000Z',
      createdAt: new Date(Date.now() - 30_000).toISOString(),
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JobGridComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(JobGridComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function flushJobs(jobs: JobDto[] = sampleJobs): void {
    fixture.detectChanges();
    const req = http.expectOne(
      (r) => r.url === 'http://127.0.0.1:5088/api/jobs' && r.method === 'GET',
    );
    req.flush({ jobs, count: jobs.length });
    fixture.detectChanges();
  }

  it('loads saved jobs and defaults to the explore filter', () => {
    flushJobs();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('h1')?.textContent).toContain('Job Grid');
    expect(element.textContent).toContain('Explore jobs that are not applied and not ignored');
    expect(element.textContent).toContain('1 to explore');
    expect(element.textContent).toContain('Director - AI-First Development');
    expect(element.textContent).not.toContain('Applied Role');
    expect(element.textContent).not.toContain('Ignored Role');
    expect(element.querySelector('.view-link')?.getAttribute('href')).toBe(
      'https://www.linkedin.com/jobs/view/4494672646/',
    );
    expect(element.querySelector('.view-link')?.textContent?.trim()).toBe('View');
  });

  it('shows an empty-database message when nothing is saved', () => {
    flushJobs([]);
    const element: HTMLElement = fixture.nativeElement;

    expect(element.textContent).toContain('Nothing in the database yet');
    expect(element.textContent).toContain('Paste & Parse');
  });

  it('shows a filter-empty message when explore has no remaining jobs', () => {
    flushJobs([sampleJobs[1], sampleJobs[2]]);
    const element: HTMLElement = fixture.nativeElement;

    expect(element.textContent).toContain('No jobs match this filter');
  });

  it('switches to All and back to To explore', () => {
    flushJobs();
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('.toolbar .btn'),
    ) as HTMLButtonElement[];
    const allBtn = buttons.find((b) => b.textContent?.includes('All'));
    const exploreBtn = buttons.find((b) => b.textContent?.includes('To explore'));

    allBtn?.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Applied Role');
    expect(fixture.nativeElement.textContent).toContain('Ignored Role');
    expect(component.filterMode()).toBe('all');

    exploreBtn?.click();
    fixture.detectChanges();
    expect(component.filterMode()).toBe('explore');
    expect(fixture.nativeElement.textContent).not.toContain('Applied Role');
  });

  it('sorts by job title when the header is clicked', () => {
    flushJobs();
    component.setFilter('all');
    fixture.detectChanges();

    const titleBtn = Array.from(
      fixture.nativeElement.querySelectorAll('.sort-btn') as NodeListOf<HTMLButtonElement>,
    ).find((b) => b.textContent?.includes('Job title'));
    expect(titleBtn).toBeTruthy();

    titleBtn!.click();
    fixture.detectChanges();
    expect(component.sortKey()).toBe('jobTitle');
    expect(component.sortAsc()).toBeTrue();
    expect(component.sortIndicator('jobTitle')).toContain('▲');

    titleBtn!.click();
    fixture.detectChanges();
    expect(component.sortAsc()).toBeFalse();
    expect(component.sortIndicator('jobTitle')).toContain('▼');
    expect(component.sortIndicator('company')).toBe('');
  });

  it('formats age labels from createdAt', () => {
    flushJobs();
    component.nowMs.set(Date.now());

    expect(component.ageLabel({ ...sampleJobs[0], createdAt: null })).toBe('—');
    expect(component.ageLabel({ ...sampleJobs[0], createdAt: 'not-a-date' })).toBe('—');
    expect(
      component.ageLabel({
        ...sampleJobs[0],
        createdAt: new Date(component.nowMs() - 30_000).toISOString(),
      }),
    ).toBe('just now');
    expect(
      component.ageLabel({
        ...sampleJobs[0],
        createdAt: new Date(component.nowMs() - 15 * 60_000).toISOString(),
      }),
    ).toBe('15m');
    expect(
      component.ageLabel({
        ...sampleJobs[0],
        createdAt: new Date(component.nowMs() - 3 * 60 * 60_000).toISOString(),
      }),
    ).toBe('3h');
    expect(
      component.ageLabel({
        ...sampleJobs[0],
        createdAt: new Date(component.nowMs() - (3 * 60 + 20) * 60_000).toISOString(),
      }),
    ).toBe('3h 20m');
    expect(
      component.ageLabel({
        ...sampleJobs[0],
        createdAt: new Date(component.nowMs() - 5 * 24 * 60 * 60_000).toISOString(),
      }),
    ).toBe('5d');
    expect(
      component.ageLabel({
        ...sampleJobs[0],
        createdAt: new Date(component.nowMs() - (5 * 24 + 2) * 60 * 60_000).toISOString(),
      }),
    ).toBe('5d 2h');
  });

  it('sorts across columns and toggles direction for the active key', () => {
    flushJobs();
    component.setFilter('all');
    fixture.detectChanges();

    component.sortBy('company');
    expect(component.sortKey()).toBe('company');
    expect(component.sortAsc()).toBeTrue();
    expect(component.sortedJobs().map((j) => j.company)).toEqual([
      'Acme',
      'Las Vegas Sands Corp.',
      'Other Co',
    ]);

    component.sortBy('location');
    expect(component.sortAsc()).toBeTrue();
    expect(component.sortedJobs()[0].location).toBe('Austin, TX');

    component.sortBy('viewed');
    expect(component.sortAsc()).toBeFalse();
    expect(component.sortedJobs()[0].viewed).toBeTrue();

    component.sortBy('isTopApplicant');
    expect(component.sortedJobs()[0].isTopApplicant).toBeTrue();

    component.sortBy('applied');
    expect(component.sortedJobs()[0].applied).toBeTrue();

    component.sortBy('ignored');
    expect(component.sortedJobs()[0].ignored).toBeTrue();

    component.sortBy('postedDateText');
    expect(component.sortedJobs().map((j) => j.postedDateText ?? '')).toEqual([
      '2 hours ago',
      '1 day ago',
      '',
    ]);

    component.sortBy('compensationText');
    expect(component.sortedJobs()[0].compensationText).toBe('$100K/yr');

    component.sortBy('appliedAt');
    expect(component.sortedJobs()[0].jobKey).toBe('JOB2');

    component.sortBy('ignoredAt');
    expect(component.sortedJobs()[0].jobKey).toBe('JOB3');

    component.sortBy('age');
    expect(component.sortAsc()).toBeFalse();
    expect(component.sortedJobs()[0].jobKey).toBe('JOB3');

    component.sortBy('age');
    expect(component.sortAsc()).toBeTrue();
    expect(component.sortedJobs()[0].jobKey).toBe('JOB2');

    // Unknown sort key hits the default branch and keeps a stable order by empty values.
    component.sortKey.set('unknown' as never);
    component.sortAsc.set(true);
    fixture.detectChanges();
    expect(
      component
        .sortedJobs()
        .map((j) => j.jobKey)
        .sort(),
    ).toEqual(['JOB1', 'JOB2', 'JOB3']);

    // Invalid timestamps coerce to 0 in numeric sort columns.
    component.jobs.update((rows) =>
      rows.map((row) =>
        row.jobKey === 'JOB2' ? { ...row, appliedAt: 'not-a-date', createdAt: 'also-bad' } : row,
      ),
    );
    component.sortBy('appliedAt');
    expect(component.sortedJobs().length).toBe(3);
    component.sortBy('age');
    expect(component.sortedJobs().length).toBe(3);
  });

  it('toggles Applied for a row', () => {
    flushJobs();
    component.setFilter('all');
    fixture.detectChanges();

    const appliedJob = component.jobs().find((j) => j.jobKey === 'JOB2')!;
    component.toggleApplied(appliedJob);

    const req = http.expectOne('http://127.0.0.1:5088/api/jobs/JOB2/applied/toggle');
    expect(req.request.method).toBe('POST');
    req.flush({ jobKey: 'JOB2', applied: false, appliedAt: null });
    fixture.detectChanges();

    expect(component.jobs().find((j) => j.jobKey === 'JOB2')?.applied).toBeFalse();
  });

  it('toggles Ignored for a row', () => {
    flushJobs();
    const exploreJob = component.jobs().find((j) => j.jobKey === 'JOB1')!;
    component.toggleIgnored(exploreJob);

    const req = http.expectOne('http://127.0.0.1:5088/api/jobs/JOB1/ignored/toggle');
    req.flush({
      jobKey: 'JOB1',
      ignored: true,
      ignoredAt: '2026-11-26T12:00:00.000Z',
    });
    fixture.detectChanges();

    expect(component.jobs().find((j) => j.jobKey === 'JOB1')?.ignored).toBeTrue();
    expect(component.exploreCount()).toBe(0);
  });

  it('surfaces load errors from the API', () => {
    fixture.detectChanges();
    const req = http.expectOne('http://127.0.0.1:5088/api/jobs?take=500');
    req.flush({ error: 'db locked' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBe('db locked');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('surfaces plain-string load errors from the API', () => {
    fixture.detectChanges();
    const req = http.expectOne('http://127.0.0.1:5088/api/jobs?take=500');
    req.flush('upstream down', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBe('upstream down');
  });

  it('surfaces toggle errors from the API', () => {
    flushJobs();
    component.toggleApplied(component.jobs()[0]);

    const req = http.expectOne('http://127.0.0.1:5088/api/jobs/JOB1/applied/toggle');
    req.flush({ error: 'not found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(component.error()).toBe('not found');
    expect(component.togglingKey()).toBeNull();
  });

  it('surfaces Ignored toggle errors and clears the in-flight key', () => {
    flushJobs();
    component.toggleIgnored(component.jobs()[0]);

    const req = http.expectOne('http://127.0.0.1:5088/api/jobs/JOB1/ignored/toggle');
    req.flush({ error: 'busy' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBe('busy');
    expect(component.togglingKey()).toBeNull();
  });

  it('resolves error copy from body, message, or fallback', () => {
    const errorMessage = (
      component as unknown as {
        errorMessage: (err: unknown, fallback: string) => string;
      }
    ).errorMessage.bind(component);

    expect(errorMessage({ error: ' plain ' }, 'fallback')).toBe(' plain ');
    expect(errorMessage({ error: { error: 'nested' } }, 'fallback')).toBe('nested');
    expect(errorMessage({ error: '   ' }, 'fallback')).toBe('fallback');
    expect(errorMessage({ error: null, message: 'transport failed' }, 'fallback')).toBe(
      'transport failed',
    );
    expect(errorMessage({ message: '' }, 'fallback')).toBe('fallback');
    expect(errorMessage(null, 'fallback')).toBe('fallback');
    expect(errorMessage('nope', 'fallback')).toBe('fallback');
  });

  it('treats a null jobs payload as an empty list', () => {
    fixture.detectChanges();
    const req = http.expectOne(
      (r) => r.url === 'http://127.0.0.1:5088/api/jobs' && r.method === 'GET',
    );
    req.flush({ jobs: null, count: 0 });
    fixture.detectChanges();

    expect(component.jobs()).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('Nothing in the database yet');
  });

  it('ignores toggle clicks without a job key or while another toggle is in flight', () => {
    flushJobs();
    component.toggleApplied({ ...sampleJobs[0], jobKey: '   ' });
    component.toggleIgnored({ ...sampleJobs[0], jobKey: '' });
    http.expectNone(() => true);

    component.togglingKey.set('BUSY');
    component.toggleIgnored(sampleJobs[0]);
    component.toggleApplied(sampleJobs[0]);
    http.expectNone(() => true);

    expect(component.togglingKey()).toBe('BUSY');
  });

  it('reloads jobs from the refresh button', () => {
    flushJobs();
    const refresh = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((b) => b.textContent?.includes('Refresh'));
    expect(refresh).toBeTruthy();

    refresh!.click();
    fixture.detectChanges();
    expect(component.loading()).toBeTrue();

    const req = http.expectOne('http://127.0.0.1:5088/api/jobs?take=500');
    req.flush({ jobs: [sampleJobs[0]], count: 1 });
    fixture.detectChanges();
    expect(component.jobs().length).toBe(1);
  });
});
