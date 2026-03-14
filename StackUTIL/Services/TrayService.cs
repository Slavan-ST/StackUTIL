using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackUTIL;
using System.Runtime.InteropServices.JavaScript;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Сервис управления иконкой в системном трее (NotifyIcon)
    /// </summary>
    /// <remarks>
    /// Реализует <see cref="IHostedService"/> для интеграции с хостингом .NET 
    /// и <see cref="IDisposable"/> для корректной очистки ресурсов.
    /// <para>
    /// Основные возможности:
    /// <list type="bullet">
    /// <item><description>Создание/удаление иконки в трее при старте/остановке</description></item>
    /// <item><description>Обработка двойного клика для показа/скрытия главного окна</description></item>
    /// <item><description>Контекстное меню с пунктом выхода из приложения</description></item>
    /// <item><description>Потокобезопасная работа с UI через Dispatcher</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public class TrayService : IHostedService, IDisposable
    {
        private readonly ILogger<TrayService> _logger;
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;
        private bool _isDisposed;
        private readonly object _lock = new();

        /// <summary>
        /// 🔹 Инициализирует новый экземпляр <see cref="TrayService"/>
        /// </summary>
        /// <param name="logger">Экземпляр логгера для диагностики</param>
        public TrayService(ILogger<TrayService> logger)
        {
            _logger = logger;
            _logger.LogDebug("🔧 TrayService.ctor: экземпляр создан");
        }

        /// <summary>
        /// 🔹 Устанавливает ссылку на главное окно приложения
        /// </summary>
        /// <param name="window">Экземпляр <see cref="MainWindow"/> для управления</param>
        /// <remarks>
        /// Метод потокобезопасен (использует <c>lock</c>).
        /// Вызывается из <see cref="App.OnStartup"/> после создания окна.
        /// </remarks>
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

        /// <inheritdoc />
        /// <summary>
        /// 🔹 Запускает сервис: создаёт иконку в трее
        /// </summary>
        /// <param name="cancellationToken">Токен отмены (не используется, т.к. запуск синхронный)</param>
        /// <returns>Завершённая задача <see cref="Task"/></returns>
        /// <remarks>
        /// Если <see cref="Application.Current"/> ещё не инициализирован, 
        /// подписывается на событие <see cref="Application.Startup"/> для отложенного создания иконки.
        /// </remarks>
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
                Application.Current!.Startup += OnApplicationStartup;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 🔹 Обработчик события <see cref="Application.Startup"/> для отложенной инициализации
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события</param>
        private void OnApplicationStartup(object? sender, StartupEventArgs e)
        {
            Application.Current.Startup -= OnApplicationStartup;
            _logger.LogDebug("🔄 Application.Startup сработал, создаём иконку...");
            CreateNotifyIcon();
        }

        /// <summary>
        /// 🔹 Создаёт и настраивает <see cref="NotifyIcon"/> в системном трее
        /// </summary>
        /// <remarks>
        /// Использует иконку исполняемого файла или запасную <see cref="SystemIcons.Application"/>.
        /// Подписывается на события мыши и создаёт контекстное меню.
        /// </remarks>
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

        /// <summary>
        /// 🔹 Обработчик двойного клика по иконке в трее
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события мыши</param>
        /// <remarks>
        /// Реагирует только на левую кнопку мыши (<see cref="MouseButtons.Left"/>).
        /// Вызывает <see cref="ToggleMainWindow"/> для показа/скрытия окна.
        /// </remarks>
        private void OnNotifyIconMouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _logger.LogDebug("🖱️ Двойной клик по трею");
                ToggleMainWindow();
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// 🔹 Останавливает сервис: удаляет иконку из трея
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Завершённая задача <see cref="Task"/></returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("🛑 TrayService.StopAsync: остановка...");
            Dispose();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 🔹 Показывает главное окно (активирует и переводит в нормальное состояние)
        /// </summary>
        /// <remarks>
        /// Выполняется в потоке UI через <see cref="Dispatcher.Invoke"/>.
        /// Если диспетчер недоступен — пытается показать окно напрямую (fallback).
        /// </remarks>
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

        /// <summary>
        /// 🔹 Скрывает главное окно
        /// </summary>
        public void HideMainWindow()
        {
            var window = GetMainWindow();
            if (window == null) return;

            Application.Current?.Dispatcher.Invoke(() => window.Hide());
        }

        /// <summary>
        /// 🔹 Переключает видимость главного окна (показать ↔ скрыть)
        /// </summary>
        /// <remarks>
        /// Проверяет текущее состояние <see cref="Window.IsVisible"/> в потоке UI,
        /// затем вызывает <see cref="ShowMainWindow"/> или <see cref="HideMainWindow"/>.
        /// </remarks>
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

        /// <summary>
        /// 🔹 Безопасно получает ссылку на главное окно
        /// </summary>
        /// <returns>Экземпляр <see cref="MainWindow"/> или <c>null</c></returns>
        /// <remarks>Использует <c>lock</c> для потокобезопасного чтения поля <c>_mainWindow</c>.</remarks>
        private MainWindow? GetMainWindow()
        {
            lock (_lock)
            {
                if (_mainWindow == null)
                    _logger.LogDebug("🔍 GetMainWindow: _mainWindow = null");
                return _mainWindow;
            }
        }

        /// <summary>
        /// 🔹 Создаёт контекстное меню для иконки в трее
        /// </summary>
        /// <returns>Настроенный экземпляр <see cref="ContextMenuStrip"/></returns>
        /// <remarks>
        /// Содержит:
        /// <list type="bullet">
        /// <item><description>Разделитель</description></item>
        /// <item><description>Пункт "Выход" — завершает приложение через <see cref="Application.Shutdown"/> или <see cref="Environment.Exit"/></description></item>
        /// </list>
        /// </remarks>
        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += (s, e) =>
            {
                _logger.LogInformation("👋 Завершение работы из трея");

                // Принудительно завершаем процесс
                Environment.Exit(0);
            };
            menu.Items.Add(exitItem);

            return menu;
        }

        /// <inheritdoc />
        /// <summary>
        /// 🔹 Освобождает неуправляемые ресурсы (иконка, подписки)
        /// </summary>
        /// <remarks>
        /// Реализует паттерн Dispose:
        /// <list type="number">
        /// <item><description>Проверяет флаг <c>_isDisposed</c> для идемпотентности</description></item>
        /// <item><description>Отписывается от событий <see cref="NotifyIcon.MouseDoubleClick"/></description></item>
        /// <item><description>Скрывает и удаляет <see cref="NotifyIcon"/></description></item>
        /// <item><description>Вызывает <see cref="GC.SuppressFinalize"/> для предотвращения финализации</description></item>
        /// </list>
        /// </remarks>
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