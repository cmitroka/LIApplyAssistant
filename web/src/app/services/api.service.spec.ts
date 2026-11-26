import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../environments/environment';
import { JobDto } from '../models/job';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let service: ApiService;
  let http: HttpTestingController;
  const originalKey = environment.apiKey;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    environment.apiKey = originalKey;
    http.verify();
  });

  it('posts paste text to /api/parse without an API key by default', () => {
    service.parse('Senior Designer').subscribe();

    const req = http.expectOne('http://127.0.0.1:5088/api/parse');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ text: 'Senior Designer' });
    expect(req.request.headers.get('Content-Type')).toBe('application/json');
    expect(req.request.headers.has('X-Api-Key')).toBeFalse();
    req.flush({ jobs: [], count: 0 });
  });

  it('sends X-Api-Key when the environment configures one', () => {
    environment.apiKey = 'dev-key';

    service.parse('card').subscribe();

    const req = http.expectOne('http://127.0.0.1:5088/api/parse');
    expect(req.request.headers.get('X-Api-Key')).toBe('dev-key');
    req.flush({ jobs: [], count: 0 });
  });

  it('posts selected jobs to /api/jobs/import', () => {
    const jobs: JobDto[] = [
      {
        jobTitle: 'Designer',
        company: 'Northstar',
        location: 'Austin, TX',
        viewed: true,
        isTopApplicant: false,
      },
    ];
    let added = -1;

    service.importJobs(jobs).subscribe((res) => {
      added = res.added;
    });

    const req = http.expectOne('http://127.0.0.1:5088/api/jobs/import');
    expect(req.request.body).toEqual({ jobs });
    req.flush({ added: 1, skipped: 0, total: 1, errors: [] });
    expect(added).toBe(1);
  });
});

describe('ApiService base URL', () => {
  const originalBase = environment.apiBaseUrl;

  beforeEach(() => {
    environment.apiBaseUrl = 'http://127.0.0.1:5088/';
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    environment.apiBaseUrl = originalBase;
    TestBed.inject(HttpTestingController).verify();
  });

  it('strips a trailing slash before joining paths', () => {
    TestBed.inject(ApiService).importJobs([]).subscribe();

    const req = TestBed.inject(HttpTestingController).expectOne(
      'http://127.0.0.1:5088/api/jobs/import',
    );
    expect(req.request.body).toEqual({ jobs: [] });
    req.flush({ added: 0, skipped: 0, total: 0, errors: [] });
  });
});
