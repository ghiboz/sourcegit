using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class FileHistories : ChromelessWindow
    {
        public FileHistories()
        {
            InitializeComponent();
        }

        private void MaximizeOrRestoreWindow(object _, TappedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;

            e.Handled = true;
        }

        private void BeginMoveWindow(object _, PointerPressedEventArgs e)
        {
            if (e.ClickCount == 1)
                BeginMoveDrag(e);

            e.Handled = true;
        }

        private void OnPressCommitSHA(object sender, PointerPressedEventArgs e)
        {
            if (sender is TextBlock { DataContext: Models.Commit commit } &&
                DataContext is ViewModels.FileHistories vm)
            {
                vm.NavigateToCommit(commit);
            }

            e.Handled = true;
        }

        private void OnResetToSelectedRevision(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FileHistories vm)
            {
                vm.ResetToSelectedRevision();
                NotifyDonePanel.IsVisible = true;
            }

            e.Handled = true;
        }

        private void OnCloseNotifyPanel(object _, PointerPressedEventArgs e)
        {
            NotifyDonePanel.IsVisible = false;
            e.Handled = true;
        }
    }
}
