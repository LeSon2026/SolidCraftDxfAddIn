using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ExportDxfAddIn.Models;
using ExportDxfAddIn.Services;

namespace ExportDxfAddIn.ViewModels
{
    /// <summary>1 dòng hiển thị trong tab Lịch sử xuất — bọc quanh ExportHistoryEntry để thêm các
    /// property tính sẵn cho UI (giờ local, nhãn "(chưa gán batch)" khi rỗng) mà không đụng vào
    /// model dữ liệu thuần của tầng lưu trữ.</summary>
    public class ExportHistoryRowVm : BindableBase
    {
        public ExportHistoryEntry Entry { get; }

        public ExportHistoryRowVm(ExportHistoryEntry entry)
        {
            Entry = entry;
        }

        public string ExportedAtDisplay => Entry.ExportedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

        public string BatchNoDisplay => string.IsNullOrWhiteSpace(Entry.BatchNo) ? Tr.Instance["Filter_Empty"] : Entry.BatchNo;

        public string PartNumber => Entry.PartNumber;
        public string Revision => Entry.Revision;
        public int Quantity => Entry.Quantity;
        public string Description => Entry.Description;
        public string FileName => Entry.FileName;
        public string UserName => Entry.UserName;
        public string MachineName => Entry.MachineName;

        public void NotifyLanguageChanged() => OnPropertyChanged(nameof(BatchNoDisplay));
    }

    /// <summary>ViewModel cho tab "Lịch sử xuất" — chỉ đọc, scope theo đúng 1 file DB lịch sử của
    /// assembly tổng đang mở trong "Xuất DXF" (xem MainViewModel.HistoryDbPath/DxfHistoryService).</summary>
    public class ExportHistoryViewModel : BindableBase
    {
        private readonly DxfHistoryService _historyService = new DxfHistoryService();
        private ICollectionView? _collectionView;

        public ObservableCollection<ExportHistoryRowVm> Rows { get; } = new ObservableCollection<ExportHistoryRowVm>();

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _collectionView?.Refresh();
                }
            }
        }

        private bool _hasHistory;
        public bool HasHistory
        {
            get => _hasHistory;
            set { if (SetProperty(ref _hasHistory, value)) OnPropertyChanged(nameof(HasNoHistory)); }
        }

        /// <summary>Nghịch đảo của HasHistory — để XAML bind trạng thái trống bằng đúng
        /// BooleanToVisibilityConverter đã dùng ở nơi khác, không cần thêm converter mới.</summary>
        public bool HasNoHistory => !HasHistory;

        public ICommand ExportExcelCommand { get; }

        public ExportHistoryViewModel()
        {
            _collectionView = CollectionViewSource.GetDefaultView(Rows);
            _collectionView.Filter = FilterRow;
            ExportExcelCommand = new DelegateCommand(_ => ExecuteExportExcel(), _ => HasHistory);
        }

        /// <summary>Đọc lại toàn bộ lịch sử từ file DB tương ứng với assembly tổng đang mở. Gọi mỗi
        /// khi người dùng chuyển sang tab này — chi phí rẻ (SQLite local), không cần cache phức tạp.</summary>
        public void LoadHistory(string? dbPath)
        {
            Rows.Clear();
            if (string.IsNullOrEmpty(dbPath))
            {
                HasHistory = false;
                return;
            }

            var entries = _historyService.GetHistory(dbPath);
            foreach (var e in entries)
            {
                Rows.Add(new ExportHistoryRowVm(e));
            }
            HasHistory = Rows.Count > 0;
        }

        public void NotifyLanguageChanged()
        {
            foreach (var row in Rows)
            {
                row.NotifyLanguageChanged();
            }
        }

        /// <summary>Luôn xuất toàn bộ lịch sử đã nạp (Rows), bất kể ô tìm kiếm đang lọc gì trên màn
        /// hình — quyết định của người dùng khi yêu cầu tính năng này (đơn giản, dữ liệu đầy đủ,
        /// người dùng tự lọc lại trong Excel nếu cần).</summary>
        private void ExecuteExportExcel()
        {
            if (Rows.Count == 0)
            {
                System.Windows.MessageBox.Show(Tr.Instance["Msg_HistoryNoDataToExport"], Tr.Instance["Title_Notice"]);
                return;
            }

            string safePartNumber = SanitizeFileName(Rows[0].PartNumber);
            string suggestedName = $"LichSuXuat_{safePartNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = suggestedName
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var entries = Rows.Select(r => r.Entry).ToList();
                new ExcelReportService().ExportHistoryToFile(entries, dlg.FileName);
                System.Windows.MessageBox.Show(Tr.Instance.Format("Msg_HistoryExcelSuccess", dlg.FileName), Tr.Instance["Title_Report"]);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Tr.Instance.Format("Msg_HistoryExcelError", ex.Message), Tr.Instance["Title_ExcelError"]);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private bool FilterRow(object obj)
        {
            if (obj is not ExportHistoryRowVm row) return true;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string kw = SearchText.ToLower();
            return (row.PartNumber?.ToLower().Contains(kw) == true) ||
                   (row.Entry.BatchNo?.ToLower().Contains(kw) == true) ||
                   (row.FileName?.ToLower().Contains(kw) == true) ||
                   (row.Description?.ToLower().Contains(kw) == true);
        }
    }
}
