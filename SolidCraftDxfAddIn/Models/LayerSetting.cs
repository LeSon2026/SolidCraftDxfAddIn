namespace ExportDxfAddIn.Models
{
    /// <summary>1 dòng cấu hình xử lý layer DXF sau export — thay cho Dictionary hard-code trước đây
    /// trong DxfPostProcessor (README mục 5). DeleteEntities=true nghĩa là xoá hẳn mọi entity ở layer
    /// đó (như IV_TANGENT/IV_ARC_CENTERS cũ); ngược lại AciColor được áp cho layer (AutoCAD ACI index,
    /// 0-255). Lưu/đọc qua LayerConfigService.</summary>
    public class LayerSetting : BindableBase
    {
        public string Name { get; set; } = "";

        private bool _deleteEntities;
        public bool DeleteEntities
        {
            get => _deleteEntities;
            set => SetProperty(ref _deleteEntities, value);
        }

        private short _aciColor;
        public short AciColor
        {
            get => _aciColor;
            set => SetProperty(ref _aciColor, value);
        }
    }
}
