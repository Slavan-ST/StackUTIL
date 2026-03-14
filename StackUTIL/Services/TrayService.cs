using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackUTIL;
using System.Windows;
using Application = System.Windows.Application;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// Сервис для управления иконкой в системном трее.
    /// </summary>
    public class TrayService : IHostedService, IDisposable
    {
        private readonly ILogger<TrayService> _logger;
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;
        private bool _isDisposed;
        private readonly object _lock = new();

        public TrayService(ILogger<TrayService> logger)
        {
            _logger = logger;
            _logger.LogDebug("🔧 TrayService.ctor: экземпляр создан");
        }

        /// <summary>
        /// Устанавливает ссылку на главное окно.
        /// </summary>
        public void SetMainWindow(MainWindow window)
        {
            if (window == null)
            {
                _logger.LogWarning("⚠️ SetMainWindow: передано null");
                return;
            }

            lock (_lock)
            {
                _mainWindow = window;
                _logger.LogDebug($"🪟 SetMainWindow: окно назначено (HashCode: {window.GetHashCode()})");
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.CompletedTask;

            _logger.LogDebug("📡 TrayService.StartAsync: инициализация...");

            // Создаём иконку только если Application уже инициализирован
            if (Application.Current != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                    CreateNotifyIcon();
                else
                    Application.Current.Dispatcher.Invoke(CreateNotifyIcon);
                _logger.LogInformation("✅ TrayService запущен, иконка создана");
            }
            else
            {
                _logger.LogWarning("⚠️ Application.Current = null, откладываем создание иконки");
                // Подписываемся на инициализацию приложения
                System.Windows.Application.Current.Startup += OnApplicationStartup;
            }

            return Task.CompletedTask;
        }

        private void OnApplicationStartup(object? sender, StartupEventArgs e)
        {
            System.Windows.Application.Current.Startup -= OnApplicationStartup;
            _logger.LogDebug("🔄 Application.Startup сработал, создаём иконку...");
            CreateNotifyIcon();
        }

        private void CreateNotifyIcon()
        {
            try
            {
                var iconPath = Environment.ProcessPath ??
                               System.Reflection.Assembly.GetEntryAssembly()?.Location ??
                               "app.ico";

                _notifyIcon = new NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath) ??
                           System.Drawing.SystemIcons.Application,
                    Text = "StackUTIL",
                    Visible = true
                };

                _notifyIcon.MouseDoubleClick += OnNotifyIconMouseDoubleClick;
                _notifyIcon.ContextMenuStrip = CreateContextMenu();

                _logger.LogDebug("✅ NotifyIcon создан");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка создания NotifyIcon");
            }
        }

        private void OnNotifyIconMouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _logger.LogDebug("🖱️ Двойной клик по трею");
                ToggleMainWindow();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("🛑 TrayService.StopAsync: остановка...");
            Dispose();
            return Task.CompletedTask;
        }

        public void ShowMainWindow()
        {
            var window = GetMainWindow();

            if (window == null)
            {
                _logger.LogWarning("⚠️ ShowMainWindow: _mainWindow = null");
                return;
            }

            _logger.LogDebug($"🪟 ShowMainWindow: показываем окно (HashCode: {window.GetHashCode()})");

            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        window.Show();
                        window.WindowState = WindowState.Normal;
                        window.Activate();
                        window.Focus();
                    });
                }
                else
                {
                    // Fallback: показываем напрямую, если диспетчер недоступен
                    window.Show();
                    window.WindowState = WindowState.Normal;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка при показе окна");
            }
        }

        public void HideMainWindow()
        {
            var window = GetMainWindow();
            if (window == null) return;

            Application.Current?.Dispatcher.Invoke(() => window.Hide());
        }

        public void ToggleMainWindow()
        {
            var window = GetMainWindow();
            if (window == null)
            {
                _logger.LogWarning("⚠️ ToggleMainWindow: окно не назначено");
                return;
            }

            bool isVisible = false;
            Application.Current?.Dispatcher.Invoke(() => isVisible = window.IsVisible);

            _logger.LogDebug($"🔄 ToggleMainWindow: isVisible = {isVisible}");

            if (isVisible) HideMainWindow();
            else ShowMainWindow();
        }

        private MainWindow? GetMainWindow()
        {
            lock (_lock)
            {
                if (_mainWindow == null)
                    _logger.LogDebug("🔍 GetMainWindow: _mainWindow = null");
                return _mainWindow;
            }
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            // Пока не нужны, м.б. потом добавлю
            //var toggleItem = new ToolStripMenuItem("Показать окно");
            //toggleItem.Click += (s, e) => ToggleMainWindow();
            //menu.Items.Add(toggleItem);

            //var settingsItem = new ToolStripMenuItem("Настройки...") { Enabled = false };
            //menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += async (s, e) =>
            {
                _logger.LogInformation("👋 Завершение работы из трея");
                if (Application.Current != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        Application.Current.Shutdown(0));
                }
                else
                {
                    Environment.Exit(0);
                }
            };
            menu.Items.Add(exitItem);

            return menu;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _logger.LogDebug("🧹 TrayService.Dispose: очистка...");

            if (_notifyIcon != null)
            {
                _notifyIcon.MouseDoubleClick -= OnNotifyIconMouseDoubleClick;
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