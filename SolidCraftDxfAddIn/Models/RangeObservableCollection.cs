using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ExportDxfAddIn.Models
{
    /// <summary>ObservableCollection cho phép nạp cả lô bằng MỘT sự kiện Reset duy nhất.
    /// Dùng cho ExportRows: nạp từng dòng khiến ICollectionView lọc lại + DataGrid đo layout
    /// mỗi lần Add (500 dòng = 500 lượt refresh).</summary>
    public class RangeObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (var i in items) Items.Add(i);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
