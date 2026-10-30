using System;
using System.Collections.Generic;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using ExportDxfAddIn.Models;

namespace ExportDxfAddIn.Services
{
    public class SolidCraftFlipService
    {
        private static SolidCraftFlipService? _instance;
        public static SolidCraftFlipService Instance => _instance ??= new SolidCraftFlipService();

        /// <summary>
        /// Ánh xạ chuẩn hoá tên Layer từ SolidCraft sang hệ Layer của Add-in
        /// để DxfPostProcessor xử lý cắt đường bend 50mm, đổi màu ACI và xoá layer thừa.
        /// </summary>
        public void NormalizeSolidCraftLayers(DxfDocument doc)
        {
            if (doc == null) return;

            // Cache các layer Add-in đã tạo trong document
            var layerCache = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in doc.Layers)
            {
                layerCache[l.Name] = l;
            }

            Layer GetOrCreateLayer(string layerName)
            {
                if (layerCache.TryGetValue(layerName, out var existing))
                {
                    return existing;
                }
                var newLyr = new Layer(layerName);
                newLyr = doc.Layers.Add(newLyr);
                layerCache[layerName] = newLyr;
                return newLyr;
            }

            foreach (var ent in doc.Entities.All)
            {
                if (ent?.Layer == null) continue;

                var origName = ent.Layer.Name.Trim().ToUpperInvariant();
                string? targetName = null;

                if (origName == "OUTLINE")
                {
                    targetName = "IV_OUTER_PROFILE";
                }
                else if (origName == "CUT" || origName == "TEXT" || origName == "PERFORATION")
                {
                    targetName = "IV_INTERIOR_PROFILES";
                }
                else if (origName == "BEND_UP")
                {
                    targetName = "IV_BEND";
                }
                else if (origName == "BEND_DOWN")
                {
                    targetName = "IV_BEND_DOWN";
                }
                else if (origName == "BEND_NOTES")
                {
                    targetName = "IV_TANGENT";
                }
                else if (origName == "ETCH" || origName.StartsWith("POCKET_TOP") || origName.StartsWith("POCKET_INTERNAL"))
                {
                    targetName = "IV_MARK_SURFACE";
                }
                else if (origName.StartsWith("POCKET_BOTTOM"))
                {
                    targetName = "IV_MARK_SURFACE_BACK";
                }

                if (targetName != null && !string.Equals(ent.Layer.Name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    ent.Layer = GetOrCreateLayer(targetName);
                }
            }
        }

        /// <summary>
        /// Tự động lật mặt phẳng bản trải phẳng 2D nếu cần (đảo trục X' = -X và hoán đổi layer chấn/khắc).
        /// </summary>
        /// <returns>Chuỗi lý do lật (nếu có lật) hoặc chuỗi rỗng (nếu giữ nguyên).</returns>
        public string FlipIfNeeded(DxfDocument doc, SolidCraftSheetItem item)
        {
            if (doc == null || item == null) return string.Empty;

            bool shouldFlip = false;
            string reason = string.Empty;

            // Trường hợp 1: Có rãnh khắc/pocket ở đáy mà không có ở mặt trên, và không có bend
            if (item.HasBottomPocket && !item.HasTopPocketOrEtch && item.BendCount == 0)
            {
                shouldFlip = true;
                reason = "Bottom Pocket/Etch";
            }
            // Trường hợp 2: Có đường chấn, số đường chấn Down nhiều hơn Up (hoặc bằng nhau nhưng tổng chiều dài Down lớn hơn)
            else if (item.BendDownCount > item.BendUpCount ||
                    (item.BendDownCount > 0 && item.BendDownCount == item.BendUpCount && item.BendDownTotalLength > item.BendUpTotalLength))
            {
                shouldFlip = true;
                reason = "BendDown > BendUp";
            }

            if (!shouldFlip)
            {
                return string.Empty;
            }

            // 1. Phép đối xứng gương qua trục X (X' = -X)
            var scaleMatrix = Matrix3.Scale(-1.0, 1.0, 1.0);
            foreach (var ent in doc.Entities.All)
            {
                if (ent == null) continue;

                ent.TransformBy(scaleMatrix, Vector3.Zero);

                // Polyline2D trong netDxf không tự đảo Bulge khi transform ma trận phản chiếu
                if (ent is Polyline2D poly2d)
                {
                    foreach (var v in poly2d.Vertexes)
                    {
                        if (v.Bulge != 0.0)
                        {
                            v.Bulge = -v.Bulge;
                        }
                    }
                }
            }

            // 2. Hoán đổi Layer: IV_BEND <-> IV_BEND_DOWN và IV_MARK_SURFACE <-> IV_MARK_SURFACE_BACK
            Layer GetOrCreateLayer(string name)
            {
                if (doc.Layers.Contains(name)) return doc.Layers[name];
                var lyr = new Layer(name);
                return doc.Layers.Add(lyr);
            }

            var lyrBendUp = GetOrCreateLayer("IV_BEND");
            var lyrBendDown = GetOrCreateLayer("IV_BEND_DOWN");
            var lyrMarkTop = GetOrCreateLayer("IV_MARK_SURFACE");
            var lyrMarkBottom = GetOrCreateLayer("IV_MARK_SURFACE_BACK");

            foreach (var ent in doc.Entities.All)
            {
                if (ent?.Layer == null) continue;

                var lName = ent.Layer.Name;
                if (string.Equals(lName, "IV_BEND", StringComparison.OrdinalIgnoreCase))
                {
                    ent.Layer = lyrBendDown;
                }
                else if (string.Equals(lName, "IV_BEND_DOWN", StringComparison.OrdinalIgnoreCase))
                {
                    ent.Layer = lyrBendUp;
                }
                else if (string.Equals(lName, "IV_MARK_SURFACE", StringComparison.OrdinalIgnoreCase))
                {
                    ent.Layer = lyrMarkBottom;
                }
                else if (string.Equals(lName, "IV_MARK_SURFACE_BACK", StringComparison.OrdinalIgnoreCase))
                {
                    ent.Layer = lyrMarkTop;
                }
            }

            return reason;
        }
    }
}
