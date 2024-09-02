using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class InteractiveRebase : ChromelessWindow
    {
        public InteractiveRebase()
        {
            InitializeComponent();
        }

        private void BeginMoveWindow(object _, PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }

        private void CloseWindow(object _1, RoutedEventArgs _2)
        {
            Close();
        }

        private void OnSetupRowHeaderDragDrop(object sender, RoutedEventArgs e)
        {
            if (sender is Border border)
            {
                DragDrop.SetAllowDrop(border, true);
                border.AddHandler(DragDrop.DragOverEvent, OnRowHeaderDragOver);
            }
        }

        private void OnRowHeaderPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is ViewModels.InteractiveRebaseItem item)
            {
                var data = new DataObject();
                data.Set("InteractiveRebaseItem", item);
                DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
            }
        }

        private void OnRowHeaderDragOver(object sender, DragEventArgs e)
        {
            if (DataContext is ViewModels.InteractiveRebase vm &&
                e.Data.Contains("InteractiveRebaseItem") &&
                e.Data.Get("InteractiveRebaseItem") is ViewModels.InteractiveRebaseItem src &&
                sender is Border { DataContext: ViewModels.InteractiveRebaseItem dst } border &&
                src != dst)
            {
                e.DragEffects = DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link;

                var p = e.GetPosition(border);
                if (p.Y > border.Bounds.Height * 0.33 && p.Y < border.Bounds.Height * 0.67)
                {
                    var srcIdx = vm.Items.IndexOf(src);
                    var dstIdx = vm.Items.IndexOf(dst);
                    if (srcIdx < dstIdx)
                    {
                        for (var i = srcIdx; i < dstIdx; i++)
                            vm.MoveItemDown(src);
                    }
                    else
                    {
                        for (var i = srcIdx; i > dstIdx; i--)
                            vm.MoveItemUp(src);
                    }
                }

                e.Handled = true;
            }
        }

        private void OnMoveItemUp(object sender, RoutedEventArgs e)
        {
            if (sender is Control control && DataContext is ViewModels.InteractiveRebase vm)
            {
                vm.MoveItemUp(control.DataContext as ViewModels.InteractiveRebaseItem);
                e.Handled = true;
            }
        }

        private void OnMoveItemDown(object sender, RoutedEventArgs e)
        {
            if (sender is Control control && DataContext is ViewModels.InteractiveRebase vm)
            {
                vm.MoveItemDown(control.DataContext as ViewModels.InteractiveRebaseItem);
                e.Handled = true;
            }
        }

        private void OnItemsListBoxKeyDown(object sender, KeyEventArgs e)
        {
            var item = (sender as ListBox)?.SelectedItem as ViewModels.InteractiveRebaseItem;
            if (item == null)
                return;

            if (e.Key == Key.P)
                item.SetAction(Models.InteractiveRebaseAction.Pick);
            else if (e.Key == Key.E)
                item.SetAction(Models.InteractiveRebaseAction.Edit);
            else if (e.Key == Key.R)
                item.SetAction(Models.InteractiveRebaseAction.Reword);
            else if (e.Key == Key.S)
                item.SetAction(Models.InteractiveRebaseAction.Squash);
            else if (e.Key == Key.F)
                item.SetAction(Models.InteractiveRebaseAction.Fixup);
            else if (e.Key == Key.D)
                item.SetAction(Models.InteractiveRebaseAction.Drop);
        }

        private async void StartJobs(object _1, RoutedEventArgs _2)
        {
            var vm = DataContext as ViewModels.InteractiveRebase;
            if (vm == null)
                return;

            Running.IsVisible = true;
            Running.IsIndeterminate = true;
            await vm.Start();
            Running.IsIndeterminate = false;
            Running.IsVisible = false;
            Close();
        }
    }
}
