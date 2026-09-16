using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using AdaptiveCardWorkbench.Services;

namespace AdaptiveCardWorkbench;

public partial class App : Application
{
    private Window? _window;

    // Initialize services during OnLaunched to avoid WinRT calls during type initialization.
    public static WorkspaceService Workspace { get; private set; } = null!;

    public static EditorPreferencesService EditorPreferences { get; private set; } = null!;

    public App()
    {
        try
        {
            InitializeComponent();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("App.InitializeComponent failed: " + ex.ToString());
            // Re-throw to preserve existing crash behavior after logging more details for diagnosis.
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Initialize services after WinRT and DispatcherQueue have been set up.
        EditorPreferences = new EditorPreferencesService();
        Workspace = new WorkspaceService(new DraftStorageService());

        _window = new MainWindow();
        ActivateWindow();
    }

    internal void ActivateWindow()
    {
        if (_window?.AppWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Minimized
            } presenter)
        {
            presenter.Restore();
        }

        _window?.Activate();
    }
}
