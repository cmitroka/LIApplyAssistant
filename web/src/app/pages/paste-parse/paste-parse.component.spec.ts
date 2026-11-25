import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PasteParseComponent } from './paste-parse.component';

describe('PasteParseComponent', () => {
  let fixture: ComponentFixture<PasteParseComponent>;
  let component: PasteParseComponent;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PasteParseComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(PasteParseComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  function parseButton(): HTMLButtonElement {
    const button = fixture.nativeElement.querySelector(
      '.paste-card .btn--primary',
    ) as HTMLButtonElement | null;
    if (!button) {
      throw new Error('Parse extract button was not rendered');
    }
    return button;
  }

  it('starts on an empty paste panel', () => {
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('h1')?.textContent).toContain('Paste & Parse');
    expect(component.charCount()).toBe(0);
    expect(component.jobs()).toEqual([]);
    expect(component.allSelected()).toBeFalse();
    expect(element.querySelector('#job-extract')).not.toBeNull();
    expect(element.textContent).toContain('No parsed jobs yet');
  });

  it('rejects an empty extract in the page before calling the API', () => {
    parseButton().click();
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Paste a job extract first.');
    expect(component.parsing()).toBeFalse();
    http.expectNone('http://127.0.0.1:5088/api/parse');
  });

  it('loads the sample and parses jobs into an editable preview', () => {
    component.loadSample();
    fixture.detectChanges();

    expect(component.charCount()).toBeGreaterThan(0);
    expect(fixture.nativeElement.querySelector('.char-count').textContent).toContain(
      String(component.charCount()),
    );

    component.parseExtract();
    expect(component.parsing()).toBeTrue();

    const req = http.expectOne('http://127.0.0.1:5088/api/parse');
    expect(req.request.body.text).toContain('Senior Product Designer');
    req.flush({
      count: 1,
      jobs: [
        {
          jobTitle: 'Senior Product Designer',
          company: 'Northstar Labs',
          location: 'Austin, TX (Hybrid)',
          viewed: true,
          isTopApplicant: true,
          postedDateText: '2 days ago',
          compensationText: '$140K/yr - $170K/yr',
        },
      ],
    });

    const job = component.jobs()[0];
    expect(component.parsing()).toBeFalse();
    expect(component.parseError()).toBeNull();
    expect(component.selectedCount()).toBe(1);
    expect(component.topApplicantCount()).toBe(1);
    expect(component.allSelected()).toBeTrue();
    expect(job.id).toMatch(/^row-/);
    expect(job.selected).toBeTrue();
    expect(job.jobKey).toBe('');
    expect(job.viewed).toBeTrue();
    expect(job.compensationText).toContain('$140K');
  });

  it('explains when the parser returns nothing', () => {
    component.onPasteInput('not a job card');
    component.parseExtract();

    http.expectOne('http://127.0.0.1:5088/api/parse').flush({ count: 0 });

    expect(component.jobs()).toEqual([]);
    expect(component.parseError()).toContain('No jobs found');
  });

  it('surfaces a string error body and an object error body', () => {
    component.onPasteInput('text');
    component.parseExtract();
    http
      .expectOne('http://127.0.0.1:5088/api/parse')
      .flush('parser down', { status: 500, statusText: 'Server Error' });
    expect(component.parseError()).toBe('parser down');
    expect(component.parsing()).toBeFalse();

    component.parseExtract();
    http
      .expectOne('http://127.0.0.1:5088/api/parse')
      .flush({ error: 'bad request' }, { status: 400, statusText: 'Bad Request' });
    expect(component.parseError()).toBe('bad request');
  });

  it('falls back to the HTTP message when the body has no error text', () => {
    component.onPasteInput('text');
    component.parseExtract();
    http.expectOne('http://127.0.0.1:5088/api/parse').error(new ProgressEvent('error'));

    expect(component.parseError()).toContain('Http failure');
    expect(component.parsing()).toBeFalse();
  });

  it('edits, toggles, and deletes preview rows', () => {
    component.onPasteInput('two cards');
    component.parseExtract();
    http.expectOne('http://127.0.0.1:5088/api/parse').flush({
      count: 2,
      jobs: [
        {
          jobTitle: 'A',
          company: 'Acme',
          location: 'Remote',
          viewed: false,
          isTopApplicant: false,
        },
        { jobTitle: 'B', company: 'Beta', location: 'NYC', viewed: true, isTopApplicant: true },
      ],
    });

    const [first, second] = component.jobs();
    component.toggleRow(first.id, false);
    expect(component.selectedCount()).toBe(1);
    expect(component.allSelected()).toBeFalse();

    component.toggleSelectAll(true);
    expect(component.selectedCount()).toBe(2);

    component.updateField(second.id, 'jobTitle', 'Renamed');
    component.updateField(second.id, 'viewed', false);
    expect(component.jobs().find((job) => job.id === second.id)?.jobTitle).toBe('Renamed');

    component.toggleRow(second.id, false);
    component.deleteSelected();
    expect(component.jobs().map((job) => job.jobTitle)).toEqual(['Renamed']);
    expect(component.topApplicantCount()).toBe(1);
  });

  it('requires a checked row before import', () => {
    component.onPasteInput('one card');
    component.parseExtract();
    http.expectOne('http://127.0.0.1:5088/api/parse').flush({
      count: 1,
      jobs: [
        {
          jobTitle: 'A',
          company: 'Acme',
          location: 'Remote',
          viewed: false,
          isTopApplicant: false,
        },
      ],
    });

    component.toggleSelectAll(false);
    component.addRow();

    expect(component.parseError()).toBe('Select at least one job to add.');
    expect(component.saving()).toBeFalse();
    http.expectNone('http://127.0.0.1:5088/api/jobs/import');
  });

  it('imports the checked rows and closes the result modal', () => {
    component.onPasteInput('one card');
    component.parseExtract();
    http.expectOne('http://127.0.0.1:5088/api/parse').flush({
      count: 1,
      jobs: [
        {
          jobKey: 'designer-northstar-austin',
          jobTitle: 'Designer',
          company: 'Northstar',
          location: 'Austin, TX',
          viewed: true,
          isTopApplicant: false,
          compensationText: '100k',
        },
      ],
    });

    component.addRow();
    expect(component.saving()).toBeTrue();

    const req = http.expectOne('http://127.0.0.1:5088/api/jobs/import');
    expect(req.request.body.jobs).toEqual([
      jasmine.objectContaining({
        jobKey: 'designer-northstar-austin',
        jobTitle: 'Designer',
        company: 'Northstar',
      }),
    ]);
    expect(req.request.body.jobs[0].id).toBeUndefined();
    expect(req.request.body.jobs[0].selected).toBeUndefined();
    req.flush({ added: 1, skipped: 0, total: 1, errors: [] });

    expect(component.saving()).toBeFalse();
    expect(component.importResult()).toEqual({ added: 1, skipped: 0, total: 1, errors: [] });

    component.closeModal();
    expect(component.importResult()).toBeNull();
  });

  it('reports a failed save and resets the paste', () => {
    component.onPasteInput('one card');
    component.parseExtract();
    http.expectOne('http://127.0.0.1:5088/api/parse').flush({
      count: 1,
      jobs: [
        {
          jobTitle: 'A',
          company: 'Acme',
          location: 'Remote',
          viewed: false,
          isTopApplicant: false,
        },
      ],
    });

    component.addRow();
    http
      .expectOne('http://127.0.0.1:5088/api/jobs/import')
      .flush({ error: 'database locked' }, { status: 503, statusText: 'Unavailable' });

    expect(component.saving()).toBeFalse();
    expect(component.importResult()).toBeNull();
    expect(component.parseError()).toBe('database locked');

    component.newPaste();
    expect(component.pasteText()).toBe('');
    expect(component.jobs()).toEqual([]);
    expect(component.parseError()).toBeNull();
    expect(component.pastePanelOpen()).toBeTrue();
  });

  it('hides and reopens the paste panel', () => {
    component.closePastePanel();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#job-extract')).toBeNull();
    expect(fixture.nativeElement.querySelector('.reopen-paste')).not.toBeNull();

    component.openPastePanel();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#job-extract')).not.toBeNull();
  });

  it('does not export an empty preview', () => {
    spyOn(URL, 'createObjectURL');

    component.exportCsv();

    expect(URL.createObjectURL).not.toHaveBeenCalled();
  });

  it('exports a CSV with quoted commas, quotes, and newlines', async () => {
    component.onPasteInput('one card');
    component.parseExtract();
    http.expectOne('http://127.0.0.1:5088/api/parse').flush({
      count: 1,
      jobs: [
        {
          jobTitle: 'Designer, Senior',
          company: 'Say "Hello"',
          location: 'Austin',
          viewed: false,
          isTopApplicant: true,
          postedDateText: '2 days ago',
          compensationText: '100k\nbonus',
        },
      ],
    });

    const blobs: Blob[] = [];
    spyOn(URL, 'createObjectURL').and.callFake((blob: Blob) => {
      blobs.push(blob);
      return 'blob:preview';
    });
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click');

    component.exportCsv();

    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
    const csv = await blobs[0].text();
    expect(csv).toContain('"Designer, Senior"');
    expect(csv).toContain('"Say ""Hello"""');
    expect(csv).toContain('"100k\nbonus"');
    expect(csv).toContain('No');
    expect(csv).toContain('Yes');
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview');
  });
});
