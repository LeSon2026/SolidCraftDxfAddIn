using ExportDxfAddIn.ViewModels;

namespace ExportDxfAddIn.Views
{
    public partial class ExportHistoryView : System.Windows.Controls.UserControl
    {
        public ExportHistoryView()
        {
            InitializeComponent();
            DataContext = new ExportHistoryViewModel();
        }

        public ExportHistoryViewModel ViewModel => (ExportHistoryViewModel)DataContext;
    }
}
