namespace CjmApplyAssistant;

public sealed class ConnectionSettingsForm : Form
{
    private readonly TextBox _connectionString = new();

    public string ConnectionString => _connectionString.Text.Trim();

    public ConnectionSettingsForm(string? current)
    {
        Text = "Azure SQL connection";
        Width = 720;
        Height = 260;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(new Label
        {
            Text = "Paste your Azure SQL connection string. You can also set env var CJM_APPLY_SQL.",
            Dock = DockStyle.Fill,
            AutoSize = false,
        }, 0, 0);

        _connectionString.Multiline = true;
        _connectionString.Dock = DockStyle.Fill;
        _connectionString.ScrollBars = ScrollBars.Vertical;
        _connectionString.Text = current ?? "";
        layout.Controls.Add(_connectionString, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
    }
}
