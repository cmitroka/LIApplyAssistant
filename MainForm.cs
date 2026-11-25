using System.Diagnostics;
using CjmApplyAssistant.Data;
using CjmApply.Core.Models;
using CjmApply.Core.Parsing;
using Microsoft.Extensions.Configuration;

namespace CjmApplyAssistant;

public sealed class MainForm : Form
{
    private readonly IConfiguration _configuration;
    private JobRepository? _repository;
    private JobRecord? _current;
    private bool _loadingFields;

    private readonly TextBox _pasteBox = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _jobTitle = new();
    private readonly TextBox _company = new();
    private readonly TextBox _location = new();
    private readonly TextBox _jobKey = new();
    private readonly CheckBox _viewed = new();
    private readonly CheckBox _topApplicant = new();
    private readonly TextBox _postedText = new();
    private readonly TextBox _postedDate = new();
    private readonly TextBox _compensation = new();
    private readonly TextBox _applyUrl = new();
    private readonly TextBox _compSpecified = new();
    private readonly TextBox _resumeUsed = new();
    private readonly DateTimePicker _appliedAt = new();
    private readonly CheckBox _appliedEnabled = new();
    private readonly Label _status = new();
    private readonly BindingSource _binding = new();

    public MainForm(IConfiguration configuration)
    {
        _configuration = configuration;

        Text = "CJM Apply Assistant";
        Width = 1180;
        Height = 780;
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        WireEvents();
        Shown += async (_, _) => await InitializeAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        Controls.Add(root);

        // Toolbar
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        var btnParse = new Button { Text = "Parse Paste", AutoSize = true };
        var btnSave = new Button { Text = "Save / Upsert", AutoSize = true };
        var btnRefresh = new Button { Text = "Refresh Saved", AutoSize = true };
        var btnEnsure = new Button { Text = "Ensure DB Table", AutoSize = true };
        var btnSettings = new Button { Text = "Connection…", AutoSize = true };
        var btnOpenUrl = new Button { Text = "Open Apply URL", AutoSize = true };
        var btnSearchLi = new Button { Text = "LinkedIn Search", AutoSize = true };
        var btnClear = new Button { Text = "Clear", AutoSize = true };

        btnParse.Click += (_, _) => ParsePaste();
        btnSave.Click += async (_, _) => await SaveAsync();
        btnRefresh.Click += async (_, _) => await RefreshGridAsync();
        btnEnsure.Click += async (_, _) => await EnsureSchemaAsync();
        btnSettings.Click += (_, _) => OpenSettings();
        btnOpenUrl.Click += (_, _) => OpenApplyUrl();
        btnSearchLi.Click += (_, _) => OpenLinkedInSearch();
        btnClear.Click += (_, _) => ClearEditor();

        toolbar.Controls.AddRange([btnParse, btnSave, btnRefresh, btnEnsure, btnSettings, btnOpenUrl, btnSearchLi, btnClear]);
        root.Controls.Add(toolbar, 0, 0);

        // Paste area
        var pastePanel = new GroupBox { Text = "LinkedIn paste", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _pasteBox.Multiline = true;
        _pasteBox.ScrollBars = ScrollBars.Vertical;
        _pasteBox.Dock = DockStyle.Fill;
        _pasteBox.AcceptsReturn = true;
        _pasteBox.Font = new Font("Consolas", 9F);
        pastePanel.Controls.Add(_pasteBox);
        root.Controls.Add(pastePanel, 0, 1);

        // Detail editor
        var details = new GroupBox { Text = "Job details", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 6,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 6; i++)
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 16.6f));

        AddField(fields, 0, 0, "Job Title", _jobTitle);
        AddField(fields, 0, 2, "Company", _company);
        AddField(fields, 1, 0, "Location", _location);
        AddField(fields, 1, 2, "Job Key", _jobKey);
        _jobKey.ReadOnly = true;

        _viewed.Text = "Viewed";
        _topApplicant.Text = "Top applicant";
        var flags = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        flags.Controls.Add(_viewed);
        flags.Controls.Add(_topApplicant);
        fields.Controls.Add(new Label { Text = "Flags", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        fields.Controls.Add(flags, 1, 2);

        AddField(fields, 2, 2, "Posted text", _postedText);
        AddField(fields, 3, 0, "Posted date", _postedDate);
        _postedDate.PlaceholderText = "yyyy-MM-dd (optional)";
        AddField(fields, 3, 2, "Comp (from LI)", _compensation);
        AddField(fields, 4, 0, "Apply URL", _applyUrl);
        AddField(fields, 4, 2, "Comp specified", _compSpecified);
        AddField(fields, 5, 0, "Resume used", _resumeUsed);

        _appliedEnabled.Text = "Applied";
        _appliedAt.Format = DateTimePickerFormat.Custom;
        _appliedAt.CustomFormat = "yyyy-MM-dd HH:mm";
        _appliedAt.ShowUpDown = true;
        _appliedAt.Enabled = false;
        var appliedRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        appliedRow.Controls.Add(_appliedEnabled);
        appliedRow.Controls.Add(_appliedAt);
        fields.Controls.Add(new Label { Text = "Applied at", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 5);
        fields.Controls.Add(appliedRow, 3, 5);

        details.Controls.Add(fields);
        root.Controls.Add(details, 0, 2);

        // Grid + status
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.DataSource = _binding;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "JobTitle", HeaderText = "Title", FillWeight = 28, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Company", HeaderText = "Company", FillWeight = 16, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Location", HeaderText = "Location", FillWeight = 16, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Viewed", HeaderText = "Viewed", Width = 60 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "IsTopApplicant", HeaderText = "Top", Width = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CompensationText", HeaderText = "Comp", FillWeight = 12, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "AppliedAt", HeaderText = "Applied", FillWeight = 12, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ApplyUrl", HeaderText = "Apply URL", FillWeight = 16, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = "Ready.";

        bottom.Controls.Add(_grid, 0, 0);
        bottom.Controls.Add(_status, 0, 1);
        root.Controls.Add(bottom, 0, 3);
    }

    private static void AddField(TableLayoutPanel panel, int row, int labelCol, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, labelCol, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, labelCol + 1, row);
    }

    private void WireEvents()
    {
        void RecalcKey(object? s, EventArgs e)
        {
            if (_loadingFields)
                return;
            _jobKey.Text = JobKeyBuilder.Build(_jobTitle.Text, _company.Text, _location.Text);
        }

        _jobTitle.TextChanged += RecalcKey;
        _company.TextChanged += RecalcKey;
        _location.TextChanged += RecalcKey;

        _appliedEnabled.CheckedChanged += (_, _) =>
        {
            _appliedAt.Enabled = _appliedEnabled.Checked;
            if (_appliedEnabled.Checked)
                _appliedAt.Value = DateTime.Now;
        };

        _grid.SelectionChanged += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is JobRecord job)
                LoadIntoEditor(job);
        };
    }

    private async Task InitializeAsync()
    {
        try
        {
            RebuildRepository();
            if (_repository is null)
            {
                SetStatus("Set an Azure SQL connection string (Connection…) before saving.");
                return;
            }

            await _repository.TestConnectionAsync();
            SetStatus("Connected to Azure SQL.");
            await RefreshGridAsync();
        }
        catch (Exception ex)
        {
            SetStatus("DB not ready: " + ex.Message);
        }
    }

    private void RebuildRepository()
    {
        var cs = AppConfig.GetConnectionString(_configuration);
        if (string.IsNullOrWhiteSpace(cs) || cs.Contains("YOUR_SERVER", StringComparison.OrdinalIgnoreCase))
        {
            _repository = null;
            return;
        }

        _repository = new JobRepository(cs);
    }

    private void ParsePaste()
    {
        try
        {
            var parsed = LinkedInPasteParser.Parse(_pasteBox.Text);
            if (parsed.Count == 0)
            {
                SetStatus("No jobs recognized in paste.");
                return;
            }

            _binding.DataSource = parsed.ToList();
            LoadIntoEditor(parsed[0]);
            SetStatus($"Parsed {parsed.Count} job(s). Review fields, set Apply URL if known, then Save.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Parse failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadIntoEditor(JobRecord job)
    {
        _loadingFields = true;
        _current = job;
        _jobTitle.Text = job.JobTitle;
        _company.Text = job.Company;
        _location.Text = job.Location ?? "";
        _jobKey.Text = string.IsNullOrWhiteSpace(job.JobKey)
            ? JobKeyBuilder.Build(job.JobTitle, job.Company, job.Location)
            : job.JobKey;
        _viewed.Checked = job.Viewed;
        _topApplicant.Checked = job.IsTopApplicant;
        _postedText.Text = job.PostedDateText ?? "";
        _postedDate.Text = job.PostedDate?.ToString("yyyy-MM-dd") ?? "";
        _compensation.Text = job.CompensationText ?? "";
        _applyUrl.Text = job.ApplyUrl ?? "";
        _compSpecified.Text = job.CompSpecified ?? "";
        _resumeUsed.Text = job.ResumeUsed ?? "";
        _appliedEnabled.Checked = job.AppliedAt.HasValue;
        if (job.AppliedAt.HasValue)
            _appliedAt.Value = job.AppliedAt.Value;
        _loadingFields = false;
    }

    private JobRecord CaptureFromEditor()
    {
        DateOnly? posted = null;
        if (DateOnly.TryParse(_postedDate.Text.Trim(), out var pd))
            posted = pd;
        else if (!string.IsNullOrWhiteSpace(_postedText.Text))
            posted = LinkedInPasteParser.TryResolveRelativeDate(_postedText.Text, DateTime.Today);

        var job = new JobRecord
        {
            JobTitle = _jobTitle.Text.Trim(),
            Company = _company.Text.Trim(),
            Location = NullIfBlank(_location.Text),
            Viewed = _viewed.Checked,
            IsTopApplicant = _topApplicant.Checked,
            PostedDateText = NullIfBlank(_postedText.Text),
            PostedDate = posted,
            CompensationText = NullIfBlank(_compensation.Text),
            ApplyUrl = NullIfBlank(_applyUrl.Text),
            CompSpecified = NullIfBlank(_compSpecified.Text),
            ResumeUsed = NullIfBlank(_resumeUsed.Text),
            AppliedAt = _appliedEnabled.Checked ? _appliedAt.Value : null,
            RawPaste = _current?.RawPaste ?? NullIfBlank(_pasteBox.Text),
        };

        job.JobKey = JobKeyBuilder.Build(job.JobTitle, job.Company, job.Location);
        return job;
    }

    private async Task SaveAsync()
    {
        if (_repository is null)
        {
            MessageBox.Show(this, "Configure Azure SQL connection first.", "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var job = CaptureFromEditor();
            if (string.IsNullOrWhiteSpace(job.JobTitle) || string.IsNullOrWhiteSpace(job.Company))
            {
                MessageBox.Show(this, "Job Title and Company are required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(job.JobKey))
            {
                MessageBox.Show(this, "Could not build JobKey (letters/numbers only from Title+Company+Location).", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var existing = await _repository.GetByKeyAsync(job.JobKey);
            if (existing is not null)
            {
                // Preserve apply fields the user may not have retyped if grid row was a fresh parse.
                if (string.IsNullOrWhiteSpace(job.ApplyUrl))
                    job.ApplyUrl = existing.ApplyUrl;
                if (string.IsNullOrWhiteSpace(job.CompSpecified))
                    job.CompSpecified = existing.CompSpecified;
                if (string.IsNullOrWhiteSpace(job.ResumeUsed))
                    job.ResumeUsed = existing.ResumeUsed;
                if (!job.AppliedAt.HasValue)
                    job.AppliedAt = existing.AppliedAt;
            }

            await _repository.UpsertAsync(job);
            _current = job;
            _jobKey.Text = job.JobKey;
            SetStatus(existing is null
                ? $"Saved new job ({job.JobKey})."
                : $"Updated existing job ({job.JobKey}).");
            await RefreshGridAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Save failed: " + ex.Message);
        }
    }

    private async Task RefreshGridAsync()
    {
        if (_repository is null)
            return;

        try
        {
            var rows = await _repository.ListRecentAsync();
            _binding.DataSource = rows.ToList();
            SetStatus($"Loaded {rows.Count} saved job(s).");
        }
        catch (Exception ex)
        {
            SetStatus("Refresh failed: " + ex.Message);
        }
    }

    private async Task EnsureSchemaAsync()
    {
        if (_repository is null)
        {
            MessageBox.Show(this, "Configure Azure SQL connection first.", "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await _repository.EnsureSchemaAsync();
            SetStatus("Ensured dbo.Jobs exists.");
            MessageBox.Show(this, "dbo.Jobs is ready.", "Schema", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Schema failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSettings()
    {
        using var dlg = new ConnectionSettingsForm(AppConfig.GetConnectionString(_configuration));
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = $$"""
                {
                  "ConnectionStrings": {
                    "AzureSql": {{System.Text.Json.JsonSerializer.Serialize(dlg.ConnectionString)}}
                  }
                }
                """;
            File.WriteAllText(path, json);
            RebuildRepository();
            _ = InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenApplyUrl()
    {
        var url = _applyUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Paste an Apply URL into the field first.", "No URL", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLinkedInSearch()
    {
        var title = Uri.EscapeDataString(_jobTitle.Text.Trim());
        var company = Uri.EscapeDataString(_company.Text.Trim());
        var keywords = string.Join("%20", new[] { title, company }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(keywords))
        {
            MessageBox.Show(this, "Need a job title and/or company to search.", "Search", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var url = $"https://www.linkedin.com/jobs/search/?keywords={keywords}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            SetStatus("Opened LinkedIn search — copy the job URL into Apply URL, then Save.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearEditor()
    {
        _pasteBox.Clear();
        _loadingFields = true;
        _current = null;
        _jobTitle.Clear();
        _company.Clear();
        _location.Clear();
        _jobKey.Clear();
        _viewed.Checked = false;
        _topApplicant.Checked = false;
        _postedText.Clear();
        _postedDate.Clear();
        _compensation.Clear();
        _applyUrl.Clear();
        _compSpecified.Clear();
        _resumeUsed.Clear();
        _appliedEnabled.Checked = false;
        _loadingFields = false;
        SetStatus("Cleared.");
    }

    private void SetStatus(string message) => _status.Text = message;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
