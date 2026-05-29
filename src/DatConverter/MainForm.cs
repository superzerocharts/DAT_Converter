namespace DatConverter;

public sealed class MainForm : Form
{
    private const int DetailsExpandedHeight = 220;
    private const int DetailsFooterHeight = 54;
    private const int DefaultWindowWidth = 1080;
    private const int DefaultWindowHeight = 740;
    private const int MinimumWindowWidth = 960;
    private const int MinimumWindowHeight = 680;
    private const int WindowScreenMargin = 40;
    private const int ActionRowHeight = 54;
    private const int FileSelectionRowHeight = 156;
    private const int MinimumBatchOptionsRowHeight = 128;
    private const int MinimumBatchOptionStackHeight = 78;
    private const int MinimumQueueGridHeight = 92;
    private const int MinimumFlexibleContentWidth = 760;
    private const int QueueStatusColumnWidth = 108;
    private const int QueueFileColumnWidth = 202;
    private const int QueueOutputColumnWidth = 278;
    private const int QueueFormatColumnWidth = 70;
    private const int QueueModeColumnWidth = 86;
    private const int QueueFpsColumnWidth = 70;
    private const int QueueResolutionColumnWidth = 92;
    private const int QueueProgressColumnWidth = 109;

    private const string MissingToolsStatusMessage = "The app needs its tools folder in the same folder as DatConverter.exe.";
    private const string MissingToolsExplanationMessage =
        "Keep the tools folder next to DatConverter.exe, then reopen the app.";
    private const string MissingToolsDetailsMessage =
        MissingToolsStatusMessage + "\r\n" +
        MissingToolsExplanationMessage;
    private const string AutoDetectFpsLabel = SourceFpsOptions.AutoDetectLabel;

    private readonly TextBox selectedFilePathTextBox;
    private readonly Button browseFileButton;
    private readonly Button addFolderButton;
    private readonly DataGridView queueGridView;
    private readonly Button startQueueButton;
    private readonly Button stopAfterCurrentButton;
    private readonly Button cancelQueueButton;
    private readonly Button removeSelectedQueueItemButton;
    private readonly Button duplicateSelectedQueueItemButton;
    private readonly Button clearCompletedQueueButton;
    private readonly RadioButton sameFolderRadioButton;
    private readonly RadioButton chooseFolderRadioButton;
    private readonly TextBox outputFolderTextBox;
    private readonly Button browseOutputFolderButton;
    private readonly ComboBox outputFormatComboBox;
    private readonly ComboBox conversionModeComboBox;
    private readonly ComboBox frameRateComboBox;
    private readonly Button convertButton;
    private readonly Button cancelButton;
    private readonly ProgressBar conversionProgressBar;
    private readonly Label currentStatusLabel;
    private readonly Button showDetailsButton;
    private readonly RichTextBox statusLogTextBox;
    private readonly Button openOutputFolderButton;
    private readonly Button wordWrapButton;
    private readonly Button copyLogButton;
    private readonly Button clearLogButton;
    private readonly FfmpegTools ffmpegTools;
    private readonly NvencCapabilityResult nvencCapability;
    private readonly ProbeService probeService;
    private readonly ConversionService conversionService;
    private readonly QueueItemFpsResolver queueItemFpsResolver = new();
    private readonly AppSettingsService appSettingsService;
    private readonly AppSettings appSettings;
    private readonly TechnicalLogBuffer technicalLog = new();
    private readonly System.Diagnostics.Stopwatch startupStopwatch = System.Diagnostics.Stopwatch.StartNew();
    private readonly FileSelectionState selectionState = new();
    private readonly List<QueueItem> queueItems = new();
    private CancellationTokenSource? probeCancellationTokenSource;
    private int probeSequence;
    private bool isConversionRunning;
    private bool isQueueProcessing;
    private bool isQueuePreProbeRunning;
    private bool stopAfterCurrentRequested;
    private bool cancelCurrentOnlyRequested;
    private bool cancelQueueRequested;
    private bool areDetailsVisible;
    private Size? detailsCollapsedClientSize;
    private TableLayoutPanel? rootLayout;
    private RowStyle? detailsRowStyle;
    private Control? detailsPanel;
    private PictureBox? headerLogoPictureBox;
    private QueueItem? currentQueueItem;
    private QueueSettingsSnapshot? activeQueueSettings;
    private QueueSettingsSnapshot? lastQueueRunSettings;
    private string lastQueueRunStatus = "Not run";
    private string? lastSuccessfulOutputPath;
    private ConversionResult? lastConversionResult;
    private CancellationTokenSource? conversionCancellationTokenSource;
    private ConversionProgress? lastConversionProgress;
    private DateTime lastProgressUiUpdateUtc = DateTime.MinValue;
    private string currentConversionHeadline = string.Empty;
    private string currentUserStatus = "Ready. Select a compatible raw H.264 .dat file to begin.";
    private readonly Font normalStatusFont;
    private readonly Font boldStatusFont;
    private bool isInitializing;
    private bool queueColumnsUserResized;
    private bool isApplyingQueueColumnWidths;
    private bool isRefreshingQueueGrid;
    private int pendingRunningQueueAddOperations;
    private bool deferredStartupCompleted;

    private enum DetailsScrollMode
    {
        Preserve,
        Bottom
    }

    private sealed record QueueItemEditorSnapshot(
        string PlannedOutputPath,
        string ConversionMode,
        TrimRange? TrimRange,
        string? CustomOutputPath,
        bool HasCustomOutputPath,
        bool HasUserCustomOutputPath,
        bool HasCustomMode,
        bool BurnTimestamp)
    {
        public static QueueItemEditorSnapshot Capture(QueueItem item)
        {
            return new QueueItemEditorSnapshot(
                item.PlannedOutputPath,
                item.ConversionMode,
                item.TrimRange,
                item.CustomOutputPath,
                item.HasCustomOutputPath,
                item.HasUserCustomOutputPath,
                item.HasCustomMode,
                item.BurnTimestamp);
        }

        public void Restore(QueueItem item)
        {
            item.PlannedOutputPath = PlannedOutputPath;
            item.ConversionMode = ConversionMode;
            item.TrimRange = TrimRange;
            item.CustomOutputPath = CustomOutputPath;
            item.HasCustomOutputPath = HasCustomOutputPath;
            item.HasUserCustomOutputPath = HasUserCustomOutputPath;
            item.HasCustomMode = HasCustomMode;
            item.BurnTimestamp = BurnTimestamp;
        }
    }

    private const int EmGetScrollPos = 0x04DD;
    private const int EmSetScrollPos = 0x04DE;
    private const int WmSetRedraw = 0x000B;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref NativePoint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static Size GetClampedStartupWindowSize(Size requestedSize, Rectangle workingArea)
    {
        var maxWidth = Math.Max(MinimumWindowWidth, Math.Min(DefaultWindowWidth, workingArea.Width - (WindowScreenMargin * 2)));
        var maxHeight = Math.Max(MinimumWindowHeight, Math.Min(DefaultWindowHeight, workingArea.Height - (WindowScreenMargin * 2)));
        var width = Math.Clamp(requestedSize.Width <= 0 ? DefaultWindowWidth : requestedSize.Width, MinimumWindowWidth, maxWidth);
        var height = Math.Clamp(requestedSize.Height <= 0 ? DefaultWindowHeight : requestedSize.Height, MinimumWindowHeight, maxHeight);
        return new Size(width, height);
    }

    public static bool CanRemoveQueueItemForState(QueueItem item, QueueItem? currentQueueItem, bool isQueueProcessing)
    {
        return !isQueueProcessing &&
               item != currentQueueItem &&
               item.Status is not QueueItemStatus.Running and
                   not QueueItemStatus.Probing and
                   not QueueItemStatus.Converting;
    }

    public static int RemoveSelectedQueueItemsForState(
        IList<QueueItem> queue,
        IEnumerable<QueueItem> selectedItems,
        QueueItem? currentQueueItem,
        bool isQueueProcessing)
    {
        var removableItems = selectedItems
            .Where(item => queue.Contains(item) && CanRemoveQueueItemForState(item, currentQueueItem, isQueueProcessing))
            .Distinct()
            .ToList();

        foreach (var item in removableItems)
        {
            queue.Remove(item);
        }

        return removableItems.Count;
    }

    public static bool CanCopyQueueItemForState(QueueItem item, bool isQueueProcessing)
    {
        return !isQueueProcessing &&
               item.Status is not QueueItemStatus.Probing and
                   not QueueItemStatus.Converting and
                   not QueueItemStatus.Running;
    }

    public static string GetQueueCopyActionText(QueueItem? item)
    {
        return item?.Status == QueueItemStatus.Completed
            ? "Re-add"
            : "Duplicate";
    }

    public MainForm()
    {
        ffmpegTools = ToolPathService.ResolveBundledTools();
        nvencCapability = new NvencCapabilityService().Detect(ffmpegTools);
        probeService = new ProbeService(ffmpegTools);
        conversionService = new ConversionService(ffmpegTools);
        appSettingsService = new AppSettingsService(nvencCapability.IsAvailable);
        appSettings = appSettingsService.Load(out var settingsLoadMessage);
        selectionState.OutputDestinationMode = ParseOutputDestinationMode(appSettings.OutputDestinationMode);
        selectionState.ChosenOutputFolderPath = appSettings.LastChosenOutputFolder;

        Text = "DAT Converter";
        DoubleBuffered = true;
        var appIcon = TryLoadAppIcon();
        if (appIcon is not null)
        {
            Icon = appIcon;
        }

        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(MinimumWindowWidth, MinimumWindowHeight);
        Size = GetClampedStartupWindowSize(
            new Size(appSettings.WindowWidth, appSettings.WindowHeight),
            Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Resize += MainForm_Resize;

        isInitializing = true;
        selectedFilePathTextBox = CreateReadOnlyTextBox("No .dat file selected");
        browseFileButton = CreateButton("Add Files...");
        browseFileButton.Size = new Size(148, 42);
        addFolderButton = CreateButton("Add Folder...");
        addFolderButton.Size = new Size(166, 42);
        queueGridView = CreateQueueGridView();
        startQueueButton = CreateButton("Start Queue");
        startQueueButton.Size = new Size(160, 42);
        stopAfterCurrentButton = CreateButton("Stop After Current");
        stopAfterCurrentButton.Size = new Size(230, 42);
        cancelQueueButton = CreateButton("Cancel Queue");
        cancelQueueButton.Size = new Size(180, 42);
        removeSelectedQueueItemButton = CreateButton("Remove");
        removeSelectedQueueItemButton.Size = new Size(216, 42);
        duplicateSelectedQueueItemButton = CreateButton("Duplicate");
        duplicateSelectedQueueItemButton.Size = new Size(216, 42);
        clearCompletedQueueButton = CreateButton("Clear");
        clearCompletedQueueButton.Size = new Size(212, 42);
        sameFolderRadioButton = new RadioButton
        {
            AutoSize = true,
            Text = "Same folder as source file",
            Checked = selectionState.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource,
            Margin = new Padding(0, 6, 18, 4)
        };
        chooseFolderRadioButton = new RadioButton
        {
            AutoSize = true,
            Text = "Choose output folder",
            Checked = selectionState.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder,
            Margin = new Padding(0, 6, 0, 4)
        };
        outputFolderTextBox = CreateReadOnlyTextBox(string.IsNullOrWhiteSpace(selectionState.ChosenOutputFolderPath) ? "No output folder selected" : selectionState.ChosenOutputFolderPath);
        browseOutputFolderButton = CreateButton("Browse...");
        outputFormatComboBox = CreateComboBox(new[] { "MP4", "MKV" }, appSettings.OutputFormat);
        conversionModeComboBox = CreateComboBox(ConversionModes.DisplayOrder, FormatConversionModeForDisplay(appSettings.ConversionMode));
        ConfigureConversionModeComboBox(conversionModeComboBox);
        frameRateComboBox = CreateComboBox(SourceFpsOptions.DisplayOrder, appSettings.Fps);
        convertButton = CreateButton("Convert");
        cancelButton = CreateButton("Cancel Current");
        cancelButton.Size = new Size(190, 42);
        conversionProgressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        currentStatusLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = currentUserStatus,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 4, 0)
        };
        normalStatusFont = currentStatusLabel.Font;
        boldStatusFont = new Font(normalStatusFont, FontStyle.Bold);
        showDetailsButton = CreateButton("Show Details");
        showDetailsButton.Size = new Size(170, 42);
        statusLogTextBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            Text = currentUserStatus,
            WordWrap = false,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            DetectUrls = false
        };
        openOutputFolderButton = CreateButton("Open Output");
        openOutputFolderButton.Size = new Size(250, 42);
        openOutputFolderButton.TextAlign = ContentAlignment.MiddleCenter;
        openOutputFolderButton.ImageAlign = ContentAlignment.MiddleCenter;
        openOutputFolderButton.TextImageRelation = TextImageRelation.Overlay;
        openOutputFolderButton.Padding = Padding.Empty;
        wordWrapButton = CreateButton("Word Wrap: Off");
        wordWrapButton.Size = new Size(190, 42);
        copyLogButton = CreateButton("Copy Log");
        clearLogButton = CreateButton("Clear Log");

        convertButton.Enabled = false;
        convertButton.Visible = false;
        cancelButton.Enabled = false;
        openOutputFolderButton.Enabled = false;
        copyLogButton.Visible = true;
        clearLogButton.Visible = true;
        startQueueButton.Enabled = false;
        stopAfterCurrentButton.Enabled = false;
        cancelQueueButton.Enabled = false;
        removeSelectedQueueItemButton.Enabled = false;
        duplicateSelectedQueueItemButton.Enabled = false;
        clearCompletedQueueButton.Enabled = false;
        browseFileButton.Enabled = ffmpegTools.AreAvailable;
        browseFileButton.Click += BrowseFileButton_Click;
        addFolderButton.Enabled = ffmpegTools.AreAvailable;
        addFolderButton.Click += AddFolderButton_Click;
        queueGridView.SelectionChanged += QueueGridView_SelectionChanged;
        queueGridView.CellDoubleClick += QueueGridView_CellDoubleClick;
        queueGridView.ColumnWidthChanged += QueueGridView_ColumnWidthChanged;
        queueGridView.Resize += QueueGridView_Resize;
        startQueueButton.Click += StartQueueButton_Click;
        stopAfterCurrentButton.Click += StopAfterCurrentButton_Click;
        cancelQueueButton.Click += CancelQueueButton_Click;
        removeSelectedQueueItemButton.Click += RemoveSelectedQueueItemButton_Click;
        duplicateSelectedQueueItemButton.Click += DuplicateSelectedQueueItemButton_Click;
        clearCompletedQueueButton.Click += ClearCompletedQueueButton_Click;
        sameFolderRadioButton.CheckedChanged += OutputDestinationRadioButton_CheckedChanged;
        chooseFolderRadioButton.CheckedChanged += OutputDestinationRadioButton_CheckedChanged;
        browseOutputFolderButton.Click += BrowseOutputFolderButton_Click;
        outputFormatComboBox.SelectedIndexChanged += OutputFormatComboBox_SelectedIndexChanged;
        conversionModeComboBox.SelectedIndexChanged += ConversionModeComboBox_SelectedIndexChanged;
        frameRateComboBox.SelectedIndexChanged += FrameRateComboBox_SelectedIndexChanged;
        convertButton.Click += ConvertButton_Click;
        cancelButton.Click += CancelButton_Click;
        openOutputFolderButton.Click += OpenOutputFolderButton_Click;
        showDetailsButton.Click += ShowDetailsButton_Click;
        wordWrapButton.Click += WordWrapButton_Click;
        copyLogButton.Click += CopyLogButton_Click;
        clearLogButton.Click += ClearLogButton_Click;
        ResizeEnd += MainForm_ResizeEnd;

        var layout = BuildLayout();
        Controls.Add(layout);
        ApplyMinimumUsableWindowSize();
        ConfigureQueueDragDropTarget(this);
        ConfigureQueueDragDropTargets(layout);
        InitializeTechnicalLog();
        technicalLog.Append(settingsLoadMessage);
        technicalLog.Append($"Output destination mode: {FormatOutputDestinationMode(selectionState.OutputDestinationMode)}.");
        isInitializing = false;
        ApplyOutputDestinationMode();
        ApplyStartupToolValidation();
    }

    public FfmpegTools FfmpegTools => ffmpegTools;

    private void ConfigureQueueDragDropTargets(Control control)
    {
        ConfigureQueueDragDropTarget(control);

        foreach (Control child in control.Controls)
        {
            if (child is TextBoxBase or ComboBox or Button or ProgressBar)
            {
                continue;
            }

            ConfigureQueueDragDropTargets(child);
        }
    }

    private void ConfigureQueueDragDropTarget(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += QueueDropTarget_DragEnter;
        control.DragOver += QueueDropTarget_DragOver;
        control.DragDrop += QueueDropTarget_DragDrop;
    }

    private void InitializeTechnicalLog()
    {
        technicalLog.Append($"DAT Converter session started. Version: {TechnicalLogBuffer.GetAppVersion()}");
        technicalLog.Append($"Application base directory: {ffmpegTools.ApplicationBaseDirectory}");
        technicalLog.Append($"Checked ffmpeg path: {ffmpegTools.FfmpegPath} ({FormatFoundStatus(ffmpegTools.FfmpegExists)})");
        technicalLog.Append($"Checked ffprobe path: {ffmpegTools.FfprobePath} ({FormatFoundStatus(ffmpegTools.FfprobeExists)})");
        technicalLog.Append($"NVENC capability: {(nvencCapability.IsAvailable ? "available" : "unavailable")}. {nvencCapability.DiagnosticSummary}");
    }

    private void ApplyStartupToolValidation()
    {
        RefreshStatusLog(ffmpegTools.AreAvailable
            ? "Ready."
            : MissingToolsStatusMessage);

        if (!ffmpegTools.AreAvailable)
        {
            technicalLog.Append("Startup validation failed: bundled FFmpeg tools are missing.");
            convertButton.Enabled = false;
        }
    }

    private static string FormatFoundStatus(bool exists)
    {
        return exists ? "found" : "missing";
    }

    private static Icon? TryLoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DatConverter.ico");
        try
        {
            return File.Exists(iconPath) ? new Icon(iconPath) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Image? TryLoadHeaderLogo()
    {
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DatConverterLogo.png");
        try
        {
            return File.Exists(logoPath)
                ? Image.FromStream(new MemoryStream(File.ReadAllBytes(logoPath)))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async void BrowseFileButton_Click(object? sender, EventArgs e)
    {
        if (!TryPrepareForQueueAdd())
        {
            return;
        }

        var addingToRunningQueue = isQueueProcessing;
        if (addingToRunningQueue)
        {
            pendingRunningQueueAddOperations++;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Add DAT files",
            Filter = "DAT files (*.dat)|*.dat",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        try
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await AddFilesToQueueAsync(dialog.FileNames, addingToRunningQueue);
        }
        finally
        {
            if (addingToRunningQueue)
            {
                pendingRunningQueueAddOperations--;
            }
        }
    }

    private async void AddFolderButton_Click(object? sender, EventArgs e)
    {
        if (isQueueProcessing)
        {
            const string message = "Add Folder is unavailable while the queue is running.";
            RefreshStatusLog(message);
            MessageBox.Show(this, message, "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryPrepareForQueueAdd())
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select folder containing .dat files",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!ShowAddFolderOptionsDialog(dialog.SelectedPath, out var includeSubfolders))
        {
            return;
        }

        await ScanFolderAndPreviewQueueAddAsync(dialog.SelectedPath, includeSubfolders);
    }

    private void QueueDropTarget_DragEnter(object? sender, DragEventArgs e)
    {
        ApplyQueueDragEffect(e);
    }

    private void QueueDropTarget_DragOver(object? sender, DragEventArgs e)
    {
        ApplyQueueDragEffect(e);
    }

    private async void QueueDropTarget_DragDrop(object? sender, DragEventArgs e)
    {
        ApplyQueueDragEffect(e);
        if (e.Effect != DragDropEffects.Copy)
        {
            return;
        }

        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] droppedPaths)
        {
            return;
        }

        await AddDroppedPathsToQueueAsync(droppedPaths);
    }

    private void ApplyQueueDragEffect(DragEventArgs e)
    {
        e.Effect = ffmpegTools.AreAvailable && e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async Task AddDroppedPathsToQueueAsync(IReadOnlyCollection<string> droppedPaths)
    {
        if (droppedPaths.Count == 0)
        {
            return;
        }

        var addingToRunningQueue = isQueueProcessing;
        var plan = QueueDropRoutingService.CreatePlan(droppedPaths, addingToRunningQueue);
        if (!plan.HasDroppedItems)
        {
            RefreshStatusLog("No files were added to the queue.");
            technicalLog.Append($"Drag and drop skipped because no existing files or folders were found. Missing paths: {plan.MissingPaths.Count}.");
            return;
        }

        if (!TryPrepareForQueueAdd())
        {
            return;
        }

        if (plan.MissingPaths.Count > 0)
        {
            technicalLog.Append($"Drag and drop ignored missing paths. Count: {plan.MissingPaths.Count}.");
        }

        var holdingRunningQueueOpen = addingToRunningQueue && plan.FilePathsToAdd.Count > 0;
        if (holdingRunningQueueOpen)
        {
            pendingRunningQueueAddOperations++;
        }

        try
        {
            if (plan.RejectedFolderPaths.Count > 0)
            {
                const string message = "Folders cannot be dropped while the queue is running. Use Add Folder if needed.";
                RefreshStatusLog(message);
                technicalLog.Append($"Drag and drop rejected folder paths while queue was running. Folders: {plan.RejectedFolderPaths.Count}; Files still routed: {plan.FilePathsToAdd.Count}.");
                MessageBox.Show(this, message, "Drag and Drop", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (plan.FilePathsToAdd.Count > 0)
            {
                await AddFilesToQueueAsync(plan.FilePathsToAdd, addingToRunningQueue);
            }
        }
        finally
        {
            if (holdingRunningQueueOpen)
            {
                pendingRunningQueueAddOperations--;
            }
        }

        if (addingToRunningQueue)
        {
            return;
        }

        foreach (var folderPath in plan.FolderPathsToAdd)
        {
            if (!ShowAddFolderOptionsDialog(folderPath, out var includeSubfolders))
            {
                technicalLog.Append($"Drag and drop folder add canceled by user. Folder: {folderPath}");
                RefreshStatusLog("Folder scan canceled.");
                continue;
            }

            await ScanFolderAndPreviewQueueAddAsync(folderPath, includeSubfolders);
        }
    }

    private bool ShowAddFolderOptionsDialog(string folderPath, out bool includeSubfolders)
    {
        includeSubfolders = false;

        using var optionsDialog = new Form
        {
            Text = "Add Folder",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(760, 360),
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

        var folderLabel = CreateLabel("Selected folder:");
        var folderTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Text = folderPath,
            BackColor = SystemColors.Window,
            Margin = new Padding(0, 6, 0, 6)
        };
        var includeSubfoldersCheckBox = new CheckBox
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Include subfolders",
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 8, 0, 8)
        };
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var addFilesButton = CreateButton("Add Files");
        addFilesButton.Size = new Size(130, 42);
        addFilesButton.DialogResult = DialogResult.OK;
        var cancelDialogButton = CreateButton("Cancel");
        cancelDialogButton.Size = new Size(120, 42);
        cancelDialogButton.DialogResult = DialogResult.Cancel;
        buttonPanel.Controls.Add(addFilesButton);
        buttonPanel.Controls.Add(cancelDialogButton);

        root.Controls.Add(folderLabel, 0, 0);
        root.Controls.Add(folderTextBox, 0, 1);
        root.Controls.Add(includeSubfoldersCheckBox, 0, 2);
        root.Controls.Add(buttonPanel, 0, 3);
        optionsDialog.Controls.Add(root);
        optionsDialog.AcceptButton = addFilesButton;
        optionsDialog.CancelButton = cancelDialogButton;

        var result = optionsDialog.ShowDialog(this);
        includeSubfolders = includeSubfoldersCheckBox.Checked;
        return result == DialogResult.OK;
    }

    private async Task ScanFolderAndPreviewQueueAddAsync(string folderPath, bool includeSubfolders)
    {
        const string tooManyFilesMessage =
            "Too many .dat files were found." + "\r\n\r\n" +
            "For safety, DAT Converter stops folder scans at 100 files. No files were added." + "\r\n\r\n" +
            "Please choose a smaller folder or add specific files instead.";
        const string queueLimitMessage = "The queue is limited to 100 files for safety. Add fewer files or process the current queue first.";

        technicalLog.Append($"Folder scan requested. Folder: {folderPath}; Include subfolders: {FormatYesNo(includeSubfolders)}.");
        RefreshStatusLog("Scanning folder for .dat files...");
        addFolderButton.Enabled = false;

        FolderScanResult scanResult;
        try
        {
            scanResult = await Task.Run(() => FolderScanService.ScanForDatFiles(folderPath, includeSubfolders, 100));
        }
        catch (Exception ex)
        {
            technicalLog.Append($"Folder scan failed unexpectedly. Folder: {folderPath}; Error: {ex}");
            RefreshStatusLog("The selected folder could not be scanned.");
            MessageBox.Show(this, "The selected folder could not be scanned.", "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            addFolderButton.Enabled = ffmpegTools.AreAvailable && !isConversionRunning && !isQueueProcessing;
        }

        foreach (var error in scanResult.Errors)
        {
            technicalLog.Append($"Folder scan note: {error}");
        }

        if (scanResult.StoppedBecauseTooManyFiles)
        {
            technicalLog.Append($"Folder scan stopped for safety because more than 100 .dat files were found. Folder: {folderPath}; Include subfolders: {FormatYesNo(includeSubfolders)}.");
            RefreshStatusLog(tooManyFilesMessage);
            MessageBox.Show(this, tooManyFilesMessage, "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (scanResult.DatFiles.Count == 0)
        {
            technicalLog.Append($"Folder scan completed. Folder: {folderPath}; Found: 0; Skipped/inaccessible folders: {scanResult.SkippedPaths.Count}.");
            RefreshStatusLog("No .dat files were found in the selected folder.");
            MessageBox.Show(this, "No .dat files were found in the selected folder.", "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var importPlan = new FolderImportPlanner().Build(scanResult.DatFiles);
        var reviewChoice = ShowFolderImportReviewDialog(importPlan);
        if (reviewChoice == FolderImportReviewChoice.Cancel)
        {
            technicalLog.Append("Folder import canceled by user.");
            RefreshStatusLog("Folder import canceled. No files were added.");
            return;
        }

        var addSettings = GetQueueAddSettings();
        var pathsForPreview = reviewChoice == FolderImportReviewChoice.ImportEveryDatSeparately
            ? importPlan.AllDatPaths
            : importPlan.RecommendedSingleDatPaths;
        var preview = CreateFolderQueueAddPreview(pathsForPreview, addSettings);
        var recommendedSplitCount = reviewChoice == FolderImportReviewChoice.UseRecommendedImport
            ? importPlan.RecommendedSplitPlans.Count
            : 0;
        technicalLog.Append($"Folder scan completed. Folder: {folderPath}; Include subfolders: {FormatYesNo(includeSubfolders)}; Found: {scanResult.DatFiles.Count}; Split recordings: {importPlan.SplitRecordingCount}; Single DAT files: {importPlan.SingleDatCount}; Ambiguous groups: {importPlan.AmbiguousItemCount}; Selected-format outputs already present: {preview.AlreadyConvertedSkippedCount}; Invalid: {preview.InvalidCount}; Output plan failures: {preview.OutputPlanFailedCount}; Will add single items: {preview.AddablePaths.Count}; Will add split items: {recommendedSplitCount}; Skipped/inaccessible folders: {scanResult.SkippedPaths.Count}.");

        var availableSlots = 100 - queueItems.Count;
        var rowsToAdd = preview.AddablePaths.Count + recommendedSplitCount;
        if (rowsToAdd > availableSlots)
        {
            technicalLog.Append($"Folder scan add blocked by queue limit. Queue count: {queueItems.Count}; Available slots: {availableSlots}; Addable rows from scan: {rowsToAdd}.");
            RefreshStatusLog(queueLimitMessage);
            MessageBox.Show(this, queueLimitMessage, "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (rowsToAdd == 0)
        {
            var noAddMessage =
                $"Found {scanResult.DatFiles.Count} .dat file{(scanResult.DatFiles.Count == 1 ? "" : "s")}, but none can be added.\r\n\r\n" +
                $"Selected output format already exists: {preview.AlreadyConvertedSkippedCount}\r\n" +
                $"Invalid/skipped: {preview.InvalidCount + preview.OutputPlanFailedCount}";

            RefreshStatusLog("No files were added to the queue.");
            MessageBox.Show(this, noAddMessage, "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (reviewChoice == FolderImportReviewChoice.UseRecommendedImport)
        {
            await AddSplitExportPlansToQueueAsync(importPlan.RecommendedSplitPlans, addSettings);
        }

        await AddFilesToQueueAsync(preview.AddablePaths);
    }

    private FolderImportReviewChoice ShowFolderImportReviewDialog(FolderImportPlan importPlan)
    {
        var content = FolderImportReviewContentBuilder.Build(importPlan);
        using var dialog = new Form
        {
            Text = content.Title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(620, content.ShowsCombineQuestion ? 290 : 190),
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var summary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = content.Text,
            TextAlign = ContentAlignment.TopLeft
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var cancelButton = CreateButton("Cancel");
        cancelButton.Size = new Size(100, 36);
        var noButton = CreateButton("No");
        noButton.Size = new Size(100, 36);
        var yesButton = CreateButton("Yes");
        yesButton.Size = new Size(100, 36);
        var choice = FolderImportReviewChoice.Cancel;
        cancelButton.Click += (_, _) =>
        {
            choice = FolderImportReviewChoice.Cancel;
            dialog.DialogResult = DialogResult.Cancel;
        };
        noButton.Click += (_, _) =>
        {
            choice = FolderImportReviewChoice.ImportEveryDatSeparately;
            dialog.DialogResult = DialogResult.OK;
        };
        yesButton.Click += (_, _) =>
        {
            choice = FolderImportReviewContent.MapYes(content.ShowsCombineQuestion);
            dialog.DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(cancelButton);
        if (content.ShowsCombineQuestion)
        {
            buttons.Controls.Add(noButton);
        }

        buttons.Controls.Add(yesButton);
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        dialog.Controls.Add(root);
        dialog.AcceptButton = yesButton;
        dialog.CancelButton = cancelButton;
        dialog.ShowDialog(this);
        return choice;
    }

    private DialogResult ShowFolderQueuePreviewDialog(int foundCount, FolderQueueAddPreview preview, QueueSettingsSnapshot addSettings)
    {
        using var dialog = new Form
        {
            Text = "Add Folder",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(560, isQueueProcessing ? 300 : 240),
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = isQueueProcessing ? 4 : 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (isQueueProcessing)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));

        var summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text =
                $"Found {foundCount} .dat file{(foundCount == 1 ? "" : "s")}.\r\n" +
                $"Selected output format already exists: {preview.AlreadyConvertedSkippedCount}\r\n" +
                $"Invalid/skipped: {preview.InvalidCount + preview.OutputPlanFailedCount}\r\n" +
                $"Will add: {preview.AddablePaths.Count}\r\n\r\n" +
                "Add these files to the queue?",
            TextAlign = ContentAlignment.TopLeft
        };
        root.Controls.Add(summaryLabel, 0, 0);

        if (isQueueProcessing)
        {
            var warningLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = new Font(Font, FontStyle.Bold),
                Text = "Queue is running. Added files will use the active queue settings.\r\n" + FormatQueueSettings(addSettings),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 4, 0, 4)
            };
            root.Controls.Add(warningLabel, 0, 1);
        }

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var addButton = CreateButton("Add Files");
        addButton.Size = new Size(130, 42);
        addButton.DialogResult = DialogResult.OK;
        var cancelButton = CreateButton("Cancel");
        cancelButton.Size = new Size(120, 42);
        cancelButton.DialogResult = DialogResult.Cancel;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(addButton);
        root.Controls.Add(buttonPanel, 0, isQueueProcessing ? 2 : 1);

        dialog.Controls.Add(root);
        dialog.AcceptButton = addButton;
        dialog.CancelButton = cancelButton;
        return dialog.ShowDialog(this);
    }

    private void BrowseOutputFolderButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select output folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ApplyOutputFolderSelection(dialog.SelectedPath);
    }

    private void QueueGridView_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateQueueButtonState();
        RefreshDetailsText(DetailsScrollMode.Preserve);
    }

    private void OutsideQueueControl_MouseDown(object? sender, MouseEventArgs e)
    {
        ClearQueueSelection();
    }

    private void QueueGridView_ColumnWidthChanged(object? sender, DataGridViewColumnEventArgs e)
    {
        if (isApplyingQueueColumnWidths || isInitializing || isRefreshingQueueGrid)
        {
            return;
        }

        queueColumnsUserResized = true;
    }

    private void QueueGridView_Resize(object? sender, EventArgs e)
    {
        ApplyQueueAutoFitColumnWidths();
    }

    private void QueueGridView_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= queueGridView.Rows.Count)
        {
            return;
        }

        if (queueGridView.Rows[e.RowIndex].Tag is not QueueItem item)
        {
            return;
        }

        if (!CanEditQueueItem(item))
        {
            const string message = "This item cannot be changed after conversion has started.";
            RefreshStatusLog(message);
            MessageBox.Show(this, message, "Edit Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ShowQueueItemEditor(item);
    }

    private static bool CanEditQueueItem(QueueItem item)
    {
        return QueueItemRefreshService.CanRefreshFromLiveSettings(item);
    }

    private bool ShowQueueItemEditor(QueueItem item, bool resolveAutoDetectInDialog = false)
    {
        using var dialog = new Form
        {
            Text = "Edit Queue Item",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(820, 480),
            Font = Font
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 8
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var saveAsTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = item.PlannedOutputPath,
            Margin = new Padding(0, 8, 8, 6)
        };
        var browseButton = CreateButton("Browse...");
        browseButton.Margin = new Padding(0, 6, 0, 6);
        var formatComboBox = CreateComboBox(new[] { "MP4", "MKV" }, item.OutputFormat.DisplayName());
        var modeComboBox = CreateComboBox(ConversionModes.DisplayOrder, FormatConversionModeForDisplay(item.ConversionMode));
        ConfigureConversionModeComboBox(modeComboBox);
        var fpsComboBox = CreateComboBox(SourceFpsOptions.DisplayOrder, FormatFpsSettingForEditor(item.FpsSettings));
        var originalEditorState = QueueItemEditorSnapshot.Capture(item);
        var trimStateLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0)
        };
        var trimModeNoteLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Text = TrimConversionPolicy.FastModeNote,
            Margin = new Padding(0, 0, 8, 0)
        };
        var trimButton = CreateButton("Trim Video");
        trimButton.Size = new Size(118, 36);
        var clearTrimButton = CreateButton("Clear Trim");
        clearTrimButton.Size = new Size(112, 36);
        var trimButtonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0)
        };
        trimButtonPanel.Controls.Add(trimButton);
        trimButtonPanel.Controls.Add(clearTrimButton);
        var trimPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 2, 0, 2)
        };
        trimPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        trimPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        trimPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        trimPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        trimPanel.Controls.Add(trimStateLabel, 0, 0);
        trimPanel.Controls.Add(trimModeNoteLabel, 0, 1);
        trimPanel.Controls.Add(trimButtonPanel, 1, 0);
        trimPanel.SetRowSpan(trimButtonPanel, 2);
        var burnTimestampCheckBox = new CheckBox
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Burn timestamp into video",
            Checked = item.BurnTimestamp,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 0, 0)
        };
        var burnTimestampNoteLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Text = BurnTimestampMetadataBuilder.ReliabilityNote,
            Margin = new Padding(22, 0, 0, 0),
            Visible = true
        };
        var burnPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        burnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        burnPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        burnPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        burnPanel.Controls.Add(burnTimestampCheckBox, 0, 0);
        burnPanel.Controls.Add(burnTimestampNoteLabel, 0, 1);
        var fpsMessageLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 4)
        };
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0)
        };
        var applyButton = CreateButton("OK");
        var cancelDialogButton = CreateButton("Cancel");
        cancelDialogButton.DialogResult = DialogResult.Cancel;
        var resetButton = CreateButton("Reset to Defaults");
        resetButton.Size = new Size(174, 42);
        resetButton.TextAlign = ContentAlignment.MiddleCenter;
        var isDetectingFps = false;
        var hasManualOutputPathInDialog = item.HasUserCustomOutputPath;

        void RefreshTrimUi()
        {
            if (item.TrimRange is null)
            {
                trimStateLabel.Text = "Full video will be converted.";
                trimModeNoteLabel.Visible = false;
                trimButton.Text = "Trim Video";
                clearTrimButton.Visible = false;
                modeComboBox.Enabled = true;
                return;
            }

            modeComboBox.Enabled = true;
            var timeline = RecordingTimelineBuilder.Build(item);
            trimStateLabel.Text = $"Start {TrimRangeFormatter.FormatOffset(timeline, item.TrimRange.Start)} \u2192 End {TrimRangeFormatter.FormatOffset(timeline, item.TrimRange.End)}";
            trimModeNoteLabel.Visible = true;
            trimButton.Text = "Edit Trim";
            clearTrimButton.Visible = true;
        }

        var burnTimeline = RecordingTimelineBuilder.Build(item);
        var canBurnTimestampUseRecordingTime = burnTimeline.RecordingStart.HasValue;

        void RefreshDefaultOutputPathForTrim()
        {
            if (item.HasUserCustomOutputPath || hasManualOutputPathInDialog)
            {
                return;
            }

            var outputFolderPath = item.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource
                ? Path.GetDirectoryName(item.InputPath)
                : item.SelectedOutputFolder;
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                return;
            }

            var plannedOutputPath = item.IsSplitRecording && item.SplitExportPlan is not null
                ? PlanSplitRecordingOutputPath(item.SplitExportPlan, item.InputPath, outputFolderValidation.FolderPath, ParseOutputFormatDisplay(formatComboBox.SelectedItem?.ToString()), GetTrimOutputSuffix(item), ResolveLogicalOutputBaseName(item))
                : PlanQueueOutputPath(item.InputPath, outputFolderValidation.FolderPath, ParseOutputFormatDisplay(formatComboBox.SelectedItem?.ToString()), item);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                return;
            }

            item.PlannedOutputPath = plannedOutputPath;
            if (item.IsSplitRecording)
            {
                item.CustomOutputPath = plannedOutputPath;
                item.HasCustomOutputPath = true;
            }
            else
            {
                item.CustomOutputPath = null;
                item.HasCustomOutputPath = false;
            }

            saveAsTextBox.Text = plannedOutputPath;
        }

        void SetApplyAvailability()
        {
            applyButton.Enabled = !isDetectingFps;
        }

        void ApplyBurnTimestampModeUi()
        {
            var state = BurnTimestampUiPolicy.Evaluate(
                ParseConversionModeDisplay(modeComboBox.SelectedItem?.ToString()),
                canBurnTimestampUseRecordingTime,
                burnTimestampCheckBox.Checked);
            if (burnTimestampCheckBox.Checked != state.Checked)
            {
                burnTimestampCheckBox.Checked = state.Checked;
            }

            burnTimestampCheckBox.Enabled = state.Enabled;
            burnTimestampNoteLabel.Text = state.Note;
            burnTimestampNoteLabel.Visible = true;
        }

        void DisableAutoDetectForManualSelection()
        {
            if (fpsComboBox.Items.Contains(AutoDetectFpsLabel))
            {
                fpsComboBox.Items.Remove(AutoDetectFpsLabel);
            }

            fpsComboBox.SelectedItem = "30";
            if (fpsComboBox.SelectedIndex < 0)
            {
                fpsComboBox.Text = "30";
            }
        }

        async Task ResolveAutoDetectForDialogAsync()
        {
            isDetectingFps = true;
            fpsComboBox.Enabled = false;
            fpsMessageLabel.ForeColor = SystemColors.GrayText;
            fpsMessageLabel.Text = "Detecting source frame rate...";
            SetApplyAvailability();

            var settings = QueueItemFpsSettings.AutoDetect();
            var resolution = await Task.Run(() => queueItemFpsResolver.ResolveQueueItemFps(item.InputPath, settings));
            if (dialog.IsDisposed)
            {
                return;
            }

            item.ApplyFpsResolution(settings, resolution);
            isDetectingFps = false;
            fpsComboBox.Enabled = true;

            if (resolution.HasResolvedFps && !resolution.RequiresManualFpsSelection)
            {
                fpsComboBox.SelectedItem = AutoDetectFpsLabel;
                fpsMessageLabel.ForeColor = SystemColors.GrayText;
                fpsMessageLabel.Text = $"Detected source frame rate: {resolution.DisplayLabel}.";
            }
            else
            {
                DisableAutoDetectForManualSelection();
                fpsMessageLabel.ForeColor = Color.FromArgb(160, 92, 0);
                fpsMessageLabel.Text = "Unable to detect source frame rate. Please select manually.";
            }

            SetApplyAvailability();
        }

        browseButton.Click += (_, _) =>
        {
            using var saveDialog = CreateSaveAsDialog(item, saveAsTextBox.Text, ParseOutputFormatDisplay(formatComboBox.SelectedItem?.ToString()));
            if (saveDialog.ShowDialog(dialog) == DialogResult.OK)
            {
                hasManualOutputPathInDialog = true;
                saveAsTextBox.Text = saveDialog.FileName;
            }
        };

        trimButton.Click += (_, _) =>
        {
            using var trimDialog = new TrimPreviewDialog(item, ffmpegTools, details => technicalLog.AppendBlock("Trim preview technical details", details));
            if (trimDialog.ShowDialog(dialog) == DialogResult.OK)
            {
                item.TrimRange = trimDialog.SelectedTrimRange;

                RefreshDefaultOutputPathForTrim();
                RefreshTrimUi();
                ApplyBurnTimestampModeUi();
                RefreshDetailsText(DetailsScrollMode.Preserve);
                RefreshStatusLog(item.TrimRange is null ? "Trim cleared. Full video will be converted." : TrimConversionPolicy.FastModeNote);
            }
        };

        clearTrimButton.Click += (_, _) =>
        {
            item.TrimRange = null;
            RefreshDefaultOutputPathForTrim();
            RefreshTrimUi();
            ApplyBurnTimestampModeUi();
            RefreshDetailsText(DetailsScrollMode.Preserve);
            RefreshStatusLog("Trim cleared. Full video will be converted.");
        };

        burnTimestampCheckBox.CheckedChanged += (_, _) =>
        {
            ApplyBurnTimestampModeUi();
        };

        modeComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (GuardUnavailableNvencSelection(modeComboBox))
            {
                return;
            }

            ApplyBurnTimestampModeUi();
        };

        applyButton.Click += async (_, _) =>
        {
            if (TryApplyQueueItemEdits(
                    item,
                    saveAsTextBox.Text,
                    ParseOutputFormatDisplay(formatComboBox.SelectedItem?.ToString()),
                    ParseConversionModeDisplay(modeComboBox.SelectedItem?.ToString()),
                    BuildFpsSettingsFromDisplay(fpsComboBox.SelectedItem?.ToString()),
                    burnTimestampCheckBox.Checked,
                    dialog))
            {
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                await PreProbeWaitingQueueItemsIfIdleAsync();
            }
        };

        resetButton.Click += async (_, _) =>
        {
            if (TryResetQueueItemToQueueDefaults(item, dialog))
            {
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                await PreProbeWaitingQueueItemsIfIdleAsync();
            }
        };

        buttonPanel.Controls.Add(applyButton);
        buttonPanel.Controls.Add(cancelDialogButton);
        buttonPanel.Controls.Add(resetButton);

        root.Controls.Add(CreateLabel("Save As:"), 0, 0);
        root.Controls.Add(saveAsTextBox, 1, 0);
        root.Controls.Add(browseButton, 2, 0);
        root.Controls.Add(CreateLabel("Output format:"), 0, 1);
        root.Controls.Add(formatComboBox, 1, 1);
        root.SetColumnSpan(formatComboBox, 2);
        root.Controls.Add(CreateLabel("Mode:"), 0, 2);
        root.Controls.Add(modeComboBox, 1, 2);
        root.SetColumnSpan(modeComboBox, 2);
        root.Controls.Add(CreateLabel("Source FPS:"), 0, 3);
        root.Controls.Add(fpsComboBox, 1, 3);
        root.SetColumnSpan(fpsComboBox, 2);
        root.Controls.Add(CreateLabel("Trim Video:"), 0, 4);
        root.Controls.Add(trimPanel, 1, 4);
        root.SetColumnSpan(trimPanel, 2);
        root.Controls.Add(CreateLabel("Timestamp:"), 0, 5);
        root.Controls.Add(burnPanel, 1, 5);
        root.SetColumnSpan(burnPanel, 2);
        root.Controls.Add(fpsMessageLabel, 1, 6);
        root.SetColumnSpan(fpsMessageLabel, 2);
        root.Controls.Add(buttonPanel, 0, 7);
        root.SetColumnSpan(buttonPanel, 3);

        dialog.Controls.Add(root);
        dialog.AcceptButton = applyButton;
        dialog.CancelButton = cancelDialogButton;
        if (resolveAutoDetectInDialog)
        {
            dialog.Shown += async (_, _) => await ResolveAutoDetectForDialogAsync();
        }
        else if (item.FpsSettings.SelectionMode == FpsSelectionMode.AutoDetect &&
                 (item.RequiresManualFpsSelection || !item.HasResolvedFps))
        {
            DisableAutoDetectForManualSelection();
            fpsMessageLabel.ForeColor = Color.FromArgb(160, 92, 0);
            fpsMessageLabel.Text = "Unable to detect source frame rate. Please select manually.";
        }

        SetApplyAvailability();
        RefreshTrimUi();
        ApplyBurnTimestampModeUi();
        var accepted = dialog.ShowDialog(this) == DialogResult.OK;
        if (!accepted)
        {
            originalEditorState.Restore(item);
            RefreshQueueGrid();
            RefreshDetailsText(DetailsScrollMode.Preserve);
        }

        return accepted;
    }

    private SaveFileDialog CreateSaveAsDialog(QueueItem item, string currentOutputPath)
    {
        return CreateSaveAsDialog(item, currentOutputPath, item.OutputFormat);
    }

    private SaveFileDialog CreateSaveAsDialog(QueueItem item, string currentOutputPath, OutputFormat outputFormat)
    {
        var extension = outputFormat.Extension();
        var extensionWithoutDot = extension.TrimStart('.');
        var dialog = new SaveFileDialog
        {
            Title = "Save As",
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = extensionWithoutDot,
            Filter = $"{outputFormat.DisplayName()} files (*{extension})|*{extension}|All files (*.*)|*.*",
            OverwritePrompt = false
        };

        var currentFolder = Path.GetDirectoryName(currentOutputPath);
        if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
        {
            dialog.InitialDirectory = currentFolder;
        }

        var currentFileName = Path.GetFileName(currentOutputPath);
        if (!string.IsNullOrWhiteSpace(currentFileName))
        {
            dialog.FileName = currentFileName;
        }

        return dialog;
    }

    private bool TryApplyCustomSaveAsPath(QueueItem item, string outputPath, IWin32Window owner)
    {
        var validation = OutputPathService.ValidateCustomOutputPath(
            item.InputPath,
            outputPath,
            item.OutputFormat,
            requireAvailable: true);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.OutputPath))
        {
            RefreshStatusLog(validation.Message);
            MessageBox.Show(owner, validation.Message, "Save As", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!IsAvailableQueueOutputPath(validation.OutputPath, item))
        {
            const string message = "Save As path is already used by another queued item.";
            RefreshStatusLog(message);
            MessageBox.Show(owner, message, "Save As", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        item.CustomOutputPath = validation.OutputPath;
        item.HasCustomOutputPath = true;
        item.HasUserCustomOutputPath = true;
        item.PlannedOutputPath = validation.OutputPath;
        item.ConversionResult = null;
        item.ResultStatusSummary = null;
        item.HasExistingDirectOutput = false;
        if (item.HasExistingDirectOutput)
        {
            item.Status = QueueItemStatus.Skipped;
            item.StatusText = "Exists";
            item.ProgressText = "Selected output exists";
        }
        else if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            item.PreProbeResult = null;
            item.Status = QueueItemStatus.Warning;
            item.StatusText = "Needs FPS";
            item.ProgressText = "Choose Source FPS";
        }
        else
        {
            item.Status = QueueItemStatus.Ready;
            item.StatusText = "Ready";
            item.ProgressText = item.PreProbeResult is null ? "Ready" : FormatProbeProgressText(item.PreProbeResult);
        }
        technicalLog.Append($"Queue item Save As applied. Input: {item.InputPath}; Output: {item.PlannedOutputPath}");
        RefreshQueueGrid();
        RefreshStatusLog("Save As path updated for the selected queue item.");
        UpdateQueueButtonState();
        return true;
    }

    private bool TryApplyQueueItemEdits(
        QueueItem item,
        string requestedOutputPath,
        OutputFormat outputFormat,
        string conversionMode,
        QueueItemFpsSettings fpsSettings,
        bool burnTimestamp,
        IWin32Window owner)
    {
        var outputPathChanged = !string.Equals(requestedOutputPath?.Trim(), item.PlannedOutputPath, StringComparison.OrdinalIgnoreCase);
        var formatChanged = outputFormat != item.OutputFormat;
        var resolvedConversionMode = conversionMode;
        var modeChanged = !string.Equals(resolvedConversionMode, item.ConversionMode, StringComparison.OrdinalIgnoreCase);
        var fpsChanged = !AreFpsSettingsEquivalent(fpsSettings, item.FpsSettings);

        string? plannedOutputPath;
        string? customOutputPath;
        var hasCustomOutputPath = item.HasUserCustomOutputPath &&
                                  (item.HasCustomOutputPath || !string.IsNullOrWhiteSpace(item.CustomOutputPath));

        if (outputPathChanged || hasCustomOutputPath)
        {
            var pathToValidate = outputPathChanged ? requestedOutputPath : item.CustomOutputPath ?? item.PlannedOutputPath;
            var validation = OutputPathService.ValidateCustomOutputPath(
                item.InputPath,
                pathToValidate,
                outputFormat,
                requireAvailable: outputPathChanged,
                allowExtensionCorrection: true);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.OutputPath))
            {
                RefreshStatusLog(validation.Message);
                MessageBox.Show(owner, validation.Message, "Edit Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!IsAvailableQueueOutputPath(validation.OutputPath, item))
            {
                const string message = "Save As path is already used by another queued item.";
                RefreshStatusLog(message);
                MessageBox.Show(owner, message, "Edit Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            plannedOutputPath = validation.OutputPath;
            customOutputPath = validation.OutputPath;
            hasCustomOutputPath = true;
        }
        else
        {
            var outputFolderPath = item.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource
                ? Path.GetDirectoryName(item.InputPath)
                : item.SelectedOutputFolder;
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                RefreshStatusLog(outputFolderValidation.Message);
                MessageBox.Show(owner, outputFolderValidation.Message, "Edit Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            plannedOutputPath = item.IsSplitRecording && item.SplitExportPlan is not null
                ? PlanSplitRecordingOutputPath(item.SplitExportPlan, item.InputPath, outputFolderValidation.FolderPath, outputFormat, GetTrimOutputSuffix(item), ResolveLogicalOutputBaseName(item))
                : PlanQueueOutputPath(item.InputPath, outputFolderValidation.FolderPath, outputFormat, item);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                const string message = "No safe automatic output path could be planned.";
                RefreshStatusLog(message);
                MessageBox.Show(owner, message, "Edit Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            customOutputPath = item.IsSplitRecording ? plannedOutputPath : null;
            hasCustomOutputPath = item.IsSplitRecording;
        }

        var directOutputPath = GetDirectOutputPathForQueueRefresh(item, Path.GetDirectoryName(plannedOutputPath) ?? "", outputFormat);
        var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) &&
            string.Equals(directOutputPath, plannedOutputPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(directOutputPath);
        var previousFfmpegRate = item.FfmpegRateValue;

        item.OutputFormat = outputFormat;
        item.ConversionMode = resolvedConversionMode;
        item.BurnTimestamp = burnTimestamp;
        item.PlannedOutputPath = plannedOutputPath;
        item.CustomOutputPath = customOutputPath;
        item.HasCustomOutputPath = hasCustomOutputPath;
        item.HasUserCustomOutputPath = item.HasUserCustomOutputPath || outputPathChanged;
        item.HasCustomFormat = item.HasCustomFormat || formatChanged;
        item.HasCustomMode = item.HasCustomMode || modeChanged || burnTimestamp;
        item.HasCustomFpsSetting = item.HasCustomFpsSetting || fpsChanged;
        item.HasExistingDirectOutput = hasExistingDirectOutput;
        if (fpsChanged || item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            item.ApplyFpsResolution(fpsSettings, queueItemFpsResolver.ResolveQueueItemFps(item.InputPath, fpsSettings));
        }

        var hasReusableProbeForFps = QueueItemStatusService.HasReusableProbeForCurrentFps(item);

        if ((!string.Equals(previousFfmpegRate, item.FfmpegRateValue, StringComparison.Ordinal) && !hasReusableProbeForFps) ||
            item.RequiresManualFpsSelection ||
            !item.HasResolvedFps)
        {
            item.PreProbeResult = null;
        }

        item.ConversionResult = null;
        item.ResultStatusSummary = hasExistingDirectOutput ? "Skipped - output already exists" : null;
        if (hasExistingDirectOutput)
        {
            item.Status = QueueItemStatus.Skipped;
            item.StatusText = "Exists";
            item.ProgressText = "Selected output exists";
        }
        else if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            item.Status = QueueItemStatus.Warning;
            item.StatusText = "Needs FPS";
            item.ProgressText = "Choose Source FPS";
        }
        else
        {
            item.Status = hasExistingDirectOutput ? QueueItemStatus.Skipped : item.PreProbeResult is null ? QueueItemStatus.WaitingForProbe : QueueItemStatus.Ready;
            item.StatusText = hasExistingDirectOutput ? "Exists" : item.PreProbeResult is null ? QueueItemStatusText.CheckingFile : "Ready";
            item.ProgressText = hasExistingDirectOutput ? "Selected output exists" : item.PreProbeResult is null ? "" : FormatProbeProgressText(item.PreProbeResult);
        }

        technicalLog.Append($"Queue item edited. Input: {item.InputPath}; Output: {item.PlannedOutputPath}; Format: {item.OutputFormat.DisplayName()}; Mode: {FormatConversionModeForDisplay(item.ConversionMode)}; FPS: {item.FpsDisplayLabel} ({item.FfmpegRateValue}).");
        AppendQueueItemFpsTechnicalLog(item, "Queue item FPS detection");
        RefreshQueueGrid();
        RefreshDetailsText(DetailsScrollMode.Bottom);
        RefreshStatusLog("Queue item updated.");
        UpdateQueueButtonState();
        return true;
    }

    private bool TryResetQueueItemOutputPath(QueueItem item, IWin32Window owner)
    {
        var settings = CaptureCurrentQueueSettings();
        var outputFolderPath = ResolveActiveQueueOutputFolder(item.InputPath, settings);
        var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
        if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
        {
            RefreshStatusLog(outputFolderValidation.Message);
            MessageBox.Show(owner, outputFolderValidation.Message, "Save As", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var directOutputPath = OutputPathService.GetDirectOutputPath(item.InputPath, outputFolderValidation.FolderPath, settings.OutputFormat);
        var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
        var previousCustomOutputPath = item.CustomOutputPath;
        var previousHasCustomOutputPath = item.HasCustomOutputPath;
        item.CustomOutputPath = null;
        item.HasCustomOutputPath = false;
        var plannedOutputPath = PlanQueueOutputPath(item.InputPath, outputFolderValidation.FolderPath, settings.OutputFormat, item);
        if (string.IsNullOrWhiteSpace(plannedOutputPath))
        {
            item.CustomOutputPath = previousCustomOutputPath;
            item.HasCustomOutputPath = previousHasCustomOutputPath;
            const string message = "No safe automatic output path could be planned.";
            RefreshStatusLog(message);
            MessageBox.Show(owner, message, "Save As", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var hasExistingPlannedOutput = hasExistingDirectOutput &&
                                       string.Equals(directOutputPath, plannedOutputPath, StringComparison.OrdinalIgnoreCase);
        QueueSettingsLockService.ApplyLockedSettings(
            item,
            settings,
            outputFolderValidation.FolderPath,
            plannedOutputPath,
            hasExistingPlannedOutput,
            item.PreProbeResult is null ? "Ready" : FormatProbeProgressText(item.PreProbeResult));
        item.ConversionResult = null;
        item.ResultStatusSummary = hasExistingPlannedOutput ? "Skipped - output already exists" : null;

        technicalLog.Append($"Queue item Save As reset. Input: {item.InputPath}; Output: {item.PlannedOutputPath}");
        RefreshQueueGrid();
        RefreshStatusLog("Save As path reset for the selected queue item.");
        UpdateQueueButtonState();
        return true;
    }

    private bool TryResetQueueItemToQueueDefaults(QueueItem item, IWin32Window owner)
    {
        item.ClearCustomSettings();
        item.BurnTimestamp = false;
        var result = QueueItemRefreshService.RefreshEditableItems(
            new[] { item },
            CaptureCurrentQueueSettings(),
            (queueItem, refreshSettings) => ResolveActiveQueueOutputFolder(queueItem.InputPath, refreshSettings),
            (queueItem, outputFolderPath, outputFormat) => PlanQueueOutputPath(queueItem.InputPath, outputFolderPath, outputFormat, queueItem),
            GetDirectOutputPathForQueueRefresh,
            (queueItem, refreshSettings) => ResolveQueueItemFps(queueItem.InputPath, refreshSettings));

        if (result.InvalidCount > 0)
        {
            const string message = "Queue defaults could not be applied to this item.";
            RefreshStatusLog(message);
            MessageBox.Show(owner, message, "Edit Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        item.ConversionResult = null;
        item.ResultStatusSummary = item.HasExistingDirectOutput ? "Skipped - output already exists" : null;
        technicalLog.Append($"Queue item reset to queue defaults. Input: {item.InputPath}; Output: {item.PlannedOutputPath}");
        RefreshQueueGrid();
        RefreshDetailsText(DetailsScrollMode.Bottom);
        RefreshStatusLog("Queue item reset to queue defaults.");
        UpdateQueueButtonState();
        return true;
    }

    private async void StartQueueButton_Click(object? sender, EventArgs e)
    {
        await StartQueueAsync();
    }

    private void StopAfterCurrentButton_Click(object? sender, EventArgs e)
    {
        if (!isQueueProcessing || currentQueueItem is null)
        {
            return;
        }

        stopAfterCurrentRequested = true;
        stopAfterCurrentButton.Enabled = false;
        stopAfterCurrentButton.Text = "Stop Requested";
        technicalLog.Append($"Stop After Current requested. Current item will finish normally. Input: {currentQueueItem.InputPath}");
        RefreshStatusLog("Queue will stop after the current item finishes.");
        UpdateQueueButtonState();
    }

    private void CancelQueueButton_Click(object? sender, EventArgs e)
    {
        if (!isConversionRunning || conversionCancellationTokenSource is null)
        {
            return;
        }

        cancelQueueRequested = true;
        cancelCurrentOnlyRequested = false;
        cancelButton.Enabled = false;
        cancelQueueButton.Enabled = false;
        stopAfterCurrentButton.Enabled = false;
        conversionCancellationTokenSource.Cancel();
        technicalLog.Append(isQueueProcessing
            ? "Cancel Queue requested. Queue processing will stop after the current operation is canceled."
            : "Cancel requested for running conversion.");
        RefreshStatusLog(isQueueProcessing ? "Canceling queue..." : "Canceling conversion...");
    }

    private void RemoveSelectedQueueItemButton_Click(object? sender, EventArgs e)
    {
        if (isQueueProcessing)
        {
            RefreshStatusLog("Remove Selected is unavailable while the queue is running.");
            return;
        }

        var removedCount = RemoveSelectedQueueItemsForState(queueItems, GetSelectedQueueItems(), currentQueueItem, isQueueProcessing);
        if (removedCount == 0)
        {
            RefreshStatusLog("Select a queued item to remove.");
            return;
        }

        ResetQueueColumnAutoFitIfQueueIsEmpty();
        technicalLog.Append($"Removed {removedCount} selected queue item(s).");
        RefreshQueueGrid();
        RefreshStatusLog(removedCount == 1 ? "Removed selected queue item." : $"Removed {removedCount} selected queue items.");
        UpdateQueueButtonState();
    }

    private void DuplicateSelectedQueueItemButton_Click(object? sender, EventArgs e)
    {
        if (isQueueProcessing)
        {
            RefreshStatusLog("Duplicate Selected is unavailable while the queue is running.");
            return;
        }

        var selectedItems = GetSelectedQueueItems();
        if (selectedItems.Count != 1)
        {
            RefreshStatusLog("Select one queued item to duplicate.");
            return;
        }

        var source = selectedItems[0];
        if (!CanCopyQueueItemForState(source, isQueueProcessing))
        {
            RefreshStatusLog("Selected queue item cannot be duplicated right now.");
            return;
        }

        var addSettings = GetQueueAddSettings();
        var plannedOutputPath = PlanCopiedQueueItemOutputPath(source, addSettings);
        if (string.IsNullOrWhiteSpace(plannedOutputPath))
        {
            RefreshStatusLog("Could not plan a safe output path for the duplicated item.");
            return;
        }

        var freshFpsSettings = QueueItemFpsSettings.AutoDetect();
        var outputFolderPath = addSettings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder
            ? ResolveQueueItemOutputFolderForCopy(source, addSettings)
            : null;
        var copy = QueueItemCopyService.CreateReadyCopy(
            source,
            plannedOutputPath,
            addSettings.OutputDestinationMode,
            addSettings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder ? outputFolderPath : null,
            addSettings.OutputFormat,
            queueItemFpsResolver.ResolveQueueItemFps(source.InputPath, freshFpsSettings),
            freshFpsSettings);
        copy.LogicalOutputBaseName = source.LogicalOutputBaseName ?? ResolveLogicalOutputBaseName(source);

        queueItems.Add(copy);
        technicalLog.Append($"{GetQueueCopyActionText(source)} created. Source: {source.InputPath}; Output: {plannedOutputPath}.");
        RefreshQueueGrid();
        SelectQueueItem(copy);
        RefreshDetailsText(DetailsScrollMode.Preserve);
        RefreshStatusLog(source.Status == QueueItemStatus.Completed ? "Re-added selected item." : "Duplicated selected item.");
        UpdateQueueButtonState();
    }

    private void ClearCompletedQueueButton_Click(object? sender, EventArgs e)
    {
        var removedCount = queueItems.RemoveAll(item => item.Status is QueueItemStatus.Completed
            or QueueItemStatus.Skipped
            or QueueItemStatus.Failed
            or QueueItemStatus.Canceled
            or QueueItemStatus.Unsupported
            or QueueItemStatus.Invalid);
        ResetQueueColumnAutoFitIfQueueIsEmpty();
        technicalLog.Append($"Cleared {removedCount} completed queue item(s).");
        RefreshQueueGrid();
        RefreshStatusLog(removedCount == 1 ? "Cleared one completed item." : $"Cleared {removedCount} completed items.");
    }

    private void ResetQueueColumnAutoFitIfQueueIsEmpty()
    {
        if (queueItems.Count != 0)
        {
            return;
        }

        queueColumnsUserResized = false;
    }

    private async void OutputDestinationRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        if (isInitializing || !((RadioButton)sender!).Checked)
        {
            return;
        }

        selectionState.OutputDestinationMode = sameFolderRadioButton.Checked
            ? OutputDestinationMode.SameFolderAsSource
            : OutputDestinationMode.ChooseOutputFolder;

        ApplyOutputDestinationMode();
        RecalculatePlannedOutputPath();
        technicalLog.Append($"Output destination mode changed to {FormatOutputDestinationMode(selectionState.OutputDestinationMode)}. Planned output: {FormatOptionalValue(selectionState.PlannedOutputFilePath, "not planned")}.");
        SaveCurrentSettings();
        RefreshStatusLog("Output destination changed.");
        UpdateConvertButtonState();
        await RefreshQueuedItemsFromCurrentSettingsAsync("Output destination changed");
    }

    private async void OutputFormatComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        RecalculatePlannedOutputPath();
        technicalLog.Append($"Output format changed to {GetSelectedOutputFormat().DisplayName()}. Planned output: {FormatOptionalValue(selectionState.PlannedOutputFilePath, "not planned")}.");
        SaveCurrentSettings();
        RefreshStatusLog("Output format changed.");
        UpdateConvertButtonState();
        await RefreshQueuedItemsFromCurrentSettingsAsync("Output format changed");
    }

    private async void ConversionModeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        if (GuardUnavailableNvencSelection(conversionModeComboBox))
        {
            return;
        }

        technicalLog.Append($"Conversion mode changed to {GetSelectedConversionModeForDisplay()}.");
        SaveCurrentSettings();
        RefreshStatusLog("Conversion mode changed.");
        UpdateConvertButtonState();
        await RefreshQueuedItemsFromCurrentSettingsAsync("Conversion mode changed");
    }

    private async void FrameRateComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        selectionState.IsProbeValid = false;
        selectionState.LastProbeResult = null;
        var fpsSettings = GetSelectedFpsSettings();
        technicalLog.Append($"Frame rate changed. Selection: {fpsSettings.RequestedDisplayValue}; Mode: {fpsSettings.SelectionMode}; FFmpeg value: {fpsSettings.ManualFfmpegRateValue}. Probe validation reset.");
        SaveCurrentSettings();
        RefreshStatusLog("Frame rate changed. Probe validation is required for the new FPS setting.");
        UpdateConvertButtonState();
        await RefreshQueuedItemsFromCurrentSettingsAsync("Frame rate changed");
        await PreProbeWaitingQueueItemsIfIdleAsync();
        await StartProbeIfReadyAsync();
    }

    private async void ConvertButton_Click(object? sender, EventArgs e)
    {
        await StartConversionAsync();
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (!isConversionRunning)
        {
            return;
        }

        cancelCurrentOnlyRequested = isQueueProcessing;
        cancelQueueRequested = !isQueueProcessing;
        cancelButton.Enabled = false;
        currentConversionHeadline = isQueueProcessing ? "Canceling current item..." : "Canceling conversion...";
        technicalLog.Append(isQueueProcessing
            ? "Cancel Current requested. Current queue item will be canceled and the queue will continue."
            : "Cancel requested for running conversion.");
        RefreshStatusLog(currentConversionHeadline);
        conversionCancellationTokenSource?.Cancel();
    }

    private void OpenOutputFolderButton_Click(object? sender, EventArgs e)
    {
        var selectedQueueItem = GetSelectedQueueItem();
        var target = OpenOutputTargetResolver.Resolve(queueItems, selectedQueueItem, lastSuccessfulOutputPath);
        if (target.QueueItem is not null && GetSelectedQueueItem() is null)
        {
            SelectQueueItem(target.QueueItem);
        }

        try
        {
            if (target.Kind == OpenOutputTargetKind.SelectFile && !string.IsNullOrWhiteSpace(target.Path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target.Path}\"",
                    UseShellExecute = true
                });
                technicalLog.Append($"Opened Explorer selecting output file: {target.Path}");
                return;
            }

            if (target.Kind == OpenOutputTargetKind.OpenFolder && !string.IsNullOrWhiteSpace(target.Path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target.Path,
                    UseShellExecute = true
                });
                technicalLog.Append($"Opened output folder: {target.Path}");
                return;
            }

            technicalLog.Append($"Open Output failed: {target.Message} Path: {FormatOptionalValue(target.Path, "none")}");
            RefreshStatusLog(target.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            technicalLog.Append($"Open Output failed: {ex}");
            RefreshStatusLog("Could not open the output location.");
        }
        finally
        {
            if (selectedQueueItem is not null)
            {
                ClearQueueSelection();
            }
        }
    }

    private void ShowDetailsButton_Click(object? sender, EventArgs e)
    {
        SetDetailsVisible(!areDetailsVisible);
    }

    private void WordWrapButton_Click(object? sender, EventArgs e)
    {
        var wasAtBottom = IsDetailsScrolledToBottom();
        var scrollPosition = GetDetailsScrollPosition();
        statusLogTextBox.WordWrap = !statusLogTextBox.WordWrap;
        statusLogTextBox.ScrollBars = statusLogTextBox.WordWrap
            ? RichTextBoxScrollBars.Vertical
            : RichTextBoxScrollBars.Both;
        wordWrapButton.Text = statusLogTextBox.WordWrap ? "Word Wrap: On" : "Word Wrap: Off";

        if (wasAtBottom)
        {
            ScrollDetailsToBottom();
        }
        else
        {
            SetDetailsScrollPosition(scrollPosition);
        }
    }

    private void CopyLogButton_Click(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(BuildDetailsText());
            RefreshStatusLog("Log copied.");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            technicalLog.Append($"Copy Log failed: {ex}");
            RefreshStatusLog("Could not copy the log.");
        }
    }

    private void ClearLogButton_Click(object? sender, EventArgs e)
    {
        technicalLog.Clear();
        InitializeTechnicalLog();
        RefreshStatusLog("Log cleared.");
    }

    private void MainForm_ResizeEnd(object? sender, EventArgs e)
    {
        SaveCurrentSettings();
    }

    private async void ApplyInputFileSelection(string filePath)
    {
        await ApplyInputFileSelectionAsync(filePath);
    }

    private async Task ApplyInputFileSelectionAsync(string filePath)
    {
        CancelCurrentProbe();

        var validation = InputFileValidator.ValidateDatFile(filePath);
        selectionState.SelectedInputFilePath = validation.IsValid ? validation.FilePath : null;
        selectionState.IsInputFileValid = validation.IsValid;
        selectionState.IsProbeValid = false;
        selectionState.IsProbeRunning = false;
        selectionState.LastProbeResult = null;
        selectedFilePathTextBox.Text = validation.IsValid ? validation.FilePath! : "No valid .dat file selected";

        ValidateOutputDestination();
        RecalculatePlannedOutputPath();

        var message = validation.IsValid
            ? $"Input file selected: {Path.GetFileName(validation.FilePath)} ({FormatFileSize(validation.FileSizeBytes)})"
            : validation.Message;

        technicalLog.Append(validation.IsValid
            ? $"Input validation succeeded. Path: {validation.FilePath}; Size: {validation.FileSizeBytes} bytes."
            : $"Input validation failed. Path: {filePath}; Reason: {validation.Message}");
        RefreshStatusLog(message);
        UpdateConvertButtonState();
        await StartProbeIfReadyAsync();
    }

    private async void ApplyOutputFolderSelection(string folderPath)
    {
        selectionState.ChosenOutputFolderPath = folderPath;
        ValidateOutputDestination();
        var validationMessage = selectionState.IsOutputFolderValid ? "Output folder is valid." : "The selected output folder is not writable or does not exist.";

        RecalculatePlannedOutputPath();
        technicalLog.Append(selectionState.IsOutputFolderValid
            ? $"Output folder validation succeeded. Path: {selectionState.SelectedOutputFolderPath}; Planned output: {FormatOptionalValue(selectionState.PlannedOutputFilePath, "not planned")}."
            : $"Output folder validation failed. Path: {folderPath}; Reason: {validationMessage}");
        SaveCurrentSettings();
        RefreshStatusLog(selectionState.IsOutputFolderValid ? "Output folder selected." : validationMessage);
        UpdateConvertButtonState();
        await RefreshQueuedItemsFromCurrentSettingsAsync("Output folder changed");
    }

    private async Task AddFilesToQueueAsync(IReadOnlyCollection<string> filePaths, bool requireItemConfirmation = false)
    {
        if (filePaths.Count == 0)
        {
            return;
        }

        var addSettings = GetQueueAddSettings();
        var availableSlots = 100 - queueItems.Count;
        if (availableSlots <= 0)
        {
            RefreshStatusLog("The queue is limited to 100 files for safety. Add fewer files or process the current queue first.");
            technicalLog.Append("Add files blocked: queue already has 100 items.");
            return;
        }

        var selectedPaths = filePaths.Take(availableSlots).ToList();
        var overflowCount = filePaths.Count - selectedPaths.Count;
        var addedCount = 0;
        var invalidCount = 0;
        var selectedOutputExistsCount = 0;
        var outputPlanFailedCount = 0;
        string? firstAddedPath = null;
        var newlyAddedItems = new List<QueueItem>();

        foreach (var filePath in selectedPaths)
        {
            var validation = InputFileValidator.ValidateDatFile(filePath);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.FilePath))
            {
                invalidCount++;
                technicalLog.Append($"Queue add skipped invalid file. Path: {filePath}; Reason: {validation.Message}");
                continue;
            }

            var outputFolderPath = ResolveActiveQueueOutputFolder(validation.FilePath, addSettings);
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                invalidCount++;
                technicalLog.Append($"Queue add skipped file because output destination is invalid. Input: {validation.FilePath}; Output folder: {outputFolderPath}; Reason: {outputFolderValidation.Message}");
                continue;
            }

            var outputFormat = addSettings.OutputFormat;
            var directOutputPath = OutputPathService.GetDirectOutputPath(validation.FilePath, outputFolderValidation.FolderPath, outputFormat);
            var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
            if (hasExistingDirectOutput)
            {
                selectedOutputExistsCount++;
                technicalLog.Append($"Queue add noticed selected output format already exists in active output folder. Input: {validation.FilePath}; Active output folder: {outputFolderValidation.FolderPath}; Selected format: {outputFormat.DisplayName()}; Existing output: {directOutputPath}");
            }

            var plannedOutputPath = PlanQueueOutputPath(validation.FilePath, outputFolderValidation.FolderPath, outputFormat);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                outputPlanFailedCount++;
                technicalLog.Append($"Queue add skipped file because no safe output path could be planned. Input: {validation.FilePath}; Output folder: {outputFolderValidation.FolderPath}");
                continue;
            }

            var hasExistingPlannedOutput = hasExistingDirectOutput &&
                                           string.Equals(directOutputPath, plannedOutputPath, StringComparison.OrdinalIgnoreCase);
            var item = new QueueItem(
                validation.FilePath,
                plannedOutputPath,
                addSettings.OutputDestinationMode,
                addSettings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder ? outputFolderValidation.FolderPath : null,
                outputFormat,
                addSettings.ConversionMode,
                addSettings.Fps,
                hasExistingPlannedOutput);
            var shouldResolveAutoDetectInEditor = requireItemConfirmation &&
                                                  addSettings.FpsSettings.SelectionMode == FpsSelectionMode.AutoDetect;
            var fpsResolution = shouldResolveAutoDetectInEditor
                ? QueueItemFpsResolution.PendingAutoDetect()
                : ResolveQueueItemFps(validation.FilePath, addSettings);
            item.ApplyFpsResolution(addSettings.FpsSettings, fpsResolution);
            var multiFileExportDetection = new SpotterMultiFileExportDetector().Detect(validation.FilePath);
            item.MultiFileExportContext = multiFileExportDetection.Context;
            QueueItemStatusService.ApplyPostFpsResolutionStatus(item);
            if (requireItemConfirmation)
            {
                if (!ShowQueueItemEditor(item, shouldResolveAutoDetectInEditor))
                {
                    technicalLog.Append($"Queue add canceled for file while queue was running. Input: {item.InputPath}");
                    continue;
                }

                if (item.Status == QueueItemStatus.WaitingForProbe)
                {
                    item.StatusText = QueueItemStatusText.Waiting;
                }
            }

            queueItems.Add(item);
            newlyAddedItems.Add(item);
            addedCount++;
            firstAddedPath ??= validation.FilePath;
            technicalLog.Append($"Queued file. Input: {item.InputPath}; Output: {item.PlannedOutputPath}; Status: {item.StatusText}; Format: {item.OutputFormat.DisplayName()}; Mode: {FormatConversionModeForDisplay(item.ConversionMode)}; FPS: {item.FpsDisplayLabel} ({item.FfmpegRateValue}); Destination: {FormatOutputDestinationMode(item.OutputDestinationMode)}.");
            if (item.MultiFileExportContext is not null)
            {
                technicalLog.Append($"{item.MultiFileExportContext.DisplayText} Input: {item.InputPath}");
            }

            AppendMultiFileExportTechnicalLog(multiFileExportDetection.TechnicalLogText);
            AppendQueueItemFpsTechnicalLog(item, "Queue item FPS detection");
        }

        if (overflowCount > 0)
        {
            technicalLog.Append($"Queue add stopped at 100-item safety limit. Overflow skipped: {overflowCount}.");
        }

        RefreshQueueGrid();

        if (!string.IsNullOrWhiteSpace(firstAddedPath) && !isQueueProcessing)
        {
            ApplyQueueInputPreview(firstAddedPath);
        }
        else if (!string.IsNullOrWhiteSpace(firstAddedPath))
        {
            technicalLog.Append($"Queue is running; newly added files were appended without changing the current input/probe selection. First appended input: {firstAddedPath}");
        }

        var summaryParts = new List<string>();
        if (addedCount > 0)
        {
            summaryParts.Add($"Added {addedCount} file{(addedCount == 1 ? "" : "s")} to the queue.");
        }

        if (invalidCount > 0)
        {
            summaryParts.Add($"Skipped {invalidCount} invalid file{(invalidCount == 1 ? "" : "s")}.");
        }

        if (selectedOutputExistsCount > 0)
        {
            summaryParts.Add($"{selectedOutputExistsCount} file{(selectedOutputExistsCount == 1 ? "" : "s")} already had the selected output format and will be marked after validation.");
        }

        if (outputPlanFailedCount > 0)
        {
            summaryParts.Add($"Skipped {outputPlanFailedCount} file{(outputPlanFailedCount == 1 ? "" : "s")} without a safe output path.");
        }

        if (overflowCount > 0)
        {
            summaryParts.Add("The queue is limited to 100 files for safety. Add fewer files or process the current queue first.");
        }

        if (isQueueProcessing && addedCount > 0)
        {
            summaryParts.Add(stopAfterCurrentRequested
                ? "Files were added to the queue. They will remain pending because Stop After Current was requested."
                : "Queue is running; confirmed files were added and will be processed automatically when ready.");
        }

        RefreshStatusLog(summaryParts.Count == 0 ? "No files were added to the queue." : string.Join(" ", summaryParts));

        if (newlyAddedItems.Count > 0 && !isQueueProcessing && !isConversionRunning)
        {
            await PreProbeWaitingQueueItemsIfIdleAsync();
        }
    }

    private void ApplyQueueInputPreview(string filePath)
    {
        CancelCurrentProbe();

        var validation = InputFileValidator.ValidateDatFile(filePath);
        selectionState.SelectedInputFilePath = validation.IsValid ? validation.FilePath : null;
        selectionState.IsInputFileValid = validation.IsValid;
        selectionState.IsProbeValid = false;
        selectionState.IsProbeRunning = false;
        selectionState.LastProbeResult = null;
        selectedFilePathTextBox.Text = validation.IsValid ? validation.FilePath! : "No valid .dat file selected";

        ValidateOutputDestination();
        RecalculatePlannedOutputPath();
        UpdateConvertButtonState();
    }

    private async Task PreProbeWaitingQueueItemsIfIdleAsync()
    {
        if (isQueuePreProbeRunning || isQueueProcessing || isConversionRunning)
        {
            return;
        }

        if (!ffmpegTools.AreAvailable)
        {
            technicalLog.Append("Queue pre-probe skipped: bundled FFmpeg tools are missing.");
            return;
        }

        isQueuePreProbeRunning = true;
        UpdateQueueButtonState();

        var readyCount = 0;
        var unsupportedCount = 0;
        var selectedOutputExistsCount = 0;
        var validatedCount = 0;

        try
        {
            while (!isQueueProcessing && !isConversionRunning)
            {
                var candidates = queueItems
                    .Where(QueuePreProbeService.ShouldPreProbe)
                    .ToList();

                if (candidates.Count == 0)
                {
                    break;
                }

                RefreshStatusLog("Probing queued files...");
                technicalLog.Append($"Queue pre-probe started. Items: {candidates.Count}.");

                foreach (var item in candidates)
                {
                    if (!queueItems.Contains(item) || !QueuePreProbeService.ShouldPreProbe(item))
                    {
                        continue;
                    }

                    SetQueueItemStatus(item, QueueItemStatus.Probing, "Probing", "");
                    var probeFps = GetProbeFpsOption(item);
                    technicalLog.Append($"Queue pre-probe item. Input: {item.InputPath}; FPS: {probeFps.Label} ({probeFps.FfmpegValue}){(item.HasResolvedFps ? "" : " probe-only")}.");

                    var probeResult = await probeService.ProbeRawH264Async(item.InputPath, probeFps, CancellationToken.None);
                    if (!queueItems.Contains(item))
                    {
                        continue;
                    }

                    validatedCount++;
                    QueueItemStatusService.ApplyPreProbeResult(item, probeResult);

                    if (probeResult.IsSuccess)
                    {
                        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
                        {
                            RefreshQueueGrid();
                            technicalLog.Append($"Queue pre-probe succeeded; Source FPS still needs manual selection. Input: {item.InputPath}; Codec: {FormatOptionalValue(probeResult.CodecName, "unknown")}; Resolution: {FormatResolution(probeResult.Width, probeResult.Height)}; Profile: {FormatOptionalValue(probeResult.Profile, "unknown")}.");
                            continue;
                        }

                        if (item.HasExistingDirectOutput)
                        {
                            selectedOutputExistsCount++;
                            RefreshQueueGrid();
                            technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, queueItems.IndexOf(item) + 1, queueItems.Count));
                        }
                        else
                        {
                            readyCount++;
                            RefreshQueueGrid();
                        }

                        technicalLog.Append($"Queue pre-probe succeeded. Input: {item.InputPath}; Codec: {FormatOptionalValue(probeResult.CodecName, "unknown")}; Resolution: {FormatResolution(probeResult.Width, probeResult.Height)}; Profile: {FormatOptionalValue(probeResult.Profile, "unknown")}.");
                    }
                    else
                    {
                        unsupportedCount++;
                        RefreshQueueGrid();
                        technicalLog.Append($"Queue pre-probe failed. Input: {item.InputPath}; Message: {ProbeResult.UnsupportedMessage}");
                        technicalLog.AppendBlock("Queue pre-probe technical details", probeResult.TechnicalDetails);
                        technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, queueItems.IndexOf(item) + 1, queueItems.Count));
                    }
                }
            }
        }
        finally
        {
            isQueuePreProbeRunning = false;
            UpdateQueueButtonState();
        }

        if (validatedCount > 0)
        {
            var unsupportedNote = unsupportedCount > 0
                ? $" {unsupportedCount} unsupported .dat file{(unsupportedCount == 1 ? "" : "s")} will not be processed."
                : string.Empty;
            var existsNote = selectedOutputExistsCount > 0
                ? $" Existing selected outputs: {selectedOutputExistsCount}."
                : string.Empty;
            var message = $"Queue probe complete. Ready: {readyCount}, Unsupported: {unsupportedCount}.{existsNote}{unsupportedNote}";
            technicalLog.Append(message);
            RefreshStatusLog(message);
        }
    }

    private static string FormatProbeProgressText(ProbeResult probeResult)
    {
        var resolution = FormatResolution(probeResult.Width, probeResult.Height);
        return string.Equals(resolution, "unknown", StringComparison.OrdinalIgnoreCase)
            ? "Ready"
            : resolution;
    }

    private async Task RefreshQueuedItemsFromCurrentSettingsAsync(string reason)
    {
        if (isQueueProcessing || queueItems.Count == 0)
        {
            return;
        }

        var settings = CaptureCurrentQueueSettings();
        var result = QueueItemRefreshService.RefreshEditableItems(
            queueItems,
            settings,
            (item, refreshSettings) => ResolveActiveQueueOutputFolder(item.InputPath, refreshSettings),
            (item, outputFolderPath, outputFormat) => PlanQueueOutputPath(item.InputPath, outputFolderPath, outputFormat, item),
            GetDirectOutputPathForQueueRefresh,
            (item, refreshSettings) => ResolveQueueItemFps(item.InputPath, refreshSettings));

        if (result.RefreshedCount == 0 && result.InvalidCount == 0)
        {
            UpdateQueueButtonState();
            return;
        }

        technicalLog.Append($"Queue refreshed from current settings. Reason: {reason}; Items refreshed: {result.RefreshedCount}; Items invalid: {result.InvalidCount}; Settings: {FormatQueueSettings(settings)}; Destination: {FormatOutputDestinationMode(settings.OutputDestinationMode)}; Chosen folder: {FormatOptionalValue(settings.ChosenOutputFolder, "none")}.");
        RefreshQueueGrid();
        RefreshDetailsText(DetailsScrollMode.Bottom);
        UpdateConvertButtonState();
        await PreProbeWaitingQueueItemsIfIdleAsync();
    }

    private async Task AddSplitExportPlansToQueueAsync(IReadOnlyList<SpotterSplitExportPlan> splitPlans, QueueSettingsSnapshot addSettings)
    {
        foreach (var plan in splitPlans)
        {
            var firstSegment = plan.Segments.FirstOrDefault();
            if (firstSegment is null || !plan.IsStrongConfidence)
            {
                technicalLog.Append($"Split recording import skipped because the plan is not strong enough. Folder: {plan.ExportFolder}; Confidence: {plan.Confidence}");
                continue;
            }

            var inputPath = firstSegment.FilePath;
            var outputFolderPath = ResolveActiveQueueOutputFolder(inputPath, addSettings);
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                technicalLog.Append($"Split recording import skipped because output destination is invalid. Folder: {plan.ExportFolder}; Output folder: {outputFolderPath}; Reason: {outputFolderValidation.Message}");
                continue;
            }

            var plannedOutputPath = PlanSplitRecordingOutputPath(plan, inputPath, outputFolderValidation.FolderPath, addSettings.OutputFormat);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                technicalLog.Append($"Split recording import skipped because no safe output path could be planned. Folder: {plan.ExportFolder}");
                continue;
            }

            var fpsResolution = ResolveQueueItemFps(inputPath, addSettings);
            var item = new QueueItem(
                inputPath,
                plannedOutputPath,
                addSettings.OutputDestinationMode,
                addSettings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder ? outputFolderValidation.FolderPath : null,
                addSettings.OutputFormat,
                "Fast",
                addSettings.Fps,
                hasExistingDirectOutput: false)
            {
                SplitExportPlan = plan,
                LogicalOutputBaseName = GetSplitRecordingOutputBaseName(plan),
                CustomOutputPath = plannedOutputPath,
                HasCustomOutputPath = true,
                HasCustomMode = true
            };
            item.ApplyFpsResolution(addSettings.FpsSettings, fpsResolution);
            QueueItemStatusService.ApplyPostFpsResolutionStatus(item);
            queueItems.Add(item);
            technicalLog.Append($"Queued split recording. Folder: {plan.ExportFolder}; Parts: {plan.SegmentCount}; Output: {item.PlannedOutputPath}; FPS: {item.FpsDisplayLabel} ({item.FfmpegRateValue}).");
        }

        RefreshQueueGrid();
        await PreProbeWaitingQueueItemsIfIdleAsync();
    }

    private FolderQueueAddPreview CreateFolderQueueAddPreview(IReadOnlyCollection<string> filePaths, QueueSettingsSnapshot addSettings)
    {
        var addablePaths = new List<string>();
        var invalidCount = 0;
        var alreadyConvertedSkippedCount = 0;
        var outputPlanFailedCount = 0;

        foreach (var filePath in filePaths)
        {
            var validation = InputFileValidator.ValidateDatFile(filePath);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.FilePath))
            {
                invalidCount++;
                continue;
            }

            var outputFolderPath = ResolveActiveQueueOutputFolder(validation.FilePath, addSettings);
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                invalidCount++;
                continue;
            }

            var outputFormat = addSettings.OutputFormat;
            var directOutputPath = OutputPathService.GetDirectOutputPath(validation.FilePath, outputFolderValidation.FolderPath, outputFormat);
            var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
            if (hasExistingDirectOutput)
            {
                alreadyConvertedSkippedCount++;
            }

            var plannedOutputPath = PlanQueueOutputPath(validation.FilePath, outputFolderValidation.FolderPath, outputFormat);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                outputPlanFailedCount++;
                continue;
            }

            addablePaths.Add(validation.FilePath);
        }

        return new FolderQueueAddPreview(addablePaths, invalidCount, alreadyConvertedSkippedCount, outputPlanFailedCount);
    }

    private void RecalculatePlannedOutputPath()
    {
        selectionState.PlannedOutputFilePath = null;

        ValidateOutputDestination();

        if (!selectionState.IsInputFileValid || !selectionState.IsOutputFolderValid)
        {
            return;
        }

        selectionState.PlannedOutputFilePath = OutputPathService.PlanOutputPath(
            selectionState.SelectedInputFilePath,
            selectionState.SelectedOutputFolderPath,
            GetSelectedOutputFormat());
    }

    private string? ResolveActiveQueueOutputFolder(string inputPath, QueueSettingsSnapshot addSettings)
    {
        return addSettings.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource
            ? Path.GetDirectoryName(inputPath)
            : addSettings.ChosenOutputFolder;
    }

    private QueueSettingsSnapshot GetQueueAddSettings()
    {
        return isQueueProcessing && activeQueueSettings is not null
            ? activeQueueSettings
            : CaptureCurrentQueueSettings();
    }

    private QueueSettingsSnapshot CaptureCurrentQueueSettings()
    {
        return new QueueSettingsSnapshot(
            GetSelectedOutputFormat(),
            GetSelectedConversionMode(),
            GetSelectedFpsOption(),
            selectionState.OutputDestinationMode,
            selectionState.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder
                ? selectionState.ChosenOutputFolderPath
                : null)
        {
            FpsSettings = GetSelectedFpsSettings()
        };
    }

    private static string FormatQueueSettings(QueueSettingsSnapshot settings)
    {
        return $"Format: {settings.OutputFormat.DisplayName()} | Mode: {FormatConversionModeForDisplay(settings.ConversionMode)} | Source FPS: {settings.FpsSettings.RequestedDisplayValue}";
    }

    private QueueItemFpsResolution ResolveQueueItemFps(string datPath, QueueSettingsSnapshot settings)
    {
        return queueItemFpsResolver.ResolveQueueItemFps(datPath, settings.FpsSettings);
    }

    private bool TryPrepareForQueueAdd()
    {
        if (!QueueAddFlowService.ShouldAutoClearBeforeAdd(queueItems, isQueueProcessing))
        {
            return true;
        }

        var clearedCount = queueItems.Count;
        queueItems.Clear();
        ResetQueueColumnAutoFitIfQueueIsEmpty();
        ResetBatchOptionsForNewQueue();
        technicalLog.Append($"Cleared completed queue before adding more files. Items cleared: {clearedCount}.");
        RefreshQueueGrid();
        RefreshDetailsText(DetailsScrollMode.Bottom);
        RefreshStatusLog("Completed queue cleared. Choose files to add.");

        return true;
    }

    private void ResetBatchOptionsForNewQueue()
    {
        var defaults = QueueAddFlowService.CreateDefaultBatchOptionsAfterAutoClear();
        isInitializing = true;
        try
        {
            outputFormatComboBox.SelectedItem = defaults.OutputFormat.DisplayName();
            conversionModeComboBox.SelectedItem = FormatConversionModeForDisplay(defaults.ConversionMode);
            frameRateComboBox.SelectedItem = defaults.FpsSettings.RequestedDisplayValue;
            sameFolderRadioButton.Checked = defaults.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource;
            chooseFolderRadioButton.Checked = defaults.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder;

            selectionState.OutputDestinationMode = defaults.OutputDestinationMode;
            selectionState.ChosenOutputFolderPath = defaults.ChosenOutputFolder;
            selectionState.SelectedOutputFolderPath = null;
            selectionState.PlannedOutputFilePath = null;
            selectionState.IsProbeValid = false;
            selectionState.LastProbeResult = null;
        }
        finally
        {
            isInitializing = false;
        }

        ApplyOutputDestinationMode();
        RecalculatePlannedOutputPath();
        SaveCurrentSettings();
        UpdateConvertButtonState();
        technicalLog.Append("Batch Options reset to defaults after completed queue auto-clear.");
    }

    private string? GetDirectOutputPathForQueueRefresh(QueueItem item, string outputFolderPath, OutputFormat outputFormat)
    {
        if (item.HasUserCustomOutputPath && !string.IsNullOrWhiteSpace(item.CustomOutputPath))
        {
            var customValidation = OutputPathService.ValidateCustomOutputPath(
                item.InputPath,
                item.CustomOutputPath,
                outputFormat,
                requireAvailable: false,
                allowExtensionCorrection: true);
            return customValidation.IsValid ? customValidation.OutputPath : null;
        }

        return OutputPathService.GetDirectOutputPath(item.InputPath, outputFolderPath, outputFormat, GetTrimOutputSuffix(item));
    }

    private string? PlanQueueOutputPath(string inputPath, string outputFolderPath, OutputFormat outputFormat, QueueItem? excludedItem = null)
    {
        if (excludedItem?.HasUserCustomOutputPath == true && !string.IsNullOrWhiteSpace(excludedItem.CustomOutputPath))
        {
            var customValidation = OutputPathService.ValidateCustomOutputPath(
                inputPath,
                excludedItem.CustomOutputPath,
                outputFormat,
                requireAvailable: false,
                allowExtensionCorrection: true);
            if (!customValidation.IsValid || string.IsNullOrWhiteSpace(customValidation.OutputPath))
            {
                return null;
            }

            excludedItem.CustomOutputPath = customValidation.OutputPath;
            if (File.Exists(customValidation.OutputPath))
            {
                return IsAllowedQueueOutputPath(customValidation.OutputPath, excludedItem)
                    ? customValidation.OutputPath
                    : null;
            }

            return IsAvailableQueueOutputPath(customValidation.OutputPath, excludedItem)
                ? customValidation.OutputPath
                : null;
        }

        var directOutputPath = OutputPathService.GetDirectOutputPath(inputPath, outputFolderPath, outputFormat);
        if (string.IsNullOrWhiteSpace(directOutputPath))
        {
            return null;
        }

        return OutputPathService.PlanUniqueOutputPath(
            inputPath,
            outputFolderPath,
            outputFormat,
            candidate => IsAllowedQueueOutputPath(candidate, excludedItem),
            allowExistingDirectOutput: true,
            baseNameSuffix: GetTrimOutputSuffix(excludedItem));
    }

    private string? PlanCopiedQueueItemOutputPath(QueueItem source, QueueSettingsSnapshot addSettings)
    {
        var logicalBaseName = source.LogicalOutputBaseName ?? ResolveLogicalOutputBaseName(source);
        var outputFolder = ResolveQueueItemOutputFolderForCopy(source, addSettings);
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return null;
        }

        if (source.IsSplitRecording && source.SplitExportPlan is not null)
        {
            return PlanSplitRecordingOutputPath(
                source.SplitExportPlan,
                source.InputPath,
                outputFolder,
                addSettings.OutputFormat,
                trimSuffix: null,
                baseNameOverride: logicalBaseName);
        }

        return PlanAutomaticQueueOutputPath(source.InputPath, outputFolder, addSettings.OutputFormat, trimSuffix: null);
    }

    private string? PlanAutomaticQueueOutputPath(string inputPath, string outputFolderPath, OutputFormat outputFormat, string? trimSuffix)
    {
        return OutputPathService.PlanUniqueOutputPath(
            inputPath,
            outputFolderPath,
            outputFormat,
            candidate => IsAllowedQueueOutputPath(candidate),
            allowExistingDirectOutput: false,
            baseNameSuffix: trimSuffix);
    }

    private string? ResolveQueueItemOutputFolderForCopy(QueueItem source, QueueSettingsSnapshot addSettings)
    {
        if (addSettings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder &&
            !string.IsNullOrWhiteSpace(addSettings.ChosenOutputFolder))
        {
            return Directory.Exists(addSettings.ChosenOutputFolder)
                ? addSettings.ChosenOutputFolder
                : null;
        }

        return Path.GetDirectoryName(source.InputPath);
    }

    private string? PlanSplitRecordingOutputPath(SpotterSplitExportPlan plan, string inputPath, string outputFolderPath, OutputFormat outputFormat)
    {
        return PlanSplitRecordingOutputPath(plan, inputPath, outputFolderPath, outputFormat, trimSuffix: null);
    }

    private string? PlanSplitRecordingOutputPath(SpotterSplitExportPlan plan, string inputPath, string outputFolderPath, OutputFormat outputFormat, string? trimSuffix)
    {
        return PlanSplitRecordingOutputPath(plan, inputPath, outputFolderPath, outputFormat, trimSuffix, baseNameOverride: null);
    }

    private string? PlanSplitRecordingOutputPath(SpotterSplitExportPlan plan, string inputPath, string outputFolderPath, OutputFormat outputFormat, string? trimSuffix, string? baseNameOverride)
    {
        var baseName = string.IsNullOrWhiteSpace(baseNameOverride)
            ? GetSplitRecordingOutputBaseName(plan)
            : SanitizeFileNamePart(baseNameOverride);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        for (var index = 0; index <= 999; index++)
        {
            var suffix = index == 0 ? "" : $"_{index:00}";
            var candidate = Path.Combine(outputFolderPath, $"{baseName}{SanitizeFileNamePart(trimSuffix)}{suffix}{outputFormat.Extension()}");
            if (OutputPathService.IsSafeOutputPath(inputPath, candidate) &&
                !File.Exists(candidate) &&
                IsAllowedQueueOutputPath(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetSplitRecordingOutputBaseName(SpotterSplitExportPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.LogicalOutputBaseName) &&
            plan.LogicalOutputBaseName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            return plan.LogicalOutputBaseName;
        }

        var folderName = Path.GetFileName(plan.ExportFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(folderName) && folderName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            return folderName;
        }

        var firstSegmentBaseName = Path.GetFileNameWithoutExtension(plan.Segments.FirstOrDefault()?.FileName);
        return string.IsNullOrWhiteSpace(firstSegmentBaseName) ? "" : firstSegmentBaseName + "_full";
    }

    public static string? ResolveLogicalOutputBaseName(QueueItem item)
    {
        if (!item.IsSplitRecording)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(item.LogicalOutputBaseName))
        {
            return item.LogicalOutputBaseName;
        }

        var fileName = Path.GetFileNameWithoutExtension(item.PlannedOutputPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return item.SplitExportPlan is null ? null : GetSplitRecordingOutputBaseName(item.SplitExportPlan);
        }

        var trimSuffix = GetTrimOutputSuffix(item);
        if (!string.IsNullOrWhiteSpace(trimSuffix))
        {
            var trimIndex = fileName.IndexOf(trimSuffix, StringComparison.OrdinalIgnoreCase);
            if (trimIndex > 0)
            {
                fileName = fileName[..trimIndex];
            }
        }
        else if (fileName.Length > 3 &&
                 fileName[^3] == '_' &&
                 char.IsDigit(fileName[^2]) &&
                 char.IsDigit(fileName[^1]))
        {
            fileName = fileName[..^3];
        }

        return string.IsNullOrWhiteSpace(fileName)
            ? item.SplitExportPlan is null ? null : GetSplitRecordingOutputBaseName(item.SplitExportPlan)
            : fileName;
    }

    private static string? GetTrimOutputSuffix(QueueItem? item)
    {
        return item?.TrimRange is null
            ? null
            : TrimOutputNameFormatter.BuildTrimSuffix(RecordingTimelineBuilder.Build(item), item.TrimRange);
    }

    private static string SanitizeFileNamePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private bool IsAvailableQueueOutputPath(string outputPath, QueueItem? excludedItem = null)
    {
        return IsAllowedQueueOutputPath(outputPath, excludedItem) &&
               !File.Exists(outputPath);
    }

    private bool IsAllowedQueueOutputPath(string outputPath, QueueItem? excludedItem = null)
    {
        return (excludedItem is null || OutputPathService.IsSafeOutputPath(excludedItem.InputPath, outputPath)) &&
               !queueItems.Any(item => item != excludedItem && string.Equals(item.PlannedOutputPath, outputPath, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyLockedQueueSettingsToQueuedItems(QueueSettingsSnapshot settings)
    {
        var lockedCount = 0;
        var failedCount = 0;

        foreach (var item in queueItems.Where(CanApplyLockedQueueSettings).ToList())
        {
            var itemFpsSettings = item.HasCustomFpsSetting ? item.FpsSettings : settings.FpsSettings;
            var itemSettings = settings with
            {
                OutputFormat = item.HasCustomFormat ? item.OutputFormat : settings.OutputFormat,
                ConversionMode = item.HasCustomMode ? item.ConversionMode : settings.ConversionMode,
                Fps = itemFpsSettings.ToManualFpsOption(),
                FpsSettings = itemFpsSettings
            };

            var outputFolderPath = ResolveActiveQueueOutputFolder(item.InputPath, itemSettings);
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                failedCount++;
                item.Status = QueueItemStatus.Failed;
                item.StatusText = "Failed";
                item.ProgressText = "Output folder invalid";
                item.ConversionResult = null;
                item.ResultStatusSummary = "Skipped - invalid output path";
                technicalLog.Append($"Queue settings lock failed for item because output destination is invalid. Input: {item.InputPath}; Output folder: {outputFolderPath}; Reason: {outputFolderValidation.Message}");
                continue;
            }

            var directOutputPath = GetDirectOutputPathForQueueRefresh(item, outputFolderValidation.FolderPath, itemSettings.OutputFormat);
            var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
            var plannedOutputPath = PlanQueueOutputPath(item.InputPath, outputFolderValidation.FolderPath, itemSettings.OutputFormat, item);
            var hasExistingPlannedOutput = hasExistingDirectOutput &&
                                           !string.IsNullOrWhiteSpace(plannedOutputPath) &&
                                           string.Equals(directOutputPath, plannedOutputPath, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                failedCount++;
                item.Status = QueueItemStatus.Failed;
                item.StatusText = "Failed";
                item.ProgressText = "No safe output path";
                item.ConversionResult = null;
                item.ResultStatusSummary = "Skipped - invalid output path";
                technicalLog.Append($"Queue settings lock failed for item because no safe output path could be planned. Input: {item.InputPath}; Output folder: {outputFolderValidation.FolderPath}; Format: {settings.OutputFormat.DisplayName()}");
                continue;
            }

            QueueSettingsLockService.ApplyLockedSettings(
                item,
                itemSettings,
                outputFolderValidation.FolderPath,
                plannedOutputPath,
                hasExistingPlannedOutput,
                item.PreProbeResult is null ? "Ready" : FormatProbeProgressText(item.PreProbeResult),
                ResolveQueueItemFps(item.InputPath, itemSettings));
            item.ConversionResult = null;
            item.ResultStatusSummary = hasExistingPlannedOutput ? "Skipped - output already exists" : null;
            lockedCount++;
        }

        if (lockedCount > 0 || failedCount > 0)
        {
            technicalLog.Append($"Queue settings locked. Items updated: {lockedCount}; Items failed: {failedCount}; Settings: {FormatQueueSettings(settings)}; Destination: {FormatOutputDestinationMode(settings.OutputDestinationMode)}; Chosen folder: {FormatOptionalValue(settings.ChosenOutputFolder, "none")}.");
            RefreshQueueGrid();
        }
    }

    private static bool CanApplyLockedQueueSettings(QueueItem item)
    {
        if (item.IsSplitRecording)
        {
            return false;
        }

        return item.Status is QueueItemStatus.Ready or QueueItemStatus.Warning or QueueItemStatus.Skipped;
    }

    private static string FormatQueueFpsForRow(QueueItem item)
    {
        return item.RequiresManualFpsSelection || !item.HasResolvedFps
            ? "Needs FPS"
            : item.FpsDisplayLabel;
    }

    private void RefreshQueueGrid()
    {
        var selectedItem = GetSelectedQueueItem();
        var firstDisplayedRowIndex = TryGetFirstDisplayedQueueRowIndex();
        var rebuiltRows = !CanUpdateQueueRowsInPlace();

        isRefreshingQueueGrid = true;
        queueGridView.SuspendLayout();
        try
        {
            if (rebuiltRows)
            {
                queueGridView.Rows.Clear();
                foreach (var item in queueItems)
                {
                    var rowIndex = queueGridView.Rows.Add();
                    UpdateQueueGridRow(queueGridView.Rows[rowIndex], item);
                }
            }
            else
            {
                for (var index = 0; index < queueItems.Count; index++)
                {
                    UpdateQueueGridRow(queueGridView.Rows[index], queueItems[index]);
                }
            }

            queueGridView.ClearSelection();
            RestoreQueueGridViewState(selectedItem, firstDisplayedRowIndex);
        }
        finally
        {
            queueGridView.ResumeLayout();
            isRefreshingQueueGrid = false;
        }

        if (rebuiltRows && !isQueueProcessing)
        {
            ApplyQueueAutoFitColumnWidths();
        }

        UpdateQueueButtonState();
    }

    private bool CanUpdateQueueRowsInPlace()
    {
        if (queueGridView.Rows.Count != queueItems.Count)
        {
            return false;
        }

        for (var index = 0; index < queueItems.Count; index++)
        {
            if (!ReferenceEquals(queueGridView.Rows[index].Tag, queueItems[index]))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateQueueGridRow(DataGridViewRow row, QueueItem item)
    {
        row.Tag = item;
        SetQueueCellValue(row, "Status", item.StatusText);
        SetQueueCellValue(row, "File", item.IsSplitRecording ? $"{Path.GetFileName(item.InputPath)} (split recording)" : Path.GetFileName(item.InputPath));
        SetQueueCellValue(row, "Output", item.PlannedOutputPath);
        SetQueueCellValue(row, "Format", item.OutputFormat.DisplayName());
        SetQueueCellValue(row, "Mode", FormatConversionModeForDisplay(item.ConversionMode));
        SetQueueCellValue(row, "Fps", FormatQueueFpsForRow(item));
        SetQueueCellValue(row, "Resolution", QueueGridRowFormatter.FormatResolution(item));
        SetQueueCellValue(row, "Progress", QueueGridRowFormatter.FormatProgress(item));
        ApplyQueueGridRowStyle(row, item);
    }

    private static void SetQueueCellValue(DataGridViewRow row, string columnName, string? value)
    {
        var cell = row.Cells[columnName];
        var displayValue = value ?? string.Empty;
        if (!string.Equals(Convert.ToString(cell.Value), displayValue, StringComparison.Ordinal))
        {
            cell.Value = displayValue;
        }
    }

    private static void ApplyQueueGridRowStyle(DataGridViewRow row, QueueItem item)
    {
        row.DefaultCellStyle.BackColor = Color.Empty;
        var fpsCell = row.Cells["Fps"];
        fpsCell.Style.ForeColor = Color.Empty;
        fpsCell.ToolTipText = string.Empty;

        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            fpsCell.Style.ForeColor = Color.FromArgb(160, 92, 0);
            fpsCell.ToolTipText = "Double-click this row and choose Source FPS.";
        }

        if (item.Status is QueueItemStatus.Probing or QueueItemStatus.Converting)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(232, 244, 255);
        }
        else if (item.Status == QueueItemStatus.Ready)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(240, 250, 240);
        }
        else if (item.Status == QueueItemStatus.Completed)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(232, 248, 232);
        }
        else if (item.Status is QueueItemStatus.Failed or QueueItemStatus.Canceled or QueueItemStatus.Unsupported)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
        }
        else if (item.Status == QueueItemStatus.Skipped || item.HasExistingDirectOutput)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 225);
        }
    }

    private int TryGetFirstDisplayedQueueRowIndex()
    {
        try
        {
            return queueGridView.Rows.Count == 0 ? -1 : queueGridView.FirstDisplayedScrollingRowIndex;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
        catch (ArgumentOutOfRangeException)
        {
            return -1;
        }
    }

    private void RestoreQueueGridViewState(QueueItem? selectedItem, int firstDisplayedRowIndex)
    {
        var restoredFirstDisplayedRowIndex = QueueGridScrollState.CoerceFirstDisplayedRowIndex(firstDisplayedRowIndex, queueGridView.Rows.Count);
        if (restoredFirstDisplayedRowIndex >= 0)
        {
            try
            {
                queueGridView.FirstDisplayedScrollingRowIndex = restoredFirstDisplayedRowIndex;
            }
            catch (InvalidOperationException)
            {
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        if (selectedItem is null)
        {
            queueGridView.CurrentCell = null;
            return;
        }

        foreach (DataGridViewRow row in queueGridView.Rows)
        {
            if (ReferenceEquals(row.Tag, selectedItem))
            {
                row.Selected = true;
                queueGridView.CurrentCell = row.Cells[0];
                return;
            }
        }

        queueGridView.CurrentCell = null;
    }

    private void ApplyQueueAutoFitColumnWidths()
    {
        if (queueColumnsUserResized || queueGridView.Columns.Count == 0 || queueGridView.ClientSize.Width <= 0)
        {
            return;
        }

        var availableWidth = queueGridView.ClientSize.Width - 2;
        if (queueGridView.Rows.Count > 0 && queueGridView.DisplayedRowCount(includePartialRow: false) < queueGridView.Rows.Count)
        {
            availableWidth -= SystemInformation.VerticalScrollBarWidth;
        }

        var minimumFileWidth = GetQueueColumnPreferredWidth("File", 110);
        var minimumOutputWidth = GetQueueColumnPreferredWidth("Output", 150);
        var statusWidth = GetQueueColumnPreferredWidth("Status", QueueStatusColumnWidth);
        var formatWidth = GetQueueColumnPreferredWidth("Format", QueueFormatColumnWidth);
        var modeWidth = GetQueueColumnPreferredWidth("Mode", QueueModeColumnWidth);
        var fpsWidth = GetQueueColumnPreferredWidth("Fps", QueueFpsColumnWidth);
        var resolutionWidth = GetQueueColumnPreferredWidth("Resolution", QueueResolutionColumnWidth);
        var progressWidth = GetQueueColumnPreferredWidth("Progress", QueueProgressColumnWidth);
        var fixedWidth = statusWidth + formatWidth + modeWidth + fpsWidth + resolutionWidth + progressWidth;
        var flexibleWidth = Math.Max(minimumFileWidth + minimumOutputWidth, availableWidth - fixedWidth);
        var extraFlexibleWidth = Math.Max(0, flexibleWidth - QueueFileColumnWidth - QueueOutputColumnWidth);
        var fileWidth = Math.Max(minimumFileWidth, QueueFileColumnWidth + (int)Math.Round(extraFlexibleWidth * 0.4D));
        var outputWidth = Math.Max(minimumOutputWidth, flexibleWidth - fileWidth);

        isApplyingQueueColumnWidths = true;
        try
        {
            SetQueueColumnWidth("Status", statusWidth);
            SetQueueColumnWidth("File", fileWidth);
            SetQueueColumnWidth("Output", outputWidth);
            SetQueueColumnWidth("Format", formatWidth);
            SetQueueColumnWidth("Mode", modeWidth);
            SetQueueColumnWidth("Fps", fpsWidth);
            SetQueueColumnWidth("Resolution", resolutionWidth);
            SetQueueColumnWidth("Progress", progressWidth);
        }
        finally
        {
            isApplyingQueueColumnWidths = false;
        }
    }

    private void SetQueueColumnWidth(string columnName, int width)
    {
        if (queueGridView.Columns[columnName] is { } column)
        {
            column.MinimumWidth = Math.Max(column.MinimumWidth, Math.Min(width, 70));
            column.Width = width;
        }
    }

    private int GetQueueColumnPreferredWidth(string columnName, int fallbackWidth)
    {
        if (queueGridView.Columns[columnName] is not { } column)
        {
            return fallbackWidth;
        }

        var font = queueGridView.ColumnHeadersDefaultCellStyle.Font ?? queueGridView.Font;
        var headerWidth = TextRenderer.MeasureText(column.HeaderText, font).Width + 18;
        return Math.Max(fallbackWidth, headerWidth);
    }

    private void UpdateQueueButtonState()
    {
        startQueueButton.Enabled = ffmpegTools.AreAvailable &&
                                   !isConversionRunning &&
                                   !isQueuePreProbeRunning &&
                                   !HasPendingQueueValidation() &&
                                   (queueItems.Any(IsProcessableQueueItem) ||
                                    QueueFpsValidationService.FindItemsRequiringManualFps(queueItems).Count > 0);
        var hasActiveQueueConversion = isQueueProcessing &&
                                       currentQueueItem is not null &&
                                       currentQueueItem.Status == QueueItemStatus.Converting;
        cancelButton.Enabled = hasActiveQueueConversion;
        stopAfterCurrentButton.Enabled = hasActiveQueueConversion && !stopAfterCurrentRequested;
        stopAfterCurrentButton.Text = stopAfterCurrentRequested ? "Stop Requested" : "Stop After Current";
        cancelQueueButton.Enabled = isQueueProcessing;
        openOutputFolderButton.Enabled = OpenOutputTargetResolver.Resolve(queueItems, GetSelectedQueueItem(), lastSuccessfulOutputPath).Kind != OpenOutputTargetKind.Unavailable;
        var allowQueueEditing = queueGridView.Enabled && !isQueueProcessing;
        var selectedItems = GetSelectedQueueItems();
        var selectedCopyItem = selectedItems.Count == 1 ? selectedItems[0] : null;
        removeSelectedQueueItemButton.Enabled = allowQueueEditing && selectedItems.Any(CanRemoveQueueItem);
        duplicateSelectedQueueItemButton.Text = GetQueueCopyActionText(selectedCopyItem);
        duplicateSelectedQueueItemButton.Enabled = allowQueueEditing &&
                                                   selectedCopyItem is not null &&
                                                   CanCopyQueueItemForState(selectedCopyItem, isQueueProcessing);
        clearCompletedQueueButton.Enabled = allowQueueEditing && queueItems.Any(item => item.Status == QueueItemStatus.Completed);
    }

    private async Task StartQueueAsync()
    {
        if (!ffmpegTools.AreAvailable || isConversionRunning)
        {
            return;
        }

        if (isQueuePreProbeRunning || HasPendingQueueValidation())
        {
            RefreshStatusLog("Queue validation is still running. Please wait.");
            UpdateQueueButtonState();
            return;
        }

        var itemsRequiringFps = QueueFpsValidationService.FindItemsRequiringManualFps(queueItems);
        if (itemsRequiringFps.Count > 0)
        {
            var message = QueueFpsValidationService.BuildManualFpsRequiredMessage(itemsRequiringFps);
            RefreshStatusLog("Some queued files need Source FPS before conversion can start.");
            technicalLog.Append(message);
            MessageBox.Show(this, message, "Source FPS Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateQueueButtonState();
            return;
        }

        CancelCurrentProbe();
        activeQueueSettings = CaptureCurrentQueueSettings();
        lastQueueRunSettings = activeQueueSettings;
        lastQueueRunStatus = "Running";
        ApplyLockedQueueSettingsToQueuedItems(activeQueueSettings);

        itemsRequiringFps = QueueFpsValidationService.FindItemsRequiringManualFps(queueItems);
        if (itemsRequiringFps.Count > 0)
        {
            activeQueueSettings = null;
            lastQueueRunStatus = "Not started";
            var message = QueueFpsValidationService.BuildManualFpsRequiredMessage(itemsRequiringFps);
            RefreshStatusLog("Some queued files need Source FPS before conversion can start.");
            technicalLog.Append(message);
            MessageBox.Show(this, message, "Source FPS Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateQueueButtonState();
            return;
        }

        if (!queueItems.Any(IsProcessableQueueItem))
        {
            activeQueueSettings = null;
            RefreshStatusLog("No processable queue items are ready.");
            UpdateQueueButtonState();
            return;
        }

        isQueueProcessing = true;
        isConversionRunning = true;
        stopAfterCurrentRequested = false;
        cancelCurrentOnlyRequested = false;
        cancelQueueRequested = false;
        lastConversionResult = null;
        lastConversionProgress = null;
        lastSuccessfulOutputPath = null;
        conversionCancellationTokenSource?.Dispose();
        conversionCancellationTokenSource = new CancellationTokenSource();
        openOutputFolderButton.Enabled = false;
        SetControlsEnabledForConversion(false);
        cancelButton.Enabled = true;
        conversionProgressBar.Style = ProgressBarStyle.Blocks;
        conversionProgressBar.MarqueeAnimationSpeed = 0;
        conversionProgressBar.Value = 0;
        technicalLog.Append($"----- Queue run started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -----");
        technicalLog.Append($"Queue started. Processable items: {queueItems.Count(IsProcessableQueueItem)}; Total queued: {queueItems.Count}; Active settings: {FormatQueueSettings(activeQueueSettings)}; Destination: {FormatOutputDestinationMode(activeQueueSettings.OutputDestinationMode)}; Chosen folder: {FormatOptionalValue(activeQueueSettings.ChosenOutputFolder, "none")}.");
        RefreshStatusLog($"Queue started. Processing 1 of {queueItems.Count(IsProcessableQueueItem)}.");

        var queueCanceled = false;
        var stoppedAfterCurrent = false;
        var attemptedItems = new HashSet<QueueItem>();

        try
        {
            while (true)
            {
                if (conversionCancellationTokenSource.IsCancellationRequested && !cancelCurrentOnlyRequested)
                {
                    queueCanceled = true;
                    break;
                }

                var validationItem = queueItems.FirstOrDefault(QueueRunningValidationService.ShouldProbeBeforeContinuing);
                if (validationItem is not null)
                {
                    var validationResult = await ProbeQueueItemForReadinessDuringRunAsync(validationItem, conversionCancellationTokenSource.Token);
                    if (validationResult?.WasCanceled == true || conversionCancellationTokenSource.IsCancellationRequested)
                    {
                        if (cancelCurrentOnlyRequested && !cancelQueueRequested)
                        {
                            ResetQueueCancellationForNextItem();
                            continue;
                        }

                        queueCanceled = true;
                        break;
                    }

                    continue;
                }

                var remainingProcessableItems = queueItems
                    .Where(item => !attemptedItems.Contains(item) && IsProcessableQueueItem(item))
                    .ToList();

                if (remainingProcessableItems.Count == 0)
                {
                    if (pendingRunningQueueAddOperations > 0)
                    {
                        try
                        {
                            await Task.Delay(200, conversionCancellationTokenSource.Token);
                            continue;
                        }
                        catch (OperationCanceledException)
                        {
                            queueCanceled = true;
                        }
                    }

                    break;
                }

                var item = remainingProcessableItems[0];
                attemptedItems.Add(item);
                currentQueueItem = item;
                var queueOrdinal = queueItems.IndexOf(item) + 1;
                var result = await ProcessQueueItemAsync(item, queueOrdinal, queueItems.Count, conversionCancellationTokenSource.Token);
                lastConversionResult = result;

                if (result?.WasCanceled == true || conversionCancellationTokenSource.IsCancellationRequested)
                {
                    if (cancelCurrentOnlyRequested && !cancelQueueRequested)
                    {
                        ResetQueueCancellationForNextItem();
                        continue;
                    }

                    queueCanceled = true;
                    break;
                }

                if (stopAfterCurrentRequested)
                {
                    technicalLog.Append("Queue stopped after current item request was honored.");
                    stoppedAfterCurrent = true;
                    break;
                }
            }
        }
        finally
        {
            currentQueueItem = null;
            isQueueProcessing = false;
            isConversionRunning = false;
            stopAfterCurrentRequested = false;
            cancelCurrentOnlyRequested = false;
            cancelQueueRequested = false;
            activeQueueSettings = null;
            conversionProgressBar.Style = ProgressBarStyle.Blocks;
            conversionProgressBar.MarqueeAnimationSpeed = 0;
            cancelButton.Enabled = false;
            cancelQueueButton.Enabled = false;
            stopAfterCurrentButton.Enabled = false;
            stopAfterCurrentButton.Text = "Stop After Current";
            conversionCancellationTokenSource?.Dispose();
            conversionCancellationTokenSource = null;
            SetControlsEnabledForConversion(true);

        }

        lastQueueRunStatus = BuildQueueRunStatus(queueCanceled, stoppedAfterCurrent);
        var summary = BuildQueueSummary(queueCanceled, stoppedAfterCurrent);
        technicalLog.Append(BuildQueueItemResultsSection());
        technicalLog.Append(summary);
        RefreshQueueGrid();
        RefreshStatusLog(summary);
        UpdateConvertButtonState();

        if (queueItems.Any(item => item.Status == QueueItemStatus.WaitingForProbe))
        {
            await PreProbeWaitingQueueItemsIfIdleAsync();
        }
    }

    private void ResetQueueCancellationForNextItem()
    {
        technicalLog.Append("Cancel Current completed. Queue will continue with the next runnable item.");
        cancelCurrentOnlyRequested = false;
        conversionCancellationTokenSource?.Dispose();
        conversionCancellationTokenSource = new CancellationTokenSource();
        cancelButton.Enabled = false;
        cancelQueueButton.Enabled = true;
        RefreshStatusLog("Current item canceled. Continuing queue...");
    }

    private async Task<ConversionResult?> ProcessQueueItemAsync(QueueItem item, int ordinal, int totalItems, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(item.InputPath);
        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            SetQueueItemStatus(item, QueueItemStatus.Warning, "Needs FPS", "Choose Source FPS");
            item.ResultStatusSummary = "Needs Source FPS";
            technicalLog.Append($"Queue item blocked because Source FPS is not set. Input: {item.InputPath}");
            return null;
        }

        SetQueueItemStatus(item, QueueItemStatus.Probing, "Probing", "");
        technicalLog.Append($"Queue item start. {ordinal} of {totalItems}; Input: {item.InputPath}; Planned output: {item.PlannedOutputPath}; Format: {item.OutputFormat.DisplayName()}; Mode: {FormatConversionModeForDisplay(item.ConversionMode)}; FPS: {item.FpsDisplayLabel} ({item.FfmpegRateValue}).");
        RefreshStatusLog($"Processing {ordinal} of {totalItems}: {fileName}");

        var inputValidation = InputFileValidator.ValidateDatFile(item.InputPath);
        if (!inputValidation.IsValid)
        {
            SetQueueItemStatus(item, QueueItemStatus.Failed, "Failed", inputValidation.Message);
            item.ResultStatusSummary = "Skipped - invalid output path";
            technicalLog.Append($"Queue item failed input revalidation. Input: {item.InputPath}; Reason: {inputValidation.Message}");
            technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, ordinal, totalItems));
            return null;
        }

        var probeResult = await probeService.ProbeRawH264Async(item.InputPath, item.Fps, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            var canceledResult = new ConversionResult(false, ConversionResult.CanceledMessage, ffmpegTools.FfmpegPath, Array.Empty<string>(), item.InputPath, item.PlannedOutputPath, item.Fps, null, "", "", WasCanceled: true);
            item.ConversionResult = canceledResult;
            SetQueueItemStatus(item, QueueItemStatus.Canceled, "Canceled", "");
            technicalLog.Append($"Queue item canceled during probe. Input: {item.InputPath}");
            technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, ordinal, totalItems));
            return canceledResult;
        }

        if (!probeResult.IsSuccess)
        {
            item.PreProbeResult = probeResult;
            item.ResultStatusSummary = "Skipped - unsupported video payload";
            SetQueueItemStatus(item, QueueItemStatus.Unsupported, "Unsupported", "Will not process");
            technicalLog.Append($"Queue item probe failed. Input: {item.InputPath}; Message: {ProbeResult.UnsupportedMessage}");
            technicalLog.AppendBlock("Queue item probe technical details", probeResult.TechnicalDetails);
            technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, ordinal, totalItems));
            return null;
        }

        technicalLog.Append($"Queue item probe succeeded. Input: {item.InputPath}; Codec: {FormatOptionalValue(probeResult.CodecName, "unknown")}; Resolution: {FormatResolution(probeResult.Width, probeResult.Height)}; Duration: {FormatOptionalValue(probeResult.Duration, "unknown")}.");
        item.PreProbeResult = probeResult;

        var outputSafety = RecheckQueueOutputSafety(item);
        if (!outputSafety.CanConvert)
        {
            SetQueueItemStatus(item, outputSafety.Status, outputSafety.StatusText, outputSafety.ProgressText);
            item.ResultStatusSummary = outputSafety.Status == QueueItemStatus.Skipped
                ? "Skipped - output already exists"
                : "Skipped - invalid output path";
            technicalLog.Append($"Queue item skipped/failed output safety check. Input: {item.InputPath}; Reason: {outputSafety.LogMessage}");
            technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, ordinal, totalItems));
            return null;
        }

        if (!string.Equals(item.PlannedOutputPath, outputSafety.OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            technicalLog.Append($"Queue item output path refreshed for safety. Old: {item.PlannedOutputPath}; New: {outputSafety.OutputPath}");
            item.PlannedOutputPath = outputSafety.OutputPath!;
        }

        var duration = item.IsSplitRecording ? null : ParseProbeDuration(probeResult.Duration);
        var hasDuration = duration.HasValue && duration.Value > TimeSpan.Zero;
        var effectiveDuration = item.TrimRange is null ? duration : item.TrimRange.End - item.TrimRange.Start;
        var hasEffectiveDuration = effectiveDuration.HasValue && effectiveDuration.Value > TimeSpan.Zero;
        ConfigureProgressBarForConversion(hasEffectiveDuration);
        conversionProgressBar.Value = 0;
        SetQueueItemStatus(item, QueueItemStatus.Converting, "Converting", hasEffectiveDuration ? "0%" : "Processing");
        RefreshStatusLog(item.TrimRange is null ? $"Processing {ordinal} of {totalItems}: {fileName}" : $"Converting selected trim... Processing {ordinal} of {totalItems}: {fileName}");

        var progress = new Progress<ConversionProgress>(progressUpdate => UpdateQueueConversionProgress(item, progressUpdate));
        var metadata = QueueItemContainerMetadataBuilder.Build(item);
        var timeline = RecordingTimelineBuilder.Build(item);
        var burnTimestamp = BurnTimestampMetadataBuilder.Build(item, timeline);
        ConversionResult conversionResult;
        if (item.BurnTimestamp && !BurnTimestampMetadataBuilder.IsSupportedMode(item.ConversionMode))
        {
            conversionResult = BuildBurnTimestampUnsupportedResult(item, BurnTimestampMetadataBuilder.RequiresFullModeMessage, effectiveDuration);
        }
        else if (item.BurnTimestamp && burnTimestamp is null)
        {
            conversionResult = BuildBurnTimestampUnsupportedResult(item, BurnTimestampMetadataBuilder.RequiresRecordingDateTimeMessage, effectiveDuration);
        }
        else
        {
            conversionResult = item.IsSplitRecording
                ? await ConvertSplitRecordingQueueItemAsync(item, duration, cancellationToken, metadata, burnTimestamp, progress)
                : item.TrimRange is not null
                    ? ConversionModes.IsNvenc(item.ConversionMode)
                        ? await conversionService.EncodeTrimmedNvencAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, item.TrimRange, progress, cancellationToken, metadata, burnTimestamp)
                        : string.Equals(item.ConversionMode, ConversionModes.Encode, StringComparison.OrdinalIgnoreCase)
                            ? await conversionService.EncodeTrimmedAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, item.TrimRange, progress, cancellationToken, metadata, burnTimestamp)
                        : TrimConversionPolicy.ShouldBlockTrimmedConversion(item.TrimRange, item.ConversionMode)
                            ? BuildTrimModeUnsupportedResult(item, duration)
                            : await conversionService.RemuxTrimmedAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, item.TrimRange, progress, cancellationToken, metadata)
                    : ConversionModes.IsNvenc(item.ConversionMode)
                        ? await conversionService.EncodeNvencAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, progress, cancellationToken, metadata, burnTimestamp)
                        : string.Equals(item.ConversionMode, ConversionModes.Encode, StringComparison.OrdinalIgnoreCase)
                            ? await conversionService.EncodeAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, progress, cancellationToken, metadata, burnTimestamp)
                        : await conversionService.RemuxAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, progress, cancellationToken, metadata);
        }

        conversionResult = EnrichConversionTelemetry(conversionResult, probeResult, item);
        item.ConversionResult = conversionResult;
        AppendConversionResultToLog(conversionResult, includeStatusSummary: false);

        if (conversionResult.IsSuccess)
        {
            item.PlannedOutputPath = conversionResult.OutputPath;
            SetQueueItemStatus(item, QueueItemStatus.Completed, "Completed", "100%");
            lastSuccessfulOutputPath = conversionResult.OutputPath;
            openOutputFolderButton.Enabled = true;
            conversionProgressBar.Value = 100;
        }
        else if (conversionResult.WasCanceled)
        {
            SetQueueItemStatus(item, QueueItemStatus.Canceled, "Canceled", "");
        }
        else
        {
            SetQueueItemStatus(item, QueueItemStatus.Failed, "Failed", "");
        }

        technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, ordinal, totalItems));
        return conversionResult;
    }

    private async Task<ConversionResult?> ProbeQueueItemForReadinessDuringRunAsync(QueueItem item, CancellationToken cancellationToken)
    {
        var ordinal = queueItems.IndexOf(item) + 1;
        var totalItems = queueItems.Count;
        SetQueueItemStatus(item, QueueItemStatus.Probing, "Probing", "");
        var probeFps = GetProbeFpsOption(item);
        technicalLog.Append($"Queue validation probe item. Input: {item.InputPath}; FPS: {probeFps.Label} ({probeFps.FfmpegValue}){(item.HasResolvedFps ? "" : " probe-only")}.");

        var probeResult = await probeService.ProbeRawH264Async(item.InputPath, probeFps, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            var canceledResult = new ConversionResult(false, ConversionResult.CanceledMessage, ffmpegTools.FfmpegPath, Array.Empty<string>(), item.InputPath, item.PlannedOutputPath, probeFps, null, "", "", WasCanceled: true);
            item.ConversionResult = canceledResult;
            SetQueueItemStatus(item, QueueItemStatus.Canceled, "Canceled", "");
            technicalLog.Append($"Queue validation probe canceled. Input: {item.InputPath}");
            technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, ordinal, totalItems));
            return canceledResult;
        }

        QueueItemStatusService.ApplyPreProbeResult(item, probeResult);
        RefreshQueueGrid();

        if (probeResult.IsSuccess)
        {
            if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
            {
                technicalLog.Append($"Queue validation probe succeeded; Source FPS still needs manual selection. Input: {item.InputPath}; Codec: {FormatOptionalValue(probeResult.CodecName, "unknown")}; Resolution: {FormatResolution(probeResult.Width, probeResult.Height)}; Profile: {FormatOptionalValue(probeResult.Profile, "unknown")}.");
            }
            else
            {
                technicalLog.Append($"Queue validation probe succeeded. Input: {item.InputPath}; Codec: {FormatOptionalValue(probeResult.CodecName, "unknown")}; Resolution: {FormatResolution(probeResult.Width, probeResult.Height)}; Profile: {FormatOptionalValue(probeResult.Profile, "unknown")}.");
            }
        }
        else
        {
            technicalLog.Append($"Queue validation probe failed. Input: {item.InputPath}; Message: {ProbeResult.UnsupportedMessage}");
            technicalLog.AppendBlock("Queue validation probe technical details", probeResult.TechnicalDetails);
            technicalLog.Append(QueueItemResultFormatter.BuildLogLine(item, ordinal, totalItems));
        }

        return null;
    }

    private async Task<ConversionResult> ConvertSplitRecordingQueueItemAsync(QueueItem item, TimeSpan? duration, CancellationToken cancellationToken, ContainerMetadata? metadata, BurnTimestampOptions? burnTimestamp, IProgress<ConversionProgress>? progress)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (TrimConversionPolicy.ShouldBlockTrimmedConversion(item.TrimRange, item.ConversionMode))
        {
            stopwatch.Stop();
            return item.TrimRange is not null
                ? BuildTrimModeUnsupportedResult(item, duration, stopwatch.Elapsed)
                : new ConversionResult(
                    false,
                    "Combined split recordings currently use Fast mode.",
                    ffmpegTools.FfmpegPath,
                    Array.Empty<string>(),
                    item.InputPath,
                    item.PlannedOutputPath,
                    item.Fps,
                    null,
                    "",
                    "Combined split recording Full mode is not supported in this pass. Choose Fast mode or import every DAT separately.",
                    ConversionMode: item.ConversionMode,
                    OutputFormat: item.OutputFormat.DisplayName(),
                    Duration: duration,
                    UsedDeterminateProgress: false,
                    ProcessingTime: stopwatch.Elapsed);
        }

        if (item.SplitExportPlan is null || !item.SplitExportPlan.IsStrongConfidence)
        {
            stopwatch.Stop();
            return new ConversionResult(
                false,
                "Split recording could not be verified.",
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                item.InputPath,
                item.PlannedOutputPath,
                item.Fps,
                null,
                "",
                "Split recording plan was not strong enough at conversion time.",
                ConversionMode: "Fast",
                OutputFormat: item.OutputFormat.DisplayName(),
                Duration: duration,
                UsedDeterminateProgress: false,
                ProcessingTime: stopwatch.Elapsed);
        }

        if (item.TrimRange is not null)
        {
            var timeline = RecordingTimelineBuilder.Build(item);
            var trimmedResult = ConversionModes.IsNvenc(item.ConversionMode)
                ? await conversionService.EncodeTrimmedSplitNvencAsync(
                    item.InputPath,
                    item.PlannedOutputPath,
                    item.OutputFormat,
                    item.Fps,
                    item.SplitExportPlan,
                    timeline,
                    item.TrimRange,
                    cancellationToken,
                    metadata,
                    burnTimestamp,
                    progress)
                : string.Equals(item.ConversionMode, ConversionModes.Encode, StringComparison.OrdinalIgnoreCase)
                    ? await conversionService.EncodeTrimmedSplitAsync(
                    item.InputPath,
                    item.PlannedOutputPath,
                    item.OutputFormat,
                    item.Fps,
                    item.SplitExportPlan,
                    timeline,
                    item.TrimRange,
                    cancellationToken,
                    metadata,
                    burnTimestamp,
                    progress)
                    : await conversionService.RemuxTrimmedSplitAsync(
                        item.InputPath,
                        item.PlannedOutputPath,
                        item.OutputFormat,
                        item.Fps,
                        item.SplitExportPlan,
                        timeline,
                        item.TrimRange,
                        cancellationToken,
                        metadata);
            stopwatch.Stop();
            return trimmedResult with
            {
                ProcessingTime = stopwatch.Elapsed,
                Telemetry = trimmedResult.Telemetry is null
                    ? null
                    : trimmedResult.Telemetry with { ElapsedConversionTime = stopwatch.Elapsed }
            };
        }

        if (item.BurnTimestamp && ConversionModes.IsEncode(item.ConversionMode))
        {
            var timeline = RecordingTimelineBuilder.Build(item);
            if (!timeline.TotalDuration.HasValue || timeline.TotalDuration.Value <= TimeSpan.Zero)
            {
                stopwatch.Stop();
                return BuildBurnTimestampUnsupportedResult(item, "Split recording duration is required for Burn timestamp.", duration);
            }

            var fullRange = new TrimRange(TimeSpan.Zero, timeline.TotalDuration.Value);
            var encodedResult = ConversionModes.IsNvenc(item.ConversionMode)
                ? await conversionService.EncodeTrimmedSplitNvencAsync(
                    item.InputPath,
                    item.PlannedOutputPath,
                    item.OutputFormat,
                    item.Fps,
                    item.SplitExportPlan,
                    timeline,
                    fullRange,
                    cancellationToken,
                    metadata,
                    burnTimestamp,
                    progress)
                : await conversionService.EncodeTrimmedSplitAsync(
                    item.InputPath,
                    item.PlannedOutputPath,
                    item.OutputFormat,
                    item.Fps,
                    item.SplitExportPlan,
                    timeline,
                    fullRange,
                    cancellationToken,
                    metadata,
                    burnTimestamp,
                    progress);
            stopwatch.Stop();
            return encodedResult with
            {
                ProcessingTime = stopwatch.Elapsed,
                Telemetry = encodedResult.Telemetry is null
                    ? null
                    : encodedResult.Telemetry with { ElapsedConversionTime = stopwatch.Elapsed }
            };
        }

        var result = await new SpotterCombinedSplitExportPrototype(_ => item.SplitExportPlan, (inputPath, outputPath, token) => new SpotterDatPayloadExtractor().Extract(inputPath, outputPath, token), (executablePath, arguments, timeout, token) => FfmpegProcessRunner.RunAsync(executablePath, arguments, timeout, token))
            .RunAsync(item.InputPath, item.PlannedOutputPath, ffmpegTools.FfmpegPath, item.Fps, keepTemp: false, cancellationToken, metadata);

        stopwatch.Stop();

        return new ConversionResult(
            result.Succeeded,
            result.Succeeded ? "Fast conversion completed." : ConversionResult.FastFailedMessage,
            ffmpegTools.FfmpegPath,
            result.FfmpegArguments,
            item.InputPath,
            item.PlannedOutputPath,
            item.Fps,
            result.FfmpegResult?.ExitCode,
            result.FfmpegResult?.StandardOutput ?? "",
            result.BuildTechnicalReport(),
            ConversionMode: "Fast",
            OutputFormat: item.OutputFormat.DisplayName(),
            WasCanceled: result.FfmpegResult?.WasCanceled == true || string.Equals(result.FailureReason, "Combined split-export remux was canceled.", StringComparison.Ordinal),
            TimedOut: result.FfmpegResult?.TimedOut == true,
            Duration: duration,
            UsedDeterminateProgress: false,
            InputPathMode: ConversionInputPathMode.CleanExtractedH264,
            ProcessingTime: stopwatch.Elapsed);
    }

    private ConversionResult BuildTrimModeUnsupportedResult(QueueItem item, TimeSpan? duration, TimeSpan? processingTime = null)
    {
        return new ConversionResult(
            false,
            TrimConversionPolicy.FastModeOnlyMessage,
            ffmpegTools.FfmpegPath,
            Array.Empty<string>(),
            item.InputPath,
            item.PlannedOutputPath,
            item.Fps,
            null,
            "",
            "TrimRange was set with an unsupported conversion mode.",
            ConversionMode: item.ConversionMode,
            OutputFormat: item.OutputFormat.DisplayName(),
            Duration: item.TrimRange is null ? duration : item.TrimRange.End - item.TrimRange.Start,
            UsedDeterminateProgress: item.TrimRange is not null || duration.HasValue,
            ProcessingTime: processingTime);
    }

    private ConversionResult BuildBurnTimestampUnsupportedResult(QueueItem item, string message, TimeSpan? duration)
    {
        return new ConversionResult(
            false,
            message,
            ffmpegTools.FfmpegPath,
            Array.Empty<string>(),
            item.InputPath,
            item.PlannedOutputPath,
            item.Fps,
            null,
            "",
            message,
            ConversionMode: item.ConversionMode,
            OutputFormat: item.OutputFormat.DisplayName(),
            Duration: duration,
            UsedDeterminateProgress: duration.HasValue);
    }

    private ConversionResult EnrichConversionTelemetry(ConversionResult result, ProbeResult? probeResult, QueueItem? item)
    {
        if (result.Telemetry is null)
        {
            return result;
        }

        var telemetry = result.Telemetry.WithProbe(probeResult).WithNvencAvailability(nvencCapability.IsAvailable);
        if (item is not null)
        {
            telemetry = telemetry.WithPathFlags(
                trimUsed: item.TrimRange is not null || telemetry.TrimUsed == true,
                burnTimestampUsed: item.BurnTimestamp || telemetry.BurnTimestampUsed == true,
                splitExportUsed: item.IsSplitRecording);
        }

        return result with { Telemetry = telemetry };
    }

    private bool IsProcessableQueueItem(QueueItem item)
    {
        return QueueProcessingEligibilityService.IsProcessable(item, CustomOutputPathExists);
    }

    private static bool CustomOutputPathExists(QueueItem item)
    {
        return !string.IsNullOrWhiteSpace(item.CustomOutputPath) && File.Exists(item.CustomOutputPath);
    }

    private bool HasPendingQueueValidation()
    {
        return queueItems.Any(item => item.Status is QueueItemStatus.WaitingForProbe or QueueItemStatus.Probing);
    }

    private static FpsOption GetProbeFpsOption(QueueItem item)
    {
        return item.HasResolvedFps && !string.IsNullOrWhiteSpace(item.Fps.FfmpegValue)
            ? item.Fps
            : FpsOption.FromLabel("30");
    }

    private void SetQueueItemStatus(QueueItem item, QueueItemStatus status, string statusText, string progressText)
    {
        item.Status = status;
        item.StatusText = statusText;
        item.ProgressText = progressText;
        RefreshQueueGrid();
    }

    private QueueOutputSafetyResult RecheckQueueOutputSafety(QueueItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.CustomOutputPath))
        {
            var customValidation = OutputPathService.ValidateCustomOutputPath(
                item.InputPath,
                item.CustomOutputPath,
                item.OutputFormat,
                requireAvailable: false,
                allowExtensionCorrection: true);
            if (!customValidation.IsValid || string.IsNullOrWhiteSpace(customValidation.OutputPath))
            {
                return QueueOutputSafetyResult.Fail(customValidation.Message);
            }

            item.CustomOutputPath = customValidation.OutputPath;
            if (File.Exists(customValidation.OutputPath))
            {
                return QueueOutputSafetyResult.Skip("Exists", "Selected output exists", $"Custom Save As output already exists: {customValidation.OutputPath}");
            }

            if (!IsAvailableQueueOutputPath(customValidation.OutputPath, item))
            {
                return QueueOutputSafetyResult.Fail($"Custom Save As output is already used by another queued item: {customValidation.OutputPath}");
            }

            return QueueOutputSafetyResult.Convert(customValidation.OutputPath, "Custom Save As output safety check passed.");
        }

        var outputFolderPath = item.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource
            ? Path.GetDirectoryName(item.InputPath)
            : item.SelectedOutputFolder;

        var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
        if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
        {
            var plannedOutputValidation = ValidatePlannedQueueOutputPath(item);
            if (plannedOutputValidation is not null)
            {
                return plannedOutputValidation;
            }

            return QueueOutputSafetyResult.Fail($"Output folder is invalid: {outputFolderValidation.Message}");
        }

        var plannedOutputResult = ValidatePlannedQueueOutputPath(item);
        if (plannedOutputResult is not null)
        {
            return plannedOutputResult;
        }

        var directOutputPath = GetDirectOutputPathForQueueRefresh(item, outputFolderValidation.FolderPath, item.OutputFormat);
        var directOutputExists = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
        var plannedOutputIsDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) &&
                                          string.Equals(item.PlannedOutputPath, directOutputPath, StringComparison.OrdinalIgnoreCase);
        if (directOutputExists && (item.HasExistingDirectOutput || plannedOutputIsDirectOutput))
        {
            return QueueOutputSafetyResult.Skip("Exists", "Selected output exists", $"Direct selected-format output already exists: {directOutputPath}");
        }

        var safeOutputPath = PlanQueueOutputPath(item.InputPath, outputFolderValidation.FolderPath, item.OutputFormat, item);
        if (string.IsNullOrWhiteSpace(safeOutputPath))
        {
            return QueueOutputSafetyResult.Fail("No safe output path could be planned.");
        }

        if (File.Exists(safeOutputPath))
        {
            return QueueOutputSafetyResult.Fail($"Safe output path unexpectedly exists: {safeOutputPath}");
        }

        return QueueOutputSafetyResult.Convert(safeOutputPath, directOutputExists
            ? "Direct output exists; using safe alternate output path."
            : "Output safety check passed.");
    }

    private QueueOutputSafetyResult? ValidatePlannedQueueOutputPath(QueueItem item)
    {
        if (string.IsNullOrWhiteSpace(item.PlannedOutputPath))
        {
            return null;
        }

        string plannedOutputPath;
        try
        {
            plannedOutputPath = Path.GetFullPath(item.PlannedOutputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        if (!string.Equals(Path.GetExtension(plannedOutputPath), item.OutputFormat.Extension(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var plannedOutputFolder = Path.GetDirectoryName(plannedOutputPath);
        if (string.IsNullOrWhiteSpace(plannedOutputFolder) || !Directory.Exists(plannedOutputFolder))
        {
            return null;
        }

        if (!OutputPathService.IsSafeOutputPath(item.InputPath, plannedOutputPath))
        {
            return QueueOutputSafetyResult.Fail($"Planned output path is not safe: {plannedOutputPath}");
        }

        if (!IsAllowedQueueOutputPath(plannedOutputPath, item))
        {
            return QueueOutputSafetyResult.Fail($"Planned output path is already used by another queued item: {plannedOutputPath}");
        }

        if (File.Exists(plannedOutputPath))
        {
            return QueueOutputSafetyResult.Skip("Exists", "Selected output exists", $"Planned output already exists: {plannedOutputPath}");
        }

        item.PlannedOutputPath = plannedOutputPath;
        return QueueOutputSafetyResult.Convert(plannedOutputPath, "Planned output path safety check passed.");
    }

    private void UpdateQueueConversionProgress(QueueItem item, ConversionProgress progress)
    {
        lastConversionProgress = progress;

        if (progress.Percent.HasValue)
        {
            if (conversionProgressBar.Style == ProgressBarStyle.Marquee)
            {
                conversionProgressBar.Style = ProgressBarStyle.Continuous;
                conversionProgressBar.MarqueeAnimationSpeed = 0;
            }

            conversionProgressBar.Value = Math.Clamp(progress.Percent.Value, conversionProgressBar.Minimum, conversionProgressBar.Maximum);
            item.ProgressText = $"{progress.Percent.Value}%";
        }
        else
        {
            if (string.Equals(progress.Summary, "Preparing selected trim...", StringComparison.Ordinal))
            {
                conversionProgressBar.Style = ProgressBarStyle.Marquee;
                conversionProgressBar.MarqueeAnimationSpeed = 30;
            }

            item.ProgressText = string.IsNullOrWhiteSpace(progress.Summary) ? "Processing" : progress.Summary;
        }

        var now = DateTime.UtcNow;
        if (!progress.IsEnd && (now - lastProgressUiUpdateUtc).TotalMilliseconds < 750)
        {
            return;
        }

        lastProgressUiUpdateUtc = now;
        RefreshQueueGrid();
        RefreshStatusLog($"Processing queue item: {Path.GetFileName(item.InputPath)}");
    }

    private static TimeSpan? ParseProbeDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration) || string.Equals(duration, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (double.TryParse(duration, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(duration, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : null;
    }

    private string BuildQueueSummary(bool queueCanceled, bool stoppedAfterCurrent)
    {
        var completed = queueItems.Count(item => item.Status == QueueItemStatus.Completed);
        var failed = queueItems.Count(item => item.Status is QueueItemStatus.Failed or QueueItemStatus.Invalid or QueueItemStatus.Unsupported);
        var exists = queueItems.Count(item => item.Status == QueueItemStatus.Skipped);
        var canceled = queueItems.Count(item => item.Status == QueueItemStatus.Canceled);
        var pending = queueItems.Count(item => item.Status is QueueItemStatus.WaitingForProbe or QueueItemStatus.Ready or QueueItemStatus.Warning);

        if (stoppedAfterCurrent)
        {
            return $"Queue stopped after current item. Completed: {completed}, Failed: {failed}, Exists: {exists}, Canceled: {canceled}, Pending: {pending}.";
        }

        return queueCanceled
            ? $"Queue stopped. Completed: {completed}, Failed: {failed}, Exists: {exists}, Canceled: {canceled}, Pending: {pending}."
            : $"Queue completed. Completed: {completed}, Failed: {failed}, Exists: {exists}, Canceled: {canceled}.";
    }

    private string BuildQueueRunStatus(bool queueCanceled, bool stoppedAfterCurrent)
    {
        if (queueCanceled || stoppedAfterCurrent)
        {
            return "Canceled";
        }

        return queueItems.Any(item => item.Status is QueueItemStatus.Failed or QueueItemStatus.Invalid or QueueItemStatus.Unsupported)
            ? "Failed"
            : "Completed";
    }

    private string BuildQueueItemResultsSection()
    {
        var lines = new List<string> { "Queue item results:" };
        for (var index = 0; index < queueItems.Count; index++)
        {
            lines.Add(QueueItemResultFormatter.BuildSummaryLine(queueItems[index], index + 1, queueItems.Count));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record QueueOutputSafetyResult(
        bool CanConvert,
        string? OutputPath,
        QueueItemStatus Status,
        string StatusText,
        string ProgressText,
        string LogMessage)
    {
        public static QueueOutputSafetyResult Convert(string outputPath, string logMessage)
        {
            return new QueueOutputSafetyResult(true, outputPath, QueueItemStatus.Converting, "Converting", "", logMessage);
        }

        public static QueueOutputSafetyResult Skip(string statusText, string progressText, string logMessage)
        {
            return new QueueOutputSafetyResult(false, null, QueueItemStatus.Skipped, statusText, progressText, logMessage);
        }

        public static QueueOutputSafetyResult Fail(string logMessage)
        {
            return new QueueOutputSafetyResult(false, null, QueueItemStatus.Failed, "Failed", "", logMessage);
        }
    }

    private sealed record FolderQueueAddPreview(
        IReadOnlyList<string> AddablePaths,
        int InvalidCount,
        int AlreadyConvertedSkippedCount,
        int OutputPlanFailedCount);

    private async Task StartConversionAsync()
    {
        if (!CanStartConversion())
        {
            technicalLog.Append("Conversion start blocked: current selections are not fully valid or probe validation is not complete.");
            RefreshStatusLog("Convert is not ready. Select a valid .dat file, output folder, and wait for probe validation to succeed.");
            UpdateConvertButtonState();
            return;
        }

        RecalculatePlannedOutputPath();
        if (string.IsNullOrWhiteSpace(selectionState.PlannedOutputFilePath))
        {
            technicalLog.Append("Conversion start blocked: no safe output path could be planned.");
            RefreshStatusLog("A safe output path could not be planned.");
            UpdateConvertButtonState();
            return;
        }

        if (File.Exists(selectionState.PlannedOutputFilePath))
        {
            technicalLog.Append($"Planned output path appeared before conversion and will be recalculated: {selectionState.PlannedOutputFilePath}");
            RecalculatePlannedOutputPath();
        }

        if (string.IsNullOrWhiteSpace(selectionState.PlannedOutputFilePath) || File.Exists(selectionState.PlannedOutputFilePath))
        {
            technicalLog.Append("Conversion start blocked: output path still conflicts after recalculation.");
            RefreshStatusLog("A non-conflicting output path could not be planned.");
            UpdateConvertButtonState();
            return;
        }

        var inputPath = selectionState.SelectedInputFilePath!;
        var outputPath = selectionState.PlannedOutputFilePath;
        var outputFormat = GetSelectedOutputFormat();
        var conversionMode = GetSelectedConversionMode();
        var fps = GetSelectedFpsOption();
        var duration = GetProbeDuration();
        var hasDuration = duration.HasValue && duration.Value > TimeSpan.Zero;
        technicalLog.Append($"Starting conversion. Mode: {FormatConversionModeForDisplay(conversionMode)}; Format: {outputFormat}; FPS: {fps.Label} ({fps.FfmpegValue}); Input: {inputPath}; Output: {outputPath}; Duration available: {FormatYesNo(hasDuration)}; Duration: {FormatDuration(duration)}; Progress mode: {(hasDuration ? "Determinate" : "Indeterminate")}.");

        isConversionRunning = true;
        lastConversionResult = null;
        lastConversionProgress = null;
        lastSuccessfulOutputPath = null;
        conversionCancellationTokenSource?.Dispose();
        conversionCancellationTokenSource = new CancellationTokenSource();
        openOutputFolderButton.Enabled = false;
        SetControlsEnabledForConversion(false);
        ConfigureProgressBarForConversion(hasDuration);
        conversionProgressBar.Value = 0;
        currentConversionHeadline = "Starting conversion...";
        RefreshStatusLog(currentConversionHeadline);
        UpdateConvertButtonState();
        cancelButton.Enabled = true;

        var progress = new Progress<ConversionProgress>(progressUpdate => UpdateConversionProgress(conversionMode, progressUpdate));

        ConversionResult result;
        try
        {
            currentConversionHeadline = "Converting...";
            RefreshStatusLog(currentConversionHeadline);
            var metadata = new ContainerMetadata(
                CreationTime: null,
                Title: Path.GetFileNameWithoutExtension(inputPath),
                Comment: "Source type: Single DAT");
            result = ConversionModes.IsNvenc(conversionMode)
                ? await conversionService.EncodeNvencAsync(inputPath, outputPath, outputFormat, fps, duration, progress, conversionCancellationTokenSource.Token, metadata)
                : string.Equals(conversionMode, ConversionModes.Encode, StringComparison.OrdinalIgnoreCase)
                    ? await conversionService.EncodeAsync(inputPath, outputPath, outputFormat, fps, duration, progress, conversionCancellationTokenSource.Token, metadata)
                : await conversionService.RemuxAsync(inputPath, outputPath, outputFormat, fps, duration, progress, conversionCancellationTokenSource.Token, metadata);
        }
        finally
        {
            isConversionRunning = false;
            conversionProgressBar.Style = ProgressBarStyle.Blocks;
            conversionProgressBar.MarqueeAnimationSpeed = 0;
            SetControlsEnabledForConversion(true);
            cancelButton.Enabled = false;
            conversionCancellationTokenSource?.Dispose();
            conversionCancellationTokenSource = null;
        }

        result = EnrichConversionTelemetry(result, selectionState.LastProbeResult, null);
        lastConversionResult = result;
        AppendConversionResultToLog(result);

        if (result.IsSuccess)
        {
            lastSuccessfulOutputPath = result.OutputPath;
            openOutputFolderButton.Enabled = true;
            conversionProgressBar.Value = 100;
            RefreshStatusLog(result.UserMessage);
        }
        else
        {
            openOutputFolderButton.Enabled = false;
            conversionProgressBar.Value = 0;
            RefreshStatusLog(result.UserMessage);
        }

        RecalculatePlannedOutputPath();
        UpdateConvertButtonState();
    }

    private bool CanStartConversion()
    {
        return
            ffmpegTools.AreAvailable &&
            selectionState.IsInputFileValid &&
            selectionState.IsOutputFolderValid &&
            selectionState.IsPlannedOutputPathValid &&
            selectionState.IsProbeValid &&
            !selectionState.IsProbeRunning &&
            !isConversionRunning;
    }

    private void SetControlsEnabledForConversion(bool enabled)
    {
        var allowQueueEditing = enabled || isQueueProcessing;
        browseFileButton.Enabled = allowQueueEditing && ffmpegTools.AreAvailable;
        addFolderButton.Enabled = enabled && ffmpegTools.AreAvailable && !isQueueProcessing;
        queueGridView.Enabled = allowQueueEditing;
        startQueueButton.Enabled = false;
        stopAfterCurrentButton.Enabled = false;
        cancelQueueButton.Enabled = false;
        removeSelectedQueueItemButton.Enabled = false;
        duplicateSelectedQueueItemButton.Enabled = false;
        clearCompletedQueueButton.Enabled = false;
        sameFolderRadioButton.Enabled = enabled;
        chooseFolderRadioButton.Enabled = enabled;
        outputFolderTextBox.Enabled = enabled && selectionState.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder;
        browseOutputFolderButton.Enabled = enabled && ffmpegTools.AreAvailable && selectionState.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder;
        outputFormatComboBox.Enabled = enabled;
        conversionModeComboBox.Enabled = enabled;
        frameRateComboBox.Enabled = enabled;
        convertButton.Enabled = enabled && CanStartConversion();
        cancelButton.Enabled = isConversionRunning && !enabled;
        UpdateQueueButtonState();
    }

    private void ApplyOutputDestinationMode()
    {
        sameFolderRadioButton.Checked = selectionState.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource;
        chooseFolderRadioButton.Checked = selectionState.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder;
        ValidateOutputDestination();
        outputFolderTextBox.Enabled = selectionState.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder;
        browseOutputFolderButton.Enabled = ffmpegTools.AreAvailable && selectionState.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder && !isConversionRunning;
    }

    private void ValidateOutputDestination()
    {
        selectionState.SelectedOutputFolderPath = null;
        selectionState.IsOutputFolderValid = false;

        if (selectionState.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource)
        {
            var sourceFolderPath = string.IsNullOrWhiteSpace(selectionState.SelectedInputFilePath)
                ? null
                : Path.GetDirectoryName(selectionState.SelectedInputFilePath);

            if (string.IsNullOrWhiteSpace(sourceFolderPath))
            {
                outputFolderTextBox.Text = "Same folder as source file";
                return;
            }

            var validation = OutputFolderValidator.ValidateOutputFolder(sourceFolderPath);
            selectionState.SelectedOutputFolderPath = validation.IsValid ? validation.FolderPath : null;
            selectionState.IsOutputFolderValid = validation.IsValid;
            outputFolderTextBox.Text = validation.IsValid ? validation.FolderPath! : "Source folder is not writable";

            if (!validation.IsValid && selectionState.IsInputFileValid)
            {
                technicalLog.Append($"Source folder validation failed. Path: {sourceFolderPath}; Reason: {validation.Message}");
            }

            return;
        }

        outputFolderTextBox.Text = string.IsNullOrWhiteSpace(selectionState.ChosenOutputFolderPath)
            ? "No output folder selected"
            : selectionState.ChosenOutputFolderPath;

        var chosenValidation = OutputFolderValidator.ValidateOutputFolder(selectionState.ChosenOutputFolderPath);
        selectionState.SelectedOutputFolderPath = chosenValidation.IsValid ? chosenValidation.FolderPath : null;
        selectionState.IsOutputFolderValid = chosenValidation.IsValid;
    }

    private void ConfigureProgressBarForConversion(bool hasDuration)
    {
        conversionProgressBar.Style = hasDuration ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
        conversionProgressBar.MarqueeAnimationSpeed = hasDuration ? 0 : 30;
    }

    private void UpdateConversionProgress(string conversionMode, ConversionProgress progress)
    {
        lastConversionProgress = progress;

        if (progress.Percent.HasValue)
        {
            if (conversionProgressBar.Style == ProgressBarStyle.Marquee)
            {
                conversionProgressBar.Style = ProgressBarStyle.Continuous;
                conversionProgressBar.MarqueeAnimationSpeed = 0;
            }

            conversionProgressBar.Value = Math.Clamp(progress.Percent.Value, conversionProgressBar.Minimum, conversionProgressBar.Maximum);
        }
        else if (string.Equals(progress.Summary, "Preparing selected trim...", StringComparison.Ordinal))
        {
            conversionProgressBar.Style = ProgressBarStyle.Marquee;
            conversionProgressBar.MarqueeAnimationSpeed = 30;
        }

        var now = DateTime.UtcNow;
        if (!progress.IsEnd && (now - lastProgressUiUpdateUtc).TotalMilliseconds < 750)
        {
            return;
        }

        lastProgressUiUpdateUtc = now;
        currentConversionHeadline = progress.Percent.HasValue
            ? $"Converting... {progress.Percent.Value}%"
            : $"Converting... {progress.Summary}";
        RefreshStatusLog(currentConversionHeadline);
    }

    private async Task StartProbeIfReadyAsync()
    {
        if (!ffmpegTools.AreAvailable || !selectionState.IsInputFileValid || string.IsNullOrWhiteSpace(selectionState.SelectedInputFilePath))
        {
            return;
        }

        CancelCurrentProbe();

        var sequence = ++probeSequence;
        var inputFilePath = selectionState.SelectedInputFilePath;
        var fps = GetSelectedFpsOption();
        probeCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = probeCancellationTokenSource.Token;

        selectionState.IsProbeRunning = true;
        selectionState.IsProbeValid = false;
        selectionState.LastProbeResult = null;
        technicalLog.Append($"Starting probe. Tool: {ffmpegTools.FfprobePath}; Input: {inputFilePath}; FPS: {fps.Label} ({fps.FfmpegValue}).");
        technicalLog.Append($"Probe command: {ffmpegTools.FfprobePath} -v error -f h264 -framerate {fps.FfmpegValue} -i \"{inputFilePath}\" -select_streams v:0 -show_entries stream=codec_name,profile,width,height,pix_fmt,r_frame_rate,avg_frame_rate,duration -show_entries format=duration -of json");
        RefreshStatusLog("Probing selected .dat file...");
        UpdateConvertButtonState();

        var result = await probeService.ProbeRawH264Async(inputFilePath, fps, cancellationToken);

        if (cancellationToken.IsCancellationRequested || sequence != probeSequence || !string.Equals(inputFilePath, selectionState.SelectedInputFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        selectionState.IsProbeRunning = false;
        selectionState.IsProbeValid = result.IsSuccess;
        selectionState.LastProbeResult = result;
        AppendProbeResultToLog(result);

        RefreshStatusLog(result.IsSuccess ? result.UserMessage : ProbeResult.UnsupportedMessage);
        UpdateConvertButtonState();
    }

    private void CancelCurrentProbe()
    {
        probeSequence++;
        probeCancellationTokenSource?.Cancel();
        probeCancellationTokenSource?.Dispose();
        probeCancellationTokenSource = null;
        selectionState.IsProbeRunning = false;
    }

    private void RefreshStatusLog(string headline)
    {
        currentUserStatus = headline;
        currentStatusLabel.Text = headline;
        currentStatusLabel.Font = string.Equals(headline, MissingToolsStatusMessage, StringComparison.Ordinal)
            ? boldStatusFont
            : normalStatusFont;
        RefreshDetailsText(DetailsScrollMode.Bottom);
    }

    private void RefreshDetailsText(DetailsScrollMode scrollMode = DetailsScrollMode.Preserve)
    {
        if (areDetailsVisible)
        {
            SetDetailsText(BuildDetailsText(), scrollMode);
        }
    }

    private void SetDetailsVisible(bool visible)
    {
        var wasVisible = areDetailsVisible;
        if (visible == wasVisible)
        {
            return;
        }

        SetRootRedraw(enabled: false);
        SuspendLayout();
        rootLayout?.SuspendLayout();
        detailsPanel?.SuspendLayout();
        try
        {
            if (visible)
            {
                detailsCollapsedClientSize = ClientSize;
                var detailsHeight = GetDetailsExpansionHeight();
                if (detailsRowStyle is not null)
                {
                    detailsRowStyle.SizeType = SizeType.Absolute;
                    detailsRowStyle.Height = detailsHeight;
                }

                if (detailsPanel is not null)
                {
                    detailsPanel.Visible = true;
                }

                ClientSize = new Size(ClientSize.Width, ClientSize.Height + detailsHeight);
                EnsureWindowVisible();
                areDetailsVisible = true;
                showDetailsButton.Text = "Hide Details";
                SetDetailsText(BuildDetailsText(), DetailsScrollMode.Bottom);
            }
            else
            {
                var detailsHeight = GetCurrentDetailsRowHeight();
                var collapsedClientSize = detailsCollapsedClientSize ?? new Size(ClientSize.Width, ClientSize.Height - detailsHeight);
                areDetailsVisible = false;
                showDetailsButton.Text = "Show Details";

                if (detailsPanel is not null)
                {
                    detailsPanel.Visible = false;
                }

                if (detailsRowStyle is not null)
                {
                    detailsRowStyle.SizeType = SizeType.Absolute;
                    detailsRowStyle.Height = 0;
                }

                ClientSize = new Size(
                    ClientSize.Width,
                    Math.Max(GetMinimumUsableClientSize().Height, collapsedClientSize.Height));
                detailsCollapsedClientSize = null;
            }
        }
        finally
        {
            detailsPanel?.ResumeLayout(false);
            rootLayout?.ResumeLayout(true);
            ResumeLayout(true);
            SetRootRedraw(enabled: true);
            Invalidate();
        }
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (!areDetailsVisible || detailsRowStyle is null)
        {
            return;
        }

        detailsRowStyle.Height = Math.Min(detailsRowStyle.Height, GetDetailsExpansionHeight());
    }

    private int GetCurrentDetailsRowHeight()
    {
        return detailsRowStyle is null ? 0 : Math.Max(0, (int)Math.Round(detailsRowStyle.Height));
    }

    private int GetDetailsExpansionHeight()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var frameHeight = Math.Max(0, Height - ClientSize.Height);
        var maxClientHeight = Math.Max(MinimumWindowHeight, workingArea.Height - (WindowScreenMargin * 2) - frameHeight);
        var availableHeight = Math.Max(DetailsFooterHeight, maxClientHeight - ClientSize.Height);
        return Math.Min(DetailsExpandedHeight, availableHeight);
    }

    private void EnsureWindowVisible()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var x = Math.Clamp(Left, workingArea.Left + WindowScreenMargin, Math.Max(workingArea.Left + WindowScreenMargin, workingArea.Right - Width - WindowScreenMargin));
        var y = Math.Clamp(Top, workingArea.Top + WindowScreenMargin, Math.Max(workingArea.Top + WindowScreenMargin, workingArea.Bottom - Height - WindowScreenMargin));
        Location = new Point(x, y);
    }

    private string BuildDetailsText()
    {
        return DetailsTextFormatter.BuildSectionedText(
            BuildQueueSummaryLines(),
            BuildSelectedItemLines(GetSelectedQueueItem()),
            BuildQueueItemResultLines(),
            technicalLog.Text);
    }

    private QueueItem? GetSelectedQueueItem()
    {
        return queueGridView.SelectedRows.Count > 0 &&
               queueGridView.SelectedRows[0].Tag is QueueItem item &&
               queueItems.Contains(item)
            ? item
            : null;
    }

    private IReadOnlyList<QueueItem> GetSelectedQueueItems()
    {
        var selected = new List<QueueItem>();
        foreach (DataGridViewRow row in queueGridView.SelectedRows)
        {
            if (row.Tag is QueueItem item && queueItems.Contains(item) && !selected.Contains(item))
            {
                selected.Add(item);
            }
        }

        if (selected.Count == 0 &&
            queueGridView.CurrentRow?.Tag is QueueItem currentItem &&
            queueItems.Contains(currentItem))
        {
            selected.Add(currentItem);
        }

        return selected;
    }

    private bool CanRemoveQueueItem(QueueItem item)
    {
        return CanRemoveQueueItemForState(item, currentQueueItem, isQueueProcessing);
    }

    private void SelectQueueItem(QueueItem item)
    {
        foreach (DataGridViewRow row in queueGridView.Rows)
        {
            if (ReferenceEquals(row.Tag, item))
            {
                row.Selected = true;
                queueGridView.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    private void ClearQueueSelection()
    {
        if (queueGridView.SelectedRows.Count == 0 && queueGridView.CurrentCell is null)
        {
            return;
        }

        queueGridView.ClearSelection();
        queueGridView.CurrentCell = null;
        UpdateQueueButtonState();
        RefreshDetailsText(DetailsScrollMode.Preserve);
    }

    private List<string> BuildQueueSummaryLines()
    {
        var completed = queueItems.Count(item => item.Status == QueueItemStatus.Completed);
        var failed = queueItems.Count(item => item.Status is QueueItemStatus.Failed or QueueItemStatus.Invalid or QueueItemStatus.Unsupported);
        var exists = queueItems.Count(item => item.Status == QueueItemStatus.Skipped);
        var canceled = queueItems.Count(item => item.Status == QueueItemStatus.Canceled);
        var settings = lastQueueRunSettings is not null
            ? $"{FormatQueueSettings(lastQueueRunSettings)} | Destination: {FormatOutputDestinationMode(lastQueueRunSettings.OutputDestinationMode)}"
            : $"{FormatQueueSettings(CaptureCurrentQueueSettings())} | Destination: {FormatOutputDestinationMode(selectionState.OutputDestinationMode)}";

        var lines = new List<string>
        {
            $"Status: {(isQueueProcessing ? "Running" : lastQueueRunStatus)}",
            $"Completed: {completed}",
            $"Failed: {failed}",
            $"Exists: {exists}",
            $"Canceled: {canceled}",
            $"Total queued: {queueItems.Count}",
            $"Settings: {settings}"
        };

        if (!ffmpegTools.AreAvailable)
        {
            lines.Add(MissingToolsStatusMessage);
            lines.Add(MissingToolsExplanationMessage);
        }

        return lines;
    }

    private List<string> BuildSelectedItemLines(QueueItem? item)
    {
        return SelectedItemDetailsFormatter.BuildLines(item);
    }

    private List<string> BuildQueueItemResultLines()
    {
        if (queueItems.Count == 0)
        {
            return new List<string> { "No queued items." };
        }

        var lines = new List<string>();
        for (var index = 0; index < queueItems.Count; index++)
        {
            lines.Add(QueueItemResultFormatter.BuildSummaryLine(queueItems[index], index + 1, queueItems.Count));
        }

        return lines;
    }

    private void SetDetailsText(string text, DetailsScrollMode scrollMode)
    {
        var displayText = DetailsTextFormatter.AddVisualBottomPadding(text);
        var wasAtBottom = IsDetailsScrolledToBottom();
        var scrollPosition = GetDetailsScrollPosition();
        var preserveSelection = statusLogTextBox.Focused && statusLogTextBox.SelectionLength > 0;
        var selectionStart = statusLogTextBox.SelectionStart;
        var selectionLength = statusLogTextBox.SelectionLength;

        statusLogTextBox.SuspendLayout();
        SetDetailsRedraw(enabled: false);

        try
        {
            statusLogTextBox.Text = displayText;

            if (preserveSelection)
            {
                statusLogTextBox.Select(
                    Math.Min(selectionStart, statusLogTextBox.TextLength),
                    Math.Min(selectionLength, Math.Max(0, statusLogTextBox.TextLength - selectionStart)));
            }
            else if (scrollMode == DetailsScrollMode.Bottom || wasAtBottom)
            {
                statusLogTextBox.Select(statusLogTextBox.TextLength, 0);
            }

            if (scrollMode == DetailsScrollMode.Bottom || wasAtBottom)
            {
                ScrollDetailsToBottom();
            }
            else
            {
                SetDetailsScrollPosition(scrollPosition);
            }
        }
        finally
        {
            SetDetailsRedraw(enabled: true);
            statusLogTextBox.ResumeLayout();
            statusLogTextBox.Invalidate();
        }
    }

    private bool IsDetailsScrolledToBottom()
    {
        if (statusLogTextBox.TextLength == 0)
        {
            return true;
        }

        var lastCharacterPosition = statusLogTextBox.GetPositionFromCharIndex(statusLogTextBox.TextLength);
        return lastCharacterPosition.Y <= statusLogTextBox.ClientSize.Height;
    }

    private Point GetDetailsScrollPosition()
    {
        var point = new NativePoint();
        SendMessage(statusLogTextBox.Handle, EmGetScrollPos, IntPtr.Zero, ref point);
        return new Point(point.X, point.Y);
    }

    private void SetDetailsScrollPosition(Point position)
    {
        var point = new NativePoint(position.X, position.Y);
        SendMessage(statusLogTextBox.Handle, EmSetScrollPos, IntPtr.Zero, ref point);
    }

    private void ScrollDetailsToBottom()
    {
        statusLogTextBox.Select(statusLogTextBox.TextLength, 0);
        statusLogTextBox.ScrollToCaret();
    }

    private void SetDetailsRedraw(bool enabled)
    {
        SendMessage(statusLogTextBox.Handle, WmSetRedraw, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
    }

    private void SetRootRedraw(bool enabled)
    {
        if (IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void UpdateConvertButtonState()
    {
        convertButton.Enabled =
            ffmpegTools.AreAvailable &&
            selectionState.IsInputFileValid &&
            selectionState.IsOutputFolderValid &&
            selectionState.IsPlannedOutputPathValid &&
            selectionState.IsProbeValid &&
            !selectionState.IsProbeRunning &&
            !isConversionRunning;
    }

    private OutputFormat GetSelectedOutputFormat()
    {
        return OutputFormatExtensions.Parse(outputFormatComboBox.SelectedItem?.ToString());
    }

    private static OutputFormat ParseOutputFormatDisplay(string? value)
    {
        return OutputFormatExtensions.Parse(value);
    }

    private string GetSelectedConversionMode()
    {
        return ParseConversionModeDisplay(conversionModeComboBox.SelectedItem?.ToString());
    }

    private string GetSelectedConversionModeForDisplay()
    {
        return FormatConversionModeForDisplay(GetSelectedConversionMode());
    }

    private static string ParseConversionModeDisplay(string? value)
    {
        return ConversionModes.ParseDisplay(value);
    }

    private static string FormatConversionModeForDisplay(string? conversionMode)
    {
        return ConversionModes.FormatDisplay(conversionMode);
    }

    private string GetSelectedFrameRate()
    {
        return frameRateComboBox.SelectedItem?.ToString() ?? AutoDetectFpsLabel;
    }

    private FpsOption GetSelectedFpsOption()
    {
        if (IsAutoDetectFps(GetSelectedFrameRate()))
        {
            return FpsOption.FromLabel("30");
        }

        return FpsOption.FromLabel(GetSelectedFrameRate());
    }

    private QueueItemFpsSettings GetSelectedFpsSettings()
    {
        var selected = GetSelectedFrameRate();
        return IsAutoDetectFps(selected)
            ? QueueItemFpsSettings.AutoDetect()
            : QueueItemFpsSettings.FromManual(FpsOption.FromLabel(selected));
    }

    private static QueueItemFpsSettings BuildFpsSettingsFromDisplay(string? selected)
    {
        return IsAutoDetectFps(selected)
            ? QueueItemFpsSettings.AutoDetect()
            : QueueItemFpsSettings.FromManual(FpsOption.FromLabel(selected));
    }

    private static string FormatFpsSettingForEditor(QueueItemFpsSettings settings)
    {
        return settings.SelectionMode == FpsSelectionMode.AutoDetect
            ? AutoDetectFpsLabel
            : settings.RequestedDisplayValue;
    }

    private static bool AreFpsSettingsEquivalent(QueueItemFpsSettings left, QueueItemFpsSettings right)
    {
        return left.SelectionMode == right.SelectionMode &&
               string.Equals(left.RequestedDisplayValue, right.RequestedDisplayValue, StringComparison.Ordinal) &&
               string.Equals(left.ManualFfmpegRateValue, right.ManualFfmpegRateValue, StringComparison.Ordinal);
    }

    private static bool IsAutoDetectFps(string? value)
    {
        return string.Equals(value, AutoDetectFpsLabel, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase);
    }

    private static OutputDestinationMode ParseOutputDestinationMode(string? value)
    {
        return Enum.TryParse<OutputDestinationMode>(value, out var mode)
            ? mode
            : OutputDestinationMode.SameFolderAsSource;
    }

    private static string FormatOutputDestinationMode(OutputDestinationMode mode)
    {
        return mode == OutputDestinationMode.ChooseOutputFolder ? "Choose output folder" : "Same folder as source file";
    }

    private void SaveCurrentSettings()
    {
        if (isInitializing)
        {
            return;
        }

        appSettings.OutputDestinationMode = selectionState.OutputDestinationMode.ToString();
        appSettings.LastChosenOutputFolder = selectionState.ChosenOutputFolderPath ?? string.Empty;
        appSettings.OutputFormat = GetSelectedOutputFormat().DisplayName();
        appSettings.ConversionMode = GetSelectedConversionMode();
        appSettings.Fps = GetSelectedFrameRate();
        appSettings.WindowWidth = WindowState == FormWindowState.Normal ? Width : appSettings.WindowWidth;
        appSettings.WindowHeight = WindowState == FormWindowState.Normal
            ? Math.Max(MinimumWindowHeight, Height - (areDetailsVisible ? GetCurrentDetailsRowHeight() : 0))
            : appSettings.WindowHeight;

        if (!appSettingsService.Save(appSettings, out var errorMessage) && !string.IsNullOrWhiteSpace(errorMessage))
        {
            technicalLog.Append(errorMessage);
        }
    }

    private string FormatProbeStatus()
    {
        if (selectionState.IsProbeRunning)
        {
            return "Running";
        }

        if (selectionState.IsProbeValid)
        {
            return "Succeeded";
        }

        return selectionState.IsInputFileValid ? "Not validated" : "Waiting for valid input";
    }

    private string FormatConversionStatus()
    {
        if (isConversionRunning)
        {
            return "Running";
        }

        if (lastConversionResult?.IsSuccess == true)
        {
            return "Succeeded";
        }

        if (lastConversionResult?.IsSuccess == false)
        {
            return "Failed";
        }

        return "Idle";
    }

    private static string FormatQueueItemProbeStatus(QueueItem item)
    {
        if (item.Status == QueueItemStatus.Probing)
        {
            return "Running";
        }

        return item.PreProbeResult is null
            ? "Not validated"
            : item.PreProbeResult.IsSuccess ? "Succeeded" : "Failed";
    }

    private static string FormatQueueItemConversionStatus(QueueItem item)
    {
        return item.Status switch
        {
            QueueItemStatus.Converting => "Running",
            QueueItemStatus.Completed => "Completed",
            QueueItemStatus.Canceled => "Canceled",
            QueueItemStatus.Failed or QueueItemStatus.Invalid => "Failed",
            QueueItemStatus.Unsupported => "Skipped",
            QueueItemStatus.Skipped => "Skipped",
            _ => "Not started"
        };
    }

    private static string FormatQueueItemFpsNote(QueueItem item)
    {
        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            return item.FpsValidationMessage ?? "Auto-detect could not determine the source FPS. Double-click this row and choose Source FPS.";
        }

        if (!string.IsNullOrWhiteSpace(item.FpsWarning))
        {
            return item.FpsWarning;
        }

        if (!string.IsNullOrWhiteSpace(item.FpsDecisionReason))
        {
            return item.FpsDecisionReason;
        }

        return item.FpsSelectionMode == FpsSelectionMode.AutoDetect
            ? "Detected from Spotter frame records."
            : "Manual FPS selection.";
    }

    private static void AddProbeResultLines(List<string> lines, ProbeResult result)
    {
        lines.Add("Probe details:");
        lines.Add($"Tool path: {result.ToolPath}");
        lines.Add($"Selected source FPS: {result.Fps.Label} (FFmpeg value: {result.Fps.FfmpegValue})");

        if (result.IsSuccess)
        {
            lines.Add($"Codec: {FormatOptionalValue(result.CodecName, "Unknown")}");
            lines.Add($"Profile: {FormatOptionalValue(result.Profile, "Unknown")}");
            lines.Add($"Resolution: {FormatResolution(result.Width, result.Height)}");
            lines.Add($"Pixel format: {FormatOptionalValue(result.PixelFormat, "Unknown")}");
            lines.Add($"Stream frame rate: {FormatOptionalValue(result.RFrameRate, "Unknown")}");
            lines.Add($"Average frame rate: {FormatOptionalValue(result.AvgFrameRate, "Unknown")}");
            lines.Add($"Approx. duration: {FormatOptionalValue(result.Duration, "Unknown")}");
        }
        else
        {
            lines.Add("Technical details are available in the log below.");
        }
    }

    private static void AddQueueItemDetailsLines(List<string> lines, QueueItem item)
    {
        lines.Add("Selected queue item:");
        lines.Add($"Status: {QueueItemResultFormatter.GetStatusSummary(item)}");
        lines.Add($"Input: {item.InputPath}");
        lines.Add($"Output: {QueueItemResultFormatter.GetOutputSummary(item)}");
        lines.Add($"Output format: {item.OutputFormat.DisplayName()}");
        lines.Add($"Mode: {FormatConversionModeForDisplay(item.ConversionMode)}");
        lines.Add($"Selected source FPS: {(item.RequiresManualFpsSelection || !item.HasResolvedFps ? "Needs manual selection" : item.FpsDisplayLabel)}");
        lines.Add($"FFmpeg FPS value: {(item.RequiresManualFpsSelection || !item.HasResolvedFps ? "Not set" : item.FfmpegRateValue)}");
        lines.Add($"FPS confidence: {FormatOptionalValue(item.RequiresManualFpsSelection || !item.HasResolvedFps ? "Unavailable" : item.FpsConfidence, "Unknown")}");
        lines.Add($"FPS note: {FormatQueueItemFpsNote(item)}");
        lines.Add($"Export segment: {item.MultiFileExportContext?.DisplayText ?? "None detected"}");

        if (item.PreProbeResult is not null)
        {
            AddProbeResultLines(lines, item.PreProbeResult);
        }

        if (item.ConversionResult is not null)
        {
            AddConversionResultLines(lines, item.ConversionResult);
        }
    }

    private static void AddConversionResultLines(List<string> lines, ConversionResult result)
    {
        lines.Add("Conversion details:");
        lines.Add(result.StatusSummary);
        lines.Add($"Tool path: {result.FfmpegPath}");
        lines.Add($"Command: {result.CommandLine}");
        lines.Add($"Mode: {FormatConversionModeForDisplay(result.ConversionMode)}");
        lines.Add($"Output format: {FormatOptionalValue(result.OutputFormat, "Unknown")}");
        lines.Add($"Input: {result.InputPath}");
        lines.Add($"Output: {result.OutputPath}");
        lines.Add($"Selected source FPS: {result.Fps.Label} (FFmpeg value: {result.Fps.FfmpegValue})");
        lines.Add($"Duration available: {FormatYesNo(result.Duration.HasValue)}");
        lines.Add($"Duration value: {FormatDuration(result.Duration)}");
        lines.Add($"Progress mode: {(result.UsedDeterminateProgress ? "Determinate" : "Indeterminate")}");
        lines.Add($"Canceled: {FormatYesNo(result.WasCanceled)}");
        lines.Add($"Timed out: {FormatYesNo(result.TimedOut)}");
        lines.Add($"Exit code: {FormatExitCode(result.ExitCode)}");

        if (!string.IsNullOrWhiteSpace(result.PartialOutputMessage))
        {
            lines.Add(result.PartialOutputMessage);
        }

        AddTelemetryLines(lines, result.Telemetry);
        lines.Add("FFmpeg stdout/stderr are available in the log below.");
    }

    private static void AddCurrentProgressLines(List<string> lines, ConversionProgress progress)
    {
        lines.Add("Current progress:");
        lines.Add($"Progress summary: {progress.Summary}");
        lines.Add($"Output time: {FormatDuration(progress.OutputTime)}");
        lines.Add($"Frame: {FormatOptionalValue(progress.Frame, "Unknown")}");
        lines.Add($"FPS: {FormatOptionalValue(progress.Fps, "Unknown")}");
        lines.Add($"Speed: {FormatOptionalValue(progress.Speed, "Unknown")}");
        lines.Add($"Bitrate: {FormatOptionalValue(progress.Bitrate, "Unknown")}");
        lines.Add($"Total size: {FormatOptionalValue(progress.TotalSize, "Unknown")}");
        lines.Add($"Duplicated frames: {FormatOptionalValue(progress.DupFrames, "Unknown")}");
        lines.Add($"Dropped frames: {FormatOptionalValue(progress.DropFrames, "Unknown")}");
    }

    private void AddDurationLines(List<string> lines)
    {
        var duration = GetProbeDuration();
        lines.Add($"Duration available: {FormatYesNo(duration.HasValue)}");
        lines.Add($"Duration value: {FormatDuration(duration)}");
        lines.Add($"Progress mode: {(duration.HasValue ? "Determinate" : "Indeterminate")}");
    }

    private void AppendProbeResultToLog(ProbeResult result)
    {
        technicalLog.Append($"Probe {(result.IsSuccess ? "succeeded" : "failed")}. Tool: {result.ToolPath}; Selected source FPS: {result.Fps.Label} ({result.Fps.FfmpegValue}); Codec: {FormatOptionalValue(result.CodecName, "unknown")}; Profile: {FormatOptionalValue(result.Profile, "unknown")}; Resolution: {FormatResolution(result.Width, result.Height)}; Pixel format: {FormatOptionalValue(result.PixelFormat, "unknown")}; Duration: {FormatOptionalValue(result.Duration, "unknown")}.");

        if (!result.IsSuccess)
        {
            technicalLog.AppendBlock("Probe technical details", result.TechnicalDetails);
        }
    }

    private void AppendQueueItemFpsTechnicalLog(QueueItem item, string title)
    {
        if (!string.IsNullOrWhiteSpace(item.FpsTechnicalLogText))
        {
            technicalLog.AppendBlock(title, item.FpsTechnicalLogText);
        }
    }

    private void AppendMultiFileExportTechnicalLog(string? technicalLogText)
    {
        if (!string.IsNullOrWhiteSpace(technicalLogText))
        {
            technicalLog.AppendBlock("Queue item multi-file export detection", technicalLogText);
        }
    }

    private void AppendConversionResultToLog(ConversionResult result, bool includeStatusSummary = true)
    {
        var statusPrefix = includeStatusSummary ? $"{result.StatusSummary}. " : "Conversion details. ";
        technicalLog.Append($"{statusPrefix}Mode: {FormatConversionModeForDisplay(result.ConversionMode)}; Format: {result.OutputFormat}; Output: {result.OutputPath}; Exit code: {FormatExitCode(result.ExitCode)}; Canceled: {FormatYesNo(result.WasCanceled)}; Timed out: {FormatYesNo(result.TimedOut)}; Duration available: {FormatYesNo(result.Duration.HasValue)}; Progress mode: {(result.UsedDeterminateProgress ? "Determinate" : "Indeterminate")}.");
        technicalLog.Append($"FFmpeg command: {result.CommandLine}");
        var telemetrySummary = BuildTelemetrySummary(result.Telemetry);
        if (!string.IsNullOrWhiteSpace(telemetrySummary))
        {
            technicalLog.AppendBlock("Conversion Telemetry", telemetrySummary);
        }

        if (IsMp4RemuxResult(result))
        {
            technicalLog.Append("Fast MP4 compatibility options: video-only stream mapping, avc1 tag, zero-based timestamps, 90k timescale, faststart.");
        }

        if (!string.IsNullOrWhiteSpace(result.PartialOutputMessage))
        {
            technicalLog.Append($"Partial output handling: {result.PartialOutputMessage}");
        }

        if (!result.IsSuccess || result.WasCanceled || result.TimedOut)
        {
            technicalLog.AppendBlock("FFmpeg stdout", result.StandardOutput);
            technicalLog.AppendBlock("FFmpeg stderr", result.StandardError);
        }
    }

    private static bool IsMp4RemuxResult(ConversionResult result)
    {
        return (string.Equals(result.ConversionMode, "Remux", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ConversionMode, "Fast", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(result.OutputFormat, OutputFormat.Mp4.DisplayName(), StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTelemetryLines(List<string> lines, ConversionTelemetry? telemetry)
    {
        if (telemetry is null)
        {
            return;
        }

        lines.Add("Conversion telemetry:");
        lines.AddRange(BuildTelemetrySummaryLines(telemetry));
    }

    private static string BuildTelemetrySummary(ConversionTelemetry? telemetry)
    {
        return telemetry is null
            ? ""
            : string.Join(Environment.NewLine, BuildTelemetrySummaryLines(telemetry));
    }

    private static IReadOnlyList<string> BuildTelemetrySummaryLines(ConversionTelemetry telemetry)
    {
        var lines = new List<string>
        {
            $"Mode/container: {FormatConversionModeForDisplay(telemetry.ConversionMode)} / {FormatOptionalValue(telemetry.OutputContainer, "Unknown")}",
            $"Encoder: {FormatOptionalValue(telemetry.EncoderFamily, "Unknown")}; preset={FormatOptionalValue(telemetry.EncoderPreset, "Unknown")}; {FormatOptionalValue(telemetry.QualityMode, "quality")}={FormatOptionalValue(telemetry.QualityValue, "Unknown")}; NVENC available={FormatNullableYesNo(telemetry.NvencAvailable)}",
            $"Input size: {FormatFileSize(telemetry.InputFileSizeBytes)}; Output size: {FormatFileSize(telemetry.OutputFileSizeBytes)}; Compression ratio: {FormatDouble(telemetry.CompressionRatio, "0.###")}",
            $"Duration: {(telemetry.DurationAvailable == true ? ConversionTelemetry.FormatSeconds(telemetry.DurationSeconds) + " s" : "Unknown")}; Elapsed: {FormatDuration(telemetry.ElapsedConversionTime)}; Average encode speed: {FormatSpeedX(telemetry.AverageEncodeSpeed)}",
            $"Final FFmpeg progress: frame={FormatOptionalValue(telemetry.FinalReportedFrame, "Unknown")}; fps={FormatOptionalValue(telemetry.FinalReportedFps, "Unknown")}; speed={FormatOptionalValue(telemetry.FinalReportedSpeed, "Unknown")}; bitrate={FormatOptionalValue(telemetry.ReportedBitrate, "Unknown")}; total_size={FormatOptionalValue(telemetry.FinalReportedTotalSize, "Unknown")}; dup={FormatOptionalValue(telemetry.FinalReportedDupFrames, "Unknown")}; drop={FormatOptionalValue(telemetry.FinalReportedDropFrames, "Unknown")}; output time={FormatDuration(telemetry.FinalOutputTime)}",
            $"Computed output bitrate: {FormatBitrate(telemetry.ComputedOutputBitrateKbps)}; Selected FPS: {FormatOptionalValue(telemetry.SelectedFpsLabel, "Unknown")} ({FormatOptionalValue(telemetry.SelectedFfmpegFpsValue, "Unknown")})",
            $"Source: codec={FormatOptionalValue(telemetry.Codec, "Unknown")}; profile={FormatOptionalValue(telemetry.Profile, "Unknown")}; resolution={FormatResolution(telemetry.Width, telemetry.Height)}; pixel format={FormatOptionalValue(telemetry.PixelFormat, "Unknown")}",
            $"Flags: trim={FormatNullableYesNo(telemetry.TrimUsed)}; burn timestamp={FormatNullableYesNo(telemetry.BurnTimestampUsed)}; split export={FormatNullableYesNo(telemetry.SplitExportUsed)}; exit code={FormatExitCode(telemetry.ExitCode)}; succeeded={FormatNullableYesNo(telemetry.Succeeded)}; canceled={FormatNullableYesNo(telemetry.Canceled)}"
        };

        if (!string.IsNullOrWhiteSpace(telemetry.FfmpegVersionLine))
        {
            lines.Add($"FFmpeg version: {telemetry.FfmpegVersionLine}");
        }

        if (!string.IsNullOrWhiteSpace(telemetry.FfmpegConfigurationLine))
        {
            lines.Add($"FFmpeg configuration: {telemetry.FfmpegConfigurationLine}");
        }

        if (!string.IsNullOrWhiteSpace(telemetry.Libx264VersionLine))
        {
            lines.Add($"libx264: {telemetry.Libx264VersionLine}");
        }

        if (telemetry.X264SummaryLines?.Count > 0)
        {
            lines.Add("x264 summary:");
            lines.AddRange(telemetry.X264SummaryLines);
        }

        return lines;
    }

    private static string FormatExitCode(int? exitCode)
    {
        return exitCode?.ToString() ?? "none";
    }

    private TimeSpan? GetProbeDuration()
    {
        var duration = selectionState.LastProbeResult?.Duration;
        if (string.IsNullOrWhiteSpace(duration) || string.Equals(duration, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (double.TryParse(duration, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(duration, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : null;
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "Unknown";
        }

        return duration.Value.TotalHours >= 1
            ? duration.Value.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : duration.Value.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatNullableYesNo(bool? value)
    {
        return value.HasValue ? FormatYesNo(value.Value) : "Unknown";
    }

    private static string FormatDouble(double? value, string format)
    {
        return value.HasValue
            ? value.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture)
            : "Unknown";
    }

    private static string FormatSpeedX(double? value)
    {
        return value.HasValue
            ? $"{value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}x"
            : "Unknown";
    }

    private static string FormatBitrate(double? kbps)
    {
        return kbps.HasValue
            ? $"{kbps.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} kb/s"
            : "Unknown";
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static string FormatResolution(int? width, int? height)
    {
        return width.HasValue && height.HasValue ? $"{width.Value}x{height.Value}" : "Unknown";
    }

    private static string FormatOptionalValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} bytes";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024D:0.##} KB";
        }

        return $"{bytes / 1024D / 1024D:0.##} MB";
    }

    private static string FormatFileSize(long? bytes)
    {
        return bytes.HasValue ? FormatFileSize(bytes.Value) : "Unknown";
    }

    private static void AddMessageLines(List<string> lines, string message)
    {
        lines.AddRange(message.Replace("\r\n", "\n").Split('\n'));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (isConversionRunning)
        {
            var result = MessageBox.Show(
                this,
                "A conversion is currently running. Cancel the conversion before closing DAT Converter?",
                "Conversion Running",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            e.Cancel = true;

            if (result == DialogResult.Yes)
            {
                CancelQueueButton_Click(this, EventArgs.Empty);
            }

            return;
        }

        SaveCurrentSettings();
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (deferredStartupCompleted)
        {
            return;
        }

        deferredStartupCompleted = true;
        BeginInvoke(new Action(CompleteDeferredStartupUiWork));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelCurrentProbe();
            conversionCancellationTokenSource?.Cancel();
            conversionCancellationTokenSource?.Dispose();
            boldStatusFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void CompleteDeferredStartupUiWork()
    {
        LoadHeaderLogoIfAvailable();
        RegisterQueueDeselectHandlers(this);
        ApplyQueueAutoFitColumnWidths();
        ApplyMinimumUsableWindowSize();
        ReportDebugLayoutIssues();
        startupStopwatch.Stop();
        technicalLog.Append($"Startup UI finalized after first paint. Elapsed: {startupStopwatch.ElapsedMilliseconds} ms.");
    }

    private void ApplyMinimumUsableWindowSize()
    {
        var minimumClientSize = GetMinimumUsableClientSize();
        var frameWidth = Math.Max(0, Width - ClientSize.Width);
        var frameHeight = Math.Max(0, Height - ClientSize.Height);
        minimumClientSize = new Size(
            Math.Min(minimumClientSize.Width, Math.Max(0, DefaultWindowWidth - frameWidth)),
            Math.Min(minimumClientSize.Height, Math.Max(0, DefaultWindowHeight - frameHeight)));
        var minimumWindowSize = SizeFromClientSize(minimumClientSize);
        minimumWindowSize.Width = Math.Min(DefaultWindowWidth, Math.Max(MinimumWindowWidth, minimumWindowSize.Width));
        minimumWindowSize.Height = Math.Min(DefaultWindowHeight, Math.Max(MinimumWindowHeight, minimumWindowSize.Height));
        MinimumSize = minimumWindowSize;

        if (WindowState == FormWindowState.Normal &&
            (ClientSize.Width < minimumClientSize.Width || ClientSize.Height < minimumClientSize.Height))
        {
            ClientSize = new Size(
                Math.Max(ClientSize.Width, minimumClientSize.Width),
                Math.Max(ClientSize.Height, minimumClientSize.Height));
        }
    }

    private Size GetMinimumUsableClientSize()
    {
        var width = Math.Min(DefaultWindowWidth, Math.Max(MinimumFlexibleContentWidth, GetMinimumQueueHeaderWidth() + 24));
        var height =
            rootLayout?.Padding.Vertical ?? 0;

        height += 44;
        height += FileSelectionRowHeight;
        height += GetBatchOptionsMinimumHeight();
        height += 34;
        height += MinimumQueueGridHeight;
        height += ActionRowHeight;
        height += ActionRowHeight;
        height += Math.Max(28, conversionProgressBar.PreferredSize.Height);
        height += Math.Max(34, currentStatusLabel.PreferredHeight);

        return new Size(width, height);
    }

    private int GetBatchOptionsMinimumHeight()
    {
        if (rootLayout?.GetControlFromPosition(0, 2) is { } optionsPanel)
        {
            return Math.Max(MinimumBatchOptionsRowHeight, optionsPanel.MinimumSize.Height);
        }

        return MinimumBatchOptionsRowHeight;
    }

    private int GetMinimumQueueHeaderWidth()
    {
        if (queueGridView.Columns.Count == 0)
        {
            return MinimumFlexibleContentWidth;
        }

        var width = 0;
        foreach (DataGridViewColumn column in queueGridView.Columns)
        {
            width += GetQueueColumnPreferredWidth(column.Name, column.Width);
        }

        return width;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void ReportDebugLayoutIssues()
    {
        var issues = new List<string>();
        CollectLayoutIssues(this, issues);
        foreach (var issue in issues)
        {
            System.Diagnostics.Debug.WriteLine(issue);
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void CollectLayoutIssues(Control parent, List<string> issues)
    {
        if (!parent.Visible || parent.ClientSize.Width <= 0 || parent.ClientSize.Height <= 0)
        {
            return;
        }

        foreach (Control child in parent.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            if (!parent.ClientRectangle.Contains(child.Bounds))
            {
                issues.Add($"Layout issue: {DescribeControl(child)} bounds {child.Bounds} exceed parent {DescribeControl(parent)} client {parent.ClientRectangle}.");
            }

            if (child is ComboBox comboBox && child.Height < comboBox.MinimumSize.Height)
            {
                issues.Add($"Layout issue: {DescribeControl(child)} height {child.Height} is smaller than minimum combo height {comboBox.MinimumSize.Height}.");
            }

            if (child is Label label && !label.AutoSize && child.Height < label.GetPreferredSize(new Size(child.Width, int.MaxValue)).Height)
            {
                issues.Add($"Layout issue: {DescribeControl(child)} height {child.Height} is smaller than preferred label height.");
            }

            CollectLayoutIssues(child, issues);
        }
    }

    private static string DescribeControl(Control control)
    {
        var name = string.IsNullOrWhiteSpace(control.Name) ? "<unnamed>" : control.Name;
        var text = string.IsNullOrWhiteSpace(control.Text) ? "" : $" \"{control.Text}\"";
        return $"{control.GetType().Name} {name}{text}";
    }

    private void LoadHeaderLogoIfAvailable()
    {
        if (headerLogoPictureBox is null || headerLogoPictureBox.Image is not null)
        {
            return;
        }

        var logoImage = TryLoadHeaderLogo();
        if (logoImage is null)
        {
            return;
        }

        headerLogoPictureBox.Image = logoImage;
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 10
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FileSelectionRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        detailsRowStyle = new RowStyle(SizeType.Absolute, 0);
        root.RowStyles.Add(detailsRowStyle);
        rootLayout = root;

        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildFileSelectionPanel(), 0, 1);
        root.Controls.Add(BuildOptionsPanel(), 0, 2);
        root.Controls.Add(BuildQueueSettingsNotePanel(), 0, 3);
        root.Controls.Add(queueGridView, 0, 4);
        root.Controls.Add(BuildQueueActionPanel(), 0, 5);
        root.Controls.Add(BuildActionPanel(), 0, 6);
        root.Controls.Add(conversionProgressBar, 0, 7);
        root.Controls.Add(currentStatusLabel, 0, 8);
        detailsPanel = BuildDetailsPanel();
        detailsPanel.Visible = false;
        root.Controls.Add(detailsPanel, 0, 9);

        return root;
    }

    private Control BuildHeaderPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        headerLogoPictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 2, 8, 2)
        };
        panel.Controls.Add(headerLogoPictureBox, 0, 0);

        var headerLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "DAT Converter",
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };

        panel.Controls.Add(headerLabel, 1, 0);
        return panel;
    }

    private Control BuildFileSelectionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        panel.Controls.Add(CreateLabel("DAT file:"), 0, 0);
        panel.Controls.Add(selectedFilePathTextBox, 1, 0);
        panel.Controls.Add(BuildFileButtonPanel(), 2, 0);
        panel.Controls.Add(CreateLabel("Output:"), 0, 1);
        panel.Controls.Add(BuildOutputDestinationPanel(), 1, 1);
        panel.SetColumnSpan(panel.GetControlFromPosition(1, 1)!, 2);
        panel.Controls.Add(CreateLabel("Output folder:"), 0, 2);
        panel.Controls.Add(outputFolderTextBox, 1, 2);
        panel.Controls.Add(browseOutputFolderButton, 2, 2);

        return panel;
    }

    private Control BuildFileButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };

        SetButtonWidth(browseFileButton, 148);
        SetButtonWidth(addFolderButton, 166);
        browseFileButton.Margin = new Padding(0, 2, 8, 2);
        addFolderButton.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(browseFileButton);
        panel.Controls.Add(addFolderButton);
        return panel;
    }

    private Control BuildOutputDestinationPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };

        panel.Controls.Add(sameFolderRadioButton);
        panel.Controls.Add(chooseFolderRadioButton);
        return panel;
    }

    private Control BuildOptionsPanel()
    {
        var groupBox = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Text = "Batch Options",
            Padding = new Padding(14, 18, 14, 12)
        };

        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 2, 0, 0)
        };

        panel.Controls.Add(BuildOptionStack("Format", outputFormatComboBox, 190));
        panel.Controls.Add(BuildOptionStack("Mode", conversionModeComboBox, 210));
        panel.Controls.Add(BuildOptionStack("Source FPS", frameRateComboBox, 190));

        groupBox.Controls.Add(panel);
        groupBox.MinimumSize = new Size(0, GetMinimumBatchOptionsHeight(groupBox, panel));
        return groupBox;
    }

    private Control BuildQueueSettingsNotePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var helpLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Double-click a row to change filename or save location.",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, 2, 0, 2)
        };

        var noteLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Option choices lock when queue starts.",
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 2, 0, 2)
        };

        panel.Controls.Add(helpLabel, 0, 0);
        panel.Controls.Add(noteLabel, 1, 0);
        return panel;
    }

    private Control BuildActionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(0, 3, 0, 3)
        };

        for (var column = 0; column < 5; column++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddActionButtonToGrid(panel, removeSelectedQueueItemButton, 0);
        AddActionButtonToGrid(panel, clearCompletedQueueButton, 1);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 2, 0);
        AddActionButtonToGrid(panel, showDetailsButton, 3);
        AddActionButtonToGrid(panel, openOutputFolderButton, 4);
        return panel;
    }

    private Control BuildQueueActionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(0, 3, 0, 3)
        };

        for (var column = 0; column < 5; column++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddActionButtonToGrid(panel, cancelButton, 0);
        AddActionButtonToGrid(panel, stopAfterCurrentButton, 1);
        AddActionButtonToGrid(panel, duplicateSelectedQueueItemButton, 2);
        AddActionButtonToGrid(panel, cancelQueueButton, 3);
        AddActionButtonToGrid(panel, startQueueButton, 4);
        return panel;
    }

    private static void AddActionButtonToGrid(TableLayoutPanel panel, Button button, int column)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 2, 0, 2);
        EnsureButtonMinimumSize(button);
        panel.Controls.Add(button, column, 0);
    }

    private Control BuildDetailsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        panel.SuspendLayout();
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, DetailsFooterHeight));

        panel.Controls.Add(statusLogTextBox, 0, 0);
        panel.Controls.Add(BuildDetailsButtonPanel(), 0, 1);
        panel.ResumeLayout(false);
        return panel;
    }

    private Control BuildDetailsButtonPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddActionButtonToGrid(panel, wordWrapButton, 0);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
        AddActionButtonToGrid(panel, clearLogButton, 2);
        AddActionButtonToGrid(panel, copyLogButton, 3);
        return panel;
    }

    private static Label CreateLabel(string text)
    {
        var label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        label.MinimumSize = new Size(0, GetLabelPreferredHeight(label));
        return label;
    }

    private static Control BuildOptionStack(string labelText, ComboBox comboBox, int comboWidth)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = comboWidth,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 70, 0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = CreateOptionLabel(labelText);
        var labelHeight = Math.Max(label.GetPreferredSize(new Size(comboWidth, int.MaxValue)).Height, 22);
        label.AutoSize = true;
        label.Dock = DockStyle.Top;
        label.MinimumSize = new Size(comboWidth, labelHeight);
        label.Margin = new Padding(0, 0, 0, 4);

        ApplyPreferredComboBoxHeight(comboBox, comboWidth);
        comboBox.Dock = DockStyle.Top;
        comboBox.Margin = new Padding(0, 2, 0, 2);

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(comboBox, 0, 1);
        panel.MinimumSize = new Size(comboWidth, GetMinimumBatchOptionStackHeight(label, comboBox));
        return panel;
    }

    private static Label CreateOptionLabel(string text)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.None,
            Margin = new Padding(0, 0, 0, 2),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static int GetLabelPreferredHeight(Label label)
    {
        return Math.Max(22, label.GetPreferredSize(new Size(Math.Max(1, label.Width), int.MaxValue)).Height);
    }

    private static void ApplyPreferredComboBoxHeight(ComboBox comboBox, int comboWidth)
    {
        var preferredHeight = Math.Max(comboBox.PreferredHeight, TextRenderer.MeasureText("Mg", comboBox.Font).Height + 8);
        comboBox.Size = new Size(comboWidth, preferredHeight);
        comboBox.MinimumSize = new Size(comboWidth, preferredHeight);
    }

    private static int GetMinimumBatchOptionStackHeight(Label label, ComboBox comboBox)
    {
        return Math.Max(
            MinimumBatchOptionStackHeight,
            label.MinimumSize.Height + label.Margin.Vertical + comboBox.MinimumSize.Height + comboBox.Margin.Vertical);
    }

    private static int GetMinimumBatchOptionsHeight(GroupBox groupBox, FlowLayoutPanel panel)
    {
        var tallestStack = panel.Controls
            .Cast<Control>()
            .Select(control => control.MinimumSize.Height + control.Margin.Vertical)
            .DefaultIfEmpty(MinimumBatchOptionStackHeight)
            .Max();

        return Math.Max(
            MinimumBatchOptionsRowHeight,
            groupBox.Padding.Vertical + panel.Padding.Vertical + tallestStack + SystemInformation.BorderSize.Height);
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            AutoSize = false,
            Size = new Size(112, 42),
            Margin = new Padding(6, 4, 0, 4),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = true
        };
        EnsureButtonMinimumSize(button);
        return button;
    }

    private static void SetButtonWidth(Button button, int width)
    {
        EnsureButtonMinimumSize(button);
        button.Size = new Size(width, button.MinimumSize.Height);
        button.MinimumSize = new Size(Math.Min(width, button.MinimumSize.Width), button.MinimumSize.Height);
    }

    private static void EnsureButtonMinimumSize(Button button)
    {
        var preferredHeight = Math.Max(42, button.GetPreferredSize(Size.Empty).Height + 4);
        button.MinimumSize = new Size(0, preferredHeight);
        if (button.Height < preferredHeight)
        {
            button.Height = preferredHeight;
        }
    }

    private void RegisterQueueDeselectHandlers(Control control)
    {
        if (ReferenceEquals(control, queueGridView) ||
            ReferenceEquals(control, removeSelectedQueueItemButton) ||
            ReferenceEquals(control, duplicateSelectedQueueItemButton) ||
            ReferenceEquals(control, openOutputFolderButton) ||
            ReferenceEquals(control, detailsPanel) ||
            ReferenceEquals(control, copyLogButton))
        {
            return;
        }

        control.MouseDown += OutsideQueueControl_MouseDown;
        foreach (Control child in control.Controls)
        {
            RegisterQueueDeselectHandlers(child);
        }
    }

    private void ConfigureConversionModeComboBox(ComboBox comboBox)
    {
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.ItemHeight = Math.Max(comboBox.ItemHeight, TextRenderer.MeasureText("Full NVENC", comboBox.Font).Height + 4);
        ApplyPreferredComboBoxHeight(comboBox, comboBox.Width > 0 ? comboBox.Width : comboBox.MinimumSize.Width);
        comboBox.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= comboBox.Items.Count)
            {
                return;
            }

            var text = comboBox.Items[e.Index]?.ToString() ?? string.Empty;
            var disabled = !nvencCapability.IsAvailable &&
                string.Equals(text, ConversionModes.FullNvencDisplayName, StringComparison.OrdinalIgnoreCase);
            var color = disabled ? SystemColors.GrayText : e.ForeColor;
            TextRenderer.DrawText(e.Graphics, text, e.Font ?? comboBox.Font, e.Bounds, color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            e.DrawFocusRectangle();
        };
    }

    private bool GuardUnavailableNvencSelection(ComboBox comboBox)
    {
        if (nvencCapability.IsAvailable ||
            !string.Equals(comboBox.SelectedItem?.ToString(), ConversionModes.FullNvencDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        comboBox.SelectedItem = ConversionModes.FullDisplayName;
        technicalLog.Append($"Full NVENC unavailable. {nvencCapability.DiagnosticSummary}");
        return true;
    }

    private static ComboBox CreateComboBox(string[] items, string selectedItem)
    {
        var comboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 6, 18, 4)
        };

        comboBox.Items.AddRange(items);
        comboBox.SelectedItem = selectedItem;
        if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
        return comboBox;
    }

    private static DataGridView CreateQueueGridView()
    {
        var gridView = new BufferedDataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            Dock = DockStyle.Fill,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            ScrollBars = ScrollBars.Vertical,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        gridView.RowTemplate.Height = 28;

        gridView.Columns.Add(CreateQueueColumn("Status", "Status", QueueStatusColumnWidth));
        gridView.Columns.Add(CreateQueueColumn("File", "File", QueueFileColumnWidth));
        gridView.Columns.Add(CreateQueueColumn("Output", "Output", QueueOutputColumnWidth));
        gridView.Columns.Add(CreateQueueColumn("Format", "Format", QueueFormatColumnWidth));
        gridView.Columns.Add(CreateQueueColumn("Mode", "Mode", QueueModeColumnWidth));
        gridView.Columns.Add(CreateQueueColumn("Fps", "FPS", QueueFpsColumnWidth));
        gridView.Columns.Add(CreateQueueColumn("Resolution", "Resolution", QueueResolutionColumnWidth));
        gridView.Columns.Add(CreateQueueColumn("Progress", "Progress", QueueProgressColumnWidth));
        return gridView;
    }

    private static DataGridViewTextBoxColumn CreateQueueColumn(string name, string headerText, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            MinimumWidth = Math.Min(width, 70),
            Resizable = DataGridViewTriState.True,
            Width = width
        };
    }

    private sealed class BufferedDataGridView : DataGridView
    {
        public BufferedDataGridView()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private static TextBox CreateReadOnlyTextBox(string text)
    {
        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Text = text,
            Margin = new Padding(0, 6, 12, 4)
        };
        textBox.MinimumSize = new Size(0, textBox.PreferredHeight);
        return textBox;
    }
}
