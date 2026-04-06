using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ValorantAfkBot.App.Services;
using ValorantAfkBot.App.ViewModels;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Services;
using ValorantAfkBot.Windows.Hotkeys;
using ValorantAfkBot.Windows.Input;
using ValorantAfkBot.Windows.SingleInstance;
using ValorantAfkBot.Windows.Startup;
using ValorantAfkBot.Windows.Tray;
using ValorantAfkBot.Windows.Windowing;

namespace ValorantAfkBot.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using SingleInstanceCoordinator singleInstanceCoordinator = new("ValorantAfkBot");
        if (!singleInstanceCoordinator.IsPrimaryInstance)
        {
            singleInstanceCoordinator.SignalExistingInstance();
            return;
        }

        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ValorantAfkBot");
        string configPath = Path.Combine(dataDirectory, "config.json");
        string logPath = Path.Combine(dataDirectory, "logs", "app.log");
        Directory.CreateDirectory(dataDirectory);

        ObservableLogService logger = new(logPath, SynchronizationContext.Current);
        IProfileRepository repository = new JsonProfileRepository(configPath);
        IAutostartService autostartService = new RegistryAutostartService();
        Win32WindowLocator windowLocator = new();
        IAntiAfkEngine engine = new AntiAfkEngine(
            [new WindowMessageInputStrategy(), new ForegroundSendInputStrategy()],
            windowLocator,
            logger);

        using Icon icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        using TrayIconController trayIconController = new("Valorant AFK Bot", icon);
        using GlobalHotkeyManager hotkeyManager = new();
        MainViewModel viewModel = new(
            engine,
            repository,
            autostartService,
            windowLocator,
            logger,
            Environment.ProcessPath ?? AppContext.BaseDirectory,
            dataDirectory,
            configPath,
            logPath);
        viewModel.InitializeAsync().GetAwaiter().GetResult();

        using MainForm form = new(
            viewModel,
            trayIconController,
            hotkeyManager,
            singleInstanceCoordinator,
            icon);

        Application.Run(form);
    }
}
