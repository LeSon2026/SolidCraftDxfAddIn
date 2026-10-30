using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ExportDxfAddIn.Models;

namespace ExportDxfAddIn.Services
{
    public class ExcelReportService
    {
        /// <summary>Xuất danh sách ExportHistoryEntry (tab "Lịch sử xuất") ra 1 file .xlsx tại đúng
        /// filePath người dùng chọn qua SaveFileDialog. Header cột cố định tiếng Việt, không qua Tr
        /// (cùng quy ước với CreateExcelReport bên dưới — file kết quả xuất, không phải UI tương tác,
        /// xem README mục 9).</summary>
        public void ExportHistoryToFile(List<ExportHistoryEntry> entries, string filePath)
        {
            var headerColor = XLColor.FromArgb(31, 73, 125);
            var borderColor = XLColor.FromArgb(217, 217, 217);

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Lich su xuat");

                string[] headers = { "STT", "Thời điểm xuất", "Dxf Batch No", "Part Number", "Revision",
                    "Số lượng", "Mô tả", "Tên file DXF", "Người dùng", "Máy" };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = headers[c];
                }
                ws.Row(1).Height = 22;

                var headerRange = ws.Range(1, 1, 1, headers.Length);
                headerRange.Style.Fill.SetBackgroundColor(headerColor);
                headerRange.Style.Font.SetFontName("Segoe UI");
                headerRange.Style.Font.SetFontSize(11);
                headerRange.Style.Font.SetBold(true);
                headerRange.Style.Font.SetFontColor(XLColor.White);
                headerRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                int rowIdx = 2;
                int idx = 1;
                foreach (var e in entries)
                {
                    ws.Cell(rowIdx, 1).Value = idx;
                    ws.Cell(rowIdx, 2).Value = e.ExportedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                    ws.Cell(rowIdx, 3).Value = string.IsNullOrWhiteSpace(e.BatchNo) ? "(trống)" : e.BatchNo;
                    ws.Cell(rowIdx, 4).Value = e.PartNumber;
                    ws.Cell(rowIdx, 5).Value = e.Revision;
                    ws.Cell(rowIdx, 6).Value = e.Quantity;
                    ws.Cell(rowIdx, 7).Value = e.Description;
                    ws.Cell(rowIdx, 8).Value = e.FileName;
                    ws.Cell(rowIdx, 9).Value = e.UserName;
                    ws.Cell(rowIdx, 10).Value = e.MachineName;

                    ws.Cell(rowIdx, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Cell(rowIdx, 6).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    ws.Cell(rowIdx, 6).Style.NumberFormat.SetFormat("#,##0");

                    var rowRange = ws.Range(rowIdx, 1, rowIdx, headers.Length);
                    rowRange.Style.Font.SetFontName("Segoe UI");
                    rowRange.Style.Font.SetFontSize(10);
                    rowRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    rowRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    rowRange.Style.Border.SetOutsideBorderColor(borderColor);
                    rowRange.Style.Border.SetInsideBorderColor(borderColor);

                    rowIdx++;
                    idx++;
                }

                ws.Columns(1, headers.Length).AdjustToContents();
                ws.SheetView.FreezeRows(1);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                wb.SaveAs(filePath);
            }
        }

        /// <param name="items">Danh sách đúng những gì VỪA xuất trong lượt export này (gom sẵn bởi
        /// DxfExportManager, xem ExportedItem) — trước đây hàm tự quét *.dxf trong folder rồi đoán
        /// ngược PartNumber/SoLuong từ tên file (Split('_')), vừa gom nhầm file của lần export trước
        /// nằm chung thư mục, vừa sai khi PartNumber chứa dấu "_".</param>
        public bool CreateExcelReport(string folderPath, IReadOnlyList<ExportedItem> items)
        {
            if (items == null || items.Count == 0) return false;

            var headerColor = XLColor.FromArgb(31, 73, 125);
            var borderColor = XLColor.FromArgb(217, 217, 217);

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Thong ke");

                var titleRange = ws.Range("A1:D1");
                titleRange.Merge();
                ws.Cell("A1").Value = "BẢNG THỐNG KÊ CHI TIẾT SẢN XUẤT";
                titleRange.Style.Font.SetFontName("Segoe UI");
                titleRange.Style.Font.SetFontSize(14);
                titleRange.Style.Font.SetBold(true);
                titleRange.Style.Font.SetFontColor(headerColor);
                titleRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                titleRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                ws.Row(1).Height = 40;

                string[] headers = { "STT", "Part Number", "Chiều dày - Vật liệu", "Số lượng" };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cell(3, c + 1).Value = headers[c];
                }
                ws.Row(3).Height = 25;

                var headerRange = ws.Range("A3:D3");
                headerRange.Style.Fill.SetBackgroundColor(headerColor);
                headerRange.Style.Font.SetFontName("Segoe UI");
                headerRange.Style.Font.SetFontSize(11);
                headerRange.Style.Font.SetBold(true);
                headerRange.Style.Font.SetFontColor(XLColor.White);
                headerRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                int rowIdx = 4;

                foreach (var item in items)
                {
                    ws.Cell(rowIdx, 1).Value = item.Stt;
                    ws.Cell(rowIdx, 2).Value = item.PartNumber;
                    ws.Cell(rowIdx, 3).Value = item.Description;
                    ws.Cell(rowIdx, 4).Value = item.Quantity;

                    ws.Row(rowIdx).Height = 20;
                    ws.Cell(rowIdx, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Cell(rowIdx, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                    ws.Cell(rowIdx, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Cell(rowIdx, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    ws.Cell(rowIdx, 4).Style.NumberFormat.SetFormat("#,##0");

                    var rowRange = ws.Range(rowIdx, 1, rowIdx, 4);
                    rowRange.Style.Font.SetFontName("Segoe UI");
                    rowRange.Style.Font.SetFontSize(10);
                    rowRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    rowRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    rowRange.Style.Border.SetOutsideBorderColor(borderColor);
                    rowRange.Style.Border.SetInsideBorderColor(borderColor);

                    rowIdx++;
                }

                ws.Columns(1, 4).AdjustToContents();

                string excelPath = Path.Combine(folderPath, "danh_sach_chi_tiet.xlsx");
                if (File.Exists(excelPath))
                {
                    File.Delete(excelPath);
                }

                try
                {
                    wb.SaveAs(excelPath);
                }
                catch (Exception ex)
                {
                    throw new Exception("Lỗi khi tạo Excel: " + ex.Message);
                }

                return File.Exists(excelPath);
            }
        }
    }
}
