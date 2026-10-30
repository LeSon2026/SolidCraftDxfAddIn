using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using netDxf;
using ExportDxfAddIn.Models;

namespace ExportDxfAddIn.Services
{
    public class SolidCraftDxfExportManager
    {
        private static SolidCraftDxfExportManager? _instance;
        public static SolidCraftDxfExportManager Instance => _instance ??= new SolidCraftDxfExportManager();

        /// <summary>
        /// Thực hiện xuất DXF cho danh sách các dòng được chọn từ SolidCraft,
        /// bao gồm chuẩn hoá layer, lật mặt (Flip), gắn nhãn văn bản, gộp file (Combine),
        /// lưu lịch sử SQLite và xuất báo cáo Excel.
        /// </summary>
        public async Task<(List<string> logOK, List<string> logFail, List<string> logFlip, string? historyWarning, List<ExportedItem> exportedItems)> ExportSelectedRowsAsync(
            IEnumerable<ExportRow> selectedRows,
            string saveDir,
            bool isCombine,
            bool isFlip,
            bool isExportExcel,
            string? excelFilePath,
            string? historyDbPath,
            string? designFileName,
            string? designFilePath,
            bool isLiveApp,
            IReadOnlyDictionary<string, SolidCraftSheetItem>? sheetItemsMap,
            IProgress<(int current, int total, string message)>? progress = null,
            CancellationToken ct = default)
        {
            var logOK = new List<string>();
            var logFail = new List<string>();
            var logFlip = new List<string>();
            var historyEntries = new List<ExportHistoryEntry>();
            var exportedItems = new List<ExportedItem>();
            string sessionId = Guid.NewGuid().ToString("N");

            if (!Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            var rowsList = selectedRows.ToList();
            int total = rowsList.Count;

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var rRow = rowsList[i];
                string sPartNum = rRow.PartNumber;
                int qty = rRow.SoLuong;
                string bodyName = !string.IsNullOrEmpty(rRow.BodyName) ? rRow.BodyName : rRow.TenFile;

                progress?.Report((i + 1, total, $"Đang xuất DXF ({i + 1}/{total}): {sPartNum}..."));

                try
                {
                    // Lấy hoặc xây dựng spec string PL{Thick}-{Material}
                    string sDesc = rRow.ChieuDayVatLieu;
                    SolidCraftSheetItem? sheetItem = null;
                    if (sheetItemsMap != null && sheetItemsMap.TryGetValue(bodyName, out var item))
                    {
                        sheetItem = item;
                        if (string.IsNullOrWhiteSpace(sDesc))
                        {
                            var thickStr = item.ActualThicknessMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                            var mat = item.MaterialName.Replace(" ", "");
                            sDesc = $"PL{thickStr}-{mat}";
                        }
                    }

                    if (string.IsNullOrWhiteSpace(sDesc))
                    {
                        sDesc = "Unknown";
                    }

                    var rawFileName = $"{sPartNum}_{qty}_{sDesc}";
                    var cleanName = rawFileName;
                    foreach (var c in Path.GetInvalidFileNameChars())
                    {
                        cleanName = cleanName.Replace(c.ToString(), "_");
                    }

                    var sFullDxfPath = Path.Combine(saveDir, cleanName + ".dxf");

                    // 1. Gọi SolidCraft CLI/Live xuất DXF thô ra đĩa (nếu là Linked body sẽ xuất từ file gốc tương ứng)
                    await SolidCraftCliService.Instance.ExportSheetBodyDxfAsync(designFilePath, isLiveApp, bodyName, sFullDxfPath, sheetItem, ct).ConfigureAwait(false);

                    // 2. Nạp file DXF vào netDxf để chuẩn hoá layer và thực hiện Auto-Flip
                    var doc = DxfDocument.Load(sFullDxfPath);
                    if (doc == null)
                    {
                        throw new InvalidOperationException($"Không thể đọc lại file DXF vừa xuất: {sFullDxfPath}");
                    }

                    // Chuẩn hoá Layer SolidCraft sang Layer Add-In
                    SolidCraftFlipService.Instance.NormalizeSolidCraftLayers(doc);

                    // Auto-Flip nếu người dùng bật tuỳ chọn
                    if (isFlip && sheetItem != null)
                    {
                        var flipNote = SolidCraftFlipService.Instance.FlipIfNeeded(doc, sheetItem);
                        if (!string.IsNullOrEmpty(flipNote))
                        {
                            logFlip.Add($"{sPartNum}: {flipNote}");
                        }
                    }

                    // Nối các đường bao (Line, Arc) thành Polyline kín (Polyline2D)
                    DxfPostProcessor.JoinBoundaryCurvesIntoClosedPolylines(doc);

                    // Lưu lại file DXF sau chuẩn hoá layer, flip và nối polyline
                    doc.Save(sFullDxfPath);

                    // 3. Hậu xử lý nhãn & cắt đường bend 50mm bằng DxfPostProcessor
                    var postProcessor = new DxfPostProcessor();
                    if (isCombine)
                    {
                        postProcessor.InsertTextIntoDxf(sFullDxfPath, sPartNum, sDesc, qty, 30, 45, 15);
                    }
                    else
                    {
                        postProcessor.ProcessSingleFileNoCombine(sFullDxfPath, sPartNum, 15);
                    }

                    logOK.Add(Path.GetFileName(sFullDxfPath));

                    exportedItems.Add(new ExportedItem
                    {
                        Stt = exportedItems.Count + 1,
                        PartNumber = sPartNum,
                        Description = sDesc,
                        Quantity = qty
                    });

                    historyEntries.Add(new ExportHistoryEntry
                    {
                        ExportSessionId = sessionId,
                        BatchNo = rRow.DxfBatchNo,
                        ExportedAtUtc = DateTime.UtcNow,
                        PartNumber = sPartNum,
                        Revision = rRow.RevisionNumber,
                        Quantity = qty,
                        Description = sDesc,
                        FileName = Path.GetFileName(sFullDxfPath),
                        MachineName = Environment.MachineName,
                        UserName = Environment.UserName,
                        AssemblyFileName = designFileName ?? string.Empty
                    });
                }
                catch (Exception ex)
                {
                    logFail.Add($"{sPartNum} ({bodyName}): {ex.Message}");
                }
            }

            // 4. Ghi lịch sử xuất vào SQLite
            string? historyWarning = null;
            if (!string.IsNullOrEmpty(historyDbPath) && historyEntries.Count > 0)
            {
                try
                {
                    new DxfHistoryService().AppendExportRecords(historyDbPath, historyEntries);
                }
                catch (Exception ex)
                {
                    historyWarning = Tr.Instance.Format("Msg_HistoryWriteError", ex.Message);
                }
            }

            // 5. Gộp các file DXF phân loại theo Chiều dày + Vật liệu
            if (isCombine && logOK.Count > 0)
            {
                try
                {
                    progress?.Report((total, total, "Đang gộp các file DXF theo Chiều dày + Vật liệu (combine.dxf)..."));
                    var postProcessor = new DxfPostProcessor();
                    postProcessor.CombineDxfFiles(saveDir, logOK, 1000);
                }
                catch (Exception ex)
                {
                    logFail.Add($"combine.dxf: {ex.Message}");
                }
            }

            // 6. Xuất báo cáo Excel
            if (isExportExcel && exportedItems.Count > 0)
            {
                try
                {
                    progress?.Report((total, total, "Đang tạo báo cáo danh sách cắt phôi Excel..."));
                    var excelPath = !string.IsNullOrWhiteSpace(excelFilePath)
                        ? excelFilePath
                        : Path.Combine(saveDir, $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                    new ExcelReportService().CreateExcelReport(excelPath, exportedItems);
                }
                catch (Exception ex)
                {
                    logFail.Add($"Excel Report: {ex.Message}");
                }
            }

            return (logOK, logFail, logFlip, historyWarning, exportedItems);
        }

        /// <summary>
        /// Kiểm tra tính hợp lệ của bản trải phẳng (Flat Pattern) cho danh sách dòng.
        /// </summary>
        public void ValidateFlatPatterns(IEnumerable<ExportRow> rows, IReadOnlyDictionary<string, SolidCraftSheetItem>? sheetItemsMap)
        {
            foreach (var r in rows)
            {
                var bodyName = !string.IsNullOrEmpty(r.BodyName) ? r.BodyName : r.TenFile;
                if (sheetItemsMap != null && sheetItemsMap.TryGetValue(bodyName, out var item))
                {
                    r.HasFlatPatternError = item.HasFlatPatternError;
                    r.HasThicknessRuleMismatch = item.ThicknessRuleMismatch;
                    r.ThicknessRuleMismatchDetail = item.ThicknessRuleMismatchDetail;
                    r.HasDescMismatch = item.DescriptionMismatch;
                }
                r.IsValidated = true;
            }
        }
    }
}

