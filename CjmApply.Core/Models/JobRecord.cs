namespace CjmApply.Core.Models;

public sealed class JobRecord
{
    public string JobKey { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Company { get; set; } = "";
    public string? Location { get; set; }
    public bool Viewed { get; set; }
    public string? PostedDateText { get; set; }
    public DateOnly? PostedDate { get; set; }
    public bool IsTopApplicant { get; set; }
    public string? ApplyUrl { get; set; }
    public string? CompensationText { get; set; }
    public string? CompSpecified { get; set; }
    public string? ResumeUsed { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? RawPaste { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool ExistsInDatabase { get; set; }
}
