export interface JobDto {
  jobKey?: string | null;
  jobTitle?: string | null;
  company?: string | null;
  location?: string | null;
  viewed: boolean;
  postedDateText?: string | null;
  postedDate?: string | null;
  isTopApplicant: boolean;
  applyUrl?: string | null;
  compensationText?: string | null;
  compSpecified?: string | null;
  resumeUsed?: string | null;
  applied: boolean;
  appliedAt?: string | null;
  ignored: boolean;
  ignoredAt?: string | null;
  createdAt?: string | null;
  rawPaste?: string | null;
}

export interface PreviewJob extends JobDto {
  id: string;
  selected: boolean;
}

export interface ParseResponse {
  jobs: JobDto[];
  count: number;
  source?: 'html' | 'text';
}

export interface ImportResponse {
  added: number;
  skipped: number;
  total: number;
  errors: Array<{ jobKey?: string; jobTitle?: string; error: string }>;
}

export interface JobsListResponse {
  jobs: JobDto[];
  count: number;
}

export interface AppliedToggleResponse {
  jobKey: string;
  applied: boolean;
  appliedAt: string | null;
}

export interface IgnoredToggleResponse {
  jobKey: string;
  ignored: boolean;
  ignoredAt: string | null;
}
