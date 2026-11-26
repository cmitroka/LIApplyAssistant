import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AppComponent } from './app.component';
import { routes } from './app.routes';

describe('AppComponent', () => {
  let fixture: ComponentFixture<AppComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter(routes)],
    }).compileComponents();

    fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
  });

  it('exposes the product title and renders the shell', () => {
    const element: HTMLElement = fixture.nativeElement;

    expect(fixture.componentInstance.title).toBe('JobGrid');
    expect(element.querySelector('.brand__name')?.textContent).toContain('JobGrid');
    expect(element.querySelector('main router-outlet, main')).not.toBeNull();
  });

  it('links Paste & Parse and Job Grid in the primary nav', () => {
    const anchors = Array.from(
      fixture.nativeElement.querySelectorAll(
        'nav[aria-label="Primary"] a',
      ) as NodeListOf<HTMLAnchorElement>,
    );
    const links = anchors.map((anchor) => ({
      text: anchor.textContent?.trim(),
      href: anchor.getAttribute('href'),
    }));

    expect(links).toEqual([
      { text: 'Paste & Parse', href: '/' },
      { text: 'Job Grid', href: '/jobs' },
    ]);
  });
});
