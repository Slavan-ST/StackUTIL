// MainWindow.xaml.cs
using System.Windows; // 👈 Явное пространство для WPF

namespace StackUTIL
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 👇 Не показывать окно в панели задач при старте
            this.ShowInTaskbar = false;
            this.WindowState = WindowState.Minimized;
            this.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Обработчик закрытия окна — скрываем в трей вместо завершения приложения
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // 👈 Если хотите, чтобы кнопка "Закрыть" тоже сворачивала в трей:
            // e.Cancel = true;
            // this.Hide();

            base.OnClosed(e);
        }
    }
}