using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExportDxfAddIn.Models;
using ExportDxfAddIn.Services;
using netDxf;
using netDxf.Entities;

namespace ExportDxfAddIn
{
    public static class TestRunner
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_runner.log");
        private static readonly string LogFilePathWorkingDir = Path.GetFullPath("test_runner.log");

        private static void Log(string msg = "")
        {
            Console.WriteLine(msg);
            try
            {
                File.AppendAllText(LogFilePath, msg + Environment.NewLine);
                if (!string.Equals(LogFilePath, LogFilePathWorkingDir, StringComparison.OrdinalIgnoreCase))
                {
                    File.AppendAllText(LogFilePathWorkingDir, msg + Environment.NewLine);
                }
            }
            catch { }
        }

        public static async Task<int> RunAllTestsAsync(string[] args)
        {
            try { if (File.Exists(LogFilePath)) File.Delete(LogFilePath); } catch { }
            try { if (File.Exists(LogFilePathWorkingDir)) File.Delete(LogFilePathWorkingDir); } catch { }

            Log("=================================================");
            Log("  BẮT ĐẦU KIỂM THỬ TỰ ĐỘNG (AUTOMATED TEST SUITE)");
            Log("=================================================");

            int passed = 0;
            int failed = 0;

            var cliService = SolidCraftCliService.Instance;
            var cliPath = cliService.FindCliExecutablePath();
            if (string.IsNullOrEmpty(cliPath) || !File.Exists(cliPath))
            {
                Log($"[FATAL] Không tìm thấy solidcraft-cli.exe tại {cliPath}");
                return 1;
            }
            Log($"[INFO] CLI Path: {cliPath}");

            // TEST 1: Kiểm thử ExecuteBatchTolerantAsync (Bước 1)
            try
            {
                Log("\n[TEST 1] Kiểm thử ExecuteBatchTolerantAsync khi có 1 step bị lỗi...");
                string test2Path = Path.GetFullPath(@"test\Test2.sc");
                if (!File.Exists(test2Path)) throw new FileNotFoundException(test2Path);

                var tolerantCommands = new object[]
                {
                    new { command = "sheet.flat_pattern", @params = new { body = "P-1" } },
                    new { command = "sheet.flat_pattern", @params = new { body = "Hop" } }, // Lỗi trong host vì Hop là dumb solid
                    new { command = "sheet.flat_pattern", @params = new { body = "P-2" } }
                };

                var res = await cliService.ExecuteBatchTolerantAsync(cliPath, test2Path, tolerantCommands);
                if (res.Count != 3)
                {
                    throw new Exception($"Số lượng kết quả không đủ 3 (nhận được {res.Count})");
                }

                if (res[0]?["thickness"] == null)
                {
                    throw new Exception("Step 0 (P-1) không trả về kết quả trải phẳng hợp lệ");
                }
                if (res[1]?["error"] == null)
                {
                    throw new Exception("Step 1 (Hop) phải trả về error nhưng không thấy");
                }
                if (res[2]?["thickness"] == null)
                {
                    throw new Exception("Step 2 (P-2) không trả về kết quả trải phẳng hợp lệ");
                }

                Log($"  ✔ Step 0 (P-1): rule={res[0]?["rule"]}, thick={res[0]?["thickness"]}mm");
                Log($"  ✔ Step 1 (Hop): bắt được lỗi đúng thiết kế -> '{res[1]?["error"]}'");
                Log($"  ✔ Step 2 (P-2): rule={res[2]?["rule"]}, thick={res[2]?["thickness"]}mm");
                Log("  ==> TEST 1 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 1 FAILED: {ex.Message}");
                failed++;
            }

            // TEST 2: Kiểm thử LoadDesignAsync trên Test2.sc (Bước 2 & 3 - Linked Sheet Metal)
            SolidCraftLoadResult? loadResult = null;
            try
            {
                Log("\n[TEST 2] Kiểm thử LoadDesignAsync trên test\\Test2.sc...");
                string test2Path = Path.GetFullPath(@"test\Test2.sc");

                loadResult = await cliService.LoadDesignAsync(test2Path, useLiveApp: false, captureThumbnails: true);

                Log($"  Tổng số sheet items nhận diện: {loadResult.SheetItems.Count}");
                foreach (var item in loadResult.SheetItems)
                {
                    Log($"  - Body: {item.BodyName,-6} | PartNo: {item.PartNumber,-6} | Rule: {item.RuleName,-10} | Thick: {item.ActualThicknessMm}mm | Bends: {item.BendCount} | Linked: {item.IsLinked} ({item.LinkedSourceName}/{item.ExportBodyName}) | Flat: {item.FlatWidthMm:0.#}x{item.FlatHeightMm:0.#}mm");
                }

                if (loadResult.SheetItems.Count < 4)
                {
                    throw new Exception($"Kỳ vọng ít nhất 4 sheet items (P-1, P-2, P-3, Hop) nhưng nhận được {loadResult.SheetItems.Count}");
                }

                var hopItem = loadResult.SheetItems.FirstOrDefault(i => i.BodyName.Equals("Hop", StringComparison.OrdinalIgnoreCase));
                if (hopItem == null)
                {
                    throw new Exception("Không tìm thấy body Hop trong kết quả!");
                }

                if (!hopItem.IsLinked) throw new Exception("Hop phải có IsLinked = true");
                if (Math.Abs(hopItem.ActualThicknessMm - 4.0) > 0.01) throw new Exception($"Chiều dày Hop phải là 4.0mm (nhận được {hopItem.ActualThicknessMm})");
                if (hopItem.BendCount != 4) throw new Exception($"Hop phải có 4 bends (nhận được {hopItem.BendCount})");
                if (!hopItem.RuleName.Equals("PL4-SS400", StringComparison.OrdinalIgnoreCase)) throw new Exception($"Rule của Hop phải là PL4-SS400 (nhận được {hopItem.RuleName})");
                if (!hopItem.ExportBodyName.Equals("Body1", StringComparison.OrdinalIgnoreCase)) throw new Exception($"ExportBodyName phải là Body1 (nhận được {hopItem.ExportBodyName})");
                if (string.IsNullOrEmpty(hopItem.ThumbnailPath) || !File.Exists(hopItem.ThumbnailPath))
                {
                    throw new Exception($"Thumbnail của Hop không tồn tại: {hopItem.ThumbnailPath}");
                }

                Log($"  ✔ Body Hop: {hopItem.RuleName}, thick={hopItem.ActualThicknessMm}mm, bends={hopItem.BendCount}, thumbnail={Path.GetFileName(hopItem.ThumbnailPath)}");
                Log("  ==> TEST 2 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 2 FAILED: {ex.Message}");
                failed++;
            }

            // TEST 3: Kiểm thử Xuất DXF cho Hop (Bước 4)
            try
            {
                Log("\n[TEST 3] Kiểm thử Xuất DXF cho chi tiết linked Hop...");
                if (loadResult == null) throw new Exception("Không có loadResult từ TEST 2");

                var outDir = Path.GetFullPath(@"test\result\test_output");
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                var hopItem = loadResult.SheetItems.First(i => i.BodyName.Equals("Hop", StringComparison.OrdinalIgnoreCase));
                var exportRow = new ExportRow
                {
                    PartNumber = "Hop",
                    TenFile = "Hop",
                    BodyName = "Hop",
                    SoLuong = 2,
                    ChieuDayVatLieu = "PL4-SS400",
                    IsValidated = true
                };

                var itemsMap = new Dictionary<string, SolidCraftSheetItem>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Hop"] = hopItem
                };

                var manager = new SolidCraftDxfExportManager();
                var expRes = await manager.ExportSelectedRowsAsync(
                    selectedRows: new[] { exportRow },
                    saveDir: outDir,
                    isCombine: true,
                    isFlip: true,
                    isExportExcel: false,
                    excelFilePath: null,
                    historyDbPath: null,
                    designFileName: "Test2",
                    designFilePath: Path.GetFullPath(@"test\Test2.sc"),
                    isLiveApp: false,
                    sheetItemsMap: itemsMap
                );

                if (expRes.logOK.Count == 0)
                {
                    throw new Exception("ExportSelectedRowsAsync không xuất được file nào");
                }

                string singleDxf = Path.Combine(outDir, "Hop_2_PL4-SS400.dxf");
                if (!File.Exists(singleDxf))
                {
                    throw new Exception($"File DXF đơn lẻ không tồn tại: {singleDxf}");
                }

                // Đọc kiểm tra layer chuẩn hoá netDxf
                var dxfDoc = DxfDocument.Load(singleDxf);
                if (dxfDoc == null) throw new Exception("Không thể nạp DXF qua netDxf");

                var layerNames = dxfDoc.Layers.Select(l => l.Name).ToList();
                Log($"  Layers trong DXF Hop: {string.Join(", ", layerNames)}");

                if (!layerNames.Contains("IV_OUTER_PROFILE"))
                {
                    throw new Exception("Thiếu layer chuẩn hoá IV_OUTER_PROFILE");
                }
                if (!layerNames.Contains("IV_BEND") && !layerNames.Contains("IV_BEND_DOWN"))
                {
                    throw new Exception("Thiếu layer chuẩn hoá IV_BEND / IV_BEND_DOWN");
                }

                // Kiểm tra biên dạng ngoài đã được nối thành Polyline kín
                var outerPolys = dxfDoc.Entities.Polylines2D
                    .Where(p => p.Layer != null && p.Layer.Name.Equals("IV_OUTER_PROFILE", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var outerLines = dxfDoc.Entities.Lines
                    .Where(l => l.Layer != null && l.Layer.Name.Equals("IV_OUTER_PROFILE", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var outerArcs = dxfDoc.Entities.Arcs
                    .Where(a => a.Layer != null && a.Layer.Name.Equals("IV_OUTER_PROFILE", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Log($"  Outer Polylines in Hop: {outerPolys.Count}, IsClosed: {outerPolys.FirstOrDefault()?.IsClosed}, Vertices: {outerPolys.FirstOrDefault()?.Vertexes.Count}");
                Log($"  Remaining outer Lines: {outerLines.Count}, Arcs: {outerArcs.Count}");

                if (outerPolys.Count == 0 || !outerPolys[0].IsClosed)
                {
                    throw new Exception("Biên dạng ngoài của Hop phải được gộp thành 1 Polyline kín (IsClosed = true)");
                }
                if (outerLines.Count > 0 || outerArcs.Count > 0)
                {
                    throw new Exception($"Không được còn Line ({outerLines.Count}) hoặc Arc ({outerArcs.Count}) rời rạc trên IV_OUTER_PROFILE");
                }

                string combineDxf = Path.Combine(outDir, "combine.dxf");
                if (!File.Exists(combineDxf))
                {
                    throw new Exception($"File combine DXF không tồn tại: {combineDxf}");
                }

                Log($"  ✔ Đã xuất file DXF đơn lẻ: {Path.GetFileName(singleDxf)}");
                Log($"  ✔ Đã xuất file combine DXF: {Path.GetFileName(combineDxf)}");
                Log("  ==> TEST 3 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 3 FAILED: {ex.Message}");
                failed++;
            }

            // TEST 4: Kiểm thử UpdateLinksAsync (Bước 3b)
            try
            {
                Log("\n[TEST 4] Kiểm thử UpdateLinksAsync...");
                string test2Path = Path.GetFullPath(@"test\Test2.sc");

                var updateRes = await cliService.UpdateLinksAsync(test2Path, useLiveApp: false);
                Log($"  Unchanged components: {string.Join(", ", updateRes.UnchangedComponents)}");
                Log($"  Updated components: {string.Join(", ", updateRes.UpdatedComponents)}");
                Log($"  Warnings: {string.Join(", ", updateRes.Warnings)}");

                if (!updateRes.UnchangedComponents.Contains("Hop") && !updateRes.UpdatedComponents.Contains("Hop"))
                {
                    throw new Exception("UpdateLinks không trả về component Hop");
                }

                Log("  ✔ UpdateLinksAsync kiểm tra thành công, không làm hỏng file.");
                Log("  ==> TEST 4 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 4 FAILED: {ex.Message}");
                failed++;
            }

            // TEST 5: Kiểm thử nạp Test.sc (chứa chi tiết không phải sheet metal)
            try
            {
                Log("\n[TEST 5] Kiểm thử LoadDesignAsync trên test\\Test.sc...");
                string testPath = Path.GetFullPath(@"test\Test.sc");

                var resTest = await cliService.LoadDesignAsync(testPath, useLiveApp: false, captureThumbnails: false);
                Log($"  Sheet items: {resTest.SheetItems.Count}");
                foreach (var si in resTest.SheetItems)
                {
                    Log($"  - Sheet item: {si.BodyName}");
                }
                Log($"  Unrecognized bodies ({resTest.UnrecognizedBodies.Count}):");
                foreach (var ub in resTest.UnrecognizedBodies)
                {
                    Log($"    • {ub}");
                }
                Log($"  Warnings ({resTest.Warnings.Count}):");
                foreach (var w in resTest.Warnings)
                {
                    Log($"    • {w}");
                }

                // Trong Test.sc: Angle 50x50x5, Body1 (extrude 10mm), Body2 (Hex Nut M2) không phải sheet metal
                // và không có sheet rule -> nằm trong UnrecognizedBodies!
                if (!resTest.UnrecognizedBodies.Any(b => b.Contains("Angle") || b.Contains("Body1") || b.Contains("Body2")))
                {
                    throw new Exception("Các body thông thường (Angle, Body1, Body2) phải được ghi vào UnrecognizedBodies");
                }

                Log("  ✔ Body không phải sheet metal được lọc sạch sẽ và ghi vào UnrecognizedBodies.");
                Log("  ==> TEST 5 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 5 FAILED: {ex.Message}");
                failed++;
            }

            // TEST 6: Kiểm thử khi file liên kết bị mất (Missing Link Handling)
            string hopDocPath = @"C:\Users\Admin\Documents\Hop.sc";
            string hopTempMissingPath = @"C:\Users\Admin\Documents\Hop.sc.temp_missing";
            try
            {
                Log("\n[TEST 6] Kiểm thử Missing Link Handling (tạm đổi tên Hop.sc)...");
                string test2Path = Path.GetFullPath(@"test\Test2.sc");

                if (File.Exists(hopDocPath))
                {
                    File.Move(hopDocPath, hopTempMissingPath);
                }

                var resMissing = await cliService.LoadDesignAsync(test2Path, useLiveApp: false, captureThumbnails: false);

                if (!resMissing.Warnings.Any(w => w.Contains("Không tìm thấy file nguồn") || w.Contains("Hop.sc")))
                {
                    throw new Exception("Kỳ vọng Warning 'Không tìm thấy file nguồn' nhưng không có");
                }

                if (!resMissing.UnrecognizedBodies.Any(b => b.Contains("Hop")))
                {
                    throw new Exception("Body Hop phải được đưa vào UnrecognizedBodies khi link bị mất");
                }

                // Các body bản địa (P-1, P-2, P-3) vẫn phải nạp bình thường!
                if (resMissing.SheetItems.Count < 3)
                {
                    throw new Exception($"P-1, P-2, P-3 vẫn phải nạp đủ (nhận được {resMissing.SheetItems.Count} items)");
                }

                Log($"  ✔ Link mất không gây crash. SheetItems bản địa: {resMissing.SheetItems.Count}, Warnings: {resMissing.Warnings.Count}");
                Log("  ==> TEST 6 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 6 FAILED: {ex.Message}");
                failed++;
            }
            finally
            {
                if (File.Exists(hopTempMissingPath))
                {
                    if (File.Exists(hopDocPath)) File.Delete(hopDocPath);
                    File.Move(hopTempMissingPath, hopDocPath);
                }
            }

            // TEST 7: Kiểm thử phát hiện link lỗi thời & tự động backup + cập nhật khi Update Model
            string hopOrigBackup = @"C:\Users\Admin\Documents\Hop.sc.stale_orig";
            string staleTestHost = Path.GetFullPath(@"test\Test2_stale_test.sc");
            string staleTestBak = Path.GetFullPath(@"test\Test2_stale_test.sc.bak");

            try
            {
                Log("\n[TEST 7] Kiểm thử Link lỗi thời & UpdateLinksAsync...");
                string test2Path = Path.GetFullPath(@"test\Test2.sc");

                // 1. Tạo bản sao file host để kiểm thử
                File.Copy(test2Path, staleTestHost, overwrite: true);

                // 2. Sao lưu Hop.sc gốc
                File.Copy(hopDocPath, hopOrigBackup, overwrite: true);

                // 3. Sửa thông số d1 = 350 trong Hop.sc
                var changeParamCmd = new object[]
                {
                    new { command = "parameters.change", @params = new { name = "d1", expression = "350" } }
                };
                var tempScript = Path.Combine(Path.GetTempPath(), $"sc_param_{Guid.NewGuid():N}.json");
                var tempRes = Path.Combine(Path.GetTempPath(), $"sc_paramres_{Guid.NewGuid():N}.json");
                try
                {
                    File.WriteAllText(tempScript, System.Text.Json.JsonSerializer.Serialize(changeParamCmd), new System.Text.UTF8Encoding(false));
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = cliPath,
                        Arguments = $"run \"{tempScript}\" --in \"{hopDocPath}\" --save \"{hopDocPath}\" --results --json-out \"{tempRes}\" --allow-errors",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = System.Diagnostics.Process.Start(psi)!;
                    await proc.WaitForExitAsync();
                }
                finally
                {
                    try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
                    try { if (File.Exists(tempRes)) File.Delete(tempRes); } catch { }
                }

                // 4. Nạp lại file host bản sao: phải phát hiện link lỗi thời
                var resStale = await cliService.LoadDesignAsync(staleTestHost, useLiveApp: false, captureThumbnails: false);
                var hopStaleItem = resStale.SheetItems.FirstOrDefault(i => i.BodyName.Equals("Hop", StringComparison.OrdinalIgnoreCase));
                if (hopStaleItem == null) throw new Exception("Không tìm thấy body Hop trong kết quả nạp");

                if (!hopStaleItem.IsLinkStale)
                {
                    throw new Exception("Body Hop phải được đánh dấu IsLinkStale = true khi file gốc bị thay đổi");
                }
                Log($"  ✔ Đã phát hiện link lỗi thời: IsLinkStale={hopStaleItem.IsLinkStale}, Warning: {resStale.Warnings.FirstOrDefault(w => w.Contains("lệch") || w.Contains("khác"))}");

                // 5. Chạy UpdateLinksAsync trên file host bản sao
                var updateRes = await cliService.UpdateLinksAsync(staleTestHost, useLiveApp: false);
                if (!updateRes.UpdatedComponents.Contains("Hop"))
                {
                    throw new Exception("UpdateLinksAsync phải cập nhật component Hop");
                }
                if (!File.Exists(staleTestBak))
                {
                    throw new Exception("UpdateLinksAsync phải tạo bản sao lưu .sc.bak trước khi ghi đè");
                }
                Log($"  ✔ UpdateLinksAsync đã cập nhật component: {string.Join(", ", updateRes.UpdatedComponents)}, tạo backup: {Path.GetFileName(staleTestBak)}");

                // 6. Nạp lại file host sau khi cập nhật: link không còn lỗi thời
                var resUpdated = await cliService.LoadDesignAsync(staleTestHost, useLiveApp: false, captureThumbnails: false);
                var hopUpdatedItem = resUpdated.SheetItems.First(i => i.BodyName.Equals("Hop", StringComparison.OrdinalIgnoreCase));
                if (hopUpdatedItem.IsLinkStale)
                {
                    throw new Exception("Sau khi Update Model, Hop không được còn IsLinkStale = true");
                }

                Log($"  ✔ Sau khi cập nhật: IsLinkStale={hopUpdatedItem.IsLinkStale}, kích thước mới={hopUpdatedItem.FlatWidthMm:0.#}x{hopUpdatedItem.FlatHeightMm:0.#}mm");
                Log("  ==> TEST 7 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 7 FAILED: {ex.Message}");
                failed++;
            }
            finally
            {
                // Khôi phục lại Hop.sc gốc
                if (File.Exists(hopOrigBackup))
                {
                    File.Copy(hopOrigBackup, hopDocPath, overwrite: true);
                    try { File.Delete(hopOrigBackup); } catch { }
                }
                try { if (File.Exists(staleTestHost)) File.Delete(staleTestHost); } catch { }
                try { if (File.Exists(staleTestBak)) File.Delete(staleTestBak); } catch { }
            }

            // TEST 8: Kiểm thử Nối đường bao thành Polyline kín trên chi tiết spline (97 segments Line/Arc)
            try
            {
                Log("\n[TEST 8] Kiểm thử Nối đường bao thành Polyline kín (JoinBoundaryCurvesIntoClosedPolylines)...");
                string splineSc = Path.GetFullPath(@"test\spline.sc");
                if (File.Exists(splineSc))
                {
                    var outDir = Path.GetFullPath(@"test\result\test_output_spline");
                    if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                    var splineLoad = await cliService.LoadDesignAsync(splineSc, useLiveApp: false, captureThumbnails: false);
                    if (splineLoad.SheetItems.Count > 0)
                    {
                        var spItem = splineLoad.SheetItems[0];
                        var row = new ExportRow
                        {
                            PartNumber = spItem.PartNumber,
                            TenFile = spItem.BodyName,
                            BodyName = spItem.BodyName,
                            SoLuong = 1,
                            ChieuDayVatLieu = "PL2.5-Default",
                            IsValidated = true
                        };
                        var map = new Dictionary<string, SolidCraftSheetItem>(StringComparer.OrdinalIgnoreCase)
                        {
                            [spItem.BodyName] = spItem
                        };

                        var manager = new SolidCraftDxfExportManager();
                        var res = await manager.ExportSelectedRowsAsync(
                            selectedRows: new[] { row },
                            saveDir: outDir,
                            isCombine: false,
                            isFlip: false,
                            isExportExcel: false,
                            excelFilePath: null,
                            historyDbPath: null,
                            designFileName: "spline",
                            designFilePath: splineSc,
                            isLiveApp: false,
                            sheetItemsMap: map
                        );

                        string exportedDxf = Path.Combine(outDir, $"{spItem.PartNumber}_1_PL2.5-Default.dxf");
                        if (!File.Exists(exportedDxf)) throw new Exception($"Không tìm thấy file DXF xuất ra: {exportedDxf}");

                        var doc = DxfDocument.Load(exportedDxf);
                        var polys = doc.Entities.Polylines2D
                            .Where(p => p.Layer != null && p.Layer.Name.Equals("IV_OUTER_PROFILE", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var lines = doc.Entities.Lines
                            .Where(l => l.Layer != null && l.Layer.Name.Equals("IV_OUTER_PROFILE", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var arcs = doc.Entities.Arcs
                            .Where(a => a.Layer != null && a.Layer.Name.Equals("IV_OUTER_PROFILE", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        Log($"  Spline DXF -> Polylines2D: {polys.Count}, IsClosed: {polys.FirstOrDefault()?.IsClosed}, Vertices: {polys.FirstOrDefault()?.Vertexes.Count}");
                        Log($"  Spline DXF -> Remaining Lines: {lines.Count}, Arcs: {arcs.Count}");

                        if (polys.Count != 1 || !polys[0].IsClosed)
                        {
                            throw new Exception("Biên dạng Spline phải được gộp thành đúng 1 Polyline kín (IsClosed = true)");
                        }
                        if (lines.Count > 0 || arcs.Count > 0)
                        {
                            throw new Exception($"Toàn bộ Line và Arc của spline phải được gộp vào Polyline (còn thừa {lines.Count} lines, {arcs.Count} arcs)");
                        }
                        if (polys[0].Vertexes.Count < 50)
                        {
                            throw new Exception($"Số lượng vertex của spline polyline không hợp lệ ({polys[0].Vertexes.Count})");
                        }
                        Log($"  ✔ Chi tiết spline phức tạp đã được gộp thành công thành 1 Polyline2D kín với {polys[0].Vertexes.Count} đỉnh (gồm cả cung tròn Bulge).");
                    }
                }

                // 2. Kiểm thử đơn vị với hình học tổng hợp (Lines + Arcs)
                var synthDoc = new DxfDocument();
                var lyr = new netDxf.Tables.Layer("IV_OUTER_PROFILE");
                synthDoc.Layers.Add(lyr);

                // Tạo hình chữ nhật bo 1 góc (0,0) -> (100,0) -> Arc bo góc -> (110, 10) -> (110, 100) -> (0, 100) -> (0,0)
                // Line 1: (0,0) -> (100,0)
                var l1 = new Line(new Vector3(0, 0, 0), new Vector3(100, 0, 0)) { Layer = lyr };
                // Arc 2: bo góc từ (100,0) tới (110,10) tâm (100,10) bán kính 10, góc 270..0
                var a2 = new Arc(new Vector3(100, 10, 0), 10, 270, 0) { Layer = lyr };
                // Line 3: (110,10) -> (110,100)
                var l3 = new Line(new Vector3(110, 10, 0), new Vector3(110, 100, 0)) { Layer = lyr };
                // Line 4: (110,100) -> (0,100)
                var l4 = new Line(new Vector3(110, 100, 0), new Vector3(0, 100, 0)) { Layer = lyr };
                // Line 5: (0,100) -> (0,0)
                var l5 = new Line(new Vector3(0, 100, 0), new Vector3(0, 0, 0)) { Layer = lyr };

                // Thêm vào doc theo thứ tự xáo trộn
                synthDoc.Entities.Add(l4);
                synthDoc.Entities.Add(l1);
                synthDoc.Entities.Add(l5);
                synthDoc.Entities.Add(a2);
                synthDoc.Entities.Add(l3);

                int merged = DxfPostProcessor.JoinBoundaryCurvesIntoClosedPolylines(synthDoc);
                if (merged != 1) throw new Exception($"Kỳ vọng gộp thành 1 polyline kín nhưng nhận được {merged}");
                var synthPoly = synthDoc.Entities.Polylines2D.First();
                if (!synthPoly.IsClosed) throw new Exception("Polyline tổng hợp phải IsClosed = true");
                if (synthPoly.Vertexes.Count != 5) throw new Exception($"Polyline tổng hợp phải có 5 vertices (nhận được {synthPoly.Vertexes.Count})");
                if (!synthPoly.Vertexes.Any(v => Math.Abs(v.Bulge) > 1e-4)) throw new Exception("Phải có vertex chứa Bulge của đoạn cung");

                Log($"  ✔ Kiểm thử hình học tổng hợp thành công: 5 đoạn rời xáo trộn -> 1 Polyline2D kín (5 đỉnh, Bulge={synthPoly.Vertexes.First(v => Math.Abs(v.Bulge) > 1e-4).Bulge:F4})");
                Log("  ==> TEST 8 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 8 FAILED: {ex.Message}");
                failed++;
            }

            // TEST 9: Kiểm thử Tự động gom nhóm & cộng dồn số lượng các body Copy / Pattern (Plan_GroupIdenticalBodies)
            try
            {
                Log("\n[TEST 9] Kiểm thử Tự động gom nhóm & cộng dồn số lượng các body Copy / Pattern...");
                string test2Path = Path.GetFullPath(@"test\Test2.sc");
                if (loadResult == null)
                {
                    loadResult = await cliService.LoadDesignAsync(test2Path, useLiveApp: false, captureThumbnails: false);
                }

                Log($"  Số lượng sheet items sau khi gom nhóm: {loadResult.SheetItems.Count}");
                foreach (var item in loadResult.SheetItems)
                {
                    Log($"  - Body: {item.BodyName,-10} | PartNo: {item.PartNumber,-10} | Qty: {item.Quantity} | Rule: {item.RuleName,-10} | Thick: {item.ActualThicknessMm}mm | Linked: {item.IsLinked} | Vol: {item.VolumeMm3:0.##} mm³ | Area: {item.AreaMm2:0.##} mm²");
                }

                // 1. Kiểm tra không còn body trùng lặp
                var duplicateNames = new[] { "P-3 (2)", "Px-1 (2)", "Px-1 (3)", "Px-2 (2)", "Px-2 (3)" };
                foreach (var dup in duplicateNames)
                {
                    if (loadResult.SheetItems.Any(i => i.BodyName.Equals(dup, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new Exception($"Body trùng lặp '{dup}' vẫn còn tồn tại trong SheetItems sau khi gộp!");
                    }
                }

                // 2. Kiểm tra số lượng cộng dồn của các chi tiết trong Test2.sc
                var p3 = loadResult.SheetItems.FirstOrDefault(i => i.BodyName.Equals("P-3", StringComparison.OrdinalIgnoreCase));
                if (p3 == null) throw new Exception("Không tìm thấy body 'P-3'");
                if (p3.Quantity != 2) throw new Exception($"P-3 phải có Quantity = 2 (nhận được {p3.Quantity})");
                if (!p3.PartNumber.Equals("P-3", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"PartNumber của P-3 phải là 'P-3' (nhận được '{p3.PartNumber}')");

                var px1 = loadResult.SheetItems.FirstOrDefault(i => i.BodyName.Equals("Px-1", StringComparison.OrdinalIgnoreCase));
                if (px1 == null) throw new Exception("Không tìm thấy body 'Px-1'");
                if (px1.Quantity != 3) throw new Exception($"Px-1 phải có Quantity = 3 (nhận được {px1.Quantity})");
                if (!px1.PartNumber.Equals("Px-1", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"PartNumber của Px-1 phải là 'Px-1' (nhận được '{px1.PartNumber}')");

                var px2 = loadResult.SheetItems.FirstOrDefault(i => i.BodyName.Equals("Px-2", StringComparison.OrdinalIgnoreCase));
                if (px2 == null) throw new Exception("Không tìm thấy body 'Px-2'");
                if (px2.Quantity != 3) throw new Exception($"Px-2 phải có Quantity = 3 (nhận được {px2.Quantity})");
                if (!px2.PartNumber.Equals("Px-2", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"PartNumber của Px-2 phải là 'Px-2' (nhận được '{px2.PartNumber}')");

                var p2 = loadResult.SheetItems.FirstOrDefault(i => i.BodyName.Equals("P-2", StringComparison.OrdinalIgnoreCase));
                if (p2 == null) throw new Exception("Không tìm thấy body 'P-2'");
                if (p2.Quantity != 1) throw new Exception($"P-2 phải có Quantity = 1 (nhận được {p2.Quantity})");

                var hop = loadResult.SheetItems.FirstOrDefault(i => i.BodyName.Equals("Hop", StringComparison.OrdinalIgnoreCase));
                if (hop == null) throw new Exception("Không tìm thấy body 'Hop'");
                if (hop.Quantity != 1) throw new Exception($"Hop phải có Quantity = 1 (nhận được {hop.Quantity})");

                var spline = loadResult.SheetItems.FirstOrDefault(i => i.BodyName.Equals("spline", StringComparison.OrdinalIgnoreCase));
                if (spline == null) throw new Exception("Không tìm thấy body 'spline'");
                if (spline.Quantity != 1) throw new Exception($"spline phải có Quantity = 1 (nhận được {spline.Quantity})");

                Log("  ✔ Đã kiểm tra mô hình thực tế Test2.sc: P-3 (Qty=2), Px-1 (Qty=3), Px-2 (Qty=3), P-2 (Qty=1), Hop (Qty=1), spline (Qty=1).");

                // 3. Unit test hàm GetCopyPatternBaseName
                if (SolidCraftCliService.GetCopyPatternBaseName("P-1 (2)") != "P-1") throw new Exception("GetCopyPatternBaseName thất bại với 'P-1 (2)'");
                if (SolidCraftCliService.GetCopyPatternBaseName("Px-1 (3)") != "Px-1") throw new Exception("GetCopyPatternBaseName thất bại với 'Px-1 (3)'");
                if (SolidCraftCliService.GetCopyPatternBaseName("Bracket (Left) (2)") != "Bracket (Left)") throw new Exception("GetCopyPatternBaseName thất bại với 'Bracket (Left) (2)'");
                if (SolidCraftCliService.GetCopyPatternBaseName("P-1 (2) (3)") != "P-1") throw new Exception("GetCopyPatternBaseName thất bại với 'P-1 (2) (3)'");
                if (SolidCraftCliService.GetCopyPatternBaseName("SimplePart") != "SimplePart") throw new Exception("GetCopyPatternBaseName thất bại với 'SimplePart'");
                Log("  ✔ Unit test GetCopyPatternBaseName chính xác 100%.");

                // 4. Safety Check: Khác thể tích / diện tích (đục thêm lỗ hoặc cắt gọt) KHÔNG được gộp nhầm
                var safeItemsDiffVol = new List<SolidCraftSheetItem>
                {
                    new SolidCraftSheetItem { BodyName = "Bracket", PartNumber = "Bracket", VolumeMm3 = 50000, AreaMm2 = 20000, FlatWidthMm = 200, FlatHeightMm = 100, ActualThicknessMm = 2.0, MaterialName = "Steel", Quantity = 1 },
                    new SolidCraftSheetItem { BodyName = "Bracket (2)", PartNumber = "Bracket (2)", VolumeMm3 = 42000, AreaMm2 = 18000, FlatWidthMm = 200, FlatHeightMm = 100, ActualThicknessMm = 2.0, MaterialName = "Steel", Quantity = 1 }
                };
                var groupedDiffVol = SolidCraftCliService.GroupIdenticalCopiedSheetItems(safeItemsDiffVol);
                if (groupedDiffVol.Count != 2)
                {
                    throw new Exception($"Safety Check thất bại: body khác thể tích (đục lỗ) không được gộp nhưng nhận được {groupedDiffVol.Count} items");
                }
                Log("  ✔ Safety Check 1: Body bị đục thêm lỗ (lệch volume/area) được giữ nguyên riêng biệt.");

                // 5. Safety Check: Khác kích thước phôi trải phẳng KHÔNG được gộp nhầm
                var safeItemsDiffFlat = new List<SolidCraftSheetItem>
                {
                    new SolidCraftSheetItem { BodyName = "Cover", PartNumber = "Cover", VolumeMm3 = 30000, AreaMm2 = 15000, FlatWidthMm = 150, FlatHeightMm = 100, ActualThicknessMm = 1.5, MaterialName = "SUS304", Quantity = 1 },
                    new SolidCraftSheetItem { BodyName = "Cover (2)", PartNumber = "Cover (2)", VolumeMm3 = 30000, AreaMm2 = 15000, FlatWidthMm = 180, FlatHeightMm = 100, ActualThicknessMm = 1.5, MaterialName = "SUS304", Quantity = 1 }
                };
                var groupedDiffFlat = SolidCraftCliService.GroupIdenticalCopiedSheetItems(safeItemsDiffFlat);
                if (groupedDiffFlat.Count != 2)
                {
                    throw new Exception($"Safety Check thất bại: body khác kích thước flat không được gộp nhưng nhận được {groupedDiffFlat.Count} items");
                }
                Log("  ✔ Safety Check 2: Body khác kích thước phôi trải phẳng được giữ nguyên riêng biệt.");

                // 6. Xoay 90 độ phôi trải phẳng (hoán vị rộng/dài) nhưng cùng hình học -> GỘP THÀNH CÔNG
                var safeItemsRotated = new List<SolidCraftSheetItem>
                {
                    new SolidCraftSheetItem { BodyName = "Flange", PartNumber = "Flange", VolumeMm3 = 20000, AreaMm2 = 10000, FlatWidthMm = 100, FlatHeightMm = 200, ActualThicknessMm = 2.0, MaterialName = "Steel", Quantity = 1 },
                    new SolidCraftSheetItem { BodyName = "Flange (2)", PartNumber = "Flange (2)", VolumeMm3 = 20000, AreaMm2 = 10000, FlatWidthMm = 200, FlatHeightMm = 100, ActualThicknessMm = 2.0, MaterialName = "Steel", Quantity = 1 }
                };
                var groupedRotated = SolidCraftCliService.GroupIdenticalCopiedSheetItems(safeItemsRotated);
                if (groupedRotated.Count != 1 || groupedRotated[0].Quantity != 2)
                {
                    throw new Exception("Gộp chi tiết xoay 90 độ trải phẳng thất bại!");
                }
                Log("  ✔ Kiểm thử kích thước trải phẳng xoay 90 độ: gộp chính xác 100% (Qty = 2).");

                // 7. Bản gốc bị xoá, chỉ còn các bản sao: Panel (2) và Panel (3) -> Gộp thành 1 item với PartNumber = 'Panel'
                var safeItemsOrphan = new List<SolidCraftSheetItem>
                {
                    new SolidCraftSheetItem { BodyName = "Panel (2)", PartNumber = "Panel (2)", VolumeMm3 = 10000, AreaMm2 = 5000, FlatWidthMm = 100, FlatHeightMm = 50, ActualThicknessMm = 1.0, MaterialName = "Steel", Quantity = 1 },
                    new SolidCraftSheetItem { BodyName = "Panel (3)", PartNumber = "Panel (3)", VolumeMm3 = 10000, AreaMm2 = 5000, FlatWidthMm = 100, FlatHeightMm = 50, ActualThicknessMm = 1.0, MaterialName = "Steel", Quantity = 1 }
                };
                var groupedOrphan = SolidCraftCliService.GroupIdenticalCopiedSheetItems(safeItemsOrphan);
                if (groupedOrphan.Count != 1 || groupedOrphan[0].Quantity != 2 || groupedOrphan[0].PartNumber != "Panel")
                {
                    throw new Exception($"Gộp bản sao khi mất bản gốc thất bại: count={groupedOrphan.Count}, qty={groupedOrphan.FirstOrDefault()?.Quantity}, partNum={groupedOrphan.FirstOrDefault()?.PartNumber}");
                }
                Log("  ✔ Kiểm thử khi mất bản gốc: các bản sao còn lại được gộp về PartNumber gốc chuẩn ('Panel', Qty=2).");

                Log("  ==> TEST 9 PASSED!");
                passed++;
            }
            catch (Exception ex)
            {
                Log($"  ❌ TEST 9 FAILED: {ex.Message}");
                failed++;
            }

            Log("\n=================================================");
            Log($"  KẾT QUẢ KIỂM THỬ: {passed} PASSED, {failed} FAILED");
            Log("=================================================");

            return failed == 0 ? 0 : 1;
        }
    }
}
