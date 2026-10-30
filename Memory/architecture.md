# Kiến trúc hệ thống mã nguồn (Architecture)

## 1. Cấu trúc thư mục dự án

```text
d:\05_MyCode\SolidCraftDxfAddIn\
├── SolidCraftDxfAddIn.sln            # Solution chính của dự án
├── Memory/                           # Tài liệu ghi nhớ và theo dõi tiến độ
│   ├── memory.md                     # File sơ đồ gốc (index)
│   ├── Project.md                    # Bản đặc tả kỹ thuật và kế hoạch
│   ├── Tracking.md                   # Nhật ký tiến độ từng phiên
│   ├── architecture.md               # Kiến trúc hệ thống và giải thích mã nguồn
│   ├── issues.md                     # Ghi chép các vấn đề kỹ thuật và giải pháp
│   ├── Plan_Option1_RibbonOverlay.md # Kế hoạch gắn Floating Ribbon Button vào SolidCraft
│   ├── Plan_Option3_NativeRustBuild.md # Kế hoạch build lại SolidCraft từ Rust source
│   └── Plan_Fix_LinkedSheetMetal.md  # Kế hoạch fix body linked sheet metal & tolerant batch
└── SolidCraftDxfAddIn/               # Project WinExe WPF (.NET 8 Windows)
    ├── SolidCraftDxfAddIn.csproj     # Cấu hình project .NET 8 WPF
    ├── App.xaml / App.xaml.cs        # Điểm khởi chạy ứng dụng (hỗ trợ cờ --test)
    ├── TestRunner.cs                 # Bộ kiểm thử tự động toàn diện (chạy qua cờ --test)
    ├── Models/                       # Các thực thể dữ liệu (DTO, View models data)
    │   ├── ExportRow.cs              # Hàng dữ liệu trên bảng DataGrid xuất DXF (hỗ trợ link, tooltip, stale)
    │   ├── ExportedItem.cs           # Thông tin file DXF đã xuất
    │   ├── ExportHistoryEntry.cs     # Dòng lịch sử xuất lưu SQLite
    │   ├── LayerSetting.cs           # Cấu hình layer (màu ACI, xóa layer)
    │   ├── ColumnFilter.cs           # Bộ lọc cột kiểu Excel
    │   ├── RangeObservableCollection.cs # Bộ sưu tập Observable tối ưu nạp hàng loạt
    │   └── SolidCraftModels.cs       # DTO hứng kết quả JSON từ SolidCraft (hỗ trợ Linked bodies, Warnings)
    ├── Services/                     # Tầng nghiệp vụ
    │   ├── SolidCraftCliService.cs   # Giao tiếp solidcraft-cli.exe & TCP Control: nạp, tolerant batch, update link, resolve linked bodies
    │   ├── SolidCraftFlipService.cs  # Chuẩn hoá layer SolidCraft & tự động lật mặt trải phẳng
    │   ├── SolidCraftDxfExportManager.cs # Điều phối quy trình xuất DXF hoàn chỉnh (hỗ trợ xuất từ file nguồn cho body linked, tự động nối Polyline kín)
    │   ├── DxfPostProcessor.cs       # Hậu xử lý DXF (nối Line/Arc thành Polyline2D kín, cắt bend 50mm, gắn text nhãn, combine DXF)
    │   ├── LayerConfigService.cs     # Quản lý cấu hình layer và lưu trữ JSON
    │   ├── ExcelReportService.cs     # Xuất báo cáo danh sách cắt phôi ra file Excel .xlsx
    │   ├── DxfHistoryService.cs      # Quản lý cơ sở dữ liệu SQLite lịch sử xuất DXF
    │   └── LocalizationService.cs    # Đa ngôn ngữ (Tiếng Việt / English)
    ├── ViewModels/                   # Tầng ViewModel (MVVM)
    │   ├── DelegateCommand.cs        # ICommand implementation
    │   ├── MainViewModel.cs          # ViewModel cho cửa sổ chính MainWindow
    │   ├── LayerConfigViewModel.cs   # ViewModel cho màn hình cấu hình layer
    │   └── ExportHistoryViewModel.cs # ViewModel cho màn hình tra cứu lịch sử xuất
    ├── Views/                        # Tầng giao diện WPF (XAML)
    │   ├── MainWindow.xaml (.cs)     # Cửa sổ chính của ứng dụng
    │   ├── LayerConfigView.xaml (.cs)# UserControl cấu hình layer
    │   └── ExportHistoryView.xaml (.cs) # UserControl xem và xuất lại từ lịch sử
    └── Resources/                    # Tài nguyên hình ảnh, icon
```

## 2. Chi tiết các module mới & hàm nâng cấp cho Linked Sheet Metal

### 2.1. `SolidCraftCliService.cs`
- **`SolidCraftBatchStepException`**: Ngoại lệ chứa `StepIndex` (0-based), `CommandName` và `Message` parse từ stderr của `solidcraft-cli` khi có step bị lỗi.
- **`ExecuteBatchTolerantAsync(...)`**: Chạy batch lệnh headless với cơ chế dung thứ lỗi: nếu step $N$ lỗi, lưu `{"error": ...}` cho step $N$ và chạy tiếp các step còn lại, đảm bảo không làm hỏng toàn bộ phiên nạp.
- **`ExecuteBatchTolerantLiveAsync(...)`**: Tương đương chế độ dung thứ lỗi qua kênh TCP Live Control Channel.
- **`UpdateLinksAsync(designPath, useLiveApp)`**: Cập nhật link (`component.update_links`). Ở Headless: chạy dry-check, nếu có link cần update thì tự động tạo file sao lưu `.sc.bak` và gọi lệnh có cờ `--save`. Ở Live: gửi qua TCP để GUI SolidCraft cập nhật.
- **`ResolveLinkedSheetBodiesAsync(...)`**: Giải quyết body dumb solid (STEP blob) trong host component bằng cách truy vết `component.links`, mở file `.sc` gốc, ghép body host ↔ body gốc theo volume và area, đọc sheet rules & flat pattern từ file gốc (hỗ trợ đệ quy đa cấp và cache theo file timestamp).
- **`ApplyFlatPatternResult(...)`**: Hàm trích xuất thông số flat pattern (chiều dày, kích thước, bends, pockets, kiểm tra sai lệch rule) dùng chung cho cả body bản địa và body linked.
- **`ExportSheetBodyDxfAsync(...)`**: Bổ sung tham số `SolidCraftSheetItem? sheetItem`: nếu là linked body, tự động chuyển hướng gọi lệnh `sheet.export_dxf` trên file gốc `ExportDesignPath` với tên body gốc `ExportBodyName`.
- **`IsSecondaryOccurrenceWorldBody(...)`**: Lọc bỏ các World Body sinh ra từ Occurrence thứ 2 trở đi (`<Body> (<OccName>)`) trong `inspect.measure`, vì số lượng của chúng đã được tính vào BOM (`count`) hoặc số lượng occurrence của Component.
- **`GetCopyPatternBaseName(name)`**: Tách tên gốc của Body bằng biểu thức chính quy tách bỏ hậu tố tự động sinh của SolidCraft: `"P-1 (2)"` $\to$ `"P-1"`, `"Px-1 (3)"` $\to$ `"Px-1"`.
- **`AreSheetItemsGeometricallyIdentical(a, b)`**: Kiểm tra độ trùng khớp hình học toàn diện giữa 2 chi tiết: cùng liên kết / file nguồn, cùng vật liệu, cùng chiều dày ($\pm 0.01\text{ mm}$), cùng số lượng và hướng đường chấn (`BendCount`, `BendUp`, `BendDown`), cùng kích thước phôi phẳng ($\le 0.05\text{ mm}$, chấp nhận hoán vị xoay 90 độ), và cùng thể tích 3D / diện tích bề mặt (dung sai $0.1\%$).
- **`GroupIdenticalCopiedSheetItems(items)`**: Gom nhóm thông minh các Body sinh ra từ lệnh Copy / Pattern và cộng dồn số lượng (`Quantity`). Tự động bảo vệ các chi tiết có hình học bị sửa riêng (Safety Check), và chạy trước bước snapshot thumbnail để tối ưu thời gian nạp.

### 2.2. `Models/SolidCraftModels.cs` & `ExportRow.cs`
- **`SolidCraftSheetItem`**: Bổ sung `IsLinked`, `ExportDesignPath`, `ExportBodyName`, `LinkedSourceName`, `IsLinkStale`, `VolumeMm3`, `AreaMm2`.
- **`SolidCraftLoadResult`**: Bổ sung `UnrecognizedBodies`, `Warnings`.
- **`ExportRow`**: Bổ sung `IsLinked`, `LinkedSourceName`, `IsLinkStale`, `LinkedTooltip`, hiển thị trạng thái `🔗 <file>` và cảnh báo link lỗi thời trong cột `Ghi chú`.

### 2.3. `DxfPostProcessor.cs` & `SolidCraftDxfExportManager.cs`
- **`DxfPostProcessor.JoinBoundaryCurvesIntoClosedPolylines(doc, targetLayers, tolerance)`**:
  - Tự động gom các đoạn `Line` và `Arc` rời rạc trên các layer biên dạng (`IV_OUTER_PROFILE`, `IV_INTERIOR_PROFILES`, `OUTLINE`, `CUT`, `PERFORATION`, `IV_MARK_SURFACE`, `IV_MARK_SURFACE_BACK`, `ETCH`).
  - Dò vết chuỗi liên tục 2 đầu mút và tính độ phình `Bulge = \pm \tan(\theta / 4)` cho từng đoạn cung tròn theo chiều duyệt.
  - Tạo thực thể `Polyline2D` khép kín (`IsClosed = true`) thay thế sạch sẽ các đoạn rời rạc, hỗ trợ hoàn hảo cho phần mềm CAM CNC laser cutting và nesting.
  - Tích hợp trực tiếp vào Bước 2 của `SolidCraftDxfExportManager` cũng như trong `PostProcessDxfDoc`, `InsertTextIntoDxf`, `CombineDxfFiles`.

### 2.4. `TestRunner.cs`
Bộ kiểm thử tự động 9 bài test:
1. `ExecuteBatchTolerantAsync` cô lập step lỗi và giữ nguyên kết quả các step hợp lệ.
2. `LoadDesignAsync` trên `test\Test2.sc` nhận diện đủ các chi tiết (`P-1`, `P-2`, `P-3`, `Hop`, `spline`).
3. Xuất DXF chi tiết linked `Hop`, chuẩn hoá layer `IV_*`, kiểm tra biên dạng ngoài được nối thành 1 Polyline kín và tạo `combine.dxf`.
4. `UpdateLinksAsync` dry check trên mô hình.
5. `LoadDesignAsync` trên `test\Test.sc` lọc sạch các body không phải sheet metal vào `UnrecognizedBodies`.
6. Xử lý an toàn khi link bị mất file nguồn (Missing Link).
7. Tự động phát hiện link lỗi thời (Stale Link), sao lưu `.sc.bak` và cập nhật khi bấm Update Model.
8. Nối đường bao thành Polyline kín trên chi tiết phức tạp `spline` (97 segments $\to$ 1 closed Polyline2D) và kiểm thử hình học tổng hợp (Lines + Arcs).
9. Gom nhóm thông minh và cộng dồn số lượng các body Copy / Pattern trên mô hình thực tế `Test2.sc` (`P-3` Qty=2, `Px-1` Qty=3, `Px-2` Qty=3), kiểm thử bóc tách tên gốc, kiểm thử kích thước phôi xoay 90 độ, kiểm thử gộp bản sao mồ côi (mất bản gốc) và 2 bài kiểm thử an toàn hình học (Safety Check khi khác volume/area do đục lỗ hoặc khác kích thước phôi).
