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
  appliedAt?: string | null;
  rawPaste?: string | null;
}

export interface PreviewJob extends JobDto {
  id: string;
  selected: boolean;
}

export interface ParseResponse {
  jobs: JobDto[];
  count: number;
}

export interface ImportResponse {
  added: number;
  skipped: number;
  total: number;
  errors: { jobKey?: string; jobTitle?: string; error: string }[];
}
