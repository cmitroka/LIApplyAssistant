import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from './app.routes';
import { JobGridComponent } from './pages/job-grid/job-grid.component';
import { PasteParseComponent } from './pages/paste-parse/paste-parse.component';

describe('routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('loads Paste & Parse at / and Job Grid at /jobs', async () => {
    const harness = await RouterTestingHarness.create();

    const paste = await harness.navigateByUrl('/', PasteParseComponent);
    expect(paste).toBeInstanceOf(PasteParseComponent);

    const grid = await harness.navigateByUrl('/jobs', JobGridComponent);
    expect(grid).toBeInstanceOf(JobGridComponent);
  });

  it('redirects unknown URLs back to Paste & Parse', async () => {
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/missing', PasteParseComponent);

    expect(TestBed.inject(Router).url).toBe('/');
  });
});
