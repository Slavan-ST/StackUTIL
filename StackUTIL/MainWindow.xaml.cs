// MainWindow.xaml.cs
using System.Windows;
using System.ComponentModel;
using DebugInterceptor.Services; // 👈 Добавляем пространство

namespace StackUTIL
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.ShowInTaskbar = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }
    }
}