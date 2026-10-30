using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ExportDxfAddIn.Models;

namespace ExportDxfAddIn.Services
{
    /// <summary>Singleton đọc/ghi cấu hình danh sách layer DXF cố định (xoá hẳn hoặc đổi màu ACI) dùng bởi
    /// DxfPostProcessor. Trước đây các giá trị này hard-code trong DxfPostProcessor.SetLayerColors/
    /// DeleteLayerEntities (xem README mục 5); giờ lưu vào %AppData%\ExportDxfAddIn\layerconfig.json,
    /// chỉnh được qua màn hình "Cấu hình Layer" (Views/LayerConfigView.xaml). Cùng pattern singleton +
    /// settings.json với LocalizationService.Tr.</summary>
    public sealed class LayerConfigService
    {
        public static LayerConfigService Instance { get; } = new LayerConfigService();

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ExportDxfAddIn", "layerconfig.json");

        private LayerConfigService() { }

        private class LayerSettingDto
        {
            public string Name { get; set; } = "";
            public bool DeleteEntities { get; set; }
            public short AciColor { get; set; }
        }

        /// <summary>Giá trị mặc định = đúng những gì DxfPostProcessor hard-code trước khi có màn hình
        /// cấu hình này, cộng thêm IV_MARK_SURFACE_BACK (thêm sau, màu giống IV_BEND_DOWN theo yêu cầu
        /// người dùng) — IV_MARK_SURFACE đổi màu mặc định thành giống IV_BEND (cũng theo yêu cầu người
        /// dùng, khác với colorMap gốc là màu 1). Thứ tự trong danh sách = thứ tự xử lý trong
        /// PostProcessDxfDoc(). 8 layer đầu (IV_TANGENT..IV_BEND) GIỮ NGUYÊN không đổi khi thêm layer
        /// mới bên dưới (đổi 2026-08-26, xem README mục 12) — 10 layer còn lại là toàn bộ layer khác
        /// Inventor có thể sinh ra khi xuất DXF từ Flat Pattern mà add-in trước đây chưa cho cấu hình;
        /// mặc định KHÔNG xoá + màu ACI 7 (by-layer trung tính, không đổi màu gốc Inventor đã xuất) vì
        /// chưa có yêu cầu cụ thể nào cho từng layer — người dùng tự chỉnh qua màn hình "Cấu hình
        /// Layer" theo nhu cầu xưởng thật.</summary>
        public static List<LayerSetting> GetDefaults() => new()
        {
            new LayerSetting { Name = "IV_TANGENT", DeleteEntities = true, AciColor = 4 },
            new LayerSetting { Name = "IV_ARC_CENTERS", DeleteEntities = true, AciColor = 7 },
            new LayerSetting { Name = "IV_MARK_SURFACE", DeleteEntities = false, AciColor = 6 },
            new LayerSetting { Name = "IV_MARK_SURFACE_BACK", DeleteEntities = false, AciColor = 5 },
            new LayerSetting { Name = "IV_INTERIOR_PROFILES", DeleteEntities = false, AciColor = 2 },
            new LayerSetting { Name = "IV_OUTER_PROFILE", DeleteEntities = false, AciColor = 2 },
            new LayerSetting { Name = "IV_BEND_DOWN", DeleteEntities = false, AciColor = 5 },
            new LayerSetting { Name = "IV_BEND", DeleteEntities = false, AciColor = 6 },
            new LayerSetting { Name = "IV_TOOL_CENTER", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_TOOL_CENTER_DOWN", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_FEATURE_PROFILES", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_FEATURE_PROFILES_DOWN", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_ALTREP_FRONT", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_ALTREP_BACK", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_UNCONSUMED_SKETCHES", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_UNCONSUMED_SKETCH_CONSTRUCTION", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_ROLL_TANGENT", DeleteEntities = false, AciColor = 7 },
            new LayerSetting { Name = "IV_ROLL", DeleteEntities = false, AciColor = 7 },
        };

        /// <summary>Đọc cấu hình đã lưu; layer nào có trong danh sách mặc định nhưng thiếu trong file
        /// (vd file cũ lưu trước khi thêm IV_MARK_SURFACE_BACK) thì lấy giá trị mặc định cho riêng
        /// layer đó, không mất cả danh sách. File hỏng/không đọc được -> trả về mặc định, không crash
        /// tính năng export (cùng nguyên tắc với LocalizationService.LoadSavedLanguage).</summary>
        public List<LayerSetting> Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var dtos = JsonSerializer.Deserialize<List<LayerSettingDto>>(json);
                    if (dtos != null && dtos.Count > 0)
                    {
                        var byName = dtos
                            .Where(d => !string.IsNullOrEmpty(d.Name))
                            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                        var result = new List<LayerSetting>();
                        foreach (var def in GetDefaults())
                        {
                            if (byName.TryGetValue(def.Name, out var dto))
                            {
                                result.Add(new LayerSetting { Name = def.Name, DeleteEntities = dto.DeleteEntities, AciColor = dto.AciColor });
                            }
                            else
                            {
                                result.Add(def);
                            }
                        }
                        return result;
                    }
                }
            }
            catch
            {
                // Không đọc được -> dùng mặc định.
            }
            return GetDefaults();
        }

        public void Save(IEnumerable<LayerSetting> settings)
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var dtos = settings.Select(s => new LayerSettingDto
            {
                Name = s.Name,
                DeleteEntities = s.DeleteEntities,
                AciColor = s.AciColor
            }).ToList();
            var json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
    }
}
