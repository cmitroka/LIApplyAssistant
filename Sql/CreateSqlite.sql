-- CJM Apply Assistant — SQLite schema (Azure Files-backed)
CREATE TABLE IF NOT EXISTS Jobs (
    JobKey            TEXT NOT NULL PRIMARY KEY,
    JobTitle          TEXT NOT NULL,
    Company           TEXT NOT NULL,
    Location          TEXT NULL,
    Viewed            INTEGER NOT NULL DEFAULT 0,
    PostedDateText    TEXT NULL,
    PostedDate        TEXT NULL,
    IsTopApplicant    INTEGER NOT NULL DEFAULT 0,
    ApplyUrl          TEXT NULL,
    CompensationText  TEXT NULL,
    CompSpecified     TEXT NULL,
    ResumeUsed        TEXT NULL,
    Applied           INTEGER NOT NULL DEFAULT 0,
    AppliedAt         TEXT NULL,
    Ignored           INTEGER NOT NULL DEFAULT 0,
    IgnoredAt         TEXT NULL,
    RawPaste          TEXT NULL,
    CreatedAt         TEXT NOT NULL,
    UpdatedAt         TEXT NOT NULL
);
