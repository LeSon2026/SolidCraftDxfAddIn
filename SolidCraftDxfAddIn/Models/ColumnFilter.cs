using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using ExportDxfAddIn.Services;

namespace ExportDxfAddIn.Models
{
    public class FilterItem : BindableBase
    {
        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value))
                {
                    CheckedChanged?.Invoke();
                }
            }
        }

        public string Value { get; set; } = "";
        public string DisplayValue { get; set; } = "";

        internal bool CommittedIsChecked { get; set; } = true;

        internal Action? CheckedChanged { get; set; }
    }

    public class ColumnFilter : BindableBase
    {
        public ObservableCollection<FilterItem> Items { get; } = new();
        public ICollectionView FilteredItems { get; private set; }

        public Action<ColumnFilter>? OnOpening { get; set; }

        // Mảng lưu trữ những giá trị BỊ BỎ CHỌN (unchecked) thay vì lưu giá trị được chọn.
        // Điều này giúp dễ dàng xử lý khi dữ liệu thay đổi hoặc filter bị giới hạn bởi các cột khác.
        private HashSet<string> _unselectedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _isOpen;
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (SetProperty(ref _isOpen, value))
                {
                    if (value)
                    {
                        OnOpening?.Invoke(this);

                        // When opening, reset the search, sync to committed state, and reset add to selection
                        SearchText = "";
                        AddCurrentSelection = false;
                        BeginEdit();
                    }
                }
            }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilteredItems.Refresh();
                    
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        // Khi có search text, tự động check tất cả các kết quả tìm kiếm giống Excel
                        _updatingSelectAll = true;
                        foreach (var item in FilteredItems.Cast<FilterItem>())
                        {
                            item.IsChecked = true;
                        }
                        _selectAll = true;
                        OnPropertyChanged(nameof(SelectAll));
                        _updatingSelectAll = false;
                    }
                    else
                    {
                        // Khôi phục lại trạng thái từ bộ nhớ SelectedItems khi xóa search
                        _updatingSelectAll = true;
                        foreach (var item in Items)
                        {
                            item.IsChecked = _selectedItems.Contains(item.Value);
                        }
                        _updatingSelectAll = false;
                        UpdateSelectAllFromItems();
                    }
                    
                    OnPropertyChanged(nameof(SelectAllText));
                    OnPropertyChanged(nameof(ShowAddCurrentSelection));
                }
            }
        }

        private bool _selectAll = true;
        private bool _updatingSelectAll;
        public bool SelectAll
        {
            get => _selectAll;
            set
            {
                if (SetProperty(ref _selectAll, value))
                {
                    if (!_updatingSelectAll)
                    {
                        _updatingSelectAll = true;
                        foreach (var item in FilteredItems.Cast<FilterItem>())
                        {
                            item.IsChecked = value;
                        }
                        
                        if (string.IsNullOrWhiteSpace(SearchText))
                        {
                            UpdateSelectedItemsFromList();
                        }
                        
                        _updatingSelectAll = false;
                    }
                }
            }
        }

        public string SelectAllText => string.IsNullOrWhiteSpace(SearchText) ? Tr.Instance["SelectAll_Text"] : Tr.Instance["SelectAll_SearchResultsText"];

        private bool _addCurrentSelection;
        public bool AddCurrentSelection
        {
            get => _addCurrentSelection;
            set => SetProperty(ref _addCurrentSelection, value);
        }

        private HashSet<string> _selectedItems = new HashSet<string>();

        // Chỉ hiển thị "Add current selection to filter" khi có item được chọn và đang có nội dung search mới
        public bool ShowAddCurrentSelection => _selectedItems.Count > 0 && !string.IsNullOrWhiteSpace(SearchText);

        public bool IsActive => _unselectedValues.Count > 0;

        public Action? OnFilterChanged { get; set; }

        public ICommand ApplyCommand { get; }
        public ICommand CancelCommand { get; }

        public ColumnFilter()
        {
            FilteredItems = CollectionViewSource.GetDefaultView(Items);
            FilteredItems.Filter = FilterItemPredicate;

            ApplyCommand = new RelayCommand(Apply);
            CancelCommand = new RelayCommand(Cancel);
        }

        private bool FilterItemPredicate(object obj)
        {
            if (obj is FilterItem item)
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                return item.DisplayValue.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        private void ItemCheckedChanged()
        {
            if (_updatingSelectAll) return;
            UpdateSelectAllFromItems();
            
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                UpdateSelectedItemsFromList();
            }
        }

        private void UpdateSelectedItemsFromList()
        {
            _selectedItems.Clear();
            foreach (var item in Items.Where(i => i.IsChecked))
            {
                _selectedItems.Add(item.Value);
            }
        }

        private void UpdateSelectAllFromItems()
        {
            _updatingSelectAll = true;
            var visibleItems = FilteredItems.Cast<FilterItem>().ToList();
            if (visibleItems.Count == 0)
            {
                SelectAll = false;
            }
            else
            {
                SelectAll = visibleItems.All(i => i.IsChecked);
            }
            _updatingSelectAll = false;
        }

        public void Populate(IEnumerable<string?> values)
        {
            _unselectedValues.Clear();
            Items.Clear();
            var distinctValues = values
                .Select(v => v ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var v in distinctValues)
            {
                Items.Add(new FilterItem
                {
                    Value = v,
                    DisplayValue = string.IsNullOrWhiteSpace(v) ? Tr.Instance["Filter_Empty"] : v,
                    IsChecked = true,
                    CommittedIsChecked = true,
                    CheckedChanged = ItemCheckedChanged
                });
            }
            
            UpdateSelectAllFromItems();
            OnPropertyChanged(nameof(IsActive));
        }

        public void RefreshValues(IEnumerable<string?> currentValues)
        {
            var distinctValues = currentValues
                .Select(v => v ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Items.Clear();
            foreach (var v in distinctValues)
            {
                bool isChecked = !_unselectedValues.Contains(v);
                Items.Add(new FilterItem
                {
                    Value = v,
                    DisplayValue = string.IsNullOrWhiteSpace(v) ? Tr.Instance["Filter_Empty"] : v,
                    IsChecked = isChecked,
                    CommittedIsChecked = isChecked,
                    CheckedChanged = ItemCheckedChanged
                });
            }
            
            UpdateSelectAllFromItems();
        }

        private void BeginEdit()
        {
            _updatingSelectAll = true;
            _selectedItems.Clear();
            foreach (var item in Items)
            {
                item.IsChecked = item.CommittedIsChecked;
                if (item.IsChecked)
                {
                    _selectedItems.Add(item.Value);
                }
            }
            _updatingSelectAll = false;
            UpdateSelectAllFromItems();
        }

        private void Apply()
        {
            bool hasSearch = !string.IsNullOrWhiteSpace(SearchText);

            foreach (var item in Items)
            {
                if (hasSearch)
                {
                    bool matches = item.DisplayValue.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matches)
                    {
                        // Item không nằm trong kết quả tìm kiếm (bị ẩn)
                        if (AddCurrentSelection)
                        {
                            // Nếu check "Add current selection to filter", giữ nguyên trạng thái từ _selectedItems
                            item.IsChecked = _selectedItems.Contains(item.Value);
                        }
                        else
                        {
                            // Nếu không, bỏ chọn hoàn toàn (chỉ lấy kết quả tìm kiếm)
                            item.IsChecked = false;
                        }
                    }
                }
                
                item.CommittedIsChecked = item.IsChecked;
                
                if (item.IsChecked)
                {
                    _unselectedValues.Remove(item.Value);
                }
                else
                {
                    _unselectedValues.Add(item.Value);
                }
            }
            
            IsOpen = false;
            OnPropertyChanged(nameof(IsActive));
            OnFilterChanged?.Invoke();
        }

        private void Cancel()
        {
            IsOpen = false;
        }

        public bool IsValueAllowed(string? value)
        {
            if (!IsActive) return true;
            string val = value ?? "";
            return !_unselectedValues.Contains(val);
        }

        public void Reset()
        {
            _unselectedValues.Clear();
            foreach (var item in Items)
            {
                item.IsChecked = true;
                item.CommittedIsChecked = true;
            }
            UpdateSelectAllFromItems();
            OnPropertyChanged(nameof(IsActive));
        }

        /// <summary>Gọi khi ngôn ngữ đổi: cập nhật "(Select All)" + nhãn "(trống)" của các item hiện có,
        /// giữ nguyên lựa chọn (unselected values không đổi vì RefreshValues chỉ đổi DisplayValue).</summary>
        public void NotifyLanguageChanged()
        {
            OnPropertyChanged(nameof(SelectAllText));
            var currentValues = Items.Select(i => i.Value).ToList();
            if (currentValues.Count > 0)
            {
                RefreshValues(currentValues);
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) { _execute = execute; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
