namespace DatConverter;

public sealed class MainForm : Form
{
    private const int DetailsExpandedHeight = 280;
    private const int DefaultClientWidth = 960;
    private const int DefaultClientHeight = 820;

    private const string MissingToolsStatusMessage = "Required FFmpeg tools were not found.";
    private const string MissingToolsExplanationMessage =
        "Usually this means DAT Converter was opened without its bundled tools folder. Run the app from within the original DAT Converter folder.";
    private const string MissingToolsDetailsMessage =
        MissingToolsStatusMessage + "\r\n" +
        MissingToolsExplanationMessage;
    private const string AddWhileRunningWarningMessage =
        "Files added while the queue is running will use the current queue settings.";

    private readonly TextBox selectedFilePathTextBox;
    private readonly Button browseFileButton;
    private readonly Button addFolderButton;
    private readonly CheckBox skipExistingOutputCheckBox;
    private readonly DataGridView queueGridView;
    private readonly Button startQueueButton;
    private readonly Button stopAfterCurrentButton;
    private readonly Button removeSelectedQueueItemButton;
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
    private readonly Button copyLogButton;
    private readonly Button clearLogButton;
    private readonly FfmpegTools ffmpegTools;
    private readonly ProbeService probeService;
    private readonly ConversionService conversionService;
    private readonly AppSettingsService appSettingsService;
    private readonly AppSettings appSettings;
    private readonly TechnicalLogBuffer technicalLog = new();
    private readonly FileSelectionState selectionState = new();
    private readonly List<QueueItem> queueItems = new();
    private CancellationTokenSource? probeCancellationTokenSource;
    private int probeSequence;
    private bool isConversionRunning;
    private bool isQueueProcessing;
    private bool isQueuePreProbeRunning;
    private bool stopAfterCurrentRequested;
    private bool areDetailsVisible;
    private TableLayoutPanel? rootLayout;
    private RowStyle? detailsRowStyle;
    private Control? detailsPanel;
    private QueueItem? currentQueueItem;
    private QueueSettingsSnapshot? activeQueueSettings;
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

    public MainForm()
    {
        ffmpegTools = ToolPathService.ResolveBundledTools();
        probeService = new ProbeService(ffmpegTools);
        conversionService = new ConversionService(ffmpegTools);
        appSettingsService = new AppSettingsService();
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
        MinimumSize = new Size(DefaultClientWidth, DefaultClientHeight);
        ClientSize = new Size(
            DefaultClientWidth,
            DefaultClientHeight);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        isInitializing = true;
        selectedFilePathTextBox = CreateReadOnlyTextBox("No .dat file selected");
        browseFileButton = CreateButton("Add Files...");
        browseFileButton.Size = new Size(148, 42);
        addFolderButton = CreateButton("Add Folder...");
        addFolderButton.Size = new Size(166, 42);
        skipExistingOutputCheckBox = new CheckBox
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Checked = true,
            Text = "Skip files that already have selected output format",
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 0, 4),
            Padding = new Padding(2, 0, 0, 0)
        };
        queueGridView = CreateQueueGridView();
        startQueueButton = CreateButton("Start Queue");
        startQueueButton.Size = new Size(160, 42);
        stopAfterCurrentButton = CreateButton("Stop After Current");
        stopAfterCurrentButton.Size = new Size(230, 42);
        removeSelectedQueueItemButton = CreateButton("Clear All");
        removeSelectedQueueItemButton.Size = new Size(216, 42);
        clearCompletedQueueButton = CreateButton("Clear Completed");
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
        conversionModeComboBox = CreateComboBox(new[] { "Remux", "Encode" }, appSettings.ConversionMode);
        frameRateComboBox = CreateComboBox(new[] { "15", "20", "24", "25", "29.97", "30" }, appSettings.Fps);
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
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = currentUserStatus,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            DetectUrls = false
        };
        openOutputFolderButton = CreateButton("Open Output Folder");
        openOutputFolderButton.Size = new Size(250, 42);
        copyLogButton = CreateButton("Copy Log");
        clearLogButton = CreateButton("Clear Log");

        convertButton.Enabled = false;
        convertButton.Visible = false;
        cancelButton.Enabled = false;
        openOutputFolderButton.Enabled = false;
        copyLogButton.Visible = false;
        clearLogButton.Visible = false;
        startQueueButton.Enabled = false;
        stopAfterCurrentButton.Enabled = false;
        removeSelectedQueueItemButton.Enabled = false;
        clearCompletedQueueButton.Enabled = false;
        browseFileButton.Enabled = ffmpegTools.AreAvailable;
        browseFileButton.Click += BrowseFileButton_Click;
        addFolderButton.Enabled = ffmpegTools.AreAvailable;
        addFolderButton.Click += AddFolderButton_Click;
        queueGridView.SelectionChanged += QueueGridView_SelectionChanged;
        startQueueButton.Click += StartQueueButton_Click;
        stopAfterCurrentButton.Click += StopAfterCurrentButton_Click;
        removeSelectedQueueItemButton.Click += RemoveSelectedQueueItemButton_Click;
        clearCompletedQueueButton.Click += ClearCompletedQueueButton_Click;
        sameFolderRadioButton.CheckedChanged += OutputDestinationRadioButton_CheckedChanged;
        chooseFolderRadioButton.CheckedChanged += OutputDestinationRadioButton_CheckedChanged;
        browseOutputFolderButton.Click += BrowseOutputFolderButton_Click;
        outputFormatComboBox.SelectedIndexChanged += OutputFormatComboBox_SelectedIndexChanged;
        conversionModeComboBox.SelectedIndexChanged += ConversionModeComboBox_SelectedIndexChanged;
        frameRateComboBox.SelectedIndexChanged += FrameRateComboBox_SelectedIndexChanged;
        skipExistingOutputCheckBox.CheckedChanged += SkipExistingOutputCheckBox_CheckedChanged;
        convertButton.Click += ConvertButton_Click;
        cancelButton.Click += CancelButton_Click;
        openOutputFolderButton.Click += OpenOutputFolderButton_Click;
        showDetailsButton.Click += ShowDetailsButton_Click;
        copyLogButton.Click += CopyLogButton_Click;
        clearLogButton.Click += ClearLogButton_Click;
        ResizeEnd += MainForm_ResizeEnd;

        Controls.Add(BuildLayout());
        InitializeTechnicalLog();
        technicalLog.Append(settingsLoadMessage);
        technicalLog.Append($"Output destination mode: {FormatOutputDestinationMode(selectionState.OutputDestinationMode)}.");
        isInitializing = false;
        ApplyOutputDestinationMode();
        ApplyStartupToolValidation();
    }

    public FfmpegTools FfmpegTools => ffmpegTools;

    private void InitializeTechnicalLog()
    {
        technicalLog.Append($"DAT Converter session started. Version: {TechnicalLogBuffer.GetAppVersion()}");
        technicalLog.Append($"Application base directory: {ffmpegTools.ApplicationBaseDirectory}");
        technicalLog.Append($"Checked ffmpeg path: {ffmpegTools.FfmpegPath} ({FormatFoundStatus(ffmpegTools.FfmpegExists)})");
        technicalLog.Append($"Checked ffprobe path: {ffmpegTools.FfprobePath} ({FormatFoundStatus(ffmpegTools.FfprobeExists)})");
    }

    private void ApplyStartupToolValidation()
    {
        RefreshStatusLog(ffmpegTools.AreAvailable
            ? "Ready. Bundled FFmpeg tools were found."
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

    private void BrowseFileButton_Click(object? sender, EventArgs e)
    {
        ShowAddWhileRunningWarningIfNeeded();

        using var dialog = new OpenFileDialog
        {
            Title = "Add DAT files",
            Filter = "DAT files (*.dat)|*.dat",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AddFilesToQueue(dialog.FileNames);
    }

    private async void AddFolderButton_Click(object? sender, EventArgs e)
    {
        ShowAddWhileRunningWarningIfNeeded();

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
            addFolderButton.Enabled = ffmpegTools.AreAvailable && (!isConversionRunning || isQueueProcessing);
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

        var addSettings = GetQueueAddSettings();
        var preview = CreateFolderQueueAddPreview(scanResult.DatFiles, addSettings);
        technicalLog.Append($"Folder scan completed. Folder: {folderPath}; Include subfolders: {FormatYesNo(includeSubfolders)}; Found: {scanResult.DatFiles.Count}; Selected-format outputs already present: {preview.AlreadyConvertedSkippedCount}; Duplicates: {preview.DuplicateCount}; Invalid: {preview.InvalidCount}; Output plan failures: {preview.OutputPlanFailedCount}; Will add: {preview.AddablePaths.Count}; Skipped/inaccessible folders: {scanResult.SkippedPaths.Count}.");

        var availableSlots = 100 - queueItems.Count;
        if (preview.AddablePaths.Count > availableSlots)
        {
            technicalLog.Append($"Folder scan add blocked by queue limit. Queue count: {queueItems.Count}; Available slots: {availableSlots}; Addable from scan: {preview.AddablePaths.Count}.");
            RefreshStatusLog(queueLimitMessage);
            MessageBox.Show(this, queueLimitMessage, "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (preview.AddablePaths.Count == 0)
        {
            var noAddMessage =
                $"Found {scanResult.DatFiles.Count} .dat file{(scanResult.DatFiles.Count == 1 ? "" : "s")}, but none can be added.\r\n\r\n" +
                $"Selected output format already exists: {preview.AlreadyConvertedSkippedCount}\r\n" +
                $"Duplicates already in queue: {preview.DuplicateCount}\r\n" +
                $"Invalid/skipped: {preview.InvalidCount + preview.OutputPlanFailedCount}";

            RefreshStatusLog("No files were added to the queue.");
            MessageBox.Show(this, noAddMessage, "Add Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmResult = ShowFolderQueuePreviewDialog(scanResult.DatFiles.Count, preview, addSettings);

        if (confirmResult != DialogResult.OK)
        {
            technicalLog.Append("Folder scan add canceled by user.");
            RefreshStatusLog("Folder scan canceled. No files were added.");
            return;
        }

        AddFilesToQueue(preview.AddablePaths);
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
                $"Duplicates already in queue: {preview.DuplicateCount}\r\n" +
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

    private void RemoveSelectedQueueItemButton_Click(object? sender, EventArgs e)
    {
        var removableItems = queueItems
            .Where(item => item != currentQueueItem &&
                           item.Status is not QueueItemStatus.Running and
                               not QueueItemStatus.Probing and
                               not QueueItemStatus.Converting)
            .ToList();

        foreach (var item in removableItems)
        {
            queueItems.Remove(item);
        }

        technicalLog.Append($"Cleared {removableItems.Count} queue item(s).");
        RefreshQueueGrid();
        RefreshStatusLog(removableItems.Count == 1 ? "Cleared one queue item." : $"Cleared {removableItems.Count} queue items.");
    }

    private void ClearCompletedQueueButton_Click(object? sender, EventArgs e)
    {
        var removedCount = queueItems.RemoveAll(item => item.Status == QueueItemStatus.Completed);
        technicalLog.Append($"Cleared {removedCount} completed queue item(s).");
        RefreshQueueGrid();
        RefreshStatusLog(removedCount == 1 ? "Cleared one completed item." : $"Cleared {removedCount} completed items.");
    }

    private void OutputDestinationRadioButton_CheckedChanged(object? sender, EventArgs e)
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
    }

    private void OutputFormatComboBox_SelectedIndexChanged(object? sender, EventArgs e)
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
    }

    private void ConversionModeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        technicalLog.Append($"Conversion mode changed to {GetSelectedConversionMode()}.");
        SaveCurrentSettings();
        RefreshStatusLog("Conversion mode changed.");
        UpdateConvertButtonState();
    }

    private async void FrameRateComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        selectionState.IsProbeValid = false;
        selectionState.LastProbeResult = null;
        var fps = GetSelectedFpsOption();
        technicalLog.Append($"Frame rate changed. Label: {fps.Label}; FFmpeg value: {fps.FfmpegValue}. Probe validation reset.");
        SaveCurrentSettings();
        RefreshStatusLog("Frame rate changed. Probe validation is required for the new FPS setting.");
        UpdateConvertButtonState();
        await PreProbeWaitingQueueItemsIfIdleAsync();
        await StartProbeIfReadyAsync();
    }

    private void SkipExistingOutputCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        technicalLog.Append($"Selected-output skip behavior changed for future queue additions. Skip existing selected output format: {FormatYesNo(skipExistingOutputCheckBox.Checked)}.");
        RefreshStatusLog("Existing-output behavior changed for newly added files.");
        UpdateConvertButtonState();
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

        cancelButton.Enabled = false;
        currentConversionHeadline = "Canceling conversion...";
        technicalLog.Append(isQueueProcessing ? "Cancel requested for running queue item. Queue will stop after cancellation." : "Cancel requested for running conversion.");
        RefreshStatusLog(currentConversionHeadline);
        conversionCancellationTokenSource?.Cancel();
    }

    private void OpenOutputFolderButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(lastSuccessfulOutputPath))
        {
            technicalLog.Append("Open output folder requested, but no successful output path is available.");
            RefreshStatusLog("Could not open the output folder.");
            return;
        }

        try
        {
            if (File.Exists(lastSuccessfulOutputPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{lastSuccessfulOutputPath}\"",
                    UseShellExecute = true
                });
                technicalLog.Append($"Opened Explorer selecting output file: {lastSuccessfulOutputPath}");
                return;
            }

            var folderPath = Path.GetDirectoryName(lastSuccessfulOutputPath);
            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
                technicalLog.Append($"Opened output folder: {folderPath}");
                return;
            }

            technicalLog.Append($"Open Output Folder failed: output path no longer exists and folder could not be opened. Path: {lastSuccessfulOutputPath}");
            RefreshStatusLog("Could not open the output folder.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            technicalLog.Append($"Open Output Folder failed: {ex}");
            RefreshStatusLog("Could not open the output folder.");
        }
    }

    private void ShowDetailsButton_Click(object? sender, EventArgs e)
    {
        SetDetailsVisible(!areDetailsVisible);
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

    private void ApplyOutputFolderSelection(string folderPath)
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
    }

    private async void AddFilesToQueue(IReadOnlyCollection<string> filePaths)
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
        var duplicateCount = 0;
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
            if (IsDuplicateQueuedJob(validation.FilePath, directOutputPath, newlyAddedItems))
            {
                duplicateCount++;
                technicalLog.Append($"Queue add skipped duplicate job. Input: {validation.FilePath}; Output: {directOutputPath}");
                continue;
            }

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

            var item = new QueueItem(
                validation.FilePath,
                plannedOutputPath,
                addSettings.OutputDestinationMode,
                addSettings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder ? outputFolderValidation.FolderPath : null,
                outputFormat,
                addSettings.ConversionMode,
                addSettings.Fps,
                hasExistingDirectOutput,
                addSettings.SkipIfDirectOutputExists);

            queueItems.Add(item);
            newlyAddedItems.Add(item);
            addedCount++;
            firstAddedPath ??= validation.FilePath;
            technicalLog.Append($"Queued file. Input: {item.InputPath}; Output: {item.PlannedOutputPath}; Status: {item.StatusText}; Format: {item.OutputFormat.DisplayName()}; Mode: {item.ConversionMode}; FPS: {item.Fps.Label} ({item.Fps.FfmpegValue}); Destination: {FormatOutputDestinationMode(item.OutputDestinationMode)}.");
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

        if (duplicateCount > 0)
        {
            summaryParts.Add($"Skipped {duplicateCount} duplicate file{(duplicateCount == 1 ? "" : "s")}.");
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
                : "Queue is running; new files were added using the active queue settings.");
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
                    .Where(item => item.Status == QueueItemStatus.WaitingForProbe)
                    .ToList();

                if (candidates.Count == 0)
                {
                    break;
                }

                RefreshStatusLog("Probing queued files...");
                technicalLog.Append($"Queue pre-probe started. Items: {candidates.Count}.");

                foreach (var item in candidates)
                {
                    if (!queueItems.Contains(item) || item.Status != QueueItemStatus.WaitingForProbe)
                    {
                        continue;
                    }

                    SetQueueItemStatus(item, QueueItemStatus.Probing, "Probing", "");
                    technicalLog.Append($"Queue pre-probe item. Input: {item.InputPath}; FPS: {item.Fps.Label} ({item.Fps.FfmpegValue}).");

                    var probeResult = await probeService.ProbeRawH264Async(item.InputPath, item.Fps, CancellationToken.None);
                    if (!queueItems.Contains(item))
                    {
                        continue;
                    }

                    item.PreProbeResult = probeResult;
                    validatedCount++;

                    if (probeResult.IsSuccess)
                    {
                        if (item.SkipIfDirectOutputExists && item.HasExistingDirectOutput)
                        {
                            selectedOutputExistsCount++;
                            SetQueueItemStatus(item, QueueItemStatus.Skipped, "Exists", "Selected output exists");
                        }
                        else
                        {
                            readyCount++;
                            var statusText = item.HasExistingDirectOutput ? "Already converted?" : "Ready";
                            var status = item.HasExistingDirectOutput ? QueueItemStatus.Warning : QueueItemStatus.Ready;
                            SetQueueItemStatus(item, status, statusText, FormatProbeProgressText(probeResult));
                        }

                        technicalLog.Append($"Queue pre-probe succeeded. Input: {item.InputPath}; Codec: {FormatOptionalValue(probeResult.CodecName, "unknown")}; Resolution: {FormatResolution(probeResult.Width, probeResult.Height)}; Profile: {FormatOptionalValue(probeResult.Profile, "unknown")}.");
                    }
                    else
                    {
                        unsupportedCount++;
                        SetQueueItemStatus(item, QueueItemStatus.Unsupported, "Unsupported", "Will not process");
                        technicalLog.Append($"Queue pre-probe failed. Input: {item.InputPath}; Message: {ProbeResult.UnsupportedMessage}");
                        technicalLog.AppendBlock("Queue pre-probe technical details", probeResult.TechnicalDetails);
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

    private FolderQueueAddPreview CreateFolderQueueAddPreview(IReadOnlyCollection<string> filePaths, QueueSettingsSnapshot addSettings)
    {
        var addablePaths = new List<string>();
        var addableJobs = new List<QueueAddCandidate>();
        var invalidCount = 0;
        var duplicateCount = 0;
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
            if (IsDuplicateQueuedJob(validation.FilePath, directOutputPath, addableJobs))
            {
                duplicateCount++;
                continue;
            }

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
            addableJobs.Add(new QueueAddCandidate(validation.FilePath, directOutputPath));
        }

        return new FolderQueueAddPreview(addablePaths, invalidCount, duplicateCount, alreadyConvertedSkippedCount, outputPlanFailedCount);
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
                : null,
            skipExistingOutputCheckBox.Checked);
    }

    private static string FormatQueueSettings(QueueSettingsSnapshot settings)
    {
        return $"Format: {settings.OutputFormat.DisplayName()} | Mode: {settings.ConversionMode} | Source FPS: {settings.Fps.Label}";
    }

    private void ShowAddWhileRunningWarningIfNeeded()
    {
        if (!isQueueProcessing || activeQueueSettings is null)
        {
            return;
        }

        var message = AddWhileRunningWarningMessage +
            "\r\n\r\n" +
            FormatQueueSettings(activeQueueSettings);
        MessageBox.Show(this, message, "Queue Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool IsDuplicateQueuedJob(string inputPath, string? directOutputPath, IEnumerable<QueueItem> newlyAddedItems)
    {
        return queueItems.Any(item => IsSameQueueJob(item.InputPath, GetDirectOutputPathForQueuedItem(item), inputPath, directOutputPath)) ||
               newlyAddedItems.Any(item => IsSameQueueJob(item.InputPath, GetDirectOutputPathForQueuedItem(item), inputPath, directOutputPath));
    }

    private bool IsDuplicateQueuedJob(string inputPath, string? directOutputPath, IEnumerable<QueueAddCandidate> newlyAddedJobs)
    {
        return queueItems.Any(item => IsSameQueueJob(item.InputPath, GetDirectOutputPathForQueuedItem(item), inputPath, directOutputPath)) ||
               newlyAddedJobs.Any(item => IsSameQueueJob(item.InputPath, item.DirectOutputPath, inputPath, directOutputPath));
    }

    private static bool IsSameQueueJob(string? existingInputPath, string? existingDirectOutputPath, string? newInputPath, string? newDirectOutputPath)
    {
        return !string.IsNullOrWhiteSpace(existingInputPath) &&
               !string.IsNullOrWhiteSpace(existingDirectOutputPath) &&
               !string.IsNullOrWhiteSpace(newInputPath) &&
               !string.IsNullOrWhiteSpace(newDirectOutputPath) &&
               string.Equals(existingInputPath, newInputPath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(existingDirectOutputPath, newDirectOutputPath, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetDirectOutputPathForQueuedItem(QueueItem item)
    {
        var outputFolderPath = item.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource
            ? Path.GetDirectoryName(item.InputPath)
            : item.SelectedOutputFolder;

        return OutputPathService.GetDirectOutputPath(item.InputPath, outputFolderPath, item.OutputFormat);
    }

    private string? PlanQueueOutputPath(string inputPath, string outputFolderPath, OutputFormat outputFormat, QueueItem? excludedItem = null)
    {
        var directOutputPath = OutputPathService.GetDirectOutputPath(inputPath, outputFolderPath, outputFormat);
        if (string.IsNullOrWhiteSpace(directOutputPath))
        {
            return null;
        }

        if (OutputPathService.IsSafeOutputPath(inputPath, directOutputPath) &&
            IsAvailableQueueOutputPath(directOutputPath, excludedItem))
        {
            return directOutputPath;
        }

        var inputBaseName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(directOutputPath);
        if (string.IsNullOrWhiteSpace(inputBaseName) || string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var convertedPath = Path.Combine(outputFolderPath, inputBaseName + "_converted" + extension);
        if (OutputPathService.IsSafeOutputPath(inputPath, convertedPath) &&
            IsAvailableQueueOutputPath(convertedPath, excludedItem))
        {
            return convertedPath;
        }

        for (var index = 1; index <= 9999; index++)
        {
            var candidatePath = Path.Combine(outputFolderPath, $"{inputBaseName}_{index:00}{extension}");
            if (OutputPathService.IsSafeOutputPath(inputPath, candidatePath) &&
                IsAvailableQueueOutputPath(candidatePath, excludedItem))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private bool IsAvailableQueueOutputPath(string outputPath, QueueItem? excludedItem = null)
    {
        return (excludedItem is null || OutputPathService.IsSafeOutputPath(excludedItem.InputPath, outputPath)) &&
               !File.Exists(outputPath) &&
               !queueItems.Any(item => item != excludedItem && string.Equals(item.PlannedOutputPath, outputPath, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyLockedQueueSettingsToQueuedItems(QueueSettingsSnapshot settings)
    {
        var lockedCount = 0;
        var failedCount = 0;

        foreach (var item in queueItems.Where(CanApplyLockedQueueSettings).ToList())
        {
            var outputFolderPath = ResolveActiveQueueOutputFolder(item.InputPath, settings);
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                failedCount++;
                item.Status = QueueItemStatus.Failed;
                item.StatusText = "Failed";
                item.ProgressText = "Output folder invalid";
                technicalLog.Append($"Queue settings lock failed for item because output destination is invalid. Input: {item.InputPath}; Output folder: {outputFolderPath}; Reason: {outputFolderValidation.Message}");
                continue;
            }

            var directOutputPath = OutputPathService.GetDirectOutputPath(item.InputPath, outputFolderValidation.FolderPath, settings.OutputFormat);
            var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
            var plannedOutputPath = PlanQueueOutputPath(item.InputPath, outputFolderValidation.FolderPath, settings.OutputFormat, item);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                failedCount++;
                item.Status = QueueItemStatus.Failed;
                item.StatusText = "Failed";
                item.ProgressText = "No safe output path";
                technicalLog.Append($"Queue settings lock failed for item because no safe output path could be planned. Input: {item.InputPath}; Output folder: {outputFolderValidation.FolderPath}; Format: {settings.OutputFormat.DisplayName()}");
                continue;
            }

            QueueSettingsLockService.ApplyLockedSettings(
                item,
                settings,
                outputFolderValidation.FolderPath,
                plannedOutputPath,
                hasExistingDirectOutput,
                item.PreProbeResult is null ? "Ready" : FormatProbeProgressText(item.PreProbeResult));
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
        return item.Status is QueueItemStatus.Ready or QueueItemStatus.Warning or QueueItemStatus.Skipped;
    }

    private void RefreshQueueGrid()
    {
        queueGridView.Rows.Clear();

        foreach (var item in queueItems)
        {
            var rowIndex = queueGridView.Rows.Add(
                item.StatusText,
                Path.GetFileName(item.InputPath),
                item.PlannedOutputPath,
                item.OutputFormat.DisplayName(),
                item.ConversionMode,
                item.Fps.Label,
                item.ProgressText);
            queueGridView.Rows[rowIndex].Tag = item;
            if (item.Status is QueueItemStatus.Probing or QueueItemStatus.Converting)
            {
                queueGridView.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(232, 244, 255);
            }
            else if (item.Status == QueueItemStatus.Ready)
            {
                queueGridView.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(240, 250, 240);
            }
            else if (item.Status == QueueItemStatus.Completed)
            {
                queueGridView.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(232, 248, 232);
            }
            else if (item.Status is QueueItemStatus.Failed or QueueItemStatus.Canceled or QueueItemStatus.Unsupported)
            {
                queueGridView.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
            }
            else if (item.Status == QueueItemStatus.Skipped || item.HasExistingDirectOutput)
            {
                queueGridView.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 225);
            }
        }

        queueGridView.ClearSelection();
        UpdateQueueButtonState();
    }

    private void UpdateQueueButtonState()
    {
        startQueueButton.Enabled = ffmpegTools.AreAvailable &&
                                   !isConversionRunning &&
                                   !isQueuePreProbeRunning &&
                                   !HasPendingQueueValidation() &&
                                   queueItems.Any(IsProcessableQueueItem);
        stopAfterCurrentButton.Enabled = isQueueProcessing &&
                                         !stopAfterCurrentRequested &&
                                         currentQueueItem is not null &&
                                         (currentQueueItem.Status is QueueItemStatus.Probing or QueueItemStatus.Converting);
        stopAfterCurrentButton.Text = stopAfterCurrentRequested ? "Stop Requested" : "Stop After Current";
        var allowQueueEditing = queueGridView.Enabled;
        removeSelectedQueueItemButton.Enabled = allowQueueEditing && queueItems.Any(item =>
            item != currentQueueItem &&
            item.Status is not QueueItemStatus.Running and not QueueItemStatus.Probing and not QueueItemStatus.Converting);
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

        CancelCurrentProbe();
        activeQueueSettings = CaptureCurrentQueueSettings();
        ApplyLockedQueueSettingsToQueuedItems(activeQueueSettings);

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
        technicalLog.Append($"Queue started. Processable items: {queueItems.Count(IsProcessableQueueItem)}; Total queued: {queueItems.Count}; Active settings: {FormatQueueSettings(activeQueueSettings)}; Destination: {FormatOutputDestinationMode(activeQueueSettings.OutputDestinationMode)}; Chosen folder: {FormatOptionalValue(activeQueueSettings.ChosenOutputFolder, "none")}; Skip existing selected format: {FormatYesNo(activeQueueSettings.SkipIfDirectOutputExists)}.");
        RefreshStatusLog($"Queue started. Processing 1 of {queueItems.Count(IsProcessableQueueItem)}.");

        var queueCanceled = false;
        var stoppedAfterCurrent = false;
        var ordinal = 0;
        var attemptedItems = new HashSet<QueueItem>();

        try
        {
            while (true)
            {
                if (conversionCancellationTokenSource.IsCancellationRequested)
                {
                    queueCanceled = true;
                    break;
                }

                var remainingProcessableItems = queueItems
                    .Where(item => !attemptedItems.Contains(item) && IsProcessableQueueItem(item))
                    .ToList();

                if (remainingProcessableItems.Count == 0)
                {
                    break;
                }

                var item = remainingProcessableItems[0];
                attemptedItems.Add(item);
                ordinal++;
                currentQueueItem = item;
                var dynamicTotal = ordinal + remainingProcessableItems.Count - 1;
                var result = await ProcessQueueItemAsync(item, ordinal, dynamicTotal, conversionCancellationTokenSource.Token);
                lastConversionResult = result;

                if (result?.WasCanceled == true || conversionCancellationTokenSource.IsCancellationRequested)
                {
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
            activeQueueSettings = null;
            conversionProgressBar.Style = ProgressBarStyle.Blocks;
            conversionProgressBar.MarqueeAnimationSpeed = 0;
            cancelButton.Enabled = false;
            stopAfterCurrentButton.Enabled = false;
            stopAfterCurrentButton.Text = "Stop After Current";
            conversionCancellationTokenSource?.Dispose();
            conversionCancellationTokenSource = null;
            SetControlsEnabledForConversion(true);

        }

        var summary = BuildQueueSummary(queueCanceled, stoppedAfterCurrent);
        technicalLog.Append(summary);
        RefreshQueueGrid();
        RefreshStatusLog(summary);
        UpdateConvertButtonState();

        if (queueItems.Any(item => item.Status == QueueItemStatus.WaitingForProbe))
        {
            await PreProbeWaitingQueueItemsIfIdleAsync();
        }
    }

    private async Task<ConversionResult?> ProcessQueueItemAsync(QueueItem item, int ordinal, int totalItems, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(item.InputPath);
        SetQueueItemStatus(item, QueueItemStatus.Probing, "Probing", "");
        technicalLog.Append($"Queue item start. {ordinal} of {totalItems}; Input: {item.InputPath}; Planned output: {item.PlannedOutputPath}; Format: {item.OutputFormat.DisplayName()}; Mode: {item.ConversionMode}; FPS: {item.Fps.Label} ({item.Fps.FfmpegValue}).");
        RefreshStatusLog($"Processing {ordinal} of {totalItems}: {fileName}");

        var inputValidation = InputFileValidator.ValidateDatFile(item.InputPath);
        if (!inputValidation.IsValid)
        {
            SetQueueItemStatus(item, QueueItemStatus.Failed, "Failed", inputValidation.Message);
            technicalLog.Append($"Queue item failed input revalidation. Input: {item.InputPath}; Reason: {inputValidation.Message}");
            return null;
        }

        var probeResult = await probeService.ProbeRawH264Async(item.InputPath, item.Fps, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            SetQueueItemStatus(item, QueueItemStatus.Canceled, "Canceled", "");
            technicalLog.Append($"Queue item canceled during probe. Input: {item.InputPath}");
            return new ConversionResult(false, ConversionResult.CanceledMessage, ffmpegTools.FfmpegPath, Array.Empty<string>(), item.InputPath, item.PlannedOutputPath, item.Fps, null, "", "", WasCanceled: true);
        }

        if (!probeResult.IsSuccess)
        {
            SetQueueItemStatus(item, QueueItemStatus.Unsupported, "Unsupported", "Will not process");
            technicalLog.Append($"Queue item probe failed. Input: {item.InputPath}; Message: {ProbeResult.UnsupportedMessage}");
            technicalLog.AppendBlock("Queue item probe technical details", probeResult.TechnicalDetails);
            return null;
        }

        technicalLog.Append($"Queue item probe succeeded. Input: {item.InputPath}; Codec: {FormatOptionalValue(probeResult.CodecName, "unknown")}; Resolution: {FormatResolution(probeResult.Width, probeResult.Height)}; Duration: {FormatOptionalValue(probeResult.Duration, "unknown")}.");

        var outputSafety = RecheckQueueOutputSafety(item);
        if (!outputSafety.CanConvert)
        {
            SetQueueItemStatus(item, outputSafety.Status, outputSafety.StatusText, outputSafety.ProgressText);
            technicalLog.Append($"Queue item skipped/failed output safety check. Input: {item.InputPath}; Reason: {outputSafety.LogMessage}");
            return null;
        }

        if (!string.Equals(item.PlannedOutputPath, outputSafety.OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            technicalLog.Append($"Queue item output path refreshed for safety. Old: {item.PlannedOutputPath}; New: {outputSafety.OutputPath}");
            item.PlannedOutputPath = outputSafety.OutputPath!;
        }

        var duration = ParseProbeDuration(probeResult.Duration);
        var hasDuration = duration.HasValue && duration.Value > TimeSpan.Zero;
        ConfigureProgressBarForConversion(hasDuration);
        conversionProgressBar.Value = 0;
        SetQueueItemStatus(item, QueueItemStatus.Converting, "Converting", hasDuration ? "0%" : "Processing");
        RefreshStatusLog($"Processing {ordinal} of {totalItems}: {fileName}");

        var progress = new Progress<ConversionProgress>(progressUpdate => UpdateQueueConversionProgress(item, progressUpdate));
        var conversionResult = string.Equals(item.ConversionMode, "Encode", StringComparison.OrdinalIgnoreCase)
            ? await conversionService.EncodeAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, progress, cancellationToken)
            : await conversionService.RemuxAsync(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps, duration, progress, cancellationToken);

        AppendConversionResultToLog(conversionResult);

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

        return conversionResult;
    }

    private bool IsProcessableQueueItem(QueueItem item)
    {
        return item.Status == QueueItemStatus.Ready ||
               (item.Status == QueueItemStatus.Warning && item.PreProbeResult?.IsSuccess == true);
    }

    private bool HasPendingQueueValidation()
    {
        return queueItems.Any(item => item.Status is QueueItemStatus.WaitingForProbe or QueueItemStatus.Probing);
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
        var outputFolderPath = item.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource
            ? Path.GetDirectoryName(item.InputPath)
            : item.SelectedOutputFolder;

        var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
        if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
        {
            return QueueOutputSafetyResult.Fail($"Output folder is invalid: {outputFolderValidation.Message}");
        }

        var directOutputPath = OutputPathService.GetDirectOutputPath(item.InputPath, outputFolderValidation.FolderPath, item.OutputFormat);
        var directOutputExists = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
        var plannedOutputIsDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) &&
                                          string.Equals(item.PlannedOutputPath, directOutputPath, StringComparison.OrdinalIgnoreCase);
        if (directOutputExists && item.SkipIfDirectOutputExists && (item.HasExistingDirectOutput || plannedOutputIsDirectOutput))
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

    private void UpdateQueueConversionProgress(QueueItem item, ConversionProgress progress)
    {
        lastConversionProgress = progress;

        if (progress.Percent.HasValue && conversionProgressBar.Style != ProgressBarStyle.Marquee)
        {
            conversionProgressBar.Value = Math.Clamp(progress.Percent.Value, conversionProgressBar.Minimum, conversionProgressBar.Maximum);
            item.ProgressText = $"{progress.Percent.Value}%";
        }
        else
        {
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
        int DuplicateCount,
        int AlreadyConvertedSkippedCount,
        int OutputPlanFailedCount);

    private sealed record QueueAddCandidate(string InputPath, string? DirectOutputPath);

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
        technicalLog.Append($"Starting conversion. Mode: {conversionMode}; Format: {outputFormat}; FPS: {fps.Label} ({fps.FfmpegValue}); Input: {inputPath}; Output: {outputPath}; Duration available: {FormatYesNo(hasDuration)}; Duration: {FormatDuration(duration)}; Progress mode: {(hasDuration ? "Determinate" : "Indeterminate")}.");

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
        currentConversionHeadline = $"Starting {conversionMode.ToLowerInvariant()}...";
        RefreshStatusLog(currentConversionHeadline);
        UpdateConvertButtonState();
        cancelButton.Enabled = true;

        var progress = new Progress<ConversionProgress>(progressUpdate => UpdateConversionProgress(conversionMode, progressUpdate));

        ConversionResult result;
        try
        {
            currentConversionHeadline = string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase) ? "Encoding..." : "Remuxing...";
            RefreshStatusLog(currentConversionHeadline);
            result = string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase)
                ? await conversionService.EncodeAsync(inputPath, outputPath, outputFormat, fps, duration, progress, conversionCancellationTokenSource.Token)
                : await conversionService.RemuxAsync(inputPath, outputPath, outputFormat, fps, duration, progress, conversionCancellationTokenSource.Token);
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
        addFolderButton.Enabled = allowQueueEditing && ffmpegTools.AreAvailable;
        skipExistingOutputCheckBox.Enabled = enabled;
        queueGridView.Enabled = allowQueueEditing;
        startQueueButton.Enabled = false;
        stopAfterCurrentButton.Enabled = false;
        removeSelectedQueueItemButton.Enabled = false;
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

        if (progress.Percent.HasValue && conversionProgressBar.Style != ProgressBarStyle.Marquee)
        {
            conversionProgressBar.Value = Math.Clamp(progress.Percent.Value, conversionProgressBar.Minimum, conversionProgressBar.Maximum);
        }

        var now = DateTime.UtcNow;
        if (!progress.IsEnd && (now - lastProgressUiUpdateUtc).TotalMilliseconds < 750)
        {
            return;
        }

        lastProgressUiUpdateUtc = now;
        var verb = string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase) ? "Encoding" : "Remuxing";
        currentConversionHeadline = progress.Percent.HasValue
            ? $"{verb}... {progress.Percent.Value}%"
            : $"{verb}... {progress.Summary}";
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
        RefreshDetailsText();
    }

    private void RefreshDetailsText()
    {
        if (areDetailsVisible)
        {
            SetDetailsText(BuildDetailsText());
        }
    }

    private void SetDetailsVisible(bool visible)
    {
        var wasVisible = areDetailsVisible;
        SuspendLayout();
        rootLayout?.SuspendLayout();
        detailsPanel?.SuspendLayout();

        areDetailsVisible = visible;
        showDetailsButton.Text = visible ? "Hide Details" : "Show Details";
        copyLogButton.Visible = visible;
        clearLogButton.Visible = visible;

        if (detailsPanel is not null)
        {
            detailsPanel.Visible = visible;
        }

        if (detailsRowStyle is not null)
        {
            detailsRowStyle.SizeType = SizeType.Absolute;
            detailsRowStyle.Height = visible ? DetailsExpandedHeight : 0;
        }

        if (visible)
        {
            SetDetailsText(BuildDetailsText());
        }

        if (visible != wasVisible)
        {
            Height += visible ? DetailsExpandedHeight : -DetailsExpandedHeight;
        }

        detailsPanel?.ResumeLayout(false);
        rootLayout?.ResumeLayout(false);
        ResumeLayout(false);
    }

    private string BuildDetailsText()
    {
        var lines = new List<string>();
        if (!ffmpegTools.AreAvailable && string.Equals(currentUserStatus, MissingToolsStatusMessage, StringComparison.Ordinal))
        {
            AddMessageLines(lines, MissingToolsDetailsMessage);
            lines.Add(string.Empty);
        }
        else
        {
            lines.Add(currentUserStatus);
            lines.Add(string.Empty);

            if (!ffmpegTools.AreAvailable)
            {
                AddMessageLines(lines, MissingToolsDetailsMessage);
                lines.Add(string.Empty);
            }
        }

        lines.Add($"Input file: {FormatOptionalValue(selectionState.SelectedInputFilePath, "None selected")}");
        lines.Add($"Queue items: {queueItems.Count}/100");
        lines.Add($"Output destination: {FormatOutputDestinationMode(selectionState.OutputDestinationMode)}");
        lines.Add($"Output folder: {FormatOptionalValue(selectionState.SelectedOutputFolderPath, selectionState.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource ? "Same folder as source file" : "None selected")}");
        lines.Add($"Planned output file: {FormatOptionalValue(selectionState.PlannedOutputFilePath, "Not planned yet")}");
        if (selectionState.IsInputFileValid && !selectionState.IsOutputFolderValid && selectionState.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource)
        {
            lines.Add("The source folder is not writable. Choose a different output folder.");
        }

        lines.Add($"Output format: {GetSelectedOutputFormat().DisplayName()}");
        lines.Add($"Mode: {GetSelectedConversionMode()}");
        var fps = GetSelectedFpsOption();
        lines.Add($"Selected source FPS: {fps.Label} (FFmpeg value: {fps.FfmpegValue})");
        lines.Add($"Probe status: {FormatProbeStatus()}");
        lines.Add($"Conversion status: {FormatConversionStatus()}");
        AddDurationLines(lines);

        if (selectionState.LastProbeResult is not null)
        {
            AddProbeResultLines(lines, selectionState.LastProbeResult);
        }

        if (lastConversionResult is not null)
        {
            AddConversionResultLines(lines, lastConversionResult);
        }
        else if (lastConversionProgress is not null)
        {
            AddCurrentProgressLines(lines, lastConversionProgress);
        }

        lines.Add(string.Empty);
        lines.Add("Technical log:");
        lines.Add(technicalLog.Text);

        return string.Join(Environment.NewLine, lines);
    }

    private void SetDetailsText(string text)
    {
        statusLogTextBox.SuspendLayout();

        if (!ffmpegTools.AreAvailable &&
            string.Equals(currentUserStatus, MissingToolsStatusMessage, StringComparison.Ordinal) &&
            text.StartsWith(MissingToolsStatusMessage, StringComparison.Ordinal))
        {
            statusLogTextBox.Clear();
            statusLogTextBox.SelectionFont = boldStatusFont;
            statusLogTextBox.AppendText(MissingToolsStatusMessage);
            statusLogTextBox.SelectionFont = statusLogTextBox.Font;
            statusLogTextBox.AppendText(Environment.NewLine);
            statusLogTextBox.AppendText(MissingToolsExplanationMessage);

            var remainingText = text[MissingToolsDetailsMessage.Length..].TrimStart('\r', '\n');
            if (!string.IsNullOrWhiteSpace(remainingText))
            {
                statusLogTextBox.AppendText(Environment.NewLine);
                statusLogTextBox.AppendText(Environment.NewLine);
                statusLogTextBox.AppendText(remainingText);
            }
        }
        else
        {
            statusLogTextBox.Text = text;
            statusLogTextBox.SelectAll();
            statusLogTextBox.SelectionFont = statusLogTextBox.Font;
        }

        statusLogTextBox.Select(0, 0);
        statusLogTextBox.ResumeLayout();
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

    private string GetSelectedConversionMode()
    {
        return conversionModeComboBox.SelectedItem?.ToString() ?? "Remux";
    }

    private string GetSelectedFrameRate()
    {
        return frameRateComboBox.SelectedItem?.ToString() ?? "30";
    }

    private FpsOption GetSelectedFpsOption()
    {
        return FpsOption.FromLabel(GetSelectedFrameRate());
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
        appSettings.Fps = GetSelectedFpsOption().Label;
        appSettings.WindowWidth = DefaultClientWidth;
        appSettings.WindowHeight = DefaultClientHeight;

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

    private static void AddConversionResultLines(List<string> lines, ConversionResult result)
    {
        lines.Add("Conversion details:");
        lines.Add($"Tool path: {result.FfmpegPath}");
        lines.Add($"Command: {result.CommandLine}");
        lines.Add($"Conversion mode: {FormatOptionalValue(result.ConversionMode, "Unknown")}");
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

        lines.Add("FFmpeg stdout/stderr are available in the log below.");
    }

    private static void AddCurrentProgressLines(List<string> lines, ConversionProgress progress)
    {
        lines.Add("Current progress:");
        lines.Add($"Progress summary: {progress.Summary}");
        lines.Add($"Output time: {FormatDuration(progress.OutputTime)}");
        lines.Add($"Frame: {FormatOptionalValue(progress.Frame, "Unknown")}");
        lines.Add($"Speed: {FormatOptionalValue(progress.Speed, "Unknown")}");
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

    private void AppendConversionResultToLog(ConversionResult result)
    {
        technicalLog.Append($"Conversion {(result.IsSuccess ? "succeeded" : result.WasCanceled ? "canceled" : "failed")}. Mode: {result.ConversionMode}; Format: {result.OutputFormat}; Output: {result.OutputPath}; Exit code: {FormatExitCode(result.ExitCode)}; Canceled: {FormatYesNo(result.WasCanceled)}; Timed out: {FormatYesNo(result.TimedOut)}; Duration available: {FormatYesNo(result.Duration.HasValue)}; Progress mode: {(result.UsedDeterminateProgress ? "Determinate" : "Indeterminate")}.");
        technicalLog.Append($"FFmpeg command: {result.CommandLine}");

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
                CancelButton_Click(this, EventArgs.Empty);
            }

            return;
        }

        SaveCurrentSettings();
        base.OnFormClosing(e);
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

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 10
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var logoImage = TryLoadHeaderLogo();
        if (logoImage is not null)
        {
            var logoBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = logoImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 2, 10, 4)
            };
            panel.Controls.Add(logoBox, 0, 0);
        }

        var headerLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "DAT Converter",
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
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
            Padding = new Padding(0, 2, 0, 2)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
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

        browseFileButton.Size = new Size(148, 42);
        addFolderButton.Size = new Size(166, 42);
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
            Dock = DockStyle.Fill,
            Text = "Options",
            Padding = new Padding(14, 28, 14, 14)
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 14, 0, 0)
        };

        panel.Controls.Add(BuildOptionStack("Format", outputFormatComboBox, 190));
        panel.Controls.Add(BuildOptionStack("Mode", conversionModeComboBox, 210));
        panel.Controls.Add(BuildOptionStack("Source FPS", frameRateComboBox, 190));

        groupBox.Controls.Add(panel);
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

        var noteLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Queue items keep the settings they were added with.",
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 4, 0, 4)
        };

        panel.Controls.Add(skipExistingOutputCheckBox, 0, 0);
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
            Padding = new Padding(0, 4, 0, 4)
        };

        for (var column = 0; column < 5; column++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddActionButtonToGrid(panel, copyLogButton, 0);
        AddActionButtonToGrid(panel, clearLogButton, 1);
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
            Padding = new Padding(0, 4, 0, 4)
        };

        for (var column = 0; column < 5; column++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddActionButtonToGrid(panel, removeSelectedQueueItemButton, 0);
        AddActionButtonToGrid(panel, clearCompletedQueueButton, 1);
        AddActionButtonToGrid(panel, stopAfterCurrentButton, 2);
        AddActionButtonToGrid(panel, cancelButton, 3);
        AddActionButtonToGrid(panel, startQueueButton, 4);
        return panel;
    }

    private static void AddActionButtonToGrid(TableLayoutPanel panel, Button button, int column)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 2, 0, 2);
        panel.Controls.Add(button, column, 0);
    }

    private Control BuildDetailsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0)
        };
        panel.SuspendLayout();
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(statusLogTextBox, 0, 0);
        panel.ResumeLayout(false);
        return panel;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Control BuildOptionStack(string labelText, ComboBox comboBox, int comboWidth)
    {
        var panel = new Panel
        {
            Width = comboWidth,
            Height = 96,
            Margin = new Padding(0, 0, 70, 0)
        };

        var label = CreateOptionLabel(labelText);
        label.Location = new Point(0, 0);
        label.Size = new Size(comboWidth, 26);

        comboBox.Dock = DockStyle.None;
        comboBox.Location = new Point(0, 38);
        comboBox.Size = new Size(comboWidth, 30);
        comboBox.MinimumSize = new Size(comboWidth, 30);
        comboBox.Margin = new Padding(0);

        panel.Controls.Add(label);
        panel.Controls.Add(comboBox);
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

    private static Button CreateButton(string text)
    {
        return new Button
        {
            AutoSize = false,
            Size = new Size(112, 42),
            Margin = new Padding(6, 4, 0, 4),
            Text = text,
            UseVisualStyleBackColor = true
        };
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
        return comboBox;
    }

    private static DataGridView CreateQueueGridView()
    {
        var gridView = new BufferedDataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            Dock = DockStyle.Fill,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        gridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 70 });
        gridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "File", HeaderText = "File", FillWeight = 130 });
        gridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Output", HeaderText = "Output", FillWeight = 180 });
        gridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Format", HeaderText = "Format", FillWeight = 45 });
        gridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mode", HeaderText = "Mode", FillWeight = 55 });
        gridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Fps", HeaderText = "FPS", FillWeight = 45 });
        gridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Progress", HeaderText = "Progress", FillWeight = 70 });
        return gridView;
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
        return new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Text = text,
            Margin = new Padding(0, 6, 12, 4)
        };
    }
}
