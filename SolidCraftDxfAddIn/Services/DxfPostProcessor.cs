using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using netDxf;
using netDxf.Entities;
using ExportDxfAddIn.Models;

namespace ExportDxfAddIn.Services
{
    public class DxfPostProcessor
    {
        /// <summary>Số đoạn dùng để rời rạc hoá đường cong (Ellipse/Spline) khi tính bounding box.
        /// 72 đoạn -> sai số hộp bao dưới 0.1% bán kính, không đáng kể so với khoảng hở 1000mm giữa
        /// các chi tiết trong combine.dxf.</summary>
        private const int CurvePrecision = 72;

        /// <summary>Bề rộng ước lượng của 1 ký tự theo chiều cao chữ (font SHX "txt" mặc định của DXF
        /// rộng khoảng 0.6-0.7 em; lấy 0.8 để hơi dư — thà chừa thừa chỗ còn hơn để nhãn text đè sang
        /// chi tiết bên cạnh).</summary>
        private const double TextCharWidthRatio = 0.8;

        /// <summary>Số entity không thuộc loại nào UpdateBounds nhận diện được trong lần CombineDxfFiles
        /// gần nhất — vẫn được TransformBy di chuyển đúng (TransformBy xử lý mọi loại entity của
        /// netDxf), chỉ là không đóng góp vào bounding box tính offset cột/dòng. 0 là bình thường.</summary>
        public int LastUnhandledEntityCount { get; private set; }

        /// <summary>Tên các loại entity đã bị đếm vào <see cref="LastUnhandledEntityCount"/> (vd
        /// "Hatch, Leader") — báo ra để biết chính xác cần bổ sung xử lý cho loại nào, thay vì chỉ có
        /// một con số trống trơn.</summary>
        public IReadOnlyList<string> LastUnhandledEntityTypes => _unhandledTypes;

        /// <summary>File nguồn KHÔNG gộp được (hoặc gộp được nhưng không đo được kích thước) trong lần
        /// CombineDxfFiles gần nhất, kèm lý do. Trước đây mọi lỗi của 1 file bị nuốt bởi `catch { }`
        /// trần: chi tiết biến mất khỏi combine.dxf mà người dùng không hề được báo — đúng kiểu
        /// "combine lúc đúng lúc sai".</summary>
        public IReadOnlyList<string> LastFileErrors => _fileErrors;

        private readonly List<string> _unhandledTypes = new List<string>();
        private readonly List<string> _fileErrors = new List<string>();

        /// <summary>Danh sách layer/màu ACI/xoá — trước đây hard-code (Dictionary colorMap + 2 lệnh
        /// DeleteLayerEntities cứng), giờ đọc từ LayerConfigService (màn hình "Cấu hình Layer", xem
        /// README mục 12) để có thể chỉnh không cần build lại add-in.</summary>
        private readonly List<LayerSetting> _layerSettings;

        /// <summary>Tên layer sẽ bị PostProcessDxfDoc xoá — tra sẵn thành HashSet để lọc entity NGAY
        /// khi đọc file nguồn lúc combine (xem CombineDxfFiles).</summary>
        private readonly HashSet<string> _deletedLayerNames;

        public DxfPostProcessor() : this(null) { }

        public DxfPostProcessor(List<LayerSetting>? layerSettings)
        {
            _layerSettings = layerSettings ?? LayerConfigService.Instance.Load();
            _deletedLayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cfg in _layerSettings)
            {
                if (cfg.DeleteEntities && !string.IsNullOrEmpty(cfg.Name)) _deletedLayerNames.Add(cfg.Name);
            }
        }

        private class BBoxInfo
        {
            public bool HasData = false;
            public double MinX, MinY, MaxX, MaxY;

            public void Include(double x, double y)
            {
                // Toạ độ NaN/Infinity (entity suy biến, phép rời rạc hoá lỗi) làm hỏng vĩnh viễn mọi
                // phép so sánh min/max phía sau -> chặn ngay tại cửa.
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return;

                if (!HasData)
                {
                    MinX = x; MaxX = x; MinY = y; MaxY = y;
                    HasData = true;
                }
                else
                {
                    if (x < MinX) MinX = x;
                    if (x > MaxX) MaxX = x;
                    if (y < MinY) MinY = y;
                    if (y > MaxY) MaxY = y;
                }
            }
        }

        public void ProcessSingleFileNoCombine(string dxfPath, string pn, double detailH)
        {
            DxfDocument doc = DxfDocument.Load(dxfPath);
            if (doc == null) throw new Exception("Không đọc được file DXF: " + dxfPath);

            var layerDetail = new netDxf.Tables.Layer("Detail Name");
            layerDetail.Color = new netDxf.AciColor(30);
            layerDetail = doc.Layers.Add(layerDetail);

            var t = new Text(pn, new Vector2(10, 10), detailH);
            t.Layer = layerDetail;
            doc.Entities.Add(t);

            PostProcessDxfDoc(doc);
            doc.Save(dxfPath);
        }

        public void InsertTextIntoDxf(string dxfPath, string pn, string desc, int qty, double textH, double spacing, double detailH)
        {
            DxfDocument doc = DxfDocument.Load(dxfPath);
            if (doc == null) throw new Exception("Không đọc được file DXF: " + dxfPath);

            var layerDetail = new netDxf.Tables.Layer("Detail Name");
            layerDetail.Color = new netDxf.AciColor(30);
            layerDetail = doc.Layers.Add(layerDetail);

            string[] texts = { "P/N: " + pn, "DESC: " + desc, "QTY: " + qty, pn };
            double[] heights = { textH, textH, textH, detailH };
            bool[] useDetailLayer = { false, false, false, true };

            // Thứ tự đặt: text số 4 (pn, layer Detail Name) lên đầu tiên tại (20,20),
            // tiếp theo là text số 1, 2, 3 xếp xuống dưới theo spacing.
            int[] placementOrder = { 3, 0, 1, 2 };

            for (int j = 0; j < placementOrder.Length; j++)
            {
                int i = placementOrder[j];
                var t = new Text(texts[i], new Vector2(20, 20 - (j * spacing)), heights[i]);
                if (useDetailLayer[i]) t.Layer = layerDetail;
                doc.Entities.Add(t);
            }

            PostProcessDxfDoc(doc);
            doc.Save(dxfPath);
        }

        /// <param name="exportedFileNames">Tên file (không kèm đường dẫn) CỦA CHÍNH LẦN EXPORT NÀY.
        /// Trước đây hàm tự quét *.dxf trong folder nên gom nhầm cả file của lần export trước: export
        /// 20 part vào thư mục X, sau đó lọc lại chỉ chọn 5 part và export tiếp vào đúng thư mục X sẽ
        /// làm combine.dxf chứa cả 20 part cũ.</param>
        public bool CombineDxfFiles(string folderPath, IReadOnlyList<string> exportedFileNames, double gapUnits)
        {
            var files = new List<string>();
            foreach (var nm in exportedFileNames)
            {
                if (!nm.Equals("combine.dxf", StringComparison.OrdinalIgnoreCase)) files.Add(nm);
            }
            if (files.Count == 0) return false;

            LastUnhandledEntityCount = 0;
            _unhandledTypes.Clear();
            _fileErrors.Clear();

            // Phân nhóm theo cặp (Chiều dày + Vật liệu), không phân biệt hoa thường
            // (vd: "PL6-S275JR", "PL6-SUS304", "PL8-S275JR")
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                string key = GetThicknessAndMaterialKey(f);
                if (!groups.ContainsKey(key)) groups[key] = new List<string>();
                groups[key].Add(f);
            }

            var newDoc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2010);
            var sortedKeys = new List<string>(groups.Keys);
            // Sắp xếp các cột từ trái sang phải:
            // 1. Theo chiều dày số thực tăng dần (vd: PL6 -> PL8 -> PL10 -> PL12.5, Unknown ở cuối).
            // 2. Nếu cùng chiều dày thì xếp theo tên vật liệu A-Z (vd: PL6-S275JR -> PL6-SS400 -> PL6-SUS304).
            sortedKeys.Sort((a, b) =>
            {
                int thkCompare = ParseThickness(a).CompareTo(ParseThickness(b));
                if (thkCompare != 0) return thkCompare;

                int matCompare = StringComparer.OrdinalIgnoreCase.Compare(ParseMaterial(a), ParseMaterial(b));
                if (matCompare != 0) return matCompare;

                return StringComparer.OrdinalIgnoreCase.Compare(a, b);
            });

            double xOffset = 0;
            foreach (var groupKey in sortedKeys)
            {
                double yOffset = 0;
                double maxColWidth = 0;
                var groupFiles = new List<string>(groups[groupKey]);
                groupFiles.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string fname in groupFiles)
                {
                    try
                    {
                        string sPath = Path.Combine(folderPath, fname);
                        DxfDocument sDoc = DxfDocument.Load(sPath);
                        if (sDoc == null)
                        {
                            _fileErrors.Add(fname + ": DxfDocument.Load trả về null");
                            continue;
                        }

                        double scale = 1.0;
                        if (sDoc.DrawingVariables.InsUnits == netDxf.Units.DrawingUnits.Meters) scale = 1000.0;

                        // Loại luôn entity thuộc layer sẽ bị xoá (IV_TANGENT/IV_ARC_CENTERS...) ngay
                        // từ đây: chúng vốn bị PostProcessDxfDoc xoá ở cuối, nhưng nếu để lại thì vẫn
                        // được tính vào bounding box -> combine.dxf chừa chỗ cho hình đã biến mất.
                        var allEntities = new List<EntityObject>();
                        foreach (var ent in sDoc.Entities.All)
                        {
                            if (ent.Layer != null && _deletedLayerNames.Contains(ent.Layer.Name)) continue;
                            allEntities.Add(ent);
                        }
                        if (allEntities.Count == 0)
                        {
                            _fileErrors.Add(fname + ": không có entity nào để gộp");
                            continue;
                        }

                        var bx = new BBoxInfo();
                        foreach (var e in allEntities) UpdateBounds(e, bx);

                        // Không đo được hộp bao (toàn entity lạ) thì VẪN gộp hình vào, giữ nguyên toạ
                        // độ tương đối, chỉ là không chừa được chỗ. Trước đây gặp trường hợp này là
                        // `continue` -> chi tiết biến mất khỏi combine.dxf trong im lặng.
                        double minX = bx.HasData ? bx.MinX : 0;
                        double minY = bx.HasData ? bx.MinY : 0;
                        double w = bx.HasData ? (bx.MaxX - bx.MinX) * scale : 0;
                        double h = bx.HasData ? (bx.MaxY - bx.MinY) * scale : 0;
                        if (!bx.HasData)
                        {
                            _fileErrors.Add(fname + ": không đo được bounding box (đã gộp hình nhưng không chừa được chỗ)");
                        }

                        if (w > maxColWidth) maxColWidth = w;

                        double tx = -minX * scale + xOffset;
                        double ty = -minY * scale + yOffset;

                        foreach (var e in allEntities)
                        {
                            var clonedEnt = (EntityObject)e.Clone();
                            if (clonedEnt == null) continue;
                            TransformEntity(clonedEnt, tx, ty, scale);
                            newDoc.Entities.Add(clonedEnt);
                        }

                        yOffset += h + gapUnits;
                    }
                    catch (Exception ex)
                    {
                        // Vẫn nuốt lỗi để 1 file hỏng không phá cả lần combine, NHƯNG phải ghi lại lý
                        // do — `catch { }` trần trước đây khiến chi tiết mất tích không dấu vết.
                        _fileErrors.Add(fname + ": " + ex.Message);
                    }
                }
                xOffset += maxColWidth + gapUnits;
            }

            PostProcessDxfDoc(newDoc);
            string outPath = Path.Combine(folderPath, "combine.dxf");
            newDoc.Save(outPath);
            return File.Exists(outPath);
        }

        /// <summary>
        /// Trích xuất khoá phân nhóm kết hợp Chiều dày và Vật liệu từ tên file DXF
        /// (vd: "PartA_4_PL6-S275JR.dxf" -> "PL6-S275JR", "PartB_2_PL6-SUS304.dxf" -> "PL6-SUS304").
        /// Hỗ trợ an toàn cả trường hợp PartNumber chứa dấu "_" hoặc tên vật liệu chứa dấu "_" / "-".
        /// </summary>
        private string GetThicknessAndMaterialKey(string fileName)
        {
            try
            {
                string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrWhiteSpace(nameOnly)) return "Unknown";

                // Ưu tiên 1: Khớp chuẩn "{PartNumber}_{Qty}_PL{Thickness}-{Material}" từ phải sang trái
                var matchWithQty = System.Text.RegularExpressions.Regex.Match(
                    nameOnly,
                    @"_\d+_(PL\s*[\d.,]+\s*-\s*.+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.RightToLeft);
                if (matchWithQty.Success)
                {
                    return NormalizeSpecKey(matchWithQty.Groups[1].Value);
                }

                // Ưu tiên 2: Khớp hậu tố "_PL{Thickness}-{Material}" hoặc "_PL{Thickness}" ở cuối tên file
                var matchPl = System.Text.RegularExpressions.Regex.Match(
                    nameOnly,
                    @"(?:^|_)(PL\s*[\d.,]+(?:\s*-\s*.+)?)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.RightToLeft);
                if (matchPl.Success)
                {
                    return NormalizeSpecKey(matchPl.Groups[1].Value);
                }

                // Fallback: lấy phần sau dấu '_' cuối cùng
                int lastUnderscore = nameOnly.LastIndexOf('_');
                string lastPart = lastUnderscore >= 0 ? nameOnly.Substring(lastUnderscore + 1).Trim() : nameOnly.Trim();
                return string.IsNullOrEmpty(lastPart) ? "Unknown" : lastPart;
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Chuẩn hoá chuỗi khoá "PL{Thickness}-{Material}" để các file có cùng chiều dày và vật liệu
        /// luôn gom vào đúng 1 nhóm (vd: "pl6,0 - s275jr" -> "PL6-S275JR").
        /// </summary>
        private static string NormalizeSpecKey(string rawSpec)
        {
            if (string.IsNullOrWhiteSpace(rawSpec)) return "Unknown";

            var m = System.Text.RegularExpressions.Regex.Match(
                rawSpec.Trim(),
                @"^PL\s*([\d.,]+)(?:\s*-\s*(.*))?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return rawSpec.Trim();

            string thkPart = m.Groups[1].Value.Trim().Replace(',', '.');
            if (double.TryParse(thkPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double thkVal))
            {
                thkPart = Math.Round(thkVal, 2).ToString("0.##", CultureInfo.InvariantCulture);
            }

            string matPart = m.Groups[2].Success ? m.Groups[2].Value.Trim() : string.Empty;
            return string.IsNullOrEmpty(matPart)
                ? $"PL{thkPart}"
                : $"PL{thkPart}-{matPart.ToUpperInvariant()}";
        }

        /// <summary>
        /// Trích số thực chiều dày từ khoá nhóm (vd "PL6-S275JR" -> 6, "PL12.5-SS400" -> 12.5)
        /// để sắp xếp đúng thứ tự số học. Chỉ đọc số ở phần chiều dày (trước dấu '-'), tránh
        /// bắt nhầm số trong tên mác thép (như 355 trong S355 hoặc 304 trong SUS304).
        /// Key không parse được (vd "Unknown") -> đẩy xuống cuối danh sách (double.MaxValue).
        /// </summary>
        private static double ParseThickness(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return double.MaxValue;

            int dashIdx = key.IndexOf('-');
            string thkSegment = dashIdx >= 0 ? key.Substring(0, dashIdx) : key;

            var m = System.Text.RegularExpressions.Regex.Match(thkSegment, @"([\d.,]+)");
            return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : double.MaxValue;
        }

        /// <summary>
        /// Trích tên vật liệu từ khoá nhóm (vd "PL6-S275JR" -> "S275JR") để sắp xếp thứ cấp A-Z
        /// khi có nhiều loại vật liệu cùng chiều dày.
        /// </summary>
        private static string ParseMaterial(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            int dashIdx = key.IndexOf('-');
            if (dashIdx < 0 || dashIdx + 1 >= key.Length) return string.Empty;

            return key.Substring(dashIdx + 1).Trim();
        }

        /// <summary>
        /// Danh sách các layer biên dạng (contour/boundary) mặc định cần gộp thành Polyline2D kín.
        /// </summary>
        public static readonly HashSet<string> DefaultProfileLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IV_OUTER_PROFILE",
            "IV_INTERIOR_PROFILES",
            "OUTLINE",
            "CUT",
            "PERFORATION",
            "IV_MARK_SURFACE",
            "IV_MARK_SURFACE_BACK",
            "ETCH"
        };

        private class Segment2D
        {
            public EntityObject OriginalEntity { get; }
            public Vector2 StartPoint { get; }
            public Vector2 EndPoint { get; }
            public netDxf.Tables.Layer Layer { get; }
            public bool IsArc { get; }
            public double SweepRad { get; }

            public Segment2D(Line ln)
            {
                OriginalEntity = ln;
                StartPoint = new Vector2(ln.StartPoint.X, ln.StartPoint.Y);
                EndPoint = new Vector2(ln.EndPoint.X, ln.EndPoint.Y);
                Layer = ln.Layer;
                IsArc = false;
                SweepRad = 0.0;
            }

            public Segment2D(Arc arc)
            {
                OriginalEntity = arc;
                Layer = arc.Layer;
                IsArc = true;

                double startRad = arc.StartAngle * Math.PI / 180.0;
                double endRad = arc.EndAngle * Math.PI / 180.0;
                StartPoint = new Vector2(
                    arc.Center.X + arc.Radius * Math.Cos(startRad),
                    arc.Center.Y + arc.Radius * Math.Sin(startRad));
                EndPoint = new Vector2(
                    arc.Center.X + arc.Radius * Math.Cos(endRad),
                    arc.Center.Y + arc.Radius * Math.Sin(endRad));

                double sweepDeg = arc.EndAngle - arc.StartAngle;
                while (sweepDeg <= 0) sweepDeg += 360.0;
                while (sweepDeg > 360) sweepDeg -= 360.0;
                SweepRad = sweepDeg * Math.PI / 180.0;
            }
        }

        /// <summary>
        /// Nối các đường rời rạc (Line, Arc) thuộc các layer biên dạng (như IV_OUTER_PROFILE, IV_INTERIOR_PROFILES)
        /// thành các đường Polyline kín (Polyline2D với IsClosed = true) để phục vụ cắt laser CAM và nesting.
        /// </summary>
        /// <param name="doc">Tài liệu DXF cần xử lý.</param>
        /// <param name="targetLayers">Danh sách layer cần nối (mặc định nếu null là DefaultProfileLayers).</param>
        /// <param name="tolerance">Dung sai khoảng cách nối giữa 2 đầu mút (mặc định 0.01mm).</param>
        /// <returns>Số lượng polyline kín đã tạo thành công.</returns>
        public static int JoinBoundaryCurvesIntoClosedPolylines(
            DxfDocument doc,
            IEnumerable<string>? targetLayers = null,
            double tolerance = 0.01)
        {
            if (doc == null) return 0;

            var targetLayerSet = targetLayers != null
                ? new HashSet<string>(targetLayers, StringComparer.OrdinalIgnoreCase)
                : DefaultProfileLayers;

            // 1. Rã (explode) các Polyline2D chưa kín trên các layer mục tiêu để lấy lại Line & Arc
            var openPolys = doc.Entities.Polylines2D
                .Where(p => p.Layer != null && targetLayerSet.Contains(p.Layer.Name) && !p.IsClosed)
                .ToList();

            foreach (var p in openPolys)
            {
                try
                {
                    var exploded = p.Explode();
                    foreach (var ent in exploded)
                    {
                        if (ent is Line or Arc)
                        {
                            ent.Layer = p.Layer;
                            doc.Entities.Add(ent);
                        }
                    }
                    doc.Entities.Remove(p);
                }
                catch
                {
                    // Giữ nguyên nếu không explode được
                }
            }

            // 2. Tìm tất cả các Layer mục tiêu có trong tài liệu
            var layerNames = doc.Layers
                .Select(l => l.Name)
                .Where(name => targetLayerSet.Contains(name))
                .ToList();

            int closedPolyCount = 0;

            foreach (var lName in layerNames)
            {
                var targetLayer = doc.Layers[lName];
                if (targetLayer == null) continue;

                var lines = doc.Entities.Lines
                    .Where(l => l.Layer != null && string.Equals(l.Layer.Name, lName, StringComparison.OrdinalIgnoreCase))
                    .Where(l => Vector2.Distance(new Vector2(l.StartPoint.X, l.StartPoint.Y), new Vector2(l.EndPoint.X, l.EndPoint.Y)) > 1e-6)
                    .ToList();

                var arcs = doc.Entities.Arcs
                    .Where(a => a.Layer != null && string.Equals(a.Layer.Name, lName, StringComparison.OrdinalIgnoreCase))
                    .Where(a => a.Radius > 1e-6)
                    .ToList();

                if (lines.Count + arcs.Count == 0) continue;

                var unused = new List<Segment2D>(lines.Count + arcs.Count);
                foreach (var ln in lines) unused.Add(new Segment2D(ln));
                foreach (var arc in arcs) unused.Add(new Segment2D(arc));

                while (unused.Count > 0)
                {
                    var seed = unused[0];
                    unused.RemoveAt(0);

                    var chain = new List<(Segment2D seg, bool isForward)> { (seed, true) };
                    Vector2 pHead = seed.StartPoint;
                    Vector2 pTail = seed.EndPoint;

                    // 2a. Nối đuôi (tail) về phía trước
                    bool grew;
                    do
                    {
                        grew = false;
                        if (chain.Count >= 2 && Vector2.Distance(pTail, pHead) <= tolerance)
                        {
                            break;
                        }

                        int bestIdx = -1;
                        bool bestForward = true;
                        double bestDist = tolerance;

                        for (int i = 0; i < unused.Count; i++)
                        {
                            var cand = unused[i];
                            double dStart = Vector2.Distance(cand.StartPoint, pTail);
                            if (dStart <= bestDist)
                            {
                                bestDist = dStart;
                                bestIdx = i;
                                bestForward = true;
                            }
                            double dEnd = Vector2.Distance(cand.EndPoint, pTail);
                            if (dEnd <= bestDist)
                            {
                                bestDist = dEnd;
                                bestIdx = i;
                                bestForward = false;
                            }
                        }

                        if (bestIdx >= 0)
                        {
                            var matched = unused[bestIdx];
                            unused.RemoveAt(bestIdx);
                            chain.Add((matched, bestForward));
                            pTail = bestForward ? matched.EndPoint : matched.StartPoint;
                            grew = true;
                        }
                    } while (grew);

                    // 2b. Nối đầu (head) về phía sau nếu chưa khép kín
                    if (Vector2.Distance(pTail, pHead) > tolerance)
                    {
                        do
                        {
                            grew = false;
                            if (chain.Count >= 2 && Vector2.Distance(pTail, pHead) <= tolerance)
                            {
                                break;
                            }

                            int bestIdx = -1;
                            bool bestForward = true;
                            double bestDist = tolerance;

                            for (int i = 0; i < unused.Count; i++)
                            {
                                var cand = unused[i];
                                double dEnd = Vector2.Distance(cand.EndPoint, pHead);
                                if (dEnd <= bestDist)
                                {
                                    bestDist = dEnd;
                                    bestIdx = i;
                                    bestForward = true;
                                }
                                double dStart = Vector2.Distance(cand.StartPoint, pHead);
                                if (dStart <= bestDist)
                                {
                                    bestDist = dStart;
                                    bestIdx = i;
                                    bestForward = false;
                                }
                            }

                            if (bestIdx >= 0)
                            {
                                var matched = unused[bestIdx];
                                unused.RemoveAt(bestIdx);
                                chain.Insert(0, (matched, bestForward));
                                pHead = bestForward ? matched.StartPoint : matched.EndPoint;
                                grew = true;
                            }
                        } while (grew);
                    }

                    // 2c. Kiểm tra xem vòng lặp có khép kín hay không
                    bool isClosed = chain.Count >= 2 && Vector2.Distance(pTail, pHead) <= tolerance;

                    if (isClosed)
                    {
                        var vertices = new List<Polyline2DVertex>(chain.Count);
                        for (int i = 0; i < chain.Count; i++)
                        {
                            var (seg, isFwd) = chain[i];
                            Vector2 pos = isFwd ? seg.StartPoint : seg.EndPoint;
                            double bulge = 0.0;
                            if (seg.IsArc)
                            {
                                bulge = isFwd ? Math.Tan(seg.SweepRad / 4.0) : -Math.Tan(seg.SweepRad / 4.0);
                            }
                            vertices.Add(new Polyline2DVertex(pos, bulge));
                        }

                        var poly = new Polyline2D(vertices, isClosed: true)
                        {
                            Layer = targetLayer
                        };
                        doc.Entities.Add(poly);

                        foreach (var (seg, _) in chain)
                        {
                            doc.Entities.Remove(seg.OriginalEntity);
                        }
                        closedPolyCount++;
                    }
                    else if (chain.Count > 1)
                    {
                        // Chuỗi mở có từ 2 đoạn trở lên: tạo Polyline2D mở (IsClosed = false)
                        var vertices = new List<Polyline2DVertex>(chain.Count + 1);
                        for (int i = 0; i < chain.Count; i++)
                        {
                            var (seg, isFwd) = chain[i];
                            Vector2 pos = isFwd ? seg.StartPoint : seg.EndPoint;
                            double bulge = 0.0;
                            if (seg.IsArc)
                            {
                                bulge = isFwd ? Math.Tan(seg.SweepRad / 4.0) : -Math.Tan(seg.SweepRad / 4.0);
                            }
                            vertices.Add(new Polyline2DVertex(pos, bulge));
                        }
                        vertices.Add(new Polyline2DVertex(pTail, 0.0));

                        var poly = new Polyline2D(vertices, isClosed: false)
                        {
                            Layer = targetLayer
                        };
                        doc.Entities.Add(poly);

                        foreach (var (seg, _) in chain)
                        {
                            doc.Entities.Remove(seg.OriginalEntity);
                        }
                    }
                    // Nếu chain.Count == 1 và không kín: giữ nguyên entity gốc (Line hoặc Arc)
                }
            }

            return closedPolyCount;
        }

        private void PostProcessDxfDoc(DxfDocument doc)
        {
            // Nối các đường bao (Line, Arc) thành Polyline kín (Polyline2D)
            JoinBoundaryCurvesIntoClosedPolylines(doc);

            // Cắt đường bend luôn chạy cố định cho 2 layer này (ngoài phạm vi màn hình "Cấu hình
            // Layer" hiện tại) — chạy trước để entity mới sinh ra (2 đoạn nét đứt) vẫn được vòng lặp
            // xoá/màu bên dưới xử lý đúng nếu layer đó cũng nằm trong _layerSettings.
            ProcessBendLines(doc, "IV_BEND_DOWN");
            ProcessBendLines(doc, "IV_BEND");

            foreach (var cfg in _layerSettings)
            {
                if (cfg.DeleteEntities)
                {
                    DeleteLayerEntities(doc, cfg.Name);
                }
                else
                {
                    SetLayerColor(doc, cfg.Name, cfg.AciColor);
                }
            }
        }

        private void DeleteLayerEntities(DxfDocument doc, string layerName)
        {
            var toRemove = new List<EntityObject>();
            foreach (var e in doc.Entities.All)
            {
                if (e.Layer != null && e.Layer.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(e);
                }
            }
            foreach (var e in toRemove) doc.Entities.Remove(e);
        }

        private void ProcessBendLines(DxfDocument doc, string layerName)
        {
            var linesInLayer = new List<Line>();
            foreach (var ln in doc.Entities.Lines)
            {
                if (ln.Layer != null && ln.Layer.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                {
                    linesInLayer.Add(ln);
                }
            }

            foreach (var ln in linesInLayer)
            {
                double sx = ln.StartPoint.X, sy = ln.StartPoint.Y;
                double ex = ln.EndPoint.X, ey = ln.EndPoint.Y;
                double dx = ex - sx, dy = ey - sy;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist > 100)
                {
                    double ux = dx / dist, uy = dy / dist;
                    var seg1 = new Line(new Vector3(sx, sy, 0), new Vector3(sx + ux * 50, sy + uy * 50, 0));
                    seg1.Layer = ln.Layer;
                    var seg2 = new Line(new Vector3(ex - ux * 50, ey - uy * 50, 0), new Vector3(ex, ey, 0));
                    seg2.Layer = ln.Layer;
                    doc.Entities.Add(seg1);
                    doc.Entities.Add(seg2);
                    doc.Entities.Remove(ln);
                }
            }
        }

        private void SetLayerColor(DxfDocument doc, string layerName, short aciColor)
        {
            try
            {
                if (doc.Layers.Contains(layerName))
                {
                    var lyr = doc.Layers[layerName];
                    lyr.Color = new netDxf.AciColor(aciColor);
                }
            }
            catch { }
        }

        // ============================================================
        //  BOUNDING BOX
        //
        //  Sai số ở đây KHÔNG lộ ra thành exception: hình vẫn được TransformBy dời đi bình thường,
        //  nhưng offset cột/dòng lại tính từ w/h nên khoảng cách giữa các chi tiết trong combine.dxf
        //  không còn bằng gap đã đặt (1000). Đo DƯ -> các chi tiết cách nhau hàng chục mét; đo THIẾU
        //  -> chồng hình lên nhau.
        // ============================================================

        private void UpdateBounds(EntityObject e, BBoxInfo bx)
        {
            if (e is Line ln)
            {
                bx.Include(ln.StartPoint.X, ln.StartPoint.Y);
                bx.Include(ln.EndPoint.X, ln.EndPoint.Y);
            }
            else if (e is Circle c)
            {
                bx.Include(c.Center.X - c.Radius, c.Center.Y - c.Radius);
                bx.Include(c.Center.X + c.Radius, c.Center.Y + c.Radius);
            }
            else if (e is Arc a)
            {
                IncludeArcBounds(a, bx);
            }
            else if (e is Polyline2D p)
            {
                IncludePolyline2DBounds(p, bx);
            }
            else if (e is Polyline3D p3)
            {
                // Vertex thẳng: chính xác. Vertex có SmoothType (spline-fit): đường cong luôn nằm
                // trong bao lồi các vertex nên đây là cận TRÊN, đo dư chứ không thiếu.
                foreach (var v in p3.Vertexes) bx.Include(v.X, v.Y);
            }
            else if (e is Ellipse el)
            {
                IncludeEllipseBounds(el, bx);
            }
            else if (e is Spline sp)
            {
                IncludeSplineBounds(sp, bx);
            }
            else if (e is Text t)
            {
                IncludeTextBounds(t, bx);
            }
            else if (e is MText mt)
            {
                IncludeMTextBounds(mt, bx);
            }
            else if (e is netDxf.Entities.Point pt)
            {
                bx.Include(pt.Position.X, pt.Position.Y);
            }
            else if (e is Insert ins)
            {
                IncludeInsertBounds(ins, bx);
            }
            else
            {
                // Loại entity chưa biết (vd Hatch, Leader) — vẫn được TransformBy di chuyển đúng,
                // chỉ là không đóng góp vào bounding box. Ghi lại để thấy được thay vì im lặng.
                CountUnhandled(e);
            }
        }

        private void CountUnhandled(EntityObject e)
        {
            LastUnhandledEntityCount++;
            string name = e.GetType().Name;
            if (!_unhandledTypes.Contains(name)) _unhandledTypes.Add(name);
        }

        /// <summary>
        /// Hộp bao của Ellipse / cung Ellipse.
        ///
        /// ĐÂY LÀ LỖI ĐÃ LÀM combine.dxf ĐẶT SAI VỊ TRÍ (xác minh trên file combine.dxf thật ngày
        /// 2026-08-25). Bản cũ viết:
        ///     bx.Include(Center.X ± el.MajorAxis, Center.Y ± el.MajorAxis)
        /// sai 3 chỗ cùng lúc:
        ///   1. netDxf định nghĩa MajorAxis là ĐỘ DÀI TRỤC ĐẦY ĐỦ (2a), không phải bán trục — đã
        ///      verify: new Ellipse(center, 100, 50) ghi ra DXF group code 11 = 50 (tức bán trục).
        ///      Dùng thẳng MajorAxis như bán kính => hộp bao rộng gấp đôi.
        ///   2. Bỏ qua StartAngle/EndAngle: một CUNG ellipse ngắn bị tính bằng cả ellipse mẹ. Trong
        ///      combine.dxf thật, 4 chi tiết F-301-1.x có cung dài khoảng 100mm cắt ra từ ellipse có
        ///      trục 6488mm (cạnh dài hơi cong của tấm 4.9m) -> mỗi chi tiết bị tính thành khối
        ///      12977 x 12977mm, đẩy chi tiết kế tiếp đi xa khoảng 14000mm thay vì 1000mm và mở cột
        ///      mới cách 8768mm.
        ///   3. Bỏ qua Rotation (hộp bao lấy theo trục X/Y trong khi ellipse có thể nghiêng).
        ///
        /// PolygonalVertexes(n) của netDxf trả về n điểm trên đúng đoạn cung, ĐÃ áp Rotation, toạ độ
        /// TƯƠNG ĐỐI so với Center (verify: center (1000,2000), major 200, minor 100, rot 30, cung
        /// 0..90 -> điểm đầu (86.60, 50.00) = (100,0) xoay 30 độ).
        /// </summary>
        private void IncludeEllipseBounds(Ellipse el, BBoxInfo bx)
        {
            try
            {
                foreach (var v in el.PolygonalVertexes(CurvePrecision))
                {
                    bx.Include(el.Center.X + v.X, el.Center.Y + v.Y);
                }
                return;
            }
            catch
            {
                // Ellipse suy biến (trục 0, tham số lỗi) -> rơi xuống cận trên bên dưới.
            }

            // Dự phòng: hình vuông ngoại tiếp theo BÁN trục lớn (MajorAxis / 2). Dư chỗ nhưng không
            // bao giờ thiếu, và vẫn nhỏ hơn 4 lần diện tích so với công thức sai cũ.
            double semiMajor = el.MajorAxis * 0.5;
            bx.Include(el.Center.X - semiMajor, el.Center.Y - semiMajor);
            bx.Include(el.Center.X + semiMajor, el.Center.Y + semiMajor);
        }

        /// <summary>
        /// Hộp bao của Polyline2D (LWPOLYLINE). Bản cũ chỉ lấy Position của các vertex nên bỏ qua
        /// hoàn toàn phần phình ra của những đoạn CUNG (Bulge khác 0) — đo THIẾU, các chi tiết bị xếp
        /// gần nhau hơn 1000mm. Đây không phải trường hợp hiếm: WriteDataToFile chạy với
        /// MergeProfilesIntoPolyline=True nên chính BIÊN DẠNG NGOÀI của mỗi chi tiết là 1 polyline có
        /// bulge (trong combine.dxf thật: 36/94 polyline có bulge, bulge lớn nhất = 1.0 = nửa đường tròn).
        /// </summary>
        private void IncludePolyline2DBounds(Polyline2D p, BBoxInfo bx)
        {
            bool anyBulge = false;
            foreach (var v in p.Vertexes)
            {
                bx.Include(v.Position.X, v.Position.Y);
                if (Math.Abs(v.Bulge) > 1e-12) anyBulge = true;
            }
            if (!anyBulge) return;

            try
            {
                // Explode() tách polyline thành Line + Arc thật (verify: vertex bulge 1.0 từ (0,0)
                // tới (100,0) -> Arc tâm (50,0) bán kính 50, góc 180..0).
                foreach (var seg in p.Explode())
                {
                    if (seg is Arc arc)
                    {
                        IncludeArcBounds(arc, bx);
                    }
                    else if (seg is Line l)
                    {
                        bx.Include(l.StartPoint.X, l.StartPoint.Y);
                        bx.Include(l.EndPoint.X, l.EndPoint.Y);
                    }
                }
            }
            catch
            {
                // Không explode được -> giữ hộp bao theo vertex (thiếu phần cung, nhưng vẫn có dữ liệu).
            }
        }

        /// <summary>
        /// Hộp bao Spline: rời rạc hoá đường cong thật. Nếu không được thì lùi về bao lồi các control
        /// point — đường NURBS luôn nằm trong bao lồi của control point nên đây là cận TRÊN an toàn
        /// (đo dư chứ không bao giờ đo thiếu).
        /// </summary>
        private void IncludeSplineBounds(Spline sp, BBoxInfo bx)
        {
            try
            {
                foreach (var v in sp.PolygonalVertexes(CurvePrecision)) bx.Include(v.X, v.Y);
                return;
            }
            catch { }

            if (sp.ControlPoints != null)
            {
                foreach (var cp in sp.ControlPoints) bx.Include(cp.X, cp.Y);
            }
        }

        /// <summary>
        /// Hộp bao chữ (xấp xỉ). Bản cũ chỉ lấy điểm chèn Position nên toàn bộ bề rộng/chiều cao chữ
        /// nằm ngoài hộp bao — nhãn "DESC: PL6-S355J2WP" cao 30 dài khoảng 430mm ăn lấn vào khoảng hở
        /// giữa 2 chi tiết. Bề rộng ước lượng theo số ký tự (không đo font thật), có tính WidthFactor,
        /// Alignment và Rotation.
        /// </summary>
        private void IncludeTextBounds(Text t, BBoxInfo bx)
        {
            string s = t.Value ?? "";
            double h = t.Height;
            double wf = t.WidthFactor > 0 ? t.WidthFactor : 1.0;
            double w = s.Length * h * wf * TextCharWidthRatio;

            IncludeTextBox(bx, t.Position.X, t.Position.Y, w, h, t.Rotation,
                AlignFactorX(t.Alignment), AlignFactorY(t.Alignment));
        }

        private void IncludeMTextBounds(MText mt, BBoxInfo bx)
        {
            string s;
            try { s = mt.PlainText() ?? ""; }
            catch { s = mt.Value ?? ""; }

            double h = mt.Height;
            double w = mt.RectangleWidth > 0 ? mt.RectangleWidth : s.Length * h * TextCharWidthRatio;

            // MTextAttachmentPoint: 1..3 = hàng Top, 4..6 = Middle, 7..9 = Bottom;
            // (ap-1) % 3 == 0 Left / 1 Center / 2 Right (đã verify bằng Enum.GetValues).
            int ap = (int)mt.AttachmentPoint;
            if (ap < 1 || ap > 9) ap = 1;
            double fx = -0.5 * ((ap - 1) % 3);
            double fy = ap <= 3 ? -1.0 : (ap <= 6 ? -0.5 : 0.0);

            IncludeTextBox(bx, mt.Position.X, mt.Position.Y, w, h, mt.Rotation, fx, fy);
        }

        /// <summary>Hệ số dời gốc hộp chữ theo phương ngang: 0 = điểm chèn ở mép trái, -0.5 = giữa,
        /// -1 = mép phải.</summary>
        private static double AlignFactorX(TextAlignment al)
        {
            switch (al)
            {
                case TextAlignment.TopCenter:
                case TextAlignment.MiddleCenter:
                case TextAlignment.BottomCenter:
                case TextAlignment.BaselineCenter:
                case TextAlignment.Middle:
                    return -0.5;
                case TextAlignment.TopRight:
                case TextAlignment.MiddleRight:
                case TextAlignment.BottomRight:
                case TextAlignment.BaselineRight:
                    return -1.0;
                default:
                    return 0.0;
            }
        }

        /// <summary>Hệ số dời gốc hộp chữ theo phương dọc: 0 = điểm chèn ở đáy (Bottom/Baseline),
        /// -0.5 = giữa, -1 = đỉnh.</summary>
        private static double AlignFactorY(TextAlignment al)
        {
            switch (al)
            {
                case TextAlignment.TopLeft:
                case TextAlignment.TopCenter:
                case TextAlignment.TopRight:
                    return -1.0;
                case TextAlignment.MiddleLeft:
                case TextAlignment.MiddleCenter:
                case TextAlignment.MiddleRight:
                case TextAlignment.Middle:
                    return -0.5;
                default:
                    return 0.0;
            }
        }

        /// <summary>Đưa 4 góc của hộp chữ (đã xoay quanh điểm chèn) vào bounding box.</summary>
        private static void IncludeTextBox(BBoxInfo bx, double px, double py, double w, double h,
            double rotationDeg, double factorX, double factorY)
        {
            double x0 = factorX * w;
            double y0 = factorY * h;

            double rad = rotationDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            double[] cx = { x0, x0 + w, x0 + w, x0 };
            double[] cy = { y0, y0, y0 + h, y0 + h };
            for (int i = 0; i < 4; i++)
            {
                bx.Include(px + cx[i] * cos - cy[i] * sin, py + cx[i] * sin + cy[i] * cos);
            }
        }

        /// <summary>Hộp bao của Insert (block reference): explode ra entity thật rồi đo từng cái.
        /// Không explode được thì chỉ tính điểm chèn.</summary>
        private void IncludeInsertBounds(Insert ins, BBoxInfo bx)
        {
            try
            {
                foreach (var sub in ins.Explode()) UpdateBounds(sub, bx);
                return;
            }
            catch { }

            bx.Include(ins.Position.X, ins.Position.Y);
        }

        private void IncludeArcBounds(Arc a, BBoxInfo bx)
        {
            double cx = a.Center.X;
            double cy = a.Center.Y;
            double r = a.Radius;
            double startA = a.StartAngle;
            double endA = a.EndAngle;
            if (endA < startA) endA += 360;

            double sx = cx + r * Math.Cos(startA * Math.PI / 180);
            double sy = cy + r * Math.Sin(startA * Math.PI / 180);
            double ex = cx + r * Math.Cos(endA * Math.PI / 180);
            double ey = cy + r * Math.Sin(endA * Math.PI / 180);
            bx.Include(sx, sy);
            bx.Include(ex, ey);

            double[] checkAngles = { 0, 90, 180, 270 };
            foreach (var ang in checkAngles)
            {
                double a2 = ang;
                while (a2 < startA) a2 += 360;
                if (a2 <= endA)
                {
                    bx.Include(cx + r * Math.Cos(a2 * Math.PI / 180), cy + r * Math.Sin(a2 * Math.PI / 180));
                }
            }
        }

        private void TransformEntity(EntityObject e, double tx, double ty, double scale)
        {
            // TransformBy xử lý ĐÚNG mọi loại entity của netDxf (kể cả Ellipse/Spline/Polyline3D/
            // Insert/Hatch), thay cho chuỗi if-else chỉ biết 5 loại — entity ngoài 5 loại đó trước đây
            // bị bỏ nguyên toạ độ gốc, chồng lên các chi tiết khác trong combine.dxf mà không có lỗi
            // báo ra. Đã verify bằng reflection: TransformBy(Matrix3.Scale(scale), translation) tính
            // đúng scale*point + translation cho Line (điểm test: (1,2)->(12,24) với scale=2, tx=ty=10).
            e.TransformBy(Matrix3.Scale(scale), new Vector3(tx, ty, 0));
        }
    }
}
