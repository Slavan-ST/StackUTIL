// Services/TrayService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackUTIL;
using System;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// Сервис для управления иконкой в системном трее.
    /// </summary>
    public class TrayService : IHostedService, IDisposable
    {
        private readonly ILogger<TrayService> _logger;
        private readonly Func<MainWindow> _mainWindowFactory;

        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow; // 👈 Ссылка на окно (устанавливается извне)
        private bool _isDisposed;

        public TrayService(ILogger<TrayService> logger, Func<MainWindow> mainWindowFactory)
        {
            _logger = logger;
            _mainWindowFactory = mainWindowFactory;
        }

        /// <summary>
        /// Устанавливает ссылку на главное окно (вызывается из App.xaml.cs после создания окна).
        /// </summary>
        public void SetMainWindow(MainWindow window)
        {
            _mainWindow = window;
            _logger.LogDebug("🪟 TrayService получил ссылку на MainWindow");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.CompletedTask;

            _logger.LogDebug("📡 Инициализация TrayService...");

            _notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    Environment.ProcessPath ??
                    System.Reflection.Assembly.GetEntryAssembly()?.Location ??
                    "app.ico"),
                Text = "StackUTIL",
                Visible = true
            };

            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ToggleMainWindow();
            };

            _notifyIcon.ContextMenuStrip = CreateContextMenu();

            _logger.LogInformation("✅ TrayService запущен");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("🛑 Остановка TrayService...");
            Dispose();
            return Task.CompletedTask;
        }

        public void ShowMainWindow()
        {
            if (_mainWindow == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _mainWindow.Focus();
            });
        }

        public void HideMainWindow()
        {
            if (_mainWindow == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainWindow.Hide();
            });
        }

        public void ToggleMainWindow()
        {
            if (_mainWindow?.IsVisible == true)
                HideMainWindow();
            else
                ShowMainWindow();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            var toggleItem = new ToolStripMenuItem("Показать окно");
            toggleItem.Click += (s, e) => ToggleMainWindow();
            menu.Items.Add(toggleItem);

            var settingsItem = new ToolStripMenuItem("Настройки...")
            {
                Enabled = false // 👈 Включим позже
            };
            menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += async (s, e) =>
            {
                _logger.LogInformation("👋 Завершение работы из трея");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    Application.Current.Shutdown(0));
            };
            menu.Items.Add(exitItem);

            return menu;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Icon?.Dispose();
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}