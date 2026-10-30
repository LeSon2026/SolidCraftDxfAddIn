namespace ExportDxfAddIn.Models
{
    /// <summary>
    /// 1 dòng trong báo cáo Excel (danh_sach_chi_tiet.xlsx) — gom trực tiếp từ dữ liệu thật của
    /// chính lần export đang chạy (DxfExportManager.ExportSelectedRows), KHÔNG đoán ngược từ tên
    /// file .dxf như trước đây (parse ngược sai khi PartNumber chứa dấu "_").
    /// </summary>
    public class ExportedItem
    {
        public int Stt { get; set; }
        public string PartNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public int Quantity { get; set; }
    }
}
