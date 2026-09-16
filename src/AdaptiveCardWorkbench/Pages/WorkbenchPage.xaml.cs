using System.Text;
using System.Text.Json;
using AdaptiveCardWorkbench.Models;
using AdaptiveCardWorkbench.Services;
using AdaptiveCards.ObjectModel.WinUI3;
using AdaptiveCards.Rendering.WinUI3;
using AdaptiveCards.Templating;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WinUIEditor;
using VirtualKey = Windows.System.VirtualKey;

namespace AdaptiveCardWorkbench.Pages;

public sealed partial class WorkbenchPage : Page
{
    private readonly AdaptiveCardRenderer _renderer = new();
    private readonly AdaptiveHostConfig _lightHostConfig = new();
    private readonly AdaptiveHostConfig _darkHostConfig = AdaptiveHostConfig.FromJsonString(
        """
        {
          "containerStyles": {
            "default": {
              "backgroundColor": "#202020",
              "foregroundColors": {
                "default": { "default": "#FFFFFF", "subtle": "#C5FFFFFF" },
                "accent": { "default": "#75B6FF", "subtle": "#C575B6FF" },
                "dark": { "default": "#1B1B1B", "subtle": "#991B1B1B" },
                "light": { "default": "#FFFFFF", "subtle": "#C5FFFFFF" },
                "good": { "default": "#6CCB5F", "subtle": "#C56CCB5F" },
                "warning": { "default": "#FCE100", "subtle": "#C5FCE100" },
                "attention": { "default": "#FF99A4", "subtle": "#C5FF99A4" }
              }
            },
            "emphasis": {
              "backgroundColor": "#2D2D2D",
              "foregroundColors": {
                "default": { "default": "#FFFFFF", "subtle": "#C5FFFFFF" },
                "accent": { "default": "#75B6FF", "subtle": "#C575B6FF" },
                "dark": { "default": "#1B1B1B", "subtle": "#991B1B1B" },
                "light": { "default": "#FFFFFF", "subtle": "#C5FFFFFF" },
                "good": { "default": "#6CCB5F", "subtle": "#C56CCB5F" },
                "warning": { "default": "#FCE100", "subtle": "#C5FCE100" },
                "attention": { "default": "#FF99A4", "subtle": "#C5FF99A4" }
              }
            }
          },
          "separator": {
            "lineColor": "#33FFFFFF"
          }
        }
        """).HostConfig;
    private readonly DispatcherQueueTimer _renderTimer;
    private readonly DispatcherQueueTimer _saveTimer;

    private CardDocument? _activeDocument;
    private RenderedAdaptiveCard? _renderedCard;
    private long _editorRevision;
    private long _saveRequestId;
    private bool _editorsInitialized;
    private bool _isLoadingEditors;
    private bool _isLoaded;
    private bool _useSplitLayout = true;
    private double _wideEditorRatio = 0.53;
    private double _stackedEditorRatio = 0.5;
    private double _payloadEditorRatio = 0.5;
    private double _outputPanelHeight = 150;
    private bool _isOutputPanelCollapsed;
    private bool _lastRenderFailed;
    private readonly List<string> _renderProblems = [];
    private string? _saveProblem;
    private string? _formatProblem;
    private MaximizedPane _maximizedPane;

    private enum MaximizedPane
    {
        None,
        Payload,
        Data,
        Preview
    }

    private enum WorkbenchStatusKind
    {
        Neutral,
        Success,
        Warning,
        Error
    }

    public WorkbenchPage()
    {
        InitializeComponent();
        OutputSelectorBar.SelectedItem = OutputSelectorBarItem;

        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(350);
        _renderTimer.IsRepeating = false;
        _renderTimer.Tick += (_, _) => RenderPreview();

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(900);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += async (_, _) => await SaveDraftAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        InitializeEditors();
        App.Workspace.ActiveDocumentChanged += Workspace_ActiveDocumentChanged;
        App.EditorPreferences.Changed += EditorPreferences_Changed;
        await App.Workspace.InitializeAsync();
        LoadActiveDocument();
        ApplyEditorPreferences();
    }

    private async void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _renderTimer.Stop();
        _saveTimer.Stop();

        App.Workspace.ActiveDocumentChanged -= Workspace_ActiveDocumentChanged;
        App.EditorPreferences.Changed -= EditorPreferences_Changed;
        DetachRenderedCard();
        await SaveDraftAsync();
    }

    private void Workspace_ActiveDocumentChanged(object? sender, EventArgs e)
    {
        LoadActiveDocument();
    }

    private void LoadActiveDocument()
    {
        _editorRevision++;
        _formatProblem = null;
        CardDocument? document = App.Workspace.ActiveDocument;
        if (document is null)
        {
            _activeDocument = null;
            DetachRenderedCard();
            ExpandedJsonText.Text = string.Empty;
            SetDocumentWorkspaceVisible(false);
            SaveStatusIcon.Glyph = "\uE73E";
            SaveStatusText.Text = "No card open";
            UpdateRenderStatus(
                "No card open",
                WorkbenchStatusKind.Neutral);
            return;
        }

        SetDocumentWorkspaceVisible(true);
        SaveStatusIcon.Glyph = "\uE73E";
        SaveStatusText.Text = "Draft loaded";
        _activeDocument = document;
        _isLoadingEditors = true;
        LoadEditorText(PayloadEditor, document.PayloadJson);
        LoadEditorText(DataEditor, document.DataJson);
        _isLoadingEditors = false;
        _lastRenderFailed = false;
        RenderPreview();
    }

    private void SetDocumentWorkspaceVisible(bool isVisible)
    {
        WorkspaceGrid.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyWorkspacePanel.Visibility = isVisible
            ? Visibility.Collapsed
            : Visibility.Visible;
        OutputPanel.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        OutputSplitter.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!isVisible)
        {
            OutputSplitterRow.Height = new GridLength(0);
            OutputPanelRow.Height = new GridLength(0);
            return;
        }

        OutputSplitterRow.Height = new GridLength(
            _isOutputPanelCollapsed ? 0 : 5);
        OutputPanelRow.Height = new GridLength(
            _isOutputPanelCollapsed ? 32 : _outputPanelHeight);
    }

    private async void EmptyNewCard_Click(object sender, RoutedEventArgs e)
    {
        App.Workspace.AddDocument();
        try
        {
            await App.Workspace.SaveAsync();
        }
        catch (Exception exception)
        {
            SaveStatusIcon.Glyph = "\uEA39";
            SaveStatusText.Text = "Workspace could not be saved";
            SetSaveProblem(
                $"Error · Workspace: {exception.Message}",
                showProblems: true);
        }
    }

    private void LoadEditorText(CodeEditorControl control, string text)
    {
        control.Editor.SetText(text);
        control.Editor.SetSel(0, 0);
        control.Editor.XOffset = 0;
        control.Editor.ScrollCaret();
        control.Editor.EmptyUndoBuffer();
        control.Editor.SetSavePoint();
        UpdateEditorCommandStates();
    }

    private void InitializeEditors()
    {
        if (_editorsInitialized)
        {
            return;
        }

        _editorsInitialized = true;
        ConfigureEditor(PayloadEditor);
        ConfigureEditor(DataEditor);
        PayloadEditor.Editor.Modified += Editor_Modified;
        DataEditor.Editor.Modified += Editor_Modified;
        UpdateEditorCommandStates();
        UpdateMaximizeButtons();
    }

    private static void ConfigureEditor(CodeEditorControl control)
    {
        control.Editor.CodePage = 65001;
        control.Editor.UseTabs = false;
        control.Editor.TabWidth = 2;
        control.Editor.Indent = 2;
        control.Editor.TabIndents = true;
        control.Editor.BackSpaceUnIndents = true;
        control.Editor.WrapMode = Wrap.None;
        control.Editor.ScrollWidthTracking = true;
        control.Editor.LayoutCache = LineCache.Document;
    }

    private void EditorPreferences_Changed(object? sender, EventArgs e)
    {
        ApplyEditorPreferences();
    }

    private void ApplyEditorPreferences()
    {
        double fontSize = App.EditorPreferences.EditorFontSize;
        string fontFamily = App.EditorPreferences.EditorFontFamily;
        ApplyEditorPreferences(PayloadEditor, fontSize, fontFamily);
        ApplyEditorPreferences(DataEditor, fontSize, fontFamily);
    }

    private static void ApplyEditorPreferences(
        CodeEditorControl control,
        double fontSize,
        string fontFamily)
    {
        const int defaultStyleIndex = 32;
        const int maximumStyleIndex = 255;
        int fractionalPointSize = (int)Math.Round(fontSize * 100);

        control.FontSize = fontSize;
        control.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontFamily);

        // WinUIEdit renders through Scintilla styles rather than the XAML
        // FontFamily property. Set STYLE_DEFAULT first, then every lexer style
        // so the JSON highlighter cannot retain the font it initialized with.
        control.Editor.StyleSetSizeFractional(
            defaultStyleIndex,
            fractionalPointSize);
        control.Editor.StyleSetFont(defaultStyleIndex, fontFamily);

        for (int styleIndex = 0; styleIndex <= maximumStyleIndex; styleIndex++)
        {
            control.Editor.StyleSetSizeFractional(
                styleIndex,
                fractionalPointSize);
            control.Editor.StyleSetFont(styleIndex, fontFamily);
        }

        control.Editor.Colourise(0, -1);
        control.InvalidateMeasure();
        control.InvalidateArrange();
    }

    private void Editor_Modified(Editor sender, ModifiedEventArgs e)
    {
        ModificationFlags modification = (ModificationFlags)e.ModificationType;
        if ((modification & (ModificationFlags.InsertText | ModificationFlags.DeleteText)) == 0)
        {
            return;
        }

        UpdateEditorCommandStates();

        if (_isLoadingEditors || _activeDocument is null)
        {
            return;
        }

        _activeDocument.PayloadJson = GetEditorText(PayloadEditor);
        _activeDocument.DataJson = GetEditorText(DataEditor);
        _activeDocument.LastModified = DateTimeOffset.Now;
        _editorRevision++;
        if (_formatProblem is not null)
        {
            _formatProblem = null;
            RefreshProblems(showProblems: false);
        }
        ExpandedJsonText.Text = string.Empty;

        SaveStatusIcon.Glyph = "\uE823";
        SaveStatusText.Text = "Unsaved changes";
        UpdateRenderStatus("Waiting to render…", WorkbenchStatusKind.Neutral);

        _renderTimer.Stop();
        _renderTimer.Start();
        QueueSave();
    }

    private void RenderPreview()
    {
        _renderTimer.Stop();
        DetachRenderedCard();
        PreviewHost.Children.Clear();
        ExpandedJsonText.Text = string.Empty;
        if (_activeDocument is null)
        {
            return;
        }

        List<string> diagnostics = [];
        try
        {
            string payloadJson = GetEditorText(PayloadEditor);
            string dataJson = GetEditorText(DataEditor);
            AdaptiveCardTemplate template = new(payloadJson);
            string expandedPayload = template.Expand(dataJson);
            ExpandedJsonText.Text = JsonFormatting.Format(expandedPayload);
            AdaptiveCardParseResult parseResult = AdaptiveCard.FromJsonString(expandedPayload);
            diagnostics.AddRange(parseResult.Warnings.Select(warning =>
                $"Warning · {warning.StatusCode}: {warning.Message}"));

            if (parseResult.Errors.Count > 0)
            {
                string parseErrors = string.Join(
                    Environment.NewLine,
                    parseResult.Errors.Select(error => $"{error.StatusCode}: {error.Message}"));
                throw new InvalidOperationException(parseErrors);
            }

            ElementTheme previewTheme = PreviewThemeToggle.IsChecked == true
                ? ElementTheme.Dark
                : ElementTheme.Light;
            _renderer.HostConfig = previewTheme == ElementTheme.Dark
                ? _darkHostConfig
                : _lightHostConfig;
            RenderedAdaptiveCard renderedCard = _renderer.RenderAdaptiveCard(parseResult.AdaptiveCard);

            if (renderedCard.FrameworkElement is null)
            {
                IEnumerable<string> renderDiagnostics = renderedCard.Errors
                    .Select(error => $"{error.StatusCode}: {error.Message}")
                    .Concat(renderedCard.Warnings.Select(warning =>
                        $"{warning.StatusCode}: {warning.Message}"));
                string details = string.Join(Environment.NewLine, renderDiagnostics);
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(details)
                        ? "The renderer did not produce a visual element."
                        : details);
            }

            PreviewCardSurface.RequestedTheme = previewTheme;
            renderedCard.FrameworkElement.RequestedTheme = previewTheme;
            renderedCard.FrameworkElement.HorizontalAlignment = HorizontalAlignment.Stretch;
            PreviewHost.Children.Add(renderedCard.FrameworkElement);
            _renderedCard = renderedCard;
            _renderedCard.Action += RenderedCard_Action;

            diagnostics.AddRange(renderedCard.Errors
                .Select(error => $"Error · {error.StatusCode}: {error.Message}")
                .Concat(renderedCard.Warnings.Select(warning =>
                    $"Warning · {warning.StatusCode}: {warning.Message}")));
            SetOutputMessage(
                $"[{DateTime.Now:T}] Rendered {_activeDocument.Name}" +
                (diagnostics.Count == 0 ? " successfully." : " with problems. See Problems for details."));
            SetRenderProblems(diagnostics);
            _lastRenderFailed = false;
            UpdateRenderStatus(
                diagnostics.Count == 0
                    ? $"Rendered · {DateTime.Now:t}"
                    : $"Rendered with {diagnostics.Count} " +
                        (diagnostics.Count == 1 ? "diagnostic" : "diagnostics"),
                renderedCard.Errors.Count > 0
                    ? WorkbenchStatusKind.Error
                    : diagnostics.Count == 0
                        ? WorkbenchStatusKind.Success
                        : WorkbenchStatusKind.Warning);
        }
        catch (Exception exception)
        {
            PreviewHost.Children.Add(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uEA39",
                        FontSize = 28,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Preview unavailable",
                        FontSize = 18,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Review Problems for details. The preview will retry as you edit.",
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = 360,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                            "TextFillColorSecondaryBrush"]
                    }
                }
            });

            SetOutputMessage($"[{DateTime.Now:T}] Render failed.");
            bool showProblems = !_lastRenderFailed;
            _lastRenderFailed = true;
            diagnostics.Insert(0, $"Error · {exception.Message}");
            SetRenderProblems(diagnostics, showProblems: showProblems);
            UpdateRenderStatus(
                "Render failed",
                WorkbenchStatusKind.Error);
        }
    }

    private void DetachRenderedCard()
    {
        if (_renderedCard is not null)
        {
            _renderedCard.Action -= RenderedCard_Action;
            _renderedCard = null;
        }
    }

    private void RenderedCard_Action(RenderedAdaptiveCard sender, AdaptiveActionEventArgs args)
    {
        try
        {
            string action = JsonFormatting.Format(args.Action.ToJson().Stringify());
            string inputs = JsonFormatting.Format(args.Inputs?.AsJson()?.Stringify() ?? "{}");
            SetOutputMessage($"[{DateTime.Now:T}] Preview action\n{action}\n\nInputs\n{inputs}");
        }
        catch (Exception exception)
        {
            SetOutputMessage($"Action could not be inspected: {exception.Message}");
        }

        ShowOutputView(OutputSelectorBarItem);
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async Task SaveDraftAsync()
    {
        if (App.Workspace.Project is null)
        {
            return;
        }

        long revision = _editorRevision;
        long requestId = ++_saveRequestId;
        SaveStatusIcon.Glyph = "\uE823";
        SaveStatusText.Text = "Saving workspace…";
        try
        {
            await App.Workspace.SaveAsync();
            if (requestId != _saveRequestId)
            {
                return;
            }

            SetSaveProblem(null);
            if (revision != _editorRevision)
            {
                return;
            }

            PayloadEditor.Editor.SetSavePoint();
            DataEditor.Editor.SetSavePoint();
            SaveStatusIcon.Glyph = "\uE73E";
            SaveStatusText.Text = $"Draft saved · {DateTime.Now:t}";
        }
        catch (Exception exception)
        {
            if (requestId != _saveRequestId)
            {
                return;
            }

            SaveStatusIcon.Glyph = "\uEA39";
            SaveStatusText.Text = "Workspace could not be saved";
            SetSaveProblem(
                $"Error · Workspace: {exception.Message}",
                showProblems: _saveProblem is null);
        }
    }

    private static string GetEditorText(CodeEditorControl control)
    {
        return control.Editor.GetText(control.Editor.TextLength + 1).TrimEnd('\0');
    }

    private void UpdateEditorCommandStates()
    {
        if (!_editorsInitialized)
        {
            return;
        }

        PayloadUndoButton.IsEnabled = PayloadEditor.Editor.CanUndo();
        PayloadRedoButton.IsEnabled = PayloadEditor.Editor.CanRedo();
        DataUndoButton.IsEnabled = DataEditor.Editor.CanUndo();
        DataRedoButton.IsEnabled = DataEditor.Editor.CanRedo();
    }

    private void PayloadUndo_Click(object sender, RoutedEventArgs e)
    {
        PayloadEditor.Editor.Undo();
        UpdateEditorCommandStates();
    }

    private void PayloadRedo_Click(object sender, RoutedEventArgs e)
    {
        PayloadEditor.Editor.Redo();
        UpdateEditorCommandStates();
    }

    private void DataUndo_Click(object sender, RoutedEventArgs e)
    {
        DataEditor.Editor.Undo();
        UpdateEditorCommandStates();
    }

    private void DataRedo_Click(object sender, RoutedEventArgs e)
    {
        DataEditor.Editor.Redo();
        UpdateEditorCommandStates();
    }

    private void Render_Click(object sender, RoutedEventArgs e)
    {
        RenderPreview();
    }

    private void RunAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RenderPreview();
    }

    private async void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _saveTimer.Stop();
        await SaveDraftAsync();
    }

    private void PayloadFormat_Click(object sender, RoutedEventArgs e) => FormatEditor(PayloadEditor, "Card payload");

    private void DataFormat_Click(object sender, RoutedEventArgs e) => FormatEditor(DataEditor, "Sample data");

    private void FormatEditor(CodeEditorControl control, string name)
    {
        try
        {
            string original = GetEditorText(control);
            string formatted = JsonFormatting.Format(original);
            if (formatted != original)
            {
                control.Editor.BeginUndoAction();
                try
                {
                    control.Editor.TargetWholeDocument();
                    control.Editor.ReplaceTarget(Encoding.UTF8.GetByteCount(formatted), formatted);
                }
                finally
                {
                    control.Editor.EndUndoAction();
                }
            }

            _formatProblem = null;
            RefreshProblems(showProblems: false);
            UpdateEditorCommandStates();
            control.Focus(FocusState.Programmatic);
        }
        catch (JsonException exception)
        {
            _formatProblem = $"Error · {name} could not be formatted: {exception.Message}";
            RefreshProblems(showProblems: true);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _saveTimer.Stop();
        await SaveDraftAsync();
    }

    private void PreviewThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        bool useDarkTheme = PreviewThemeToggle.IsChecked == true;
        PreviewThemeToggle.Label = useDarkTheme ? "Dark" : "Light";
        PreviewThemeToggle.Icon = new FontIcon
        {
            Glyph = useDarkTheme ? "\uE708" : "\uE706"
        };

        string themeName = useDarkTheme ? "Dark" : "Light";
        ToolTipService.SetToolTip(PreviewThemeToggle, $"Preview theme: {themeName}");
        AutomationProperties.SetName(
            PreviewThemeToggle,
            useDarkTheme ? "Use light preview theme" : "Use dark preview theme");

        RenderPreview();
    }

    private void PreviewWidthPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewCardSurface is null ||
            PreviewWidthPicker.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag?.ToString(), out double width))
        {
            return;
        }

        if (width <= 0)
        {
            PreviewScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PreviewCardSurface.Width = double.NaN;
            PreviewCardSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            PreviewScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            PreviewCardSurface.Width = width;
            PreviewCardSurface.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWorkspaceLayout(e.NewSize.Width);
    }

    private void UpdateWorkspaceLayout(double availableWidth)
    {
        if (WorkspaceGrid is null)
        {
            return;
        }

        _useSplitLayout = availableWidth >= 960;
        if (_maximizedPane != MaximizedPane.None)
        {
            ApplyMaximizedPaneLayout();
            return;
        }

        EditorsPanel.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Visible;
        WorkspaceSplitter.Visibility = Visibility.Visible;
        ApplyEditorPaneLayout();

        if (_useSplitLayout)
        {
            EditorColumn.Width = new GridLength(_wideEditorRatio, GridUnitType.Star);
            WorkspaceSplitterColumn.Width = new GridLength(5);
            PreviewColumn.Width = new GridLength(1 - _wideEditorRatio, GridUnitType.Star);
            EditorRow.Height = new GridLength(1, GridUnitType.Star);
            WorkspaceSplitterRow.Height = new GridLength(0);
            PreviewRow.Height = new GridLength(0);

            Grid.SetRow(EditorsPanel, 0);
            Grid.SetColumn(EditorsPanel, 0);
            Grid.SetRow(WorkspaceSplitter, 0);
            Grid.SetColumn(WorkspaceSplitter, 1);
            Grid.SetRow(PreviewPanel, 0);
            Grid.SetColumn(PreviewPanel, 2);
        }
        else
        {
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkspaceSplitterColumn.Width = new GridLength(0);
            PreviewColumn.Width = new GridLength(0);
            EditorRow.Height = new GridLength(_stackedEditorRatio, GridUnitType.Star);
            WorkspaceSplitterRow.Height = new GridLength(5);
            PreviewRow.Height = new GridLength(1 - _stackedEditorRatio, GridUnitType.Star);

            Grid.SetRow(EditorsPanel, 0);
            Grid.SetColumn(EditorsPanel, 0);
            Grid.SetRow(WorkspaceSplitter, 1);
            Grid.SetColumn(WorkspaceSplitter, 0);
            Grid.SetRow(PreviewPanel, 2);
            Grid.SetColumn(PreviewPanel, 0);
        }
    }

    private void ApplyEditorPaneLayout()
    {
        bool payloadOnly = _maximizedPane == MaximizedPane.Payload;
        bool dataOnly = _maximizedPane == MaximizedPane.Data;

        PayloadHeader.Visibility = dataOnly ? Visibility.Collapsed : Visibility.Visible;
        PayloadEditor.Visibility = dataOnly ? Visibility.Collapsed : Visibility.Visible;
        DataHeader.Visibility = payloadOnly ? Visibility.Collapsed : Visibility.Visible;
        DataEditor.Visibility = payloadOnly ? Visibility.Collapsed : Visibility.Visible;
        EditorSplitter.Visibility = payloadOnly || dataOnly
            ? Visibility.Collapsed
            : Visibility.Visible;

        PayloadHeaderRow.Height = dataOnly ? new GridLength(0) : new GridLength(34);
        PayloadEditorRow.Height = dataOnly
            ? new GridLength(0)
            : new GridLength(
                payloadOnly ? 1 : _payloadEditorRatio,
                GridUnitType.Star);
        EditorSplitterRow.Height = payloadOnly || dataOnly
            ? new GridLength(0)
            : new GridLength(5);
        DataHeaderRow.Height = payloadOnly ? new GridLength(0) : new GridLength(34);
        DataEditorRow.Height = payloadOnly
            ? new GridLength(0)
            : new GridLength(
                dataOnly ? 1 : 1 - _payloadEditorRatio,
                GridUnitType.Star);
    }

    private void ApplyMaximizedPaneLayout()
    {
        WorkspaceSplitter.Visibility = Visibility.Collapsed;
        WorkspaceSplitterColumn.Width = new GridLength(0);
        WorkspaceSplitterRow.Height = new GridLength(0);
        EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        PreviewColumn.Width = new GridLength(0);
        EditorRow.Height = new GridLength(1, GridUnitType.Star);
        PreviewRow.Height = new GridLength(0);

        bool previewOnly = _maximizedPane == MaximizedPane.Preview;
        EditorsPanel.Visibility = previewOnly ? Visibility.Collapsed : Visibility.Visible;
        PreviewPanel.Visibility = previewOnly ? Visibility.Visible : Visibility.Collapsed;

        FrameworkElement visiblePanel = previewOnly ? PreviewPanel : EditorsPanel;
        Grid.SetRow(visiblePanel, 0);
        Grid.SetColumn(visiblePanel, 0);
        ApplyEditorPaneLayout();
    }

    private void WorkspaceSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWorkspaceSplit(_useSplitLayout ? e.HorizontalChange : e.VerticalChange);
    }

    private void ResizeWorkspaceSplit(double change)
    {
        if (_maximizedPane != MaximizedPane.None)
        {
            return;
        }

        double primarySize = _useSplitLayout
            ? EditorColumn.ActualWidth
            : EditorRow.ActualHeight;
        double secondarySize = _useSplitLayout
            ? PreviewColumn.ActualWidth
            : PreviewRow.ActualHeight;
        double totalSize = primarySize + secondarySize;
        if (totalSize <= 0)
        {
            return;
        }

        double minimumSize = Math.Min(240, totalSize * 0.4);
        double newPrimarySize = Math.Clamp(
            primarySize + change,
            minimumSize,
            totalSize - minimumSize);
        double ratio = newPrimarySize / totalSize;

        if (_useSplitLayout)
        {
            _wideEditorRatio = ratio;
        }
        else
        {
            _stackedEditorRatio = ratio;
        }

        UpdateWorkspaceLayout(WorkspaceGrid.ActualWidth);
    }

    private void EditorSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeEditorSplit(e.VerticalChange);
    }

    private void ResizeEditorSplit(double change)
    {
        if (_maximizedPane is MaximizedPane.Payload or MaximizedPane.Data)
        {
            return;
        }

        double totalSize = PayloadEditorRow.ActualHeight + DataEditorRow.ActualHeight;
        if (totalSize <= 0)
        {
            return;
        }

        double minimumSize = Math.Min(120, totalSize * 0.4);
        double newPayloadSize = Math.Clamp(
            PayloadEditorRow.ActualHeight + change,
            minimumSize,
            totalSize - minimumSize);
        _payloadEditorRatio = newPayloadSize / totalSize;
        ApplyEditorPaneLayout();
    }

    private void WorkspaceSplitter_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        if (_useSplitLayout)
        {
            _wideEditorRatio = 0.53;
        }
        else
        {
            _stackedEditorRatio = 0.5;
        }

        UpdateWorkspaceLayout(WorkspaceGrid.ActualWidth);
        e.Handled = true;
    }

    private void EditorSplitter_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _payloadEditorRatio = 0.5;
        ApplyEditorPaneLayout();
        e.Handled = true;
    }

    private void WorkspaceSplitter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        double change = 0;
        if (_useSplitLayout && e.Key == VirtualKey.Left)
        {
            change = -24;
        }
        else if (_useSplitLayout && e.Key == VirtualKey.Right)
        {
            change = 24;
        }
        else if (!_useSplitLayout && e.Key == VirtualKey.Up)
        {
            change = -24;
        }
        else if (!_useSplitLayout && e.Key == VirtualKey.Down)
        {
            change = 24;
        }

        if (change != 0)
        {
            ResizeWorkspaceSplit(change);
            e.Handled = true;
        }
    }

    private void EditorSplitter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Up or VirtualKey.Down))
        {
            return;
        }

        ResizeEditorSplit(e.Key == VirtualKey.Up ? -24 : 24);
        e.Handled = true;
    }

    private void PayloadMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizedPane(MaximizedPane.Payload);
    }

    private void DataMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizedPane(MaximizedPane.Data);
    }

    private void PreviewMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizedPane(MaximizedPane.Preview);
    }

    private void ToggleMaximizedPane(MaximizedPane pane)
    {
        _maximizedPane = _maximizedPane == pane ? MaximizedPane.None : pane;
        UpdateMaximizeButtons();
        UpdateWorkspaceLayout(WorkspaceGrid.ActualWidth);
    }

    private void UpdateMaximizeButtons()
    {
        UpdateMaximizeButton(
            PayloadMaximizeButton,
            _maximizedPane == MaximizedPane.Payload,
            "card payload");
        UpdateMaximizeButton(
            DataMaximizeButton,
            _maximizedPane == MaximizedPane.Data,
            "sample data");
        UpdateMaximizeButton(
            PreviewMaximizeButton,
            _maximizedPane == MaximizedPane.Preview,
            "preview");
    }

    private static void UpdateMaximizeButton(
        AppBarButton button,
        bool isMaximized,
        string paneName)
    {
        string action = isMaximized ? "Restore" : "Maximize";
        button.Icon = new FontIcon
        {
            Glyph = isMaximized ? "\uE73F" : "\uE740"
        };
        button.Label = action;
        AutomationProperties.SetName(button, $"{action} {paneName} pane");
        ToolTipService.SetToolTip(button, $"{action} {paneName} pane");
    }

    private void OutputSelectorBar_SelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not null)
        {
            ShowOutputView(sender.SelectedItem);
        }
    }

    private void ProblemsStatusLink_Click(object sender, RoutedEventArgs e)
    {
        ShowOutputView(ProblemsSelectorBarItem);
    }

    private void ShowOutputView(SelectorBarItem selectedItem)
    {
        if (OutputSelectorBar.SelectedItem != selectedItem)
        {
            OutputSelectorBar.SelectedItem = selectedItem;
            return;
        }

        OutputScrollViewer.Visibility = selectedItem == OutputSelectorBarItem
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProblemsScrollViewer.Visibility = selectedItem == ProblemsSelectorBarItem
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExpandedJsonText.Visibility = selectedItem == ExpandedJsonSelectorBarItem
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExpandOutputPanel();
    }

    private void SetOutputMessage(string message)
    {
        OutputText.Text = message;
    }

    private void SetRenderProblems(
        IReadOnlyCollection<string> problems,
        bool showProblems = false)
    {
        _renderProblems.Clear();
        _renderProblems.AddRange(problems);
        RefreshProblems(showProblems);
    }

    private void SetSaveProblem(string? problem, bool showProblems = false)
    {
        _saveProblem = problem;
        RefreshProblems(showProblems);
    }

    private void RefreshProblems(bool showProblems)
    {
        List<string> problems = [.. _renderProblems];
        if (_saveProblem is not null)
        {
            problems.Add(_saveProblem);
        }
        if (_formatProblem is not null)
        {
            problems.Add(_formatProblem);
        }

        ProblemsSelectorBarItem.Text = problems.Count == 0
            ? "Problems"
            : $"Problems ({problems.Count})";
        ProblemsText.Text = problems.Count == 0
            ? "No problems detected."
            : string.Join(Environment.NewLine, problems);
        ProblemsStatusLink.Content = problems.Count == 0
            ? "View problems"
            : $"View problems ({problems.Count})";
        ProblemsStatusLink.Visibility = problems.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (showProblems)
        {
            ShowOutputView(ProblemsSelectorBarItem);
        }
    }

    private void UpdateRenderStatus(
        string message,
        WorkbenchStatusKind statusKind)
    {
        RenderStatusText.Text = message;
        (string glyph, string brushKey) = statusKind switch
        {
            WorkbenchStatusKind.Success => (
                "\uE73E",
                "SystemFillColorSuccessBrush"),
            WorkbenchStatusKind.Warning => (
                "\uE7BA",
                "SystemFillColorCautionBrush"),
            WorkbenchStatusKind.Error => (
                "\uEA39",
                "SystemFillColorCriticalBrush"),
            _ => (
                "\uE823",
                "TextFillColorSecondaryBrush")
        };
        RenderStatusIcon.Glyph = glyph;
        RenderStatusIcon.Foreground =
            (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[brushKey];
        AutomationProperties.SetName(
            RenderStatusIcon,
            $"Renderer status: {message}");
    }

    private void OutputPanelToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOutputPanelCollapsed)
        {
            ExpandOutputPanel();
        }
        else
        {
            CollapseOutputPanel();
        }
    }

    private void CollapseOutputPanel()
    {
        if (_isOutputPanelCollapsed)
        {
            return;
        }

        _outputPanelHeight = Math.Max(OutputPanelRow.ActualHeight, 96);
        _isOutputPanelCollapsed = true;
        OutputSplitterRow.Height = new GridLength(0);
        OutputPanelRow.Height = new GridLength(32);
        OutputPanelBody.Visibility = Visibility.Collapsed;
        UpdateOutputPanelToggleButton();
    }

    private void ExpandOutputPanel()
    {
        if (!_isOutputPanelCollapsed)
        {
            return;
        }

        _isOutputPanelCollapsed = false;
        OutputSplitterRow.Height = new GridLength(5);
        OutputPanelRow.Height = new GridLength(_outputPanelHeight);
        OutputPanelBody.Visibility = Visibility.Visible;
        UpdateOutputPanelToggleButton();
    }

    private void UpdateOutputPanelToggleButton()
    {
        string action = _isOutputPanelCollapsed ? "Expand" : "Collapse";
        OutputPanelToggleButton.Icon = new FontIcon
        {
            Glyph = _isOutputPanelCollapsed ? "\uE70E" : "\uE70D"
        };
        OutputPanelToggleButton.Label = action;
        AutomationProperties.SetName(OutputPanelToggleButton, $"{action} output panel");
        ToolTipService.SetToolTip(OutputPanelToggleButton, $"{action} output panel");
    }

    private void OutputSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeOutputPanel(e.VerticalChange);
    }

    private void ResizeOutputPanel(double change)
    {
        if (_isOutputPanelCollapsed)
        {
            return;
        }

        double availableHeight = LayoutRoot.ActualHeight - 140;
        double maximumHeight = Math.Max(96, availableHeight * 0.55);
        _outputPanelHeight = Math.Clamp(
            OutputPanelRow.ActualHeight - change,
            96,
            maximumHeight);
        OutputPanelRow.Height = new GridLength(_outputPanelHeight);
    }

    private void OutputSplitter_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        _outputPanelHeight = 150;
        OutputPanelRow.Height = new GridLength(_outputPanelHeight);
        e.Handled = true;
    }

    private void OutputSplitter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Up or VirtualKey.Down))
        {
            return;
        }

        ResizeOutputPanel(e.Key == VirtualKey.Up ? -24 : 24);
        e.Handled = true;
    }
}
