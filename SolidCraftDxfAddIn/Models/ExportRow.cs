using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ExportDxfAddIn.Services;

namespace ExportDxfAddIn.Models
{
    public class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class ExportRow : BindableBase
    {
        private string _partNumber = "";
        public string PartNumber
        {
            get => _partNumber;
            set => SetProperty(ref _partNumber, value);
        }

        private string _assemblyName = "";
        /// <summary>Part Number của assembly cha TRỰC TIẾP chứa Part này (AssemblyReaderLib.
        /// ComponentData.ParentPartNumber, đọc từ StructuredView). Part nằm thẳng trong Assembly tổng
        /// thì đây là Part Number của chính file .iam đang mở. Cùng 1 PartNumber nằm ở nhiều assembly
        /// con sẽ tạo NHIỀU dòng ExportRow, mỗi dòng 1 AssemblyName + số lượng riêng (xem README mục 20).</summary>
        public string AssemblyName
        {
            get => _assemblyName;
            set => SetProperty(ref _assemblyName, value);
        }

        private string _assemblyFilePath = "";
        /// <summary>Đường dẫn file .iam của assembly cấp 1 hiển thị ở AssemblyName (AssemblyReaderLib.
        /// ComponentData.TopLevelAssemblyFilePath) — dùng bởi context menu "Open Assembly" (mục 21).
        /// Part nằm thẳng trong Assembly tổng thì đây là đường dẫn của chính Assembly tổng đang mở.</summary>
        public string AssemblyFilePath
        {
            get => _assemblyFilePath;
            set => SetProperty(ref _assemblyFilePath, value);
        }

        private string _revisionNumber = "";
        public string RevisionNumber
        {
            get => _revisionNumber;
            set => SetProperty(ref _revisionNumber, value);
        }

        private int _soLuong;
        public int SoLuong
        {
            get => _soLuong;
            set => SetProperty(ref _soLuong, value);
        }

        private string _chieuDayVatLieu = "";
        public string ChieuDayVatLieu
        {
            get => _chieuDayVatLieu;
            set => SetProperty(ref _chieuDayVatLieu, value);
        }

        private string _tenFile = "";
        public string TenFile
        {
            get => _tenFile;
            set => SetProperty(ref _tenFile, value);
        }

        private string _dxfBatchNo = "";
        public string DxfBatchNo
        {
            get => _dxfBatchNo;
            set => SetProperty(ref _dxfBatchNo, value);
        }

        private bool _hasFlatPatternError;
        public bool HasFlatPatternError
        {
            get => _hasFlatPatternError;
            set
            {
                if (SetProperty(ref _hasFlatPatternError, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                    OnPropertyChanged(nameof(IsReady));
                }
            }
        }

        private bool _hasDescMismatch;
        public bool HasDescMismatch
        {
            get => _hasDescMismatch;
            set
            {
                if (SetProperty(ref _hasDescMismatch, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                    OnPropertyChanged(nameof(IsReady));
                }
            }
        }

        private bool _hasThicknessRuleMismatch;
        /// <summary>
        /// True nếu chiều dày thực tế của tấm 3D (khi trải phẳng) không khớp với chiều dày
        /// trong Sheet Metal Rule (ActiveSheetMetalStyle / smDef.Thickness).
        /// </summary>
        public bool HasThicknessRuleMismatch
        {
            get => _hasThicknessRuleMismatch;
            set
            {
                if (SetProperty(ref _hasThicknessRuleMismatch, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                    OnPropertyChanged(nameof(IsReady));
                }
            }
        }

        private string _thicknessRuleMismatchDetail = "";
        public string ThicknessRuleMismatchDetail
        {
            get => _thicknessRuleMismatchDetail;
            set
            {
                if (SetProperty(ref _thicknessRuleMismatchDetail, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                }
            }
        }

        private bool _hasConflictingSpec;
        /// <summary>True nếu 2+ dòng BOM khác nhau vô tình trùng PartNumber nhưng có
        /// Chiều dày/Vật liệu khác nhau — gộp theo PartNumber (RecalculateAsync) sẽ khiến dòng
        /// thứ 2 trở đi mất thông tin thật của chính nó, xuất ra DXF sai vật liệu mà không biết.</summary>
        public bool HasConflictingSpec
        {
            get => _hasConflictingSpec;
            set
            {
                if (SetProperty(ref _hasConflictingSpec, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                    OnPropertyChanged(nameof(IsReady));
                }
            }
        }

        private bool _isValidated;
        /// <summary>False khi dòng vừa được dựng (load/Tính lại/Làm mới Model) — trạng thái thật CHƯA
        /// được xác nhận, cột Ghi chú hiển thị "Không xác định". Chỉ chuyển thành True khi người dùng
        /// bấm nút "Kiểm tra Flat Pattern" (DxfExportManager.ValidateFlatPatterns) — lúc đó Ghi chú mới
        /// hiện "Sẵn sàng" hoặc danh sách cảnh báo. Lý do: các cờ HasFlatPatternError/HasDescMismatch
        /// đọc lúc quét BOM không đủ tin cậy (part chưa từng Unfold thì lỗi "rule bị override" không lộ
        /// ra, xem README mục 5b) — hiện "Sẵn sàng" ngay lúc load là hứa hẹn sai.</summary>
        public bool IsValidated
        {
            get => _isValidated;
            set
            {
                if (SetProperty(ref _isValidated, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                    OnPropertyChanged(nameof(IsReady));
                }
            }
        }

        /// <summary>Không dùng string cố định nữa (từng so sánh "Sẵn sàng") để tránh vỡ khi đổi ngôn ngữ.
        /// Dòng chưa kiểm tra (IsValidated == false) KHÔNG được coi là sẵn sàng — xem IsValidated.</summary>
        public bool IsReady => IsValidated
                            && !HasFlatPatternError
                            && !HasThicknessRuleMismatch
                            && !HasDescMismatch
                            && !HasConflictingSpec;

        private bool _isLinked;
        public bool IsLinked
        {
            get => _isLinked;
            set
            {
                if (SetProperty(ref _isLinked, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                    OnPropertyChanged(nameof(LinkedTooltip));
                }
            }
        }

        private string _linkedSourceName = "";
        public string LinkedSourceName
        {
            get => _linkedSourceName;
            set
            {
                if (SetProperty(ref _linkedSourceName, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                    OnPropertyChanged(nameof(LinkedTooltip));
                }
            }
        }

        private bool _isLinkStale;
        public bool IsLinkStale
        {
            get => _isLinkStale;
            set
            {
                if (SetProperty(ref _isLinkStale, value))
                {
                    OnPropertyChanged(nameof(GhiChu));
                }
            }
        }

        public string LinkedTooltip => IsLinked
            ? (string.IsNullOrWhiteSpace(LinkedSourceName)
                ? Tr.Instance.Format("Tooltip_LinkedPart", "Link", BodyName)
                : Tr.Instance.Format("Tooltip_LinkedPart", LinkedSourceName, BodyName))
            : PartNumber;

        public string GhiChu
        {
            get
            {
                if (!IsValidated) return Tr.Instance["Note_Unknown"];

                var notes = new List<string>();
                if (IsLinked && !string.IsNullOrEmpty(LinkedSourceName))
                {
                    notes.Add($"🔗 {LinkedSourceName}");
                }
                if (IsLinkStale)
                {
                    notes.Add(Tr.Instance["Note_LinkStale"]);
                }
                if (HasFlatPatternError) notes.Add(Tr.Instance["Note_FlatPatternError"]);
                if (HasThicknessRuleMismatch)
                {
                    string msg = string.IsNullOrWhiteSpace(ThicknessRuleMismatchDetail)
                        ? Tr.Instance["Note_ThicknessRuleMismatch"]
                        : Tr.Instance.Format("Note_ThicknessRuleMismatchDetail", ThicknessRuleMismatchDetail);
                    notes.Add(msg);
                }
                if (HasDescMismatch) notes.Add(Tr.Instance["Note_DescMismatch"]);
                if (HasConflictingSpec) notes.Add(Tr.Instance["Note_ConflictingSpec"]);
                return notes.Count > 0 ? string.Join("; ", notes) : Tr.Instance["Note_Ready"];
            }
        }

        /// <summary>Gọi khi ngôn ngữ đổi để cập nhật lại text GhiChu (vốn tính toán từ Tr, không lưu trực tiếp).</summary>
        public void NotifyLanguageChanged() => OnPropertyChanged(nameof(GhiChu));

        private string _fullPath = "";
        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }

        private string _modelState = string.Empty;
        /// <summary>Model State (AssemblyReaderLib.ComponentData.ModelState) mà occurrence này thực sự
        /// dùng trong assembly — dùng nội bộ để DxfExportManager activate đúng model state trước khi
        /// export, KHÔNG hiển thị trên DataGrid (xem Plan.md PHẦN 6).</summary>
        public string ModelState
        {
            get => _modelState;
            set => SetProperty(ref _modelState, value);
        }

        private bool _hasFlatPattern;
        /// <summary>True nếu part đã có sẵn Flat Pattern lưu trong file (AssemblyReaderLib.ComponentData.
        /// HasFlatPattern lúc quét BOM) — dùng nội bộ để DxfExportManager.ValidateFlatPatterns() bỏ qua
        /// part này (lỗi "rule bị override", nếu có, đã được phát hiện ngay lúc quét, xem
        /// HasFlatPatternError), KHÔNG hiển thị trên DataGrid.</summary>
        public bool HasFlatPattern
        {
            get => _hasFlatPattern;
            set => SetProperty(ref _hasFlatPattern, value);
        }

        private string _bodyName = "";
        public string BodyName
        {
            get => string.IsNullOrEmpty(_bodyName) ? TenFile : _bodyName;
            set => SetProperty(ref _bodyName, value);
        }

        private BitmapImage? _thumbnail;
        public BitmapImage? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }
    }
}
