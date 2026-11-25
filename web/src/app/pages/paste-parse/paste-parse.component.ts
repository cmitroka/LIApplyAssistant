import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ResultModalComponent } from '../../components/result-modal/result-modal.component';
import { ImportResponse, JobDto, PreviewJob } from '../../models/job';
import { ApiService } from '../../services/api.service';

const SAMPLE_PASTE = `Senior Product Designer
Northstar Labs
Austin, TX (Hybrid)
Viewed
2 days ago
You'd be a top applicant
$140K/yr - $170K/yr

---

UX Researcher
Field & Finch
Remote
5 days ago

---

Design Systems Lead
Cedar Health
Chicago, IL
Viewed
1 week ago
You'd be a top applicant`;

@Component({
  selector: 'app-paste-parse',
  standalone: true,
  imports: [CommonModule, FormsModule, ResultModalComponent],
  templateUrl: './paste-parse.component.html',
  styleUrl: './paste-parse.component.scss',
})
export class PasteParseComponent {
  private readonly api = inject(ApiService);
  private nextId = 1;

  readonly pasteText = signal('');
  readonly jobs = signal<PreviewJob[]>([]);
  readonly pastePanelOpen = signal(true);
  readonly parsing = signal(false);
  readonly saving = signal(false);
  readonly parseError = signal<string | null>(null);
  readonly importResult = signal<ImportResponse | null>(null);

  readonly charCount = computed(() => this.pasteText().length);
  readonly selectedCount = computed(() => this.jobs().filter((j) => j.selected).length);
  readonly topApplicantCount = computed(
    () => this.jobs().filter((j) => j.isTopApplicant).length,
  );
  readonly allSelected = computed(
    () => this.jobs().length > 0 && this.jobs().every((j) => j.selected),
  );

  onPasteInput(value: string): void {
    this.pasteText.set(value);
  }

  loadSample(): void {
    this.pasteText.set(SAMPLE_PASTE);
  }

  newPaste(): void {
    this.pasteText.set('');
    this.jobs.set([]);
    this.parseError.set(null);
    this.pastePanelOpen.set(true);
  }

  closePastePanel(): void {
    this.pastePanelOpen.set(false);
  }

  openPastePanel(): void {
    this.pastePanelOpen.set(true);
  }

  parseExtract(): void {
    const text = this.pasteText().trim();
    if (!text) {
      this.parseError.set('Paste a job extract first.');
      return;
    }

    this.parsing.set(true);
    this.parseError.set(null);

    this.api.parse(text).subscribe({
      next: (res) => {
        this.jobs.set((res.jobs ?? []).map((job) => this.toPreview(job, true)));
        this.parsing.set(false);
        if ((res.jobs ?? []).length === 0) {
          this.parseError.set('No jobs found in the paste. Try separating cards with ---.');
        }
      },
      error: (err) => {
        this.parsing.set(false);
        this.parseError.set(this.errorMessage(err, 'Parse failed.'));
      },
    });
  }

  toggleSelectAll(checked: boolean): void {
    this.jobs.update((rows) => rows.map((row) => ({ ...row, selected: checked })));
  }

  toggleRow(id: string, checked: boolean): void {
    this.jobs.update((rows) =>
      rows.map((row) => (row.id === id ? { ...row, selected: checked } : row)),
    );
  }

  updateField<K extends keyof PreviewJob>(id: string, field: K, value: PreviewJob[K]): void {
    this.jobs.update((rows) =>
      rows.map((row) => (row.id === id ? { ...row, [field]: value } : row)),
    );
  }

  deleteSelected(): void {
    this.jobs.update((rows) => rows.filter((row) => !row.selected));
  }

  exportCsv(): void {
    const rows = this.jobs();
    if (rows.length === 0) return;

    const headers = [
      'JobTitle',
      'Company',
      'Location',
      'Viewed',
      'PostedDateText',
      'IsTopApplicant',
      'CompensationText',
    ];
    const lines = [
      headers.join(','),
      ...rows.map((r) =>
        [
          r.jobTitle,
          r.company,
          r.location,
          r.viewed ? 'Yes' : 'No',
          r.postedDateText,
          r.isTopApplicant ? 'Yes' : 'No',
          r.compensationText,
        ]
          .map((v) => this.csvEscape(v ?? ''))
          .join(','),
      ),
    ];

    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'jobgrid-preview.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  /** Persist checked preview rows (first-write wins on JobKey). */
  addRow(): void {
    const selected = this.jobs().filter((j) => j.selected);
    if (selected.length === 0) {
      this.parseError.set('Select at least one job to add.');
      return;
    }

    this.saving.set(true);
    this.parseError.set(null);

    const payload: JobDto[] = selected.map((j) => ({
      jobKey: j.jobKey,
      jobTitle: j.jobTitle,
      company: j.company,
      location: j.location,
      viewed: j.viewed,
      postedDateText: j.postedDateText,
      postedDate: j.postedDate,
      isTopApplicant: j.isTopApplicant,
      applyUrl: j.applyUrl,
      compensationText: j.compensationText,
      compSpecified: j.compSpecified,
      resumeUsed: j.resumeUsed,
      appliedAt: j.appliedAt,
      rawPaste: j.rawPaste,
    }));

    this.api.importJobs(payload).subscribe({
      next: (res) => {
        this.saving.set(false);
        this.importResult.set(res);
      },
      error: (err) => {
        this.saving.set(false);
        this.parseError.set(this.errorMessage(err, 'Save failed.'));
      },
    });
  }

  closeModal(): void {
    this.importResult.set(null);
  }

  private toPreview(job: JobDto, selected: boolean): PreviewJob {
    return {
      id: `row-${this.nextId++}`,
      selected,
      jobKey: job.jobKey ?? '',
      jobTitle: job.jobTitle ?? '',
      company: job.company ?? '',
      location: job.location ?? '',
      viewed: !!job.viewed,
      postedDateText: job.postedDateText ?? '',
      postedDate: job.postedDate ?? '',
      isTopApplicant: !!job.isTopApplicant,
      applyUrl: job.applyUrl ?? '',
      compensationText: job.compensationText ?? '',
      compSpecified: job.compSpecified ?? '',
      resumeUsed: job.resumeUsed ?? '',
      appliedAt: job.appliedAt ?? '',
      rawPaste: job.rawPaste ?? '',
    };
  }

  private csvEscape(value: string): string {
    if (/[",\n]/.test(value)) {
      return `"${value.replace(/"/g, '""')}"`;
    }
    return value;
  }

  private errorMessage(err: unknown, fallback: string): string {
    if (err && typeof err === 'object' && 'error' in err) {
      const body = (err as { error?: { error?: string } | string }).error;
      if (typeof body === 'string' && body.trim()) return body;
      if (body && typeof body === 'object' && body.error) return body.error;
    }
    if (err && typeof err === 'object' && 'message' in err) {
      const msg = (err as { message?: string }).message;
      if (msg) return msg;
    }
    return fallback;
  }
}
