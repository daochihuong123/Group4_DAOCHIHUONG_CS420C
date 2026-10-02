using System;
using System.Windows;

namespace RemotingServerWPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show("Đã xảy ra lỗi:\n\n" + args.Exception.Message, "Lỗi Server", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Exception ex = args.ExceptionObject as Exception;
                MessageBox.Show("Lỗi hệ thống Server:\n\n" + (ex != null ? ex.Message : "Không xác định"), "Lỗi Server", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}
