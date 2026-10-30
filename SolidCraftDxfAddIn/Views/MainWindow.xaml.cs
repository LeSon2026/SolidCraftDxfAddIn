using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ExportDxfAddIn.Models;
using ExportDxfAddIn.Services;
using ExportDxfAddIn.ViewModels;

namespace ExportDxfAddIn.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.ContentRendered += MainWindow_ContentRendered;
            this.Closed += (s, e) =>
            {
                Tr.Instance.LanguageChanged -= OnLanguageChanged;
                // Huỷ pha 2 (nạp thumbnail nền) nếu còn đang chạy — không giữ COM sau khi đóng cửa sổ.
                (this.DataContext as MainViewModel)?.CancelThumbnailLoading();
            };

            // Register standard Copy and Paste command bindings to force CanExecute and bypass DataGrid's default limitations
            MainDataGrid.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Paste, OnPasteExecuted, OnPasteCanExecute));
            MainDataGrid.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Copy, OnCopyExecuted, OnCopyCanExecute));

            // Đăng ký 1 lần duy nhất cho cả vòng đời window (huỷ đăng ký khi Closed ở trên) để tránh
            // rò rỉ handler vào singleton Tr.Instance nếu người dùng mở/đóng cửa sổ này nhiều lần.
            Tr.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            (this.DataContext as MainViewModel)?.NotifyLanguageChanged();
            HistoryView.ViewModel.NotifyLanguageChanged();
        }

        // Sidebar "Xuất DXF" / "Lịch sử xuất" / "Cấu hình Layer": 3 ToggleButton độc lập (không dùng
        // RadioButton.GroupName, xem lý do ở README mục 9 — tự quản lý trạng thái check chồng lên
        // binding), nên phải tự loại trừ lẫn nhau bằng tay + tự chuyển Visibility giữa 3 khối nội dung
        // cùng nằm Grid.Column="1".
        private void NavExportDxf_Click(object sender, RoutedEventArgs e)
        {
            NavExportDxfToggle.IsChecked = true;
            NavHistoryToggle.IsChecked = false;
            NavLayerConfigToggle.IsChecked = false;
            ExportDxfContent.Visibility = Visibility.Visible;
            HistoryView.Visibility = Visibility.Collapsed;
            LayerConfigViewCtrl.Visibility = Visibility.Collapsed;
        }

        private void NavHistory_Click(object sender, RoutedEventArgs e)
        {
            NavExportDxfToggle.IsChecked = false;
            NavHistoryToggle.IsChecked = true;
            NavLayerConfigToggle.IsChecked = false;
            ExportDxfContent.Visibility = Visibility.Collapsed;
            HistoryView.Visibility = Visibility.Visible;
            LayerConfigViewCtrl.Visibility = Visibility.Collapsed;

            // Đọc lại lịch sử mỗi lần chuyển sang tab này — rẻ (SQLite local), đảm bảo thấy được
            // ngay các lần export vừa thực hiện trong cùng phiên "Xuất DXF".
            var vm = this.DataContext as MainViewModel;
            HistoryView.ViewModel.LoadHistory(vm?.HistoryDbPath);
        }

        private void NavLayerConfig_Click(object sender, RoutedEventArgs e)
        {
            NavExportDxfToggle.IsChecked = false;
            NavHistoryToggle.IsChecked = false;
            NavLayerConfigToggle.IsChecked = true;
            ExportDxfContent.Visibility = Visibility.Collapsed;
            HistoryView.Visibility = Visibility.Collapsed;
            LayerConfigViewCtrl.Visibility = Visibility.Visible;
        }

        private void OnPasteCanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
            e.Handled = true;
        }

        private void OnPasteExecuted(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            PasteClipboardToDataGrid();
            e.Handled = true;
        }

        private void OnCopyCanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = MainDataGrid.SelectedCells.Count > 0;
            e.Handled = true;
        }

        private void OnCopyExecuted(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            CopyDataGridToClipboard();
            e.Handled = true;
        }

        /// <summary>
        /// Sau khi cửa sổ đã hiển thị hoàn chỉnh trên màn hình,
        /// bắt đầu tải dữ liệu từ Inventor API (kèm progress bar).
        /// </summary>
        private void MainWindow_ContentRendered(object? sender, System.EventArgs e)
        {
            if (this.DataContext is ViewModels.MainViewModel vm)
            {
                vm.LoadData();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Mục "Giới thiệu" trên sidebar: nút bấm là ToggleButton (dùng chung NavRailButtonStyle
        // với các mục điều hướng khác) nên phải tự trả IsChecked về False sau khi hiện About,
        // tránh mục này bị "dính" sáng như đang active.
        private void NavAbout_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton toggle)
            {
                toggle.IsChecked = false;
            }

            System.Windows.MessageBox.Show(
                Tr.Instance["About_Body"],
                Tr.Instance["Nav_About"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ============================================================
        // XỬ LÝ MỞ PART/ASSEMBLY TỪ CỘT "HÌNH ẢNH" / "PART NUMBER" / "ASSEMBLY" (gắn trực tiếp
        // trên DataTemplate/ElementStyle, KHÔNG dò VisualTreeHelper, KHÔNG so sánh Header,
        // KHÔNG dùng Command binding) — xem README mục 21.
        // ============================================================

        // Dòng dữ liệu (ExportRow) của ô vừa bị right-click (Hình ảnh/Part Number/Assembly), được
        // chốt lại tại thời điểm ContextMenuOpening (khi Grid còn gắn trong Visual Tree chính), để
        // MenuItem_Click dùng lại thay vì phải dò qua DataContext của MenuItem/Popup (vốn không
        // đáng tin cậy). Dùng chung 1 field vì chỉ 1 context menu mở tại 1 thời điểm.
        private ExportRow? _contextMenuRow;

        // Dùng sự kiện MouseDoubleClick chuẩn của DataGrid (thay vì tự đếm ClickCount trên
        // MouseLeftButtonUp gắn ở từng cell) vì DataGrid quản lý selection ở MouseLeftButtonDown,
        // có thể làm lệch việc đếm click khi tự bắt ở cấp cell, khiến double-click không nhận ra
        // đủ 2 lần bấm. MouseDoubleClick là sự kiện gắn sẵn trên Control, đáng tin cậy hơn.
        private void MainDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (this.DataContext is not MainViewModel vm) return;

            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell?.Column == null) return;

            // Chỉ mở Part khi double-click đúng cột ảnh. Nhận diện qua token nội bộ "ImageColumn" (Header
            // thật) thay vì text hiển thị, vì text hiển thị đổi theo ngôn ngữ (HeaderTemplate ghi đè phần
            // hiển thị, Header chỉ còn dùng làm định danh nội bộ ở đây). Các cột khác (VD: "Dxf batch No")
            // cho phép sửa trực tiếp, double-click ở đó phải giữ hành vi edit mặc định của DataGrid.
            if (!string.Equals(cell.Column.Header?.ToString(), "ImageColumn", System.StringComparison.Ordinal)) return;

            if (cell.DataContext is ExportRow row)
            {
                vm.OpenPart(row);
                e.Handled = true;
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // Dùng chung cho ô Hình ảnh, ô Assembly, ô Part Number — logic hoàn toàn tổng quát (chỉ chốt
        // lại DataContext của element vừa bị right-click), không có gì riêng cho từng cột.
        private void RowCell_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            _contextMenuRow = (sender as FrameworkElement)?.DataContext as ExportRow;
        }

        private void MenuItemOpenPart_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is not MainViewModel vm) return;

            // Ưu tiên dòng đã chốt tại ContextMenuOpening (đáng tin cậy nhất).
            var row = _contextMenuRow;

            // Fallback: bắc cầu qua PlacementTarget của ContextMenu cha, phòng trường hợp
            // ContextMenuOpening không kịp chạy (ví dụ context menu mở bằng phím Menu/Shift+F10).
            if (row == null &&
                sender is MenuItem mi &&
                mi.Parent is ContextMenu cm &&
                cm.PlacementTarget is FrameworkElement placementTarget)
            {
                row = placementTarget.DataContext as ExportRow;
            }

            if (row != null)
            {
                vm.OpenPart(row);
            }
            else
            {
                vm.ShowWarning(Tr.Instance["Msg_ContextMenuRowUnknown"]);
            }
        }

        private void MenuItemOpenAssembly_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is not MainViewModel vm) return;

            var row = _contextMenuRow;

            if (row == null &&
                sender is MenuItem mi &&
                mi.Parent is ContextMenu cm &&
                cm.PlacementTarget is FrameworkElement placementTarget)
            {
                row = placementTarget.DataContext as ExportRow;
            }

            if (row != null)
            {
                vm.OpenAssembly(row);
            }
            else
            {
                vm.ShowWarning(Tr.Instance["Msg_ContextMenuRowUnknownAssembly"]);
            }
        }

        private void MainDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool isTextBox = e.OriginalSource is System.Windows.Controls.TextBox;
            bool isMultiSelect = MainDataGrid.SelectedCells.Count > 1;

            if (isTextBox && !isMultiSelect)
            {
                return;
            }

            bool isCtrlPressed = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;

            if (isCtrlPressed && e.Key == System.Windows.Input.Key.V)
            {
                MainDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
                MainDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

                PasteClipboardToDataGrid();
                e.Handled = true;
                return;
            }

            if (isCtrlPressed && e.Key == System.Windows.Input.Key.C)
            {
                CopyDataGridToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key == System.Windows.Input.Key.Delete || e.Key == System.Windows.Input.Key.Back)
            {
                if (ClearSelectedCells())
                {
                    e.Handled = true;
                }
            }
        }

        private void MenuItemCopy_Click(object sender, RoutedEventArgs e)
        {
            CopyDataGridToClipboard();
        }

        private void MenuItemPaste_Click(object sender, RoutedEventArgs e)
        {
            PasteClipboardToDataGrid();
        }

        private void MenuItemClear_Click(object sender, RoutedEventArgs e)
        {
            ClearSelectedCells();
        }

        private bool ClearSelectedCells()
        {
            var cells = MainDataGrid.SelectedCells;
            if (cells.Count > 0)
            {
                bool cleared = false;
                foreach (var cell in cells)
                {
                    if (cell.Column != null && !cell.Column.IsReadOnly)
                    {
                        SetCellValue(cell, "");
                        cleared = true;
                    }
                }
                return cleared;
            }
            return false;
        }

        private void PasteClipboardToDataGrid()
        {
            var clipboardData = System.Windows.Clipboard.GetText();
            if (string.IsNullOrEmpty(clipboardData)) return;

            var rows = clipboardData.Split(new string[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length == 0) return;

            var cells = MainDataGrid.SelectedCells;
            if (cells.Count == 0) return;

            bool isSingleCellClipboard = (rows.Length == 1 && !rows[0].Contains("\t"));

            if (isSingleCellClipboard)
            {
                string valueToPaste = rows[0];
                foreach (var cell in cells)
                {
                    SetCellValue(cell, valueToPaste);
                }
            }
            else
            {
                int minRowIndex = int.MaxValue;
                int minColumnIndex = int.MaxValue;

                foreach (var cell in cells)
                {
                    int rowIndex = MainDataGrid.Items.IndexOf(cell.Item);
                    int colIndex = cell.Column.DisplayIndex;

                    if (rowIndex < minRowIndex) minRowIndex = rowIndex;
                    if (colIndex < minColumnIndex) minColumnIndex = colIndex;
                }

                if (minRowIndex == int.MaxValue || minColumnIndex == int.MaxValue) return;

                for (int i = 0; i < rows.Length; i++)
                {
                    var rowData = rows[i].Split('\t');
                    int targetRowIndex = minRowIndex + i;

                    if (targetRowIndex >= MainDataGrid.Items.Count) break;

                    var targetItem = MainDataGrid.Items[targetRowIndex];

                    for (int j = 0; j < rowData.Length; j++)
                    {
                        int targetColumnIndex = minColumnIndex + j;
                        if (targetColumnIndex >= MainDataGrid.Columns.Count) break;

                        System.Windows.Controls.DataGridColumn? targetColumn = null;
                        foreach (var col in MainDataGrid.Columns)
                        {
                            if (col.DisplayIndex == targetColumnIndex)
                            {
                                targetColumn = col;
                                break;
                            }
                        }

                        if (targetColumn != null)
                        {
                            SetCellValue(new System.Windows.Controls.DataGridCellInfo(targetItem, targetColumn), rowData[j]);
                        }
                    }
                }
            }
        }

        private void SetCellValue(System.Windows.Controls.DataGridCellInfo cellInfo, string value)
        {
            if (cellInfo.Column == null || cellInfo.Column.IsReadOnly) return;

            if (cellInfo.Column is System.Windows.Controls.DataGridBoundColumn boundColumn)
            {
                var binding = boundColumn.Binding as System.Windows.Data.Binding;
                if (binding != null)
                {
                    var propertyPath = binding.Path.Path;
                    var propertyInfo = cellInfo.Item.GetType().GetProperty(propertyPath);
                    if (propertyInfo != null && propertyInfo.CanWrite)
                    {
                        try
                        {
                            var val = System.Convert.ChangeType(value, propertyInfo.PropertyType);
                            propertyInfo.SetValue(cellInfo.Item, val);
                        }
                        catch { }
                    }
                }
            }
        }

        private void CopyDataGridToClipboard()
        {
            try
            {
                var cells = MainDataGrid.SelectedCells;
                if (cells.Count == 0) return;

                var sortedCells = System.Linq.Enumerable.ToList(
                    System.Linq.Enumerable.ThenBy(
                        System.Linq.Enumerable.OrderBy(cells, c => MainDataGrid.Items.IndexOf(c.Item)),
                        c => c.Column.DisplayIndex)
                );

                var sb = new System.Text.StringBuilder();
                int currentRowIndex = -1;

                foreach (var cell in sortedCells)
                {
                    int rowIndex = MainDataGrid.Items.IndexOf(cell.Item);

                    if (currentRowIndex != -1 && rowIndex != currentRowIndex)
                    {
                        sb.AppendLine();
                    }
                    else if (currentRowIndex != -1)
                    {
                        sb.Append("\t");
                    }

                    currentRowIndex = rowIndex;

                    string cellValue = GetCellValue(cell) ?? "";
                    sb.Append(cellValue);
                }

                if (sb.Length > 0)
                {
                    System.Windows.Clipboard.SetText(sb.ToString());
                }
            }
            catch { }
        }

        private string? GetCellValue(System.Windows.Controls.DataGridCellInfo cellInfo)
        {
            if (cellInfo.Column is System.Windows.Controls.DataGridBoundColumn boundColumn)
            {
                var binding = boundColumn.Binding as System.Windows.Data.Binding;
                if (binding != null)
                {
                    var propertyPath = binding.Path.Path;
                    var propertyInfo = cellInfo.Item.GetType().GetProperty(propertyPath);
                    if (propertyInfo != null && propertyInfo.CanRead)
                    {
                        var val = propertyInfo.GetValue(cellInfo.Item);
                        return val?.ToString();
                    }
                }
            }
            return null;
        }
    }
}
