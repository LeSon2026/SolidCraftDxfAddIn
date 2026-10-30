using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using ExportDxfAddIn.ViewModels;

namespace ExportDxfAddIn.Services
{
    public enum AppLanguage
    {
        Vietnamese,
        English
    }

    /// <summary>
    /// Singleton dịch thuật cho toàn bộ UI/thông báo. XAML dùng binding indexer
    /// (Source={x:Static loc:Tr.Instance}, Path=[Key]) để tự cập nhật khi đổi ngôn ngữ
    /// (PropertyChanged raise với "Item[]"); code-behind/ViewModel dùng Tr.Instance["Key"].
    /// </summary>
    public sealed class Tr : INotifyPropertyChanged
    {
        public static Tr Instance { get; } = new Tr();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? LanguageChanged;

        private AppLanguage _language = AppLanguage.Vietnamese;
        public AppLanguage Language => _language;

        public bool IsVietnamese => _language == AppLanguage.Vietnamese;
        public bool IsEnglish => _language == AppLanguage.English;

        public ICommand SetLanguageCommand { get; }

        public string this[string key] => Get(key);

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ExportDxfAddIn", "settings.json");

        private Tr()
        {
            SetLanguageCommand = new DelegateCommand(o => SetLanguage(o as string ?? "vi"));
            LoadSavedLanguage();
        }

        public string Get(string key)
        {
            if (_map.TryGetValue(key, out var byLang) && byLang.TryGetValue(_language, out var value))
            {
                return value;
            }
            return key;
        }

        public string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        public void SetLanguage(string code)
        {
            SetLanguage(string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.English : AppLanguage.Vietnamese);
        }

        public void SetLanguage(AppLanguage language)
        {
            if (_language == language) return;
            _language = language;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVietnamese)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
            LanguageChanged?.Invoke();

            SaveLanguage();
        }

        private void LoadSavedLanguage()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("language", out var langEl))
                    {
                        var code = langEl.GetString();
                        _language = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)
                            ? AppLanguage.English
                            : AppLanguage.Vietnamese;
                    }
                }
            }
            catch
            {
                // Không đọc được settings -> giữ mặc định Tiếng Việt.
            }
        }

        private void SaveLanguage()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var json = JsonSerializer.Serialize(new { language = _language == AppLanguage.English ? "en" : "vi" });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Không lưu được cũng không sao, chỉ mất lựa chọn ngôn ngữ ở lần mở kế tiếp.
            }
        }

        private static readonly Dictionary<string, Dictionary<AppLanguage, string>> _map = new()
        {
            // ===== Sidebar (app shell — thêm khi tái cấu trúc UI theo hướng nhiều màn hình) =====
            ["Nav_ExportDxf"] = Vi("Xuất DXF").En("Export DXF"),
            ["Nav_ExportHistory"] = Vi("Lịch sử xuất").En("Export History"),
            ["Nav_LayerConfig"] = Vi("Cấu hình Layer").En("Layer Settings"),
            ["Nav_About"] = Vi("Giới thiệu").En("About"),
            ["Badge_ComingSoon"] = Vi("Sắp có").En("Soon"),
            ["Tooltip_ComingSoon"] = Vi("Tính năng đang phát triển").En("Feature in development"),
            ["About_Body"] = Vi("SolidCraft DXF Exporter V20\nCông cụ xuất & gộp DXF cho SolidCraft (C#/WPF/.NET 8)\n\nXuất hàng loạt file DXF trải phẳng từ các chi tiết Sheet Metal trong SolidCraft, tự động lật mặt (Flip), xử lý layer, gộp file theo Chiều dày + Vật liệu và báo cáo Excel.")
                .En("SolidCraft DXF Exporter V20\nDXF Export & Combine Tool for SolidCraft (C#/WPF/.NET 8)\n\nBatch-exports flattened DXF files from Sheet Metal parts in SolidCraft, with auto-flip, layer post-processing, thickness+material combine, and Excel reporting."),

            ["Btn_ConnectLive"] = Vi("Kết nối SolidCraft (Live)").En("Connect SolidCraft (Live)"),
            ["Btn_OpenFile"] = Vi("Mở file thiết kế (.sc / .step)...").En("Open Design File (.sc / .step)..."),
            ["Btn_CliPath"] = Vi("Cấu hình CLI").En("Configure CLI"),
            ["Tooltip_ConnectLive"] = Vi("Đọc cấu trúc BOM và trải phẳng trực tiếp từ cửa sổ SolidCraft đang mở").En("Read BOM and flat patterns directly from the currently open SolidCraft window"),
            ["Tooltip_OpenFile"] = Vi("Mở file thiết kế (.sc, .solidcraft, .step) để trích xuất không cần mở 3D").En("Open a design file (.sc, .solidcraft, .step) in headless mode"),
            ["Tooltip_CliPath"] = Vi("Cài đặt đường dẫn tới file thực thi solidcraft-cli.exe").En("Configure path to solidcraft-cli.exe"),
            ["Msg_NoSolidCraftLive"] = Vi("Không tìm thấy cửa sổ SolidCraft đang mở hoặc cổng điều khiển chưa sẵn sàng.").En("Could not find an active SolidCraft window or control channel is not ready."),
            ["Msg_CliNotFound"] = Vi("Không tìm thấy file thực thi solidcraft-cli.exe.\nVui lòng cài đặt SolidCraft hoặc chọn đường dẫn CLI.").En("Could not find solidcraft-cli.exe.\nPlease install SolidCraft or specify the CLI path."),
            ["Title_SelectDesignFile"] = Vi("Chọn file thiết kế SolidCraft").En("Select SolidCraft Design File"),
            ["Filter_DesignFiles"] = Vi("File thiết kế SolidCraft (*.sc;*.solidcraft;*.step;*.stp)|*.sc;*.solidcraft;*.step;*.stp|Tất cả file (*.*)|*.*").En("SolidCraft Design Files (*.sc;*.solidcraft;*.step;*.stp)|*.sc;*.solidcraft;*.step;*.stp|All Files (*.*)|*.*"),

            // ===== Tab Lịch sử xuất (ExportHistoryView) =====
            ["History_Subtitle"] = Vi("Lịch sử xuất DXF của assembly đang mở, theo Dxf Batch No")
                .En("DXF export history of the currently open assembly, by Dxf Batch No"),
            ["History_Header_ExportedAt"] = Vi("Thời điểm xuất").En("Exported At"),
            ["History_Header_FileName"] = Vi("Tên file DXF").En("DXF File Name"),
            ["History_Header_User"] = Vi("Người dùng").En("User"),
            ["History_Header_Machine"] = Vi("Máy").En("Machine"),
            ["History_Empty"] = Vi("Chưa có lịch sử xuất cho assembly này.").En("No export history for this assembly yet."),
            ["Btn_ExportExcel"] = Vi("Xuất Excel").En("Export Excel"),
            ["Msg_HistoryNoDataToExport"] = Vi("Không có dữ liệu lịch sử để xuất.").En("No history data to export."),
            ["Msg_HistoryExcelSuccess"] = Vi("Đã xuất file Excel:\n{0}").En("Excel file exported:\n{0}"),
            ["Msg_HistoryExcelError"] = Vi("Lỗi khi xuất Excel: {0}").En("Error exporting Excel: {0}"),

            // ===== Top panel =====
            ["Label_AsmQty"] = Vi("Số lượng Asm:").En("Assembly Qty:"),
            ["Btn_Recalculate"] = Vi("Tính lại").En("Recalculate"),
            ["Chk_Combine"] = Vi("Gộp DXF (Combine)").En("Combine (merge DXF)"),
            ["Chk_FlipUp"] = Vi("Lật lên (Flip up)").En("Flip up"),
            ["Label_Search"] = Vi("Tìm kiếm:").En("Search:"),
            ["Btn_ClearFilter"] = Vi("Xóa Filter").En("Clear Filter"),
            ["Count_Displayed"] = Vi("Đang hiển thị: {0}").En("Displayed: {0}"),
            ["Count_Ready"] = Vi("Sẵn sàng: {0}").En("Ready: {0}"),
            ["Count_Error"] = Vi("Lỗi: {0}").En("Error: {0}"),
            ["Count_Unchecked"] = Vi("Chưa kiểm tra: {0}").En("Not checked: {0}"),
            ["Count_TotalQuantity"] = Vi("Tổng số lượng: {0}").En("Total quantity: {0}"),
            ["Label_DisplayedCount"] = Vi("Đang hiển thị").En("Displayed"),
            ["Label_ReadyCount"] = Vi("Sẵn sàng").En("Ready"),
            ["Label_ErrorCount"] = Vi("Lỗi").En("Error"),
            ["Label_UncheckedCount"] = Vi("Chưa kiểm tra").En("Not checked"),
            ["Label_TotalQuantity"] = Vi("Tổng số lượng").En("Total quantity"),

            // ===== Refresh model =====
            ["Btn_RefreshModel"] = Vi("Làm mới Model").En("Refresh Model"),
            ["Tooltip_RefreshModel"] = Vi("Đọc lại toàn bộ BOM & thuộc tính mới nhất từ Assembly đang mở trong Inventor")
                .En("Reload the full BOM & latest properties from the Assembly currently open in Inventor"),
            ["LastRefreshed_Format"] = Vi("Cập nhật lúc {0}").En("Updated at {0}"),

            // ===== Language switch =====
            ["Tooltip_LangVi"] = Vi("Tiếng Việt").En("Vietnamese"),
            ["Tooltip_LangEn"] = Vi("Tiếng Anh").En("English"),

            // ===== DataGrid headers =====
            ["Header_Image"] = Vi("Hình ảnh").En("Image"),
            ["Tooltip_ImageCell"] = Vi("Nhấp đúp hoặc chuột phải để mở Part").En("Double-click or right-click to open the Part"),
            ["MenuItem_OpenPart"] = Vi("Open Part").En("Open Part"),
            ["MenuItem_OpenAssembly"] = Vi("Open Assembly").En("Open Assembly"),
            ["Tooltip_AssemblyCell"] = Vi("Chuột phải để mở Assembly").En("Right-click to open the Assembly"),
            ["Tooltip_PartNumberCell"] = Vi("Chuột phải để mở Part").En("Right-click to open the Part"),
            ["Header_PartNumber"] = Vi("Part Number").En("Part Number"),
            ["Header_Assembly"] = Vi("Assembly").En("Assembly"),
            ["Header_Revision"] = Vi("Revision").En("Revision"),
            ["Header_SoLuong"] = Vi("Số lượng").En("Quantity"),
            ["Header_ChieuDayVatLieu"] = Vi("Chiều dày/Vật Liệu").En("Thickness/Material"),
            ["Header_TenFile"] = Vi("Tên file gốc").En("Original File Name"),
            ["Header_DxfBatchNo"] = Vi("Dxf batch No").En("Dxf Batch No"),
            ["Header_GhiChu"] = Vi("Ghi chú").En("Notes"),

            // ===== Filter popup =====
            ["Tooltip_FilterDropdown"] = Vi("Bộ lọc").En("Filter"),
            ["Tooltip_SearchBox"] = Vi("Tìm kiếm").En("Search"),
            ["Text_AddCurrentSelection"] = Vi("Thêm lựa chọn hiện tại vào bộ lọc").En("Add current selection to filter"),
            ["SelectAll_Text"] = Vi("(Chọn tất cả)").En("(Select All)"),
            ["SelectAll_SearchResultsText"] = Vi("(Chọn tất cả kết quả tìm kiếm)").En("(Select All Search Results)"),
            ["Btn_OK"] = Vi("OK").En("OK"),
            ["Btn_FilterCancel"] = Vi("Hủy").En("Cancel"),
            ["Filter_Empty"] = Vi("(trống)").En("(empty)"),

            // ===== Context menu =====
            ["Menu_Copy"] = Vi("Sao chép (Ctrl+C)").En("Copy (Ctrl+C)"),
            ["Menu_Paste"] = Vi("Dán (Ctrl+V)").En("Paste (Ctrl+V)"),
            ["Menu_ClearData"] = Vi("Xóa dữ liệu (Delete)").En("Clear Data (Delete)"),

            // ===== Loading overlay =====
            ["Loading_Title"] = Vi("⏳ Đang tải dữ liệu...").En("⏳ Loading data..."),
            ["Loading_TimeFormat"] = Vi("{0}  ({1}%) - Thời gian: {2}").En("{0}  ({1}%) - Time: {2}"),
            ["Loading_Preparing"] = Vi("Đang chuẩn bị...").En("Preparing..."),
            ["Loading_CheckingDocument"] = Vi("Đang kiểm tra tài liệu...").En("Checking document..."),
            ["Loading_InitBomReader"] = Vi("Đang khởi tạo BOM Reader...").En("Initializing BOM Reader..."),
            ["Loading_ProcessingData"] = Vi("Đang xử lý dữ liệu...").En("Processing data..."),
            ["Loading_ValidatingFlatPattern"] = Vi("Đang kiểm tra Flat Pattern...").En("Validating Flat Pattern..."),
            ["Loading_Done"] = Vi("Hoàn tất!").En("Done!"),

            // ===== Bottom panel =====
            ["Btn_Export"] = Vi("Xuất").En("Export"),
            ["Btn_CloseWindow"] = Vi("Đóng").En("Close"),
            ["Btn_ValidateFlatPattern"] = Vi("Kiểm tra Flat Pattern").En("Validate Flat Pattern"),
            ["Tooltip_ValidateFlatPattern"] = Vi("Xác định trạng thái cột Ghi chú cho các dòng ĐANG HIỂN THỊ — trước khi bấm nút này mọi dòng đều là \"Không xác định\". Part CHƯA có sẵn Flat Pattern sẽ được mở thử để phát hiện sớm lỗi \"Flat pattern from body with overridden rule\", KHÔNG xuất DXF, KHÔNG lưu Part.")
                .En("Determines the Notes column state for the rows currently DISPLAYED — before this button is pressed every row shows \"Not checked\". Parts without a saved Flat Pattern are opened to catch \"Flat pattern from body with overridden rule\" early, does NOT export DXF, does NOT save the Part."),

            // ===== Row status notes =====
            ["Note_Unknown"] = Vi("Không xác định").En("Not checked"),
            ["Note_Ready"] = Vi("Sẵn sàng").En("Ready"),
            ["Note_FlatPatternError"] = Vi("Cảnh báo: Flat Pattern Error").En("Warning: Flat Pattern Error"),
            ["Note_ThicknessRuleMismatch"] = Vi("Cảnh báo: Chiều dày 3D thực tế không khớp Sheet Metal Rule")
                .En("Warning: Actual 3D thickness does not match Sheet Metal Rule"),
            ["Note_ThicknessRuleMismatchDetail"] = Vi("Cảnh báo: Chiều dày 3D thực tế lệch Sheet Metal Rule ({0})")
                .En("Warning: Actual 3D thickness differs from Sheet Metal Rule ({0})"),
            ["Note_DescMismatch"] = Vi("Cảnh báo: Description iProperty không khớp Chiều dày 3D / Rule / Vật liệu")
                .En("Warning: Description iProperty does not match 3D Thickness / Rule / Material"),
            ["Note_ConflictingSpec"] = Vi("Trùng Part Number, khác chiều dày/vật liệu").En("Duplicate Part Number with different thickness/material"),
            ["Note_LinkStale"] = Vi("Cảnh báo: Link lỗi thời - cần Update Model").En("Warning: Stale link - please Update Model"),
            ["Tooltip_LinkedPart"] = Vi("Chi tiết liên kết từ: {0} (Body: {1})").En("Linked component from: {0} (Body: {1})"),
            ["Msg_UnrecognizedBodiesHeader"] = Vi("Các chi tiết không nhận diện được (không phải Sheet Metal):").En("Unrecognized bodies (not Sheet Metal):"),
            ["Msg_LoadWarningsHeader"] = Vi("Cảnh báo khi nạp mô hình:").En("Warnings during model loading:"),
            ["Header_LinkedStatus"] = Vi("Liên kết").En("Link"),

            // ===== Dialogs / MessageBox (MainViewModel) =====
            ["Title_Error"] = Vi("Lỗi").En("Error"),
            ["Title_Notice"] = Vi("Thông báo").En("Notice"),
            ["Title_Confirm"] = Vi("Xác nhận").En("Confirm"),
            ["Title_Report"] = Vi("Báo cáo").En("Report"),
            ["Title_ExcelError"] = Vi("Lỗi Excel").En("Excel Error"),
            ["Title_Warning"] = Vi("Cảnh báo").En("Warning"),
            ["Msg_NeedOpenAssembly"] = Vi("Cần mở Assembly (.iam) làm active document.").En("Please open an Assembly (.iam) as the active document."),
            ["Msg_InitError"] = Vi("Lỗi khởi tạo: {0}").En("Initialization error: {0}"),
            ["Msg_NoRowDisplayed"] = Vi("Không có dòng nào đang hiển thị. Hãy nới bộ lọc hoặc xoá từ khoá tìm kiếm.")
                .En("No rows are currently displayed. Loosen the filters or clear the search text."),
            ["Hint_FilterIsSelection"] = Vi("Mọi dòng đang hiển thị sau khi lọc sẽ được xuất.")
                .En("Every row currently displayed after filtering will be exported."),
            ["Msg_ExportConfirmBody"] = Vi("Sẽ export {0} Part đang hiển thị.\nCombine: {1}")
                .En("Will export the {0} Part(s) currently displayed.\nCombine: {1}"),
            ["Msg_ExportMergedRows"] = Vi("Lưu ý: {0} dòng đang hiển thị được gộp thành {1} Part để xuất — cùng một Part Number nằm ở nhiều Assembly chỉ ra 1 file DXF, số lượng bằng tổng.")
                .En("Note: the {0} displayed rows are merged into {1} Part(s) for export — the same Part Number appearing in several Assemblies produces a single DXF whose quantity is the sum."),
            ["Msg_EmptyBatchWarning"] = Vi("{0} dòng chưa có Dxf Batch No.")
                .En("{0} row(s) have no Dxf Batch No."),
            ["Msg_ConfirmContinue"] = Vi("Bạn có chắc chắn muốn tiếp tục?").En("Are you sure you want to continue?"),
            ["Word_Yes"] = Vi("CÓ").En("YES"),
            ["Word_No"] = Vi("KHÔNG").En("NO"),
            ["Dialog_ChooseSaveFolder"] = Vi("Chọn thư mục lưu").En("Select Folder"),
            ["Report_Header"] = Vi("--- HOÀN THÀNH XUẤT DXF ---").En("--- DXF EXPORT COMPLETE ---"),
            ["Report_ExportedCount"] = Vi("✔ Đã xuất: {0} file").En("✔ Exported: {0} file(s)"),
            ["Report_FailedCount"] = Vi("✘ Lỗi xuất: {0} file").En("✘ Failed: {0} file(s)"),
            ["Report_LogSavedTo"] = Vi("Chi tiết đã được lưu vào:\n{0}").En("Details saved to:\n{0}"),
            ["Msg_CannotWriteLog"] = Vi("Không thể ghi file log:\n{0}").En("Could not write log file:\n{0}"),
            ["Msg_InvalidRowData"] = Vi("Dữ liệu dòng không hợp lệ.").En("Invalid row data."),
            ["Msg_PartNoValidPath"] = Vi("Part '{0}' không có đường dẫn file hợp lệ.").En("Part '{0}' has no valid file path."),
            ["Msg_AssemblyNoValidPath"] = Vi("Assembly '{0}' không có đường dẫn file hợp lệ.").En("Assembly '{0}' has no valid file path."),
            ["Msg_EmptyRowPath"] = Vi("Đường dẫn file của dòng được chọn rỗng hoặc không hợp lệ.").En("The selected row's file path is empty or invalid."),
            ["Msg_EmptyFilePath"] = Vi("Đường dẫn file rỗng hoặc không hợp lệ.").En("The file path is empty or invalid."),
            ["Msg_InvalidFilePath"] = Vi("Đường dẫn file không hợp lệ:\n{0}").En("Invalid file path:\n{0}"),
            ["Msg_FileNotExist"] = Vi("File không tồn tại:\n{0}").En("File does not exist:\n{0}"),
            ["Msg_NoInventorConnection"] = Vi("Không tìm thấy kết nối tới Autodesk Inventor.").En("Could not find a connection to Autodesk Inventor."),
            ["Msg_CannotOpenDocument"] = Vi("Không thể mở tài liệu trong Inventor:\n{0}").En("Could not open the document in Inventor:\n{0}"),
            ["Title_InventorBusy"] = Vi("Inventor đang bận").En("Inventor is busy"),
            ["Msg_InventorBusy"] = Vi("Autodesk Inventor đang bận hoặc có hộp thoại đang mở.\nVui lòng đóng các hộp thoại trong Inventor rồi thử lại.\n(Chi tiết: 0x{0})")
                .En("Autodesk Inventor is busy or has an open dialog.\nPlease close any dialogs in Inventor and try again.\n(Detail: 0x{0})"),
            ["Title_ConnectionError"] = Vi("Lỗi kết nối").En("Connection Error"),
            ["Msg_ConnectionLost"] = Vi("Mất kết nối tới Autodesk Inventor (RPC Server Unavailable).\n(Chi tiết: 0x{0})")
                .En("Lost connection to Autodesk Inventor (RPC Server Unavailable).\n(Detail: 0x{0})"),
            ["Msg_ComErrorOpening"] = Vi("Lỗi COM khi mở file trong Inventor:\n{0}\n(HRESULT: 0x{1})").En("COM error while opening the file in Inventor:\n{0}\n(HRESULT: 0x{1})"),
            ["Msg_ErrorOpeningFile"] = Vi("Lỗi khi mở file trong Inventor:\n{0}").En("Error opening the file in Inventor:\n{0}"),
            ["Msg_ContextMenuRowUnknown"] = Vi("Không xác định được dòng Part để mở (context menu).").En("Could not determine which Part row to open (context menu)."),
            ["Msg_ContextMenuRowUnknownAssembly"] = Vi("Không xác định được dòng Assembly để mở (context menu).").En("Could not determine which Assembly row to open (context menu)."),

            // ===== DxfExportManager notifications =====
            ["Msg_SaveDirNotFound"] = Vi("Thư mục lưu không tồn tại: {0}").En("Save folder does not exist: {0}"),
            ["Msg_ModelStateNotFound"] = Vi("Không tìm thấy Model State '{0}' trong Part — có thể đã bị xoá/đổi tên. Bấm \"Làm mới Model\" rồi thử lại.")
                .En("Model State '{0}' not found in the Part — it may have been deleted/renamed. Click \"Refresh Model\" and try again."),
            ["Note_FlipSkippedMixedBend"] = Vi("Bỏ qua flip (Bend trộn Up+Down)").En("Flip skipped (mixed Up+Down bends)"),
            ["Note_FlipSkippedMarkAndBend"] = Vi("Bỏ qua flip (có cả Mark và Bend)").En("Flip skipped (has both Mark and Bend)"),
            ["Note_FlipDone"] = Vi("Đã flip (Bend hướng Down chiếm đa số)").En("Flipped (majority of bends face Down)"),
            ["Note_FlipDoneMarkBottom"] = Vi("Đã flip (Mark đang nằm mặt ẩn - Bottom)").En("Flipped (Mark was on the hidden Bottom face)"),
            ["Note_FlipFailed"] = Vi("Lỗi: Không thể lật mặt Flat Pattern").En("Error: Could not flip the Flat Pattern face"),
            ["Note_FlipError"] = Vi("Lỗi lật mặt: {0}").En("Flip error: {0}"),
            ["Msg_CombineDxfError"] = Vi("Lỗi Combine DXF: {0}").En("Combine DXF error: {0}"),
            ["Msg_CombineUnhandledEntities"] = Vi("Cảnh báo: {0} entity trong combine.dxf không xác định được loại để tính bounding box ({1}) — vẫn được dịch chuyển đúng, chỉ không tính vào bố cục.")
                .En("Warning: {0} entities in combine.dxf were of an unrecognized type for bounding-box purposes ({1}) — still transformed correctly, just excluded from layout sizing."),
            ["Msg_CombineFileSkipped"] = Vi("⚠ Combine DXF bỏ qua/không đo được file: {0}")
                .En("⚠ Combine DXF skipped or could not measure file: {0}"),
            ["Msg_HistoryWriteError"] = Vi("⚠ Không ghi được lịch sử Dxf Batch No (file DXF vẫn đã xuất thành công):\n{0}")
                .En("⚠ Could not write Dxf Batch No history (DXF files were still exported successfully):\n{0}"),
            ["Msg_ValidateFlatPatternAllOk"] = Vi("Đã kiểm tra {0} Part (chưa có Flat Pattern sẵn) — không phát hiện lỗi.\n(Part đã có sẵn Flat Pattern được bỏ qua vì đã kiểm tra lúc quét BOM.)\n\nCột Ghi chú của các dòng đang hiển thị đã chuyển từ \"Không xác định\" sang trạng thái thật.")
                .En("Checked {0} Part(s) that had no saved Flat Pattern — no errors found.\n(Parts that already had a saved Flat Pattern were skipped since they were already checked while scanning the BOM.)\n\nThe Notes column of the displayed rows has changed from \"Not checked\" to its real state."),
            ["Msg_ValidateFlatPatternDone"] = Vi("Đã kiểm tra {0} Part (chưa có Flat Pattern sẵn) — phát hiện {1} Part lỗi:\n{2}\n\nCột Ghi chú đã được cập nhật.")
                .En("Checked {0} Part(s) that had no saved Flat Pattern — found {1} Part(s) with errors:\n{2}\n\nThe Notes column has been updated."),

            // ===== Màn hình Cấu hình Layer (LayerConfigView) =====
            ["LayerConfig_Subtitle"] = Vi("Chỉnh layer nào bị xoá và màu ACI áp dụng khi xuất DXF")
                .En("Configure which layers are deleted and the ACI color applied when exporting DXF"),
            ["LayerConfig_Header_Name"] = Vi("Tên layer").En("Layer Name"),
            ["LayerConfig_Header_Delete"] = Vi("Xoá khỏi DXF").En("Delete from DXF"),
            ["LayerConfig_Header_Color"] = Vi("Màu (ACI)").En("Color (ACI)"),
            ["LayerConfig_Header_Preview"] = Vi("Xem trước").En("Preview"),
            ["Btn_LayerConfigSave"] = Vi("Lưu").En("Save"),
            ["Btn_LayerConfigReset"] = Vi("Khôi phục mặc định").En("Restore Defaults"),
            ["LayerConfig_Saved"] = Vi("✔ Đã lưu cấu hình layer").En("✔ Layer settings saved"),
            ["LayerConfig_ResetNotice"] = Vi("Đã nạp lại mặc định — bấm Lưu để áp dụng").En("Defaults loaded — click Save to apply"),
        };

        private static Builder Vi(string text) => new Builder().Vi(text);

        private sealed class Builder
        {
            private readonly Dictionary<AppLanguage, string> _dict = new();
            public Builder Vi(string text) { _dict[AppLanguage.Vietnamese] = text; return this; }
            public Dictionary<AppLanguage, string> En(string text) { _dict[AppLanguage.English] = text; return _dict; }
        }
    }
}
