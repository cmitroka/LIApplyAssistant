import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { JobDto } from '../../models/job';
import { ApiService } from '../../services/api.service';

type SortKey =
  | 'jobTitle'
  | 'company'
  | 'location'
  | 'viewed'
  | 'postedDateText'
  | 'isTopApplicant'
  | 'compensationText'
  | 'applied'
  | 'appliedAt'
  | 'ignored'
  | 'ignoredAt'
  | 'age';

type FilterMode = 'explore' | 'all';

@Component({
  selector: 'app-job-grid',
  standalone: true,
  templateUrl: './job-grid.component.html',
  styleUrl: './job-grid.component.scss',
})
export class JobGridComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly jobs = signal<JobDto[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly togglingKey = signal<string | null>(null);
  readonly sortKey = signal<SortKey>('age');
  readonly sortAsc = signal(false);
  readonly filterMode = signal<FilterMode>('explore');
  readonly nowMs = signal(Date.now());

  readonly exploreCount = computed(
    () => this.jobs().filter((j) => !j.applied && !j.ignored).length,
  );
  readonly appliedCount = computed(
    () => this.jobs().filter((j) => j.applied).length,
  );
  readonly ignoredCount = computed(
    () => this.jobs().filter((j) => j.ignored).length,
  );

  readonly visibleJobs = computed(() => {
    const mode = this.filterMode();
    const rows = this.jobs();
    if (mode === 'explore') return rows.filter((j) => !j.applied && !j.ignored);
    return rows;
  });

  readonly sortedJobs = computed(() => {
    const key = this.sortKey();
    const asc = this.sortAsc();
    const rows = [...this.visibleJobs()];
    rows.sort((a, b) => this.compare(a, b, key) * (asc ? 1 : -1));
    return rows;
  });

  ngOnInit(): void {
    this.reload();
  }

  setFilter(mode: FilterMode): void {
    this.filterMode.set(mode);
  }

  reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.nowMs.set(Date.now());
    this.api.listJobs().subscribe({
      next: (res) => {
        this.jobs.set(
          (res.jobs ?? []).map((j) => ({
            ...j,
            viewed: !!j.viewed,
            isTopApplicant: !!j.isTopApplicant,
            applied: !!j.applied,
            ignored: !!j.ignored,
          })),
        );
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(this.errorMessage(err, 'Failed to load jobs.'));
      },
    });
  }

  sortBy(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortAsc.update((v) => !v);
      return;
    }
    this.sortKey.set(key);
    this.sortAsc.set(
      key === 'jobTitle' || key === 'company' || key === 'location',
    );
  }

  sortIndicator(key: SortKey): string {
    if (this.sortKey() !== key) return '';
    return this.sortAsc() ? ' ▲' : ' ▼';
  }

  ageLabel(job: JobDto): string {
    if (!job.createdAt) return '—';
    const created = Date.parse(job.createdAt);
    if (Number.isNaN(created)) return '—';
    const ms = Math.max(0, this.nowMs() - created);
    const minutes = Math.floor(ms / 60_000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m`;
    const hours = Math.floor(minutes / 60);
    if (hours < 48) {
      const remMin = minutes % 60;
      return remMin === 0 ? `${hours}h` : `${hours}h ${remMin}m`;
    }
    const days = Math.floor(hours / 24);
    const remHours = hours % 24;
    return remHours === 0 ? `${days}d` : `${days}d ${remHours}h`;
  }

  toggleApplied(job: JobDto): void {
    const key = job.jobKey?.trim();
    if (!key || this.togglingKey()) return;

    this.togglingKey.set(key);
    this.error.set(null);
    this.api.toggleApplied(key).subscribe({
      next: (res) => {
        this.jobs.update((rows) =>
          rows.map((row) =>
            row.jobKey === res.jobKey
              ? { ...row, applied: res.applied, appliedAt: res.appliedAt }
              : row,
          ),
        );
        this.togglingKey.set(null);
      },
      error: (err) => {
        this.togglingKey.set(null);
        this.error.set(this.errorMessage(err, 'Could not update Applied.'));
      },
    });
  }

  toggleIgnored(job: JobDto): void {
    const key = job.jobKey?.trim();
    if (!key || this.togglingKey()) return;

    this.togglingKey.set(key);
    this.error.set(null);
    this.api.toggleIgnored(key).subscribe({
      next: (res) => {
        this.jobs.update((rows) =>
          rows.map((row) =>
            row.jobKey === res.jobKey
              ? { ...row, ignored: res.ignored, ignoredAt: res.ignoredAt }
              : row,
          ),
        );
        this.togglingKey.set(null);
      },
      error: (err) => {
        this.togglingKey.set(null);
        this.error.set(this.errorMessage(err, 'Could not update Ignored.'));
      },
    });
  }

  private compare(a: JobDto, b: JobDto, key: SortKey): number {
    const av = this.sortValue(a, key);
    const bv = this.sortValue(b, key);
    if (typeof av === 'number' && typeof bv === 'number') return av - bv;
    return String(av).localeCompare(String(bv), undefined, {
      sensitivity: 'base',
    });
  }

  private sortValue(job: JobDto, key: SortKey): string | number {
    switch (key) {
      case 'viewed':
        return job.viewed ? 1 : 0;
      case 'isTopApplicant':
        return job.isTopApplicant ? 1 : 0;
      case 'applied':
        return job.applied ? 1 : 0;
      case 'ignored':
        return job.ignored ? 1 : 0;
      case 'appliedAt':
        return job.appliedAt ? Date.parse(job.appliedAt) || 0 : 0;
      case 'ignoredAt':
        return job.ignoredAt ? Date.parse(job.ignoredAt) || 0 : 0;
      case 'age':
        return job.createdAt ? Date.parse(job.createdAt) || 0 : 0;
      case 'jobTitle':
        return job.jobTitle ?? '';
      case 'company':
        return job.company ?? '';
      case 'location':
        return job.location ?? '';
      case 'postedDateText':
        return job.postedDateText ?? '';
      case 'compensationText':
        return job.compensationText ?? '';
      default:
        return '';
    }
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
