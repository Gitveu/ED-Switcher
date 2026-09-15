using System;
using EDAccountSwitcher.Updating;

namespace EDAccountSwitcher
{
    public static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], UpdateService.ApplySwitch, StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = UpdateService.ApplyStagedUpdate(args);
                return;
            }

            UpdateService.CleanupAfterUpdate();  
            global::WinRT.ComWrappersSupport.InitializeComWrappers();
            global::Microsoft.UI.Xaml.Application.Start(_ =>
            {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }
}