using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AdaptiveCardWorkbench;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppInstance instance = AppInstance.FindOrRegisterForKey("workspace");
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(
                AppInstance.GetCurrent().GetActivatedEventArgs());
            return;
        }

        Application.Start(_ =>
        {
            DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));
            App app = new();
            instance.Activated += (_, _) =>
            {
                dispatcher.TryEnqueue(app.ActivateWindow);
            };
        });
    }
}
