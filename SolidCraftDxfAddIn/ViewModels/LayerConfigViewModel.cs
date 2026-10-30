using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using ExportDxfAddIn.Models;
using ExportDxfAddIn.Services;

namespace ExportDxfAddIn.ViewModels
{
    /// <summary>1 dòng hiển thị trong màn hình "Cấu hình Layer" — bọc LayerSetting (model dữ liệu
    /// thuần, dùng chung với DxfPostProcessor) để thêm ô preview màu (SolidColorBrush quy đổi từ ACI
    /// index qua netDxf.AciColor) và khoá ô màu khi layer đang bị đánh dấu Xoá.</summary>
    public class LayerConfigRowVm : BindableBase
    {
        public LayerSetting Setting { get; }

        public LayerConfigRowVm(LayerSetting setting)
        {
            Setting = setting;
            Setting.PropertyChanged += Setting_PropertyChanged;
        }

        private void Setting_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LayerSetting.AciColor))
            {
                OnPropertyChanged(nameof(AciColor));
                OnPropertyChanged(nameof(ColorPreview));
            }
            else if (e.PropertyName == nameof(LayerSetting.DeleteEntities))
            {
                OnPropertyChanged(nameof(DeleteEntities));
                OnPropertyChanged(nameof(IsColorEditable));
            }
        }

        public string Name => Setting.Name;

        public bool DeleteEntities
        {
            get => Setting.DeleteEntities;
            set => Setting.DeleteEntities = value;
        }

        public short AciColor
        {
            get => Setting.AciColor;
            set => Setting.AciColor = value;
        }

        /// <summary>Ô màu chỉ có ý nghĩa khi layer KHÔNG bị xoá (mutual exclusive với DeleteEntities,
        /// xem DxfPostProcessor.PostProcessDxfDoc).</summary>
        public bool IsColorEditable => !DeleteEntities;

        public System.Windows.Media.Brush ColorPreview
        {
            get
            {
                var c = new netDxf.AciColor(AciColor);
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
            }
        }
    }

    /// <summary>ViewModel cho màn hình "Cấu hình Layer" (Views/LayerConfigView.xaml) — đưa danh sách
    /// layer/màu ACI vốn hard-code trong DxfPostProcessor.cs (README mục 5) ra chỉnh được, lưu qua
    /// LayerConfigService vào %AppData%\ExportDxfAddIn\layerconfig.json.</summary>
    public class LayerConfigViewModel : BindableBase
    {
        public ObservableCollection<LayerConfigRowVm> Rows { get; } = new ObservableCollection<LayerConfigRowVm>();

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public LayerConfigViewModel()
        {
            LoadFrom(LayerConfigService.Instance.Load());
            SaveCommand = new DelegateCommand(_ => ExecuteSave());
            ResetCommand = new DelegateCommand(_ => ExecuteReset());
        }

        private void LoadFrom(List<LayerSetting> settings)
        {
            Rows.Clear();
            foreach (var s in settings)
            {
                Rows.Add(new LayerConfigRowVm(s));
            }
        }

        private void ExecuteSave()
        {
            LayerConfigService.Instance.Save(Rows.Select(r => r.Setting));
            StatusMessage = Tr.Instance["LayerConfig_Saved"];
        }

        /// <summary>Nạp lại danh sách mặc định vào UI — CHƯA ghi file, người dùng vẫn phải bấm "Lưu"
        /// để áp dụng, tránh mất cấu hình đang chỉnh dở nếu bấm nhầm.</summary>
        private void ExecuteReset()
        {
            LoadFrom(LayerConfigService.GetDefaults());
            StatusMessage = Tr.Instance["LayerConfig_ResetNotice"];
        }
    }
}
