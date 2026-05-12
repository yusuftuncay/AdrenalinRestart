using System.Runtime.Versioning;

namespace AdrenalinRestart.Tray;

[SupportedOSPlatform("windows")]
internal sealed class TrayManager : IDisposable
{
    // Tray Icon Instance
    private readonly NotifyIcon _notifyIcon;

    // Callback Invoked When User Requests Open Console
    private readonly Action _openConsoleCallback;

    // Callback Invoked When User Requests Manual Reset
    private readonly Action _resetCallback;

    // Callback Invoked When User Requests Restart Monitoring
    private readonly Action _restartMonitoringCallback;

    // Callback Invoked When User Requests Exit
    private readonly Action _exitCallback;

    #region Methods
    internal TrayManager(
        Action openConsoleCallback,
        Action resetCallback,
        Action restartMonitoringCallback,
        Action exitCallback
    )
    {
        _openConsoleCallback = openConsoleCallback;
        _resetCallback = resetCallback;
        _restartMonitoringCallback = restartMonitoringCallback;
        _exitCallback = exitCallback;

        // Build Context Menu
        var contextMenu = new ContextMenuStrip();

        var openConsoleItem = new ToolStripMenuItem("Open Console");
        openConsoleItem.Click += (_, _) => _openConsoleCallback();
        contextMenu.Items.Add(openConsoleItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var resetItem = new ToolStripMenuItem("Reset");
        resetItem.Click += (_, _) => _resetCallback();
        contextMenu.Items.Add(resetItem);

        var restartMonitoringItem = new ToolStripMenuItem("Restart Monitoring");
        restartMonitoringItem.Click += (_, _) => _restartMonitoringCallback();
        contextMenu.Items.Add(restartMonitoringItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => _exitCallback();
        contextMenu.Items.Add(exitItem);

        // Build Tray Icon
        _notifyIcon = new NotifyIcon
        {
            Text = "Adrenalin Restart",
            Icon = SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        // Double Click Opens Console
        _notifyIcon.DoubleClick += (_, _) => _openConsoleCallback();
    }

    internal void ShowBalloonTip(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
    #endregion
}
