using System;

namespace ExportDxfAddIn.Models
{
    /// <summary>
    /// 1 dòng lịch sử xuất DXF (1 Part, 1 lần export) — lưu trong SQLite riêng theo từng
    /// assembly tổng (xem DxfHistoryService). BatchNo rỗng/null là trạng thái hợp lệ
    /// ("chưa gán batch"), KHÔNG tự sinh mã thay thế.
    /// </summary>
    public class ExportHistoryEntry
    {
        public long Id { get; set; }

        /// <summary>1 GUID chung cho mọi dòng sinh ra trong cùng 1 lần bấm nút Export
        /// (1 lượt export có thể xuất nhiều Part với Batch No khác nhau từng dòng).</summary>
        public string ExportSessionId { get; set; } = "";

        public string BatchNo { get; set; } = "";

        public DateTime ExportedAtUtc { get; set; }

        public string PartNumber { get; set; } = "";
        public string Revision { get; set; } = "";
        public int Quantity { get; set; }
        public string Description { get; set; } = "";
        public string FileName { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string AssemblyFileName { get; set; } = "";
    }
}
