using System;
using System.Collections.Generic;

namespace ExportDxfAddIn.Models
{
    /// <summary>
    /// Thông tin tổng hợp của một chi tiết kim loại tấm đọc từ SolidCraft (qua CLI hoặc Control Channel).
    /// </summary>
    public class SolidCraftSheetItem
    {
        public string BodyName { get; set; } = string.Empty;
        public ulong ComponentId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string DescriptionProp { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public string MaterialName { get; set; } = "Steel";
        public string RuleName { get; set; } = string.Empty;
        public double ActualThicknessMm { get; set; }
        public double RuleThicknessMm { get; set; }
        public double FlatWidthMm { get; set; }
        public double FlatHeightMm { get; set; }
        public double VolumeMm3 { get; set; }
        public double AreaMm2 { get; set; }
        public int BendCount { get; set; }
        public int BendUpCount { get; set; }
        public int BendDownCount { get; set; }
        public double BendUpTotalLength { get; set; }
        public double BendDownTotalLength { get; set; }
        public bool HasTopPocketOrEtch { get; set; }
        public bool HasBottomPocket { get; set; }
        public bool HasFlatPatternError { get; set; }
        public string FlatPatternErrorMessage { get; set; } = string.Empty;
        public bool ThicknessRuleMismatch { get; set; }
        public string ThicknessRuleMismatchDetail { get; set; } = string.Empty;
        public bool DescriptionMismatch { get; set; }
        public string? ThumbnailPath { get; set; }
        public string SourceDesignPath { get; set; } = string.Empty;

        /// <summary>True nếu body đến từ file .sc được liên kết (component link / Insert Design)</summary>
        public bool IsLinked { get; set; }

        /// <summary>Đường dẫn file thiết kế thực tế dùng để export DXF / Flat Pattern / Snapshot (file gốc hoặc host)</summary>
        public string ExportDesignPath { get; set; } = string.Empty;

        /// <summary>Tên body thực tế trong ExportDesignPath (ví dụ Body1 trong Hop.sc)</summary>
        public string ExportBodyName { get; set; } = string.Empty;

        /// <summary>Tên file nguồn được liên kết (ví dụ Hop.sc)</summary>
        public string LinkedSourceName { get; set; } = string.Empty;

        /// <summary>True nếu component liên kết trong host khác với file gốc (cần bấm Update Model)</summary>
        public bool IsLinkStale { get; set; }
    }

    public class SolidCraftLoadResult
    {
        public string DesignName { get; set; } = string.Empty;
        public string DesignPath { get; set; } = string.Empty;
        public bool IsFromLiveApp { get; set; }
        public List<SolidCraftSheetItem> SheetItems { get; set; } = new();

        /// <summary>Danh sách body không nhận diện được (không phải sheet metal và không liên kết tới file sheet metal)</summary>
        public List<string> UnrecognizedBodies { get; set; } = new();

        /// <summary>Danh sách cảnh báo phát sinh khi nạp mô hình</summary>
        public List<string> Warnings { get; set; } = new();
    }
}

