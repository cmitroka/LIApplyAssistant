import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ImportResponse } from '../../models/job';
import { ResultModalComponent } from './result-modal.component';

describe('ResultModalComponent', () => {
  let fixture: ComponentFixture<ResultModalComponent>;
  let component: ResultModalComponent;

  const result: ImportResponse = {
    added: 2,
    skipped: 1,
    total: 3,
    errors: [{ jobTitle: 'Designer', error: 'database locked' }],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ResultModalComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ResultModalComponent);
    component = fixture.componentInstance;
    component.result = result;
    fixture.detectChanges();
  });

  it('summarizes added, skipped, and row errors', () => {
    const text = fixture.nativeElement.textContent as string;

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
    expect(text).toContain('2');
    expect(text).toContain('jobs added');
    expect(text).toContain('1');
    expect(text).toContain('skipped');
    expect(text).toContain('Designer — database locked');
  });

  it('uses the singular label for a single added job', () => {
    component.result = { ...result, added: 1, errors: [] };
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('1 job added');
    expect(fixture.nativeElement.querySelector('.errors')).toBeNull();
  });

  it('emits closed from the dialog actions', () => {
    let emissions = 0;
    component.closed.subscribe(() => emissions++);

    fixture.nativeElement.querySelector('.btn--primary').click();
    fixture.nativeElement.querySelector('[aria-label="Close"]').click();
    component.close();

    expect(emissions).toBe(3);
  });
});
