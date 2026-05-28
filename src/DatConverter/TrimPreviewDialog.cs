namespace DatConverter;

public sealed class TrimPreviewDialog : Form
{
    private readonly TrimPreviewFrameProvider frameProvider;
    private readonly TrimPreviewState state;
    private readonly FpsOption fps;
    private readonly Action<string>? technicalLog;
    private readonly PictureBox previewPictureBox;
    private readonly Label previewMessageLabel;
    private readonly Label currentValueLabel;
    private readonly Label startValueLabel;
    private readonly Label endValueLabel;
    private readonly Label durationValueLabel;
    private readonly TrimTimelineControl timelineControl;
    private CancellationTokenSource? previewCancellation;
    private int previewRequestVersion;

    public TrimPreviewDialog(
        QueueItem item,
        FfmpegTools ffmpegTools,
        Action<string>? technicalLog = null)
    {
        frameProvider = new TrimPreviewFrameProvider(ffmpegTools);
        state = new TrimPreviewState(RecordingTimelineBuilder.Build(item), item.TrimRange);
        fps = item.Fps;
        this.technicalLog = technicalLog;

        Text = "Trim Video";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 560);
        Font = SystemFonts.MessageBoxFont;

        previewPictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };
        previewMessageLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Loading preview...",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            BackColor = Color.Black
        };
        currentValueLabel = CreateValueLabel();
        startValueLabel = CreateValueLabel();
        endValueLabel = CreateValueLabel();
        durationValueLabel = CreateValueLabel();
        timelineControl = new TrimTimelineControl
        {
            Dock = DockStyle.Fill,
        };
        timelineControl.SetTimeline(state.Timeline.TotalDuration);
        timelineControl.Current = state.Current;

        Controls.Add(BuildRoot());
        UpdateLabels();
        timelineControl.CurrentChanged += (_, _) =>
        {
            state.SetCurrent(timelineControl.Current);
            UpdateLabels();
            _ = LoadPreviewAsync();
        };
        Shown += async (_, _) => await LoadPreviewAsync();
        FormClosed += (_, _) => Cleanup();
    }

    public TrimRange? SelectedTrimRange { get; private set; }

    private Control BuildRoot()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var previewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };
        previewPanel.Controls.Add(previewPictureBox);
        previewPanel.Controls.Add(previewMessageLabel);

        root.Controls.Add(previewPanel, 0, 0);
        root.Controls.Add(timelineControl, 0, 1);
        root.Controls.Add(BuildValueGrid(), 0, 2);
        root.Controls.Add(BuildTrimButtonPanel(), 0, 3);
        root.Controls.Add(BuildDialogButtonPanel(), 0, 4);
        return root;
    }

    private Control BuildValueGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        grid.Controls.Add(CreateHeaderLabel("Current"), 0, 0);
        grid.Controls.Add(currentValueLabel, 1, 0);
        grid.Controls.Add(CreateHeaderLabel("Start"), 0, 1);
        grid.Controls.Add(startValueLabel, 1, 1);
        grid.Controls.Add(CreateHeaderLabel("End"), 0, 2);
        grid.Controls.Add(endValueLabel, 1, 2);
        grid.Controls.Add(CreateHeaderLabel("Duration"), 0, 3);
        grid.Controls.Add(durationValueLabel, 1, 3);
        return grid;
    }

    private Control BuildTrimButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var setStartButton = CreateButton("Set Start");
        var setEndButton = CreateButton("Set End");
        var clearTrimButton = CreateButton("Clear Trim");
        setStartButton.Click += (_, _) =>
        {
            state.SetStartToCurrent();
            UpdateLabels();
        };
        setEndButton.Click += (_, _) =>
        {
            state.SetEndToCurrent();
            UpdateLabels();
        };
        clearTrimButton.Click += (_, _) =>
        {
            state.ClearTrim();
            UpdateLabels();
        };
        panel.Controls.Add(setStartButton);
        panel.Controls.Add(setEndButton);
        panel.Controls.Add(clearTrimButton);
        return panel;
    }

    private Control BuildDialogButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var okButton = CreateButton("OK");
        var cancelButton = CreateButton("Cancel");
        okButton.Click += (_, _) =>
        {
            if (!state.TryBuildTrimRange(out var trimRange, out var message))
            {
                MessageBox.Show(this, message == "End must be after Start." ? message : "End must be after Start.", "Trim Video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedTrimRange = trimRange;
            DialogResult = DialogResult.OK;
            Close();
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        panel.Controls.Add(okButton);
        panel.Controls.Add(cancelButton);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        return panel;
    }

    private async Task LoadPreviewAsync()
    {
        var version = Interlocked.Increment(ref previewRequestVersion);
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = new CancellationTokenSource();
        var token = previewCancellation.Token;

        previewMessageLabel.Text = "Loading preview...";
        previewMessageLabel.Visible = true;
        previewPictureBox.Visible = false;

        TrimPreviewFrameResult result;
        try
        {
            result = await frameProvider.ExtractFrameAsync(state.Timeline, state.Current, fps, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || version != previewRequestVersion || IsDisposed)
        {
            return;
        }

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.ImagePath))
        {
            previewPictureBox.Image?.Dispose();
            previewPictureBox.Image = null;
            previewPictureBox.Visible = false;
            previewMessageLabel.Text = "Preview unavailable at this position.";
            previewMessageLabel.Visible = true;
            technicalLog?.Invoke(result.TechnicalDetails);
            return;
        }

        SetPreviewImage(result.ImagePath);
        technicalLog?.Invoke(result.TechnicalDetails);
    }

    private void SetPreviewImage(string path)
    {
        using var loaded = Image.FromFile(path);
        var image = new Bitmap(loaded);
        var previous = previewPictureBox.Image;
        previewPictureBox.Image = image;
        previous?.Dispose();
        previewPictureBox.Visible = true;
        previewMessageLabel.Visible = false;
    }

    private void UpdateLabels()
    {
        currentValueLabel.Text = TrimRangeFormatter.FormatOffset(state.Timeline, state.Current);
        startValueLabel.Text = state.Start.HasValue ? TrimRangeFormatter.FormatOffset(state.Timeline, state.Start.Value) : "Full video";
        endValueLabel.Text = state.End.HasValue ? TrimRangeFormatter.FormatOffset(state.Timeline, state.End.Value) : "Full video";
        durationValueLabel.Text = TrimRangeFormatter.FormatPreviewDuration(state.Current, state.Start, state.End);
        timelineControl.Current = state.Current;
        timelineControl.SetMarkers(state.Start, state.End);
    }

    private void Cleanup()
    {
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = null;
        previewPictureBox.Image?.Dispose();
        previewPictureBox.Image = null;
        frameProvider.Dispose();
        technicalLog?.Invoke(frameProvider.CleanupTechnicalDetails);
    }

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText
        };
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            Size = new Size(112, 36),
            Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = true
        };
    }
}
