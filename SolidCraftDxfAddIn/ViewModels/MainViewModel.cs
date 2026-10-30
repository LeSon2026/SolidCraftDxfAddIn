using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ExportDxfAddIn.Models;
using ExportDxfAddIn.Services;
using MessageBox = System.Windows.MessageBox;

namespace ExportDxfAddIn.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private readonly ICollectionView _collectionView;
        private readonly Dictionary<string, SolidCraftSheetItem> _sheetItemsMap = new(StringComparer.OrdinalIgnoreCase);
        private SolidCraftLoadResult? _lastLoadResult;
        private string? _historyDbPath;
        private CancellationTokenSource? _cts;

        public RangeObservableCollection<ExportRow> ExportRows { get; } = new();

        public string? HistoryDbPath => _historyDbPath;

        private string _saveDirectory = "";
        public string SaveDirectory
        {
            get => _saveDirectory;
            set => SetProperty(ref _saveDirectory, value);
        }

        private string _assemblyFileName = "";
        public string AssemblyFileName
        {
            get => _assemblyFileName;
            set => SetProperty(ref _assemblyFileName, value);
        }

        private int _asmQuantity = 1;
        public int AsmQuantity
        {
            get => _asmQuantity;
            set
            {
                if (value < 1) value = 1;
                SetProperty(ref _asmQuantity, value);
            }
        }

        private bool _isCombine = true;
        public bool IsCombine
        {
            get => _isCombine;
            set => SetProperty(ref _isCombine, value);
        }

        private bool _isFlip = true;
        public bool IsFlip
        {
            get => _isFlip;
            set => SetProperty(ref _isFlip, value);
        }

        private bool _isExportExcel = true;
        public bool IsExportExcel
        {
            get => _isExportExcel;
            set => SetProperty(ref _isExportExcel, value);
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        private int _displayedCount;
        public int DisplayedCount
        {
            get => _displayedCount;
            private set
            {
                if (SetProperty(ref _displayedCount, value))
                {
                    OnPropertyChanged(nameof(DisplayedCountText));
                }
            }
        }

        private int _readyCount;
        public int ReadyCount
        {
            get => _readyCount;
            private set
            {
                if (SetProperty(ref _readyCount, value))
                {
                    OnPropertyChanged(nameof(ReadyCountText));
                }
            }
        }

        private int _errorCount;
        public int ErrorCount
        {
            get => _errorCount;
            private set
            {
                if (SetProperty(ref _errorCount, value))
                {
                    OnPropertyChanged(nameof(ErrorCountText));
                }
            }
        }

        private int _uncheckedCount;
        public int UncheckedCount
        {
            get => _uncheckedCount;
            private set
            {
                if (SetProperty(ref _uncheckedCount, value))
                {
                    OnPropertyChanged(nameof(UncheckedCountText));
                }
            }
        }

        private int _totalQuantity;
        public int TotalQuantity
        {
            get => _totalQuantity;
            private set
            {
                if (SetProperty(ref _totalQuantity, value))
                {
                    OnPropertyChanged(nameof(TotalQuantityText));
                }
            }
        }

        public string DisplayedCountText => Tr.Instance.Format("Count_Displayed", DisplayedCount);
        public string ReadyCountText => Tr.Instance.Format("Count_Ready", ReadyCount);
        public string ErrorCountText => Tr.Instance.Format("Count_Error", ErrorCount);
        public string UncheckedCountText => Tr.Instance.Format("Count_Unchecked", UncheckedCount);
        public string TotalQuantityText => Tr.Instance.Format("Count_TotalQuantity", TotalQuantity);

        public ColumnFilter PartNumberFilter { get; }
        public ColumnFilter AssemblyFilter { get; }
        public ColumnFilter RevisionNumberFilter { get; }
        public ColumnFilter SoLuongFilter { get; }
        public ColumnFilter ChieuDayFilter { get; }
        public ColumnFilter TenFileFilter { get; }
        public ColumnFilter DxfBatchFilter { get; }
        public ColumnFilter GhiChuFilter { get; }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private int _loadingProgress;
        public int LoadingProgress
        {
            get => _loadingProgress;
            set
            {
                if (SetProperty(ref _loadingProgress, value))
                {
                    OnPropertyChanged(nameof(LoadingStatusText));
                }
            }
        }

        private string _loadingMessage = "";
        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                if (SetProperty(ref _loadingMessage, value))
                {
                    OnPropertyChanged(nameof(LoadingStatusText));
                }
            }
        }

        private string _loadingTimeText = "";
        public string LoadingTimeText
        {
            get => _loadingTimeText;
            set
            {
                if (SetProperty(ref _loadingTimeText, value))
                {
                    OnPropertyChanged(nameof(LoadingStatusText));
                }
            }
        }

        public string LoadingStatusText => Tr.Instance.Format("Loading_TimeFormat", LoadingMessage, LoadingProgress, LoadingTimeText);

        private string _lastRefreshedText = "";
        public string LastRefreshedText
        {
            get => _lastRefreshedText;
            set => SetProperty(ref _lastRefreshedText, value);
        }

        public ICommand ExportCommand { get; }
        public ICommand ValidateFlatPatternCommand { get; }
        public ICommand RecalculateCommand { get; }
        public ICommand ClearAllFiltersCommand { get; }
        public ICommand RefreshModelCommand { get; }
        public ICommand ConnectLiveCommand { get; }
        public ICommand OpenDesignFileCommand { get; }
        public ICommand ConfigureCliPathCommand { get; }
        public ICommand OpenPartCommand { get; }

        public MainViewModel()
        {
            _collectionView = CollectionViewSource.GetDefaultView(ExportRows);
            _collectionView.Filter = FilterRow;

            PartNumberFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.PartNumber)) };
            AssemblyFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.AssemblyName)) };
            RevisionNumberFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.RevisionNumber)) };
            SoLuongFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.SoLuong.ToString())) };
            ChieuDayFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.ChieuDayVatLieu)) };
            TenFileFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.TenFile)) };
            DxfBatchFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.DxfBatchNo)) };
            GhiChuFilter = new ColumnFilter { OnFilterChanged = () => ApplyFilter(), OnOpening = f => f.RefreshValues(GetFilterValuesExcept(f, r => r.GhiChu)) };

            ExportCommand = new DelegateCommand(async _ => await ExecuteExportAsync());
            ValidateFlatPatternCommand = new DelegateCommand(_ => ExecuteValidateFlatPattern());
            RecalculateCommand = new DelegateCommand(async _ => await RecalculateAsync());
            ClearAllFiltersCommand = new DelegateCommand(_ => ClearAllFilters());
            RefreshModelCommand = new DelegateCommand(async _ => await ReloadCurrentModelAsync());
            ConnectLiveCommand = new DelegateCommand(async _ => await ConnectLiveAsync());
            OpenDesignFileCommand = new DelegateCommand(async _ => await OpenDesignFileDialogAsync());
            ConfigureCliPathCommand = new DelegateCommand(_ => ConfigureCliPath());
            OpenPartCommand = new DelegateCommand(param => OpenPart(param as ExportRow));
        }

        public void NotifyLanguageChanged()
        {
            OnPropertyChanged(nameof(DisplayedCountText));
            OnPropertyChanged(nameof(ReadyCountText));
            OnPropertyChanged(nameof(ErrorCountText));
            OnPropertyChanged(nameof(UncheckedCountText));
            OnPropertyChanged(nameof(TotalQuantityText));
            OnPropertyChanged(nameof(LoadingStatusText));

            PartNumberFilter.NotifyLanguageChanged();
            AssemblyFilter.NotifyLanguageChanged();
            RevisionNumberFilter.NotifyLanguageChanged();
            SoLuongFilter.NotifyLanguageChanged();
            ChieuDayFilter.NotifyLanguageChanged();
            TenFileFilter.NotifyLanguageChanged();
            DxfBatchFilter.NotifyLanguageChanged();
            GhiChuFilter.NotifyLanguageChanged();

            foreach (var r in ExportRows)
            {
                r.NotifyLanguageChanged();
            }
        }

        public void CancelThumbnailLoading()
        {
            _cts?.Cancel();
        }

        public void LoadData()
        {
            if (SolidCraftCliService.Instance.TryGetLiveControlEndpoint(out _, out _))
            {
                _ = ConnectLiveAsync();
            }
            else
            {
                AssemblyFileName = "Chưa kết nối SolidCraft. Vui lòng bấm 'Kết nối (Live)' hoặc 'Mở file (.sc/.step)'.";
                LastRefreshedText = DateTime.Now.ToString("HH:mm:ss");
            }
        }

        public async Task ConnectLiveAsync()
        {
            try
            {
                IsLoading = true;
                LoadingProgress = 0;
                LoadingMessage = "Đang kết nối SolidCraft Desktop (Live)...";

                var progress = new Progress<(int current, int total, string message)>(p =>
                {
                    LoadingProgress = (int)((double)p.current / Math.Max(1, p.total) * 100);
                    LoadingMessage = p.message;
                });

                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                var res = await SolidCraftCliService.Instance.LoadDesignAsync(
                    designFilePath: null,
                    useLiveApp: true,
                    captureThumbnails: false,
                    progress: progress,
                    ct: _cts.Token
                );

                PopulateFromLoadResult(res);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi kết nối SolidCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadDesignFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                IsLoading = true;
                LoadingProgress = 0;
                LoadingMessage = $"Đang đọc file: {Path.GetFileName(filePath)}...";

                var progress = new Progress<(int current, int total, string message)>(p =>
                {
                    LoadingProgress = (int)((double)p.current / Math.Max(1, p.total) * 100);
                    LoadingMessage = p.message;
                });

                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                var res = await SolidCraftCliService.Instance.LoadDesignAsync(
                    designFilePath: filePath,
                    useLiveApp: false,
                    captureThumbnails: true,
                    progress: progress,
                    ct: _cts.Token
                );

                PopulateFromLoadResult(res);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi đọc file SolidCraft", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task OpenDesignFileDialogAsync()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = Tr.Instance["Title_SelectDesignFile"],
                Filter = Tr.Instance["Filter_DesignFiles"]
            };

            if (ofd.ShowDialog() == true)
            {
                await LoadDesignFromFileAsync(ofd.FileName);
            }
        }

        public void ConfigureCliPath()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn file solidcraft-cli.exe",
                Filter = "solidcraft-cli (solidcraft-cli.exe)|solidcraft-cli.exe|Executable (*.exe)|*.exe"
            };

            if (ofd.ShowDialog() == true)
            {
                SolidCraftCliService.Instance.CustomCliPath = ofd.FileName;
                MessageBox.Show($"Đã lưu đường dẫn CLI:\n{ofd.FileName}", "Cấu hình CLI", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public async Task ReloadCurrentModelAsync()
        {
            try
            {
                IsLoading = true;
                LoadingProgress = 0;
                LoadingMessage = "Đang kiểm tra và cập nhật các liên kết (Update Links)...";

                string? designPath = _lastLoadResult?.DesignPath;
                bool isLive = _lastLoadResult?.IsFromLiveApp ?? true;

                var updateRes = await SolidCraftCliService.Instance.UpdateLinksAsync(designPath, isLive).ConfigureAwait(true);
                if (updateRes.UpdatedComponents.Count > 0 || updateRes.Warnings.Count > 0)
                {
                    var msg = new StringBuilder();
                    if (updateRes.UpdatedComponents.Count > 0)
                    {
                        msg.AppendLine($"Đã cập nhật các liên kết: {string.Join(", ", updateRes.UpdatedComponents)}");
                        if (!string.IsNullOrEmpty(updateRes.BackupPath))
                        {
                            msg.AppendLine($"Bản sao lưu: {Path.GetFileName(updateRes.BackupPath)}");
                        }
                    }
                    if (updateRes.Warnings.Count > 0)
                    {
                        msg.AppendLine($"Cảnh báo khi cập nhật liên kết:\n{string.Join("\n", updateRes.Warnings)}");
                    }
                    MessageBox.Show(msg.ToString().TrimEnd(), "Cập nhật liên kết", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // Warning, không crash khi update links gặp lỗi
                MessageBox.Show($"Không thể tự động cập nhật liên kết: {ex.Message}", "Cảnh báo liên kết", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsLoading = false;
            }

            if (_lastLoadResult != null)
            {
                if (_lastLoadResult.IsFromLiveApp)
                {
                    await ConnectLiveAsync();
                }
                else if (!string.IsNullOrEmpty(_lastLoadResult.DesignPath))
                {
                    await LoadDesignFromFileAsync(_lastLoadResult.DesignPath);
                }
            }
            else
            {
                await ConnectLiveAsync();
            }
        }

        private void PopulateFromLoadResult(SolidCraftLoadResult result)
        {
            _lastLoadResult = result;
            _sheetItemsMap.Clear();

            AssemblyFileName = !string.IsNullOrEmpty(result.DesignName) ? result.DesignName : "SolidCraft Design";

            if (!string.IsNullOrEmpty(result.DesignPath) && File.Exists(result.DesignPath))
            {
                var dir = Path.GetDirectoryName(result.DesignPath);
                _historyDbPath = Path.Combine(dir ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $"{Path.GetFileNameWithoutExtension(result.DesignPath)}_DxfHistory.db");
                if (string.IsNullOrEmpty(SaveDirectory))
                {
                    SaveDirectory = Path.Combine(dir ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DXF_Export");
                }
            }
            else
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SolidCraftDxfAddIn");
                if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
                _historyDbPath = Path.Combine(appData, "SolidCraft_DxfHistory.db");
                if (string.IsNullOrEmpty(SaveDirectory))
                {
                    SaveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DXF_Export");
                }
            }

            var previousBatchNo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in ExportRows)
            {
                if (!string.IsNullOrEmpty(r.PartNumber))
                {
                    previousBatchNo[r.PartNumber] = r.DxfBatchNo;
                }
            }

            var newRows = new List<ExportRow>();
            foreach (var item in result.SheetItems)
            {
                _sheetItemsMap[item.BodyName] = item;

                var thickStr = item.ActualThicknessMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                var matName = item.MaterialName.Replace(" ", "");
                var spec = $"PL{thickStr}-{matName}";

                var row = new ExportRow
                {
                    PartNumber = item.PartNumber,
                    AssemblyName = result.DesignName,
                    AssemblyFilePath = result.DesignPath,
                    RevisionNumber = item.Revision,
                    SoLuong = Math.Max(1, item.Quantity) * AsmQuantity,
                    ChieuDayVatLieu = spec,
                    TenFile = item.BodyName,
                    BodyName = item.BodyName,
                    FullPath = item.IsLinked ? item.ExportDesignPath : result.DesignPath,
                    HasFlatPatternError = item.HasFlatPatternError,
                    HasThicknessRuleMismatch = item.ThicknessRuleMismatch,
                    ThicknessRuleMismatchDetail = item.ThicknessRuleMismatchDetail,
                    HasDescMismatch = item.DescriptionMismatch,
                    IsLinked = item.IsLinked,
                    LinkedSourceName = item.LinkedSourceName,
                    IsLinkStale = item.IsLinkStale,
                    IsValidated = true
                };

                if (previousBatchNo.TryGetValue(row.PartNumber, out var bNo))
                {
                    row.DxfBatchNo = bNo;
                }

                if (!string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(item.ThumbnailPath, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        row.Thumbnail = bmp;
                    }
                    catch { }
                }

                newRows.Add(row);
            }

            ExportRows.ReplaceAll(newRows);
            PopulateAllFilters();
            ApplyFilter();

            LastRefreshedText = DateTime.Now.ToString("HH:mm:ss");

            if (result.Warnings.Count > 0 || result.UnrecognizedBodies.Count > 0)
            {
                var sbNotice = new StringBuilder();
                if (result.UnrecognizedBodies.Count > 0)
                {
                    sbNotice.AppendLine(Tr.Instance["Msg_UnrecognizedBodiesHeader"]);
                    foreach (var ub in result.UnrecognizedBodies)
                    {
                        sbNotice.AppendLine($" • {ub}");
                    }
                    sbNotice.AppendLine();
                }
                if (result.Warnings.Count > 0)
                {
                    sbNotice.AppendLine(Tr.Instance["Msg_LoadWarningsHeader"]);
                    foreach (var w in result.Warnings)
                    {
                        sbNotice.AppendLine($" • {w}");
                    }
                }
                MessageBox.Show(sbNotice.ToString().TrimEnd(), Tr.Instance["Title_Notice"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public async Task RecalculateAsync()
        {
            if (ExportRows.Count == 0) return;

            foreach (var r in ExportRows)
            {
                var bodyName = !string.IsNullOrEmpty(r.BodyName) ? r.BodyName : r.TenFile;
                if (_sheetItemsMap.TryGetValue(bodyName, out var item))
                {
                    r.SoLuong = Math.Max(1, item.Quantity) * AsmQuantity;
                }
            }

            ApplyFilter();
            await Task.CompletedTask;
        }

        private void ExecuteValidateFlatPattern()
        {
            SolidCraftDxfExportManager.Instance.ValidateFlatPatterns(ExportRows, _sheetItemsMap);
            ApplyFilter();
            MessageBox.Show(Tr.Instance["Msg_ValidateCompleted"], Tr.Instance["Title_Validate"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static List<ExportRow> MergeRowsByPartNumber(List<ExportRow> rows)
        {
            var merged = new List<ExportRow>();

            foreach (var group in rows.GroupBy(r => r.PartNumber ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var members = group.ToList();
                var first = members[0];

                if (members.Count == 1)
                {
                    merged.Add(first);
                    continue;
                }

                merged.Add(new ExportRow
                {
                    PartNumber = first.PartNumber,
                    AssemblyName = first.AssemblyName,
                    AssemblyFilePath = first.AssemblyFilePath,
                    RevisionNumber = first.RevisionNumber,
                    SoLuong = members.Sum(r => r.SoLuong),
                    ChieuDayVatLieu = first.ChieuDayVatLieu,
                    TenFile = first.TenFile,
                    BodyName = first.BodyName,
                    DxfBatchNo = members.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.DxfBatchNo))?.DxfBatchNo ?? "",
                    FullPath = first.FullPath,
                    HasFlatPattern = first.HasFlatPattern,
                    HasFlatPatternError = members.Any(r => r.HasFlatPatternError),
                    HasThicknessRuleMismatch = members.Any(r => r.HasThicknessRuleMismatch),
                    ThicknessRuleMismatchDetail = members.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.ThicknessRuleMismatchDetail))?.ThicknessRuleMismatchDetail ?? "",
                    HasDescMismatch = members.Any(r => r.HasDescMismatch),
                    IsValidated = members.All(r => r.IsValidated),
                    Thumbnail = first.Thumbnail
                });
            }

            return merged;
        }

        private async Task ExecuteExportAsync()
        {
            var displayedRows = _collectionView.Cast<ExportRow>().ToList();
            if (displayedRows.Count == 0)
            {
                MessageBox.Show(Tr.Instance["Msg_NoRowsSelected"], Tr.Instance["Title_Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var checkedRows = MergeRowsByPartNumber(displayedRows);
            int emptyBatch = checkedRows.Count(r => string.IsNullOrWhiteSpace(r.DxfBatchNo));
            var sbConfirm = new StringBuilder();
            sbConfirm.AppendLine(Tr.Instance.Format("Msg_ExportConfirmCount", checkedRows.Count));
            if (checkedRows.Count < displayedRows.Count)
            {
                sbConfirm.AppendLine();
                sbConfirm.AppendLine(Tr.Instance.Format("Msg_ExportMergedRows", displayedRows.Count, checkedRows.Count));
            }
            if (emptyBatch > 0)
            {
                sbConfirm.AppendLine();
                sbConfirm.AppendLine(Tr.Instance.Format("Msg_EmptyBatchWarning", emptyBatch));
            }
            sbConfirm.AppendLine();
            sbConfirm.Append(Tr.Instance["Msg_ConfirmContinue"]);

            if (MessageBox.Show(sbConfirm.ToString(), Tr.Instance["Title_Confirm"], MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            // Chọn thư mục lưu
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                fbd.Description = Tr.Instance["Dialog_ChooseSaveFolder"];
                fbd.UseDescriptionForTitle = true;
                fbd.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(SaveDirectory) && Directory.Exists(SaveDirectory))
                {
                    fbd.SelectedPath = SaveDirectory;
                }

                if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }
                SaveDirectory = fbd.SelectedPath;
            }

            try
            {
                IsLoading = true;
                LoadingProgress = 0;
                LoadingMessage = "Đang xuất bản trải phẳng DXF...";

                var progress = new Progress<(int current, int total, string message)>(p =>
                {
                    LoadingProgress = (int)((double)p.current / Math.Max(1, p.total) * 100);
                    LoadingMessage = p.message;
                });

                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                var (logOK, logFail, logFlip, historyWarning, exportedItems) = await SolidCraftDxfExportManager.Instance.ExportSelectedRowsAsync(
                    selectedRows: checkedRows,
                    saveDir: SaveDirectory,
                    isCombine: IsCombine,
                    isFlip: IsFlip,
                    isExportExcel: IsExportExcel,
                    excelFilePath: null,
                    historyDbPath: _historyDbPath,
                    designFileName: AssemblyFileName,
                    designFilePath: _lastLoadResult?.DesignPath,
                    isLiveApp: _lastLoadResult?.IsFromLiveApp ?? false,
                    sheetItemsMap: _sheetItemsMap,
                    progress: progress,
                    ct: _cts.Token
                );

                // Ghi file log kết quả
                var logFilePath = Path.Combine(SaveDirectory, $"ExportLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                var sb = new StringBuilder();
                sb.AppendLine("=== KẾT QUẢ XUẤT DXF SOLIDCRAFT ===");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine($"Số file xuất thành công: {logOK.Count}");
                foreach (var ok in logOK) sb.AppendLine("  [OK] " + ok);

                if (logFlip.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Chi tiết tự động lật mặt (Flip): {logFlip.Count}");
                    foreach (var fl in logFlip) sb.AppendLine("  [FLIP] " + fl);
                }

                if (logFail.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Số file lỗi: {logFail.Count}");
                    foreach (var f in logFail) sb.AppendLine("  [FAIL] " + f);
                }

                try
                {
                    File.WriteAllText(logFilePath, sb.ToString(), Encoding.UTF8);
                }
                catch { }

                var summaryMsg = $"Xuất thành công {logOK.Count}/{checkedRows.Count} chi tiết.\n";
                if (logFail.Count > 0) summaryMsg += $"Có {logFail.Count} chi tiết bị lỗi (xem file log).\n";
                if (!string.IsNullOrEmpty(historyWarning)) summaryMsg += $"\nCảnh báo lịch sử: {historyWarning}\n";
                summaryMsg += $"\nThư mục lưu: {SaveDirectory}";

                MessageBox.Show(summaryMsg, Tr.Instance["Title_ExportCompleted"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi quá trình xuất DXF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void OpenPart(ExportRow? row)
        {
            if (row == null) return;
            var path = !string.IsNullOrEmpty(row.FullPath) ? row.FullPath : row.AssemblyFilePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        public void OpenAssembly(ExportRow? row)
        {
            if (row == null) return;
            var path = !string.IsNullOrEmpty(row.AssemblyFilePath) ? row.AssemblyFilePath : row.FullPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        public void ShowWarning(string message, string? title = null)
        {
            title ??= Tr.Instance["Title_Warning"];
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void PopulateAllFilters()
        {
            PartNumberFilter.Populate(ExportRows.Select(r => r.PartNumber));
            AssemblyFilter.Populate(ExportRows.Select(r => r.AssemblyName));
            RevisionNumberFilter.Populate(ExportRows.Select(r => r.RevisionNumber));
            SoLuongFilter.Populate(ExportRows.Select(r => r.SoLuong.ToString()));
            ChieuDayFilter.Populate(ExportRows.Select(r => r.ChieuDayVatLieu));
            TenFileFilter.Populate(ExportRows.Select(r => r.TenFile));
            DxfBatchFilter.Populate(ExportRows.Select(r => r.DxfBatchNo));
            GhiChuFilter.Populate(ExportRows.Select(r => r.GhiChu));
        }

        private IEnumerable<string> GetFilterValuesExcept(ColumnFilter excludeFilter, Func<ExportRow, string> selector)
        {
            var kw = SearchText?.Trim().ToLowerInvariant();
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in ExportRows)
            {
                if (!string.IsNullOrEmpty(kw))
                {
                    bool match = (row.PartNumber?.ToLower().Contains(kw) == true) ||
                                 (row.AssemblyName?.ToLower().Contains(kw) == true) ||
                                 (row.ChieuDayVatLieu?.ToLower().Contains(kw) == true) ||
                                 (row.TenFile?.ToLower().Contains(kw) == true) ||
                                 (row.DxfBatchNo?.ToLower().Contains(kw) == true);
                    if (!match) continue;
                }

                if (excludeFilter != PartNumberFilter && !PartNumberFilter.IsValueAllowed(row.PartNumber)) continue;
                if (excludeFilter != AssemblyFilter && !AssemblyFilter.IsValueAllowed(row.AssemblyName)) continue;
                if (excludeFilter != RevisionNumberFilter && !RevisionNumberFilter.IsValueAllowed(row.RevisionNumber)) continue;
                if (excludeFilter != SoLuongFilter && !SoLuongFilter.IsValueAllowed(row.SoLuong.ToString())) continue;
                if (excludeFilter != ChieuDayFilter && !ChieuDayFilter.IsValueAllowed(row.ChieuDayVatLieu)) continue;
                if (excludeFilter != TenFileFilter && !TenFileFilter.IsValueAllowed(row.TenFile)) continue;
                if (excludeFilter != DxfBatchFilter && !DxfBatchFilter.IsValueAllowed(row.DxfBatchNo)) continue;
                if (excludeFilter != GhiChuFilter && !GhiChuFilter.IsValueAllowed(row.GhiChu)) continue;

                var val = selector(row);
                if (val != null) results.Add(val);
            }

            return results;
        }

        private bool FilterRow(object obj)
        {
            if (obj is not ExportRow row) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string kw = SearchText.ToLower();
                bool match = (row.PartNumber?.ToLower().Contains(kw) == true) ||
                             (row.AssemblyName?.ToLower().Contains(kw) == true) ||
                             (row.RevisionNumber?.ToLower().Contains(kw) == true) ||
                             (row.TenFile?.ToLower().Contains(kw) == true) ||
                             (row.GhiChu?.ToLower().Contains(kw) == true) ||
                             (row.ChieuDayVatLieu?.ToLower().Contains(kw) == true);
                if (!match) return false;
            }

            if (!PartNumberFilter.IsValueAllowed(row.PartNumber)) return false;
            if (!AssemblyFilter.IsValueAllowed(row.AssemblyName)) return false;
            if (!RevisionNumberFilter.IsValueAllowed(row.RevisionNumber)) return false;
            if (!SoLuongFilter.IsValueAllowed(row.SoLuong.ToString())) return false;
            if (!ChieuDayFilter.IsValueAllowed(row.ChieuDayVatLieu)) return false;
            if (!TenFileFilter.IsValueAllowed(row.TenFile)) return false;
            if (!DxfBatchFilter.IsValueAllowed(row.DxfBatchNo)) return false;
            if (!GhiChuFilter.IsValueAllowed(row.GhiChu)) return false;

            return true;
        }

        private void ApplyFilter()
        {
            _collectionView.Refresh();

            int totalQty = 0;
            int ready = 0;
            int err = 0;
            int unchk = 0;
            int dispCount = 0;

            foreach (var item in _collectionView)
            {
                if (item is ExportRow row)
                {
                    dispCount++;
                    totalQty += row.SoLuong;
                    if (!row.IsValidated) unchk++;
                    else if (row.IsReady) ready++;
                    else err++;
                }
            }

            DisplayedCount = dispCount;
            TotalQuantity = totalQty;
            ReadyCount = ready;
            ErrorCount = err;
            UncheckedCount = unchk;
        }

        private void ClearAllFilters()
        {
            SearchText = "";
            PartNumberFilter.Reset();
            AssemblyFilter.Reset();
            RevisionNumberFilter.Reset();
            SoLuongFilter.Reset();
            ChieuDayFilter.Reset();
            TenFileFilter.Reset();
            DxfBatchFilter.Reset();
            GhiChuFilter.Reset();

            PopulateAllFilters();
            ApplyFilter();
        }
    }
}
