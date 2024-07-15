using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class LauncherPage : UserControl
    {
        public LauncherPage()
        {
            InitializeComponent();
        }

        private void OnPopupSure(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPage page)
                page.ProcessPopup();

            e.Handled = true;
        }

        private void OnPopupCancel(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPage page)
                page.CancelPopup();

            e.Handled = true;
        }

        private void OnPopupCancelByClickMask(object sender, PointerPressedEventArgs e)
        {
            OnPopupCancel(sender, e);
        }

        private void OnDismissNotification(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ViewModels.Notification notice } &&
                DataContext is ViewModels.LauncherPage page)
                page.Notifications.Remove(notice);

            e.Handled = true;
        }
    }
}
