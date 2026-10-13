using System.Diagnostics;
using OfficeHealthCheck.Core;

namespace OfficeHealthCheck;

/// <summary>Главният прозорец. Светла тема, всичко се строи в кода (без Designer).</summary>
internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(0x1B, 0x24, 0x30);
    private static readonly Color Muted = Color.FromArgb(0x5B, 0x66, 0x75);
    private static readonly Color Paper = Color.White;
    private static readonly Color Band = Color.FromArgb(0xF4, 0xF6, 0xF9);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _overall = new();
    private readonly ListView _list = new();
    private readonly Label _status = new();
    private readonly Button _run = new();
    private readonly Button _save = new();
    private readonly Button _backup = new();
    private HealthReport? _report;

    public MainForm()
    {
        Text = "Office Health Check";
        Font = new Font("Segoe UI", 10f);
        BackColor = Paper;
        ForeColor = Ink;
        MinimumSize = new Size(820, 560);
        Size = new Size(1040, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);

        BuildLayout();
        Shown += async (_, _) => await RunChecksAsync();
    }

    private void BuildLayout()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 8),
            BackColor = Paper,
        };
        _title.Text = "Проверка на компютъра";
        _title.Font = new Font("Segoe UI Semibold", 17f);
        _title.AutoSize = true;
        _subtitle.ForeColor = Muted;
        _subtitle.AutoSize = true;
        _subtitle.Text = $"{Environment.MachineName} · {Environment.UserName}";
        _overall.AutoSize = false;
        _overall.Height = 44;
        _overall.Dock = DockStyle.Fill;
        _overall.TextAlign = ContentAlignment.MiddleLeft;
        _overall.Padding = new Padding(12, 0, 12, 0);
        _overall.Margin = new Padding(0, 14, 0, 6);
        _overall.Font = new Font("Segoe UI Semibold", 11.5f);
        _overall.BackColor = Band;
        _overall.Text = "Проверката започва…";
        header.Controls.Add(_title);
        header.Controls.Add(_subtitle);
        header.Controls.Add(_overall);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.BorderStyle = BorderStyle.None;
        _list.ShowItemToolTips = true;
        _list.Columns.Add("Състояние", 120);
        _list.Columns.Add("Проверка", 230);
        _list.Columns.Add("Резултат", 330);
        _list.Columns.Add("Какво да направиш", 320);
        _list.DoubleClick += (_, _) => ShowDetails();
        _list.Resize += (_, _) => StretchLastColumn();

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 0), BackColor = Paper };
        listHost.Controls.Add(_list);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(20, 10, 20, 16),
            BackColor = Band,
            WrapContents = true,
        };
        StyleButton(_run, "Провери отново", primary: false);
        StyleButton(_save, "Запази отчет (PDF)", primary: true);
        StyleButton(_backup, "Папка за архив…", primary: false);
        _run.Click += async (_, _) => await RunChecksAsync();
        _save.Click += async (_, _) => await SaveReportAsync();
        _backup.Click += async (_, _) => await ChooseBackupFolderAsync();
        _status.AutoSize = true;
        _status.ForeColor = Muted;
        _status.Margin = new Padding(12, 14, 0, 0);
        footer.Controls.AddRange(new Control[] { _save, _run, _backup, _status });

        Controls.Add(listHost);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private static void StyleButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(10, 4, 10, 4);
        button.Margin = new Padding(4);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = primary ? Ink : Color.FromArgb(0xC9, 0xD1, 0xDB);
        button.BackColor = primary ? Ink : Paper;
        button.ForeColor = primary ? Paper : Ink;
        button.Cursor = Cursors.Hand;
    }

    private void StretchLastColumn()
    {
        if (_list.Columns.Count < 4) return;
        int used = _list.Columns[0].Width + _list.Columns[1].Width + _list.Columns[2].Width;
        _list.Columns[3].Width = Math.Max(200, _list.ClientSize.Width - used - 4);
    }

    private async Task RunChecksAsync()
    {
        SetBusy(true);
        _overall.Text = "Проверявам…";
        _overall.BackColor = Band;
        _overall.ForeColor = Ink;
        _list.Items.Clear();

        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            _report = await Task.Run(() => SystemInfo.CollectAsync(_settings, progress));
            ShowReport(_report);
            _status.Text = $"Проверено в {_report.GeneratedAt:HH:mm}. Двоен клик на ред за подробности.";
        }
        catch (Exception ex)
        {
            _overall.Text = "Проверката не успя: " + ex.Message;
            _status.Text = "";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowReport(HealthReport report)
    {
        _subtitle.Text = $"{report.ComputerName} · {report.UserName} · {report.OsDescription}";

        var (back, fore) = Colors(report.Overall);
        _overall.BackColor = back;
        _overall.ForeColor = fore;
        var counts = new[] { CheckStatus.Fail, CheckStatus.Warning, CheckStatus.Ok }
            .Where(s => report.Count(s) > 0)
            .Select(s => $"{StatusText.Label(s)}: {report.Count(s)}");
        _overall.Text = $"{StatusText.OverallHeadline(report.Overall)}     {string.Join("   ", counts)}";

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Groups.Clear();
        foreach (var group in report.Results.GroupBy(r => r.Category))
        {
            var lvGroup = new ListViewGroup(group.Key);
            _list.Groups.Add(lvGroup);
            foreach (var r in group.OrderBy(r => r.Status))
            {
                var item = new ListViewItem(new[] { StatusText.Label(r.Status), r.Title, r.Summary, r.Advice ?? "" }, lvGroup)
                {
                    Tag = r,
                    UseItemStyleForSubItems = false,
                    ToolTipText = r.Details is { Count: > 0 } ? string.Join("\n", r.Details) : r.Summary,
                };
                var (b, f) = Colors(r.Status);
                item.SubItems[0].BackColor = b;
                item.SubItems[0].ForeColor = f;
                item.SubItems[0].Font = new Font(_list.Font, FontStyle.Bold);
                item.SubItems[3].ForeColor = Muted;
                _list.Items.Add(item);
            }
        }
        _list.EndUpdate();
        StretchLastColumn();
    }

    private void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not CheckResult r) return;

        var text = $"{r.Summary}";
        if (!string.IsNullOrEmpty(r.Advice)) text += $"\n\nКакво да направиш:\n{r.Advice}";
        if (r.Details is { Count: > 0 }) text += "\n\nПодробности:\n• " + string.Join("\n• ", r.Details);
        MessageBox.Show(this, text, r.Title, MessageBoxButtons.OK,
            r.Status switch
            {
                CheckStatus.Fail => MessageBoxIcon.Error,
                CheckStatus.Warning => MessageBoxIcon.Warning,
                _ => MessageBoxIcon.Information,
            });
    }

    private async Task SaveReportAsync()
    {
        if (_report is null) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Запази отчета",
            Filter = "PDF документ (*.pdf)|*.pdf|Уеб страница (*.html)|*.html",
            FileName = ReportExport.DefaultFileName(_report),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true);
        _status.Text = "Записвам отчета…";
        try
        {
            string saved = await ReportExport.SaveAsync(_report, dialog.FileName);
            _status.Text = saved.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase)
                ? $"Записан: {saved}"
                : $"PDF не можа да се направи. Записан е като уеб страница: {saved}";
            Process.Start(new ProcessStartInfo(saved) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = "Отчетът не е записан: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ChooseBackupFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Избери папката, в която се правят архивите (USB диск или мрежова папка).",
            UseDescriptionForTitle = true,
            SelectedPath = _settings.BackupFolder ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _settings.BackupFolder = dialog.SelectedPath;
        _settings.Save();
        await RunChecksAsync();
    }

    private void SetBusy(bool busy)
    {
        _run.Enabled = _save.Enabled = _backup.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private static (Color Back, Color Fore) Colors(CheckStatus status) => status switch
    {
        CheckStatus.Ok => (Color.FromArgb(0xE6, 0xF4, 0xEC), Color.FromArgb(0x1E, 0x7F, 0x4F)),
        CheckStatus.Warning => (Color.FromArgb(0xFF, 0xF4, 0xDC), Color.FromArgb(0x9A, 0x62, 0x00)),
        CheckStatus.Fail => (Color.FromArgb(0xFD, 0xE9, 0xE7), Color.FromArgb(0xB4, 0x23, 0x18)),
        CheckStatus.Info => (Color.FromArgb(0xE9, 0xF0, 0xFB), Color.FromArgb(0x33, 0x56, 0x8C)),
        _ => (Color.FromArgb(0xEE, 0xF1, 0xF4), Color.FromArgb(0x5B, 0x66, 0x75)),
    };
}
