using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Dialogs;
using System;
using System.Runtime.InteropServices;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var bundlePath = AppDomain.CurrentDomain.BaseDirectory;
                MacOsDataMigration.EnsureApplicationSupportExists(bundlePath);
                AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE",
                    MacOsDataMigration.GetUserConfigPath());
            }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            //.UseManagedSystemDialogs()
            //.UseManagedSystemDialogs<AppBuilder, MyCustomWindowType>();
            .LogToTrace();

    }
}
