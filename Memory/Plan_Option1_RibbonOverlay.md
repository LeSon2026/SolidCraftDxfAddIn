# KẾ HOẠCH TRIỂN KHAI (PHƯƠNG ÁN 1): TÍCH HỢP NÚT BẤM LÊN THANH RIBBON SOLIDCRAFT BẰNG OVERLAY & HOTKEY (KHÔNG CẦN BUILD LẠI SOLIDCRAFT)

> **Dành cho AI thực thi (Gemini 3.8 Flash):**  
> Tài liệu này hướng dẫn chi tiết từng bước từ A → Z để nâng cấp dự án **`SolidCraftDxfAddIn`** tại `d:\05_MyCode\SolidCraftDxfAddIn` nhằm gắn nút bấm **"Batch Export DXF"** nổi trực tiếp trên thanh Ribbon của cửa sổ phần mềm **SolidCraft** (`solidcraft.exe`) kèm phím tắt nhanh (`Ctrl + Shift + D`) và biểu tượng khay hệ thống (System Tray).  
> **Quy tắc bắt buộc:** Đọc `Memory/memory.md` trước khi làm và ghi nhật ký vào `Memory/Tracking.md` sau khi hoàn thành.

---

## 1. MỤC TIÊU & NGUYÊN LÝ HOẠT ĐỘNG

### 1.1. Tại sao dùng cơ chế Win32/WPF Ribbon Overlay?
- Giao diện của `solidcraft.exe` (`v0.24.0`) được vẽ bằng thư viện **Rust `egui`** trên một mặt phẳng GPU duy nhất và danh sách lệnh trên tab `SHEET METAL` đang được định nghĩa tĩnh trong mã máy.
- Thay vì phải sửa và biên dịch lại mã nguồn Rust của SolidCraft mỗi khi phần mềm cập nhật, chúng ta sử dụng **Cửa sổ nút bấm không viền (Borderless Transparent WPF Overlay)** kết hợp **Win32 Window Tracking (`User32.dll`)**:
  1. Theo dõi tiến trình `solidcraft` (`solidcraft.exe`).
  2. Khi cửa sổ chính của SolidCraft đang hiển thị ở tiền cảnh (Foreground) và không bị thu nhỏ (Minimized), tự động tính toán toạ độ khung cửa sổ (`GetWindowRect`) và neo nút bấm **"Export DXF (Batch)"** có màu sắc chuẩn Dark Theme của SolidCraft (`#1E222A`, viền `#3A4150`, chữ trắng/xanh `#38BDF8`) nằm khít vào vùng trống bên phải thanh Ribbon của SolidCraft.
  3. Khi người dùng di chuyển, phóng to/thu nhỏ cửa sổ SolidCraft hoặc chuyển sang phần mềm khác (`Alt+Tab`), nút bấm tự động di chuyển theo hoặc tự ẩn đi mượt mà.
  4. Hỗ trợ phím tắt `Ctrl + Shift + D` (chỉ kích hoạt khi cửa sổ SolidCraft đang active) để mở ngay cửa sổ xuất DXF.
  5. Khi bấm vào nút trên Ribbon (hoặc nhấn `Ctrl + Shift + D`), cửa sổ `MainWindow` của `SolidCraftDxfAddIn` sẽ hiện lên và gọi thẳng `MainViewModel.ConnectLiveAsync()` để nạp toàn bộ danh sách Sheet Metal từ bản vẽ đang mở qua cổng TCP `27878`!

---

## 2. CẤU TRÚC FILE CẦN THÊM / CẬP NHẬT

```text
d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\
├── Helpers\
│   └── Win32WindowHelper.cs              [MỚI] P/Invoke User32.dll dò cửa sổ SolidCraft & đăng ký Hotkey
├── Services\
│   └── SolidCraftWindowTrackerService.cs [MỚI] Dịch vụ theo dõi vị trí cửa sổ SolidCraft theo thời gian thực
├── Views\
│   ├── RibbonOverlayWindow.xaml          [MỚI] Nút bấm nổi chuẩn Dark Theme trên thanh Ribbon SolidCraft
│   └── RibbonOverlayWindow.xaml.cs       [MỚI] Logic xử lý click, kéo tinh chỉnh vị trí và gọi MainWindow
└── App.xaml.cs                           [CẬP NHẬT] Hỗ trợ chế độ chạy kèm Overlay (--overlay / Tray Icon) & Single Instance
```

---

## 3. HƯỚNG DẪN THỰC HIỆN TỪNG BƯỚC CHO GEMINI 3.8 FLASH

### BƯỚC 1: Tạo `Helpers/Win32WindowHelper.cs`

Tạo file `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Helpers\Win32WindowHelper.cs` chứa các hàm Win32 API cần thiết để lấy toạ độ cửa sổ `solidcraft.exe`, kiểm tra cửa sổ đang active và thiết lập thuộc tính cửa sổ Overlay không chiếm focus (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`):

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExportDxfAddIn.Helpers
{
    public static class Win32WindowHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint VK_D = 0x44;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>
        /// Tìm MainWindowHandle của tiến trình solidcraft.exe đang chạy.
        /// </summary>
        public static IntPtr FindSolidCraftMainWindow()
        {
            var procs = Process.GetProcessesByName("solidcraft");
            foreach (var p in procs)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle))
                    {
                        return p.MainWindowHandle;
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
            return IntPtr.Zero;
        }
    }
}
```

---

### BƯỚC 2: Tạo cửa sổ nút bấm nổi `Views/RibbonOverlayWindow.xaml` & `.cs`

1. Tạo file `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Views\RibbonOverlayWindow.xaml`:
   - Thiết kế nút bấm kích thước nhỏ gọn (`Height="52"`, `Width="110"`), bo góc `CornerRadius="6"`, tông màu tiệp với thanh Ribbon `SHEET METAL` của SolidCraft (`Background="#232730"`, `BorderBrush="#3B4252"`, khi hover sáng màu xanh `#0284C7`).
   - Có icon tấm kim loại / DXF ở trên và dòng chữ **"EXPORT DXF"** bên dưới giống hệt bố cục nút `Create Flat Pattern` của SolidCraft.
   - Cho phép giữ chuột phải kéo nhẹ (`MouseRightButtonDown`) nếu người dùng muốn dịch chuyển vị trí ngang (Offset X) trên thanh Ribbon và tự lưu lại vào `SolidCraftSettings`.

```xml
<Window x:Class="ExportDxfAddIn.Views.RibbonOverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="SolidCraft DXF Ribbon Button"
        Width="108" Height="54"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize"
        Focusable="False">
    <Border x:Name="BtnBorder"
            Background="#232730"
            BorderBrush="#3E4656"
            BorderThickness="1"
            CornerRadius="6"
            Margin="2"
            Cursor="Hand"
            MouseLeftButtonUp="BtnBorder_MouseLeftButtonUp"
            MouseEnter="BtnBorder_MouseEnter"
            MouseLeave="BtnBorder_MouseLeave"
            ToolTip="Xuất hàng loạt bản trải phẳng Sheet Metal DXF (Ctrl+Shift+D)&#x0a;Giữ chuột phải để di chuyển vị trí nút trên thanh Ribbon">
        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
            <TextBlock Text="📐 DXF"
                       FontSize="13"
                       FontWeight="Bold"
                       Foreground="#38BDF8"
                       HorizontalAlignment="Center"/>
            <TextBlock Text="BATCH EXPORT"
                       FontSize="9.5"
                       FontWeight="SemiBold"
                       Foreground="#E2E8F0"
                       Margin="0,2,0,0"
                       HorizontalAlignment="Center"/>
        </StackPanel>
    </Border>
</Window>
```

2. Tạo file `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Views\RibbonOverlayWindow.xaml.cs`:
   - Trong `OnSourceInitialized`, bật cờ `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` để nút bấm không hiện trên Alt+Tab và không làm mất focus của cửa sổ SolidCraft khi di chuột qua.
   - Bấm chuột trái $\rightarrow$ phát sự kiện `ExportDxfRequested`.
   - Kéo bằng chuột phải $\rightarrow$ cập nhật `RibbonOffsetX` và `RibbonOffsetY` để người dùng có thể đặt nút ngay sát bên phải nút `Create Flat Pattern` trong tab `SHEET METAL`.

```csharp
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ExportDxfAddIn.Helpers;

namespace ExportDxfAddIn.Views
{
    public partial class RibbonOverlayWindow : Window
    {
        public event Action? ExportDxfRequested;
        public event Action<double, double>? OffsetChanged;

        private bool _isDragging;
        private System.Windows.Point _dragStartScreen;

        public RibbonOverlayWindow()
        {
            InitializeComponent();
            this.MouseRightButtonDown += OnMouseRightButtonDown;
            this.MouseRightButtonUp += OnMouseRightButtonUp;
            this.MouseMove += OnMouseMove;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = Win32WindowHelper.GetWindowLong(hwnd, Win32WindowHelper.GWL_EXSTYLE);
            Win32WindowHelper.SetWindowLong(
                hwnd,
                Win32WindowHelper.GWL_EXSTYLE,
                exStyle | Win32WindowHelper.WS_EX_TOOLWINDOW | Win32WindowHelper.WS_EX_NOACTIVATE);
        }

        private void BtnBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            BtnBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x35, 0x45));
            BtnBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8));
        }

        private void BtnBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            BtnBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x23, 0x27, 0x30));
            BtnBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x46, 0x56));
        }

        private void BtnBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                ExportDxfRequested?.Invoke();
            }
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStartScreen = PointToScreen(e.GetPosition(this));
            CaptureMouse();
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;
            var currentScreen = PointToScreen(e.GetPosition(this));
            double dx = currentScreen.X - _dragStartScreen.X;
            double dy = currentScreen.Y - _dragStartScreen.Y;
            _dragStartScreen = currentScreen;
            OffsetChanged?.Invoke(dx, dy);
        }

        private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
        }
    }
}
```

---

### BƯỚC 3: Tạo `Services/SolidCraftWindowTrackerService.cs`

1. Mở rộng `SolidCraftSettings` trong `Services/SolidCraftCliService.cs` thêm 2 thuộc tính lưu vị trí neo nút trên thanh Ribbon (tính từ góc trên bên trái của cửa sổ SolidCraft):
   ```csharp
   public double RibbonButtonOffsetX { get; set; } = 560; // Ngay bên phải cụm nút Sheet Metal (Flange..Create Flat Pattern)
   public double RibbonButtonOffsetY { get; set; } = 42;  // Độ cao dòng Ribbon của SolidCraft
   ```
2. Tạo file `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Services\SolidCraftWindowTrackerService.cs`:
   - Dùng `DispatcherTimer` chu kỳ `100 ms` để kiểm tra cửa sổ `solidcraft.exe`:
     - Nếu `FindSolidCraftMainWindow()` tìm thấy cửa sổ SolidCraft, cửa sổ đó không bị thu nhỏ (`!IsIconic(hwnd)`) và đang là cửa sổ Foreground (hoặc chính cửa sổ Overlay đang được bấm):
       - Lấy `GetWindowRect(hwnd, out var rect)` và quy đổi theo DPI của màn hình.
       - Đặt vị trí `RibbonOverlayWindow.Left = rectLeftDip + OffsetX`, `RibbonOverlayWindow.Top = rectTopDip + OffsetY`.
       - Hiển thị `RibbonOverlayWindow` nếu đang ẩn.
       - Nếu người dùng nhấn `Ctrl + Shift + D` khi cửa sổ SolidCraft đang active $\rightarrow$ kích hoạt mở `MainWindow` và nạp dữ liệu Live.
     - Ngược lại (SolidCraft bị thu nhỏ, đóng hoặc bị cửa sổ phần mềm khác đè lên) $\rightarrow$ ẩn `RibbonOverlayWindow` ngay lập tức để không bao giờ che cửa sổ ứng dụng khác.

---

### BƯỚC 4: Cập nhật `App.xaml.cs` hỗ trợ System Tray & Mở nhanh từ Ribbon Overlay

Trong `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\App.xaml.cs`:
1. Khởi tạo `RibbonOverlayWindow` và `SolidCraftWindowTrackerService` ngay khi ứng dụng chạy.
2. Thêm biểu tượng dưới khay hệ thống (`System.Windows.Forms.NotifyIcon`) với menu chuột phải:
   - **"Mở bảng xuất DXF SolidCraft"**
   - **"Đặt lại vị trí nút trên Ribbon"**
   - **"Thoát hoàn toàn"**
3. Khi người dùng đóng `MainWindow`, ứng dụng thu gọn về thanh Ribbon của SolidCraft (hoặc thoát hẳn nếu chọn từ Tray). Khi người dùng bấm nút **`📐 DXF BATCH EXPORT`** trên thanh Ribbon của SolidCraft:
   - Nếu `MainWindow` đang ẩn $\rightarrow$ hiện `MainWindow`, đưa lên tiền cảnh (`Activate()`) và tự động gọi `await vm.ConnectLiveAsync()` để cập nhật danh sách Sheet Metal mới nhất từ bản vẽ SolidCraft đang mở!

---

### BƯỚC 5: Biên dịch & Kiểm thử nghiệm thu

1. Chạy lệnh biên dịch:
   ```powershell
   dotnet build d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn.sln -c Debug
   ```
   Đảm bảo **`0 Warning(s), 0 Error(s)`**.
2. Kiểm thử thực tế:
   - Mở phần mềm **SolidCraft**, chuyển sang tab **`SHEET METAL`**.
   - Chạy `SolidCraftDxfAddIn.exe`.
   - Kiểm tra nút **`📐 DXF BATCH EXPORT`** xuất hiện ngay trên thanh Ribbon của SolidCraft, di chuyển mượt theo cửa sổ SolidCraft, tự ẩn khi `Alt+Tab` sang phần mềm khác và tự động nạp bản vẽ Live khi click vào nút hoặc bấm `Ctrl+Shift+D`.
3. Cập nhật kết quả vào `Memory/Tracking.md`.

