using ExportDxfAddIn.ViewModels;

namespace ExportDxfAddIn.Views
{
    public partial class LayerConfigView : System.Windows.Controls.UserControl
    {
        public LayerConfigView()
        {
            InitializeComponent();
            DataContext = new LayerConfigViewModel();
        }

        public LayerConfigViewModel ViewModel => (LayerConfigViewModel)DataContext;
    }
}
