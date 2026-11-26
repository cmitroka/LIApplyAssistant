import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { routes } from '../../app.routes';
import { JobGridComponent } from './job-grid.component';

describe('JobGridComponent', () => {
  let fixture: ComponentFixture<JobGridComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JobGridComponent],
      providers: [provideRouter(routes)],
    }).compileComponents();

    fixture = TestBed.createComponent(JobGridComponent);
    fixture.detectChanges();
  });

  it('explains that saved jobs are not here yet and links back to ingest', () => {
    const element: HTMLElement = fixture.nativeElement;
    const back = element.querySelector('a');

    expect(element.querySelector('h1')?.textContent).toContain('Job Grid');
    expect(element.textContent).toContain('Saved jobs will live here');
    expect(back?.textContent).toContain('Paste & Parse');
    expect(back?.getAttribute('href')).toBe('/');
  });
});
