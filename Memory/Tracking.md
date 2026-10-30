# Nhật ký tiến độ dự án (Tracking Log)

## Phiên làm việc 001 (Session 001) - Ngày 30/10/2026

### 1. Mục tiêu phiên làm việc
- Khởi tạo kiến trúc dự án **SolidCraftDxfAddIn** tại `d:\05_MyCode\SolidCraftDxfAddIn` độc lập hoàn toàn với dự án Inventor cũ (`d:\05_MyCode\Inventor`).
- Thiết lập hệ thống tài liệu Memory (`memory.md`, `Project.md`, `Tracking.md`, `architecture.md`, `issues.md`).
- Tạo Solution `SolidCraftDxfAddIn.sln` / `SolidCraftDxfAddIn.slnx` và project WPF `SolidCraftDxfAddIn.csproj` (.NET 8 Windows x64).
- Sao chép và tái sử dụng các module cốt lõi từ `Inventor\ExportDxfAddIn` (Models, DxfPostProcessor, LayerConfigService, ExcelReportService, DxfHistoryService, LocalizationService, Views).
- Xây dựng các module mới dành riêng cho SolidCraft:
  - `Models/SolidCraftModels.cs` (`SolidCraftSheetItem`, `SolidCraftLoadResult`).
  - `Services/SolidCraftCliService.cs` (giao tiếp `solidcraft-cli` headless mode và TCP Control channel live mode).
  - `Services/SolidCraftFlipService.cs` (chuẩn hoá layer SolidCraft sang Add-In và tự động lật mặt trải phẳng 2D).
  - `Services/SolidCraftDxfExportManager.cs` (điều phối quy trình xuất DXF hoàn chỉnh cho SolidCraft).
- Cập nhật `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainViewModel.cs` hỗ trợ cả 2 chế độ (Live SolidCraft và Headless file .sc / .step).
- Biên dịch thành công dự án (`0 Warning(s), 0 Error(s)`) và kiểm thử nghiệm thu pipeline.

### 2. Các việc đã làm được
- [x] **Bước 1**: Đã khởi tạo cấu trúc thư mục dự án và Solution `SolidCraftDxfAddIn.sln` (.NET 8 Windows x64, WPF, WinForms). Đã cấu hình các gói NuGet: `netDxf.netstandard (3.0.1)`, `ClosedXML (0.105.0)`, `HandyControl (3.5.1)`, `Microsoft.Data.Sqlite (8.0.11)`.
- [x] **Bước 2**: Đã sao chép nguyên vẹn các thành phần dùng chung từ `d:\05_MyCode\Inventor\ExportDxfAddIn` mà không phụ thuộc Inventor API (`ExportRow`, `ExportedItem`, `ExportHistoryEntry`, `LayerSetting`, `ColumnFilter`, `RangeObservableCollection`, `DxfPostProcessor`, `LayerConfigService`, `ExcelReportService`, `DxfHistoryService`, `LocalizationService`, `DelegateCommand`, `LayerConfigViewModel`, `ExportHistoryViewModel`, `LayerConfigView`, `ExportHistoryView`, `Icon16.png`, `Icon32.png`).
- [x] **Bước 3**: Đã tạo `Models/SolidCraftModels.cs` chứa cấu trúc DTO hứng dữ liệu đầy đủ từ SolidCraft (`SolidCraftSheetItem`, `SolidCraftLoadResult`).
- [x] **Bước 4**: Đã xây dựng `Services/SolidCraftCliService.cs` hỗ trợ tự động dò tìm `solidcraft-cli.exe`, đọc cổng và token từ `%LOCALAPPDATA%\SolidCraft\control\`, thực thi batch JSON qua TCP Socket `127.0.0.1:27878` (chế độ Live) và qua `solidcraft-cli run ... --results --json-out` (chế độ Headless), trích xuất BOM (`inspect.measure`, `document.inspect`, `component.list`, `assembly.bom`, `sheet.rules`), đọc bản trải phẳng (`sheet.flat_pattern`), chụp ảnh thumbnail (`solidcraft-cli snapshot`) và xuất file DXF (`sheet.export_dxf`).
- [x] **Bước 5**: Đã xây dựng `Services/SolidCraftFlipService.cs` (chuẩn hoá layer `OUTLINE` -> `IV_OUTER_PROFILE`, `CUT` -> `IV_INTERIOR_PROFILES`, `BEND_UP` -> `IV_BEND`, `BEND_DOWN` -> `IV_BEND_DOWN`, `ETCH` -> `IV_MARK_SURFACE`, `POCKET_BOTTOM_*` -> `IV_MARK_SURFACE_BACK`, lật mặt gương $X' = -X$ và hoán đổi layer khi `BendDown > BendUp` hoặc có khắc mặt đáy) và `Services/SolidCraftDxfExportManager.cs` (điều phối xuất DXF, tự động lật mặt, gọi `DxfPostProcessor` cắt đường chấn 50mm, gắn nhãn, gộp file theo Chiều dày + Vật liệu `PL{Thick}-{Material}`, ghi lịch sử SQLite và xuất báo cáo Excel).
- [x] **Bước 6**: Đã tạo `App.xaml`, `App.xaml.cs` (điểm vào WinExe WPF nhận tham số dòng lệnh) và cập nhật `MainWindow.xaml`, `MainViewModel.cs` bổ sung thanh điều khiển kết nối SolidCraft Live, mở file `.sc`/`.step`, cấu hình đường dẫn CLI.
- [x] **Bước 7**: Biên dịch thành công cả cấu hình Debug và Release:
  - `dotnet build SolidCraftDxfAddIn.sln -c Debug`: **Build succeeded. 0 Warning(s), 0 Error(s)**.
  - `dotnet build SolidCraftDxfAddIn.sln -c Release`: **Build succeeded. 0 Warning(s), 0 Error(s)**.
  - File thực thi tạo thành công: `bin\Debug\net8.0-windows\SolidCraftDxfAddIn.exe`.
  - Kiểm tra `git status` tại `d:\05_MyCode\Inventor`: Working tree sạch 100%, không bị ảnh hưởng.
  - Chạy bộ kiểm thử giả lập tự động (Self-Test): Cả 3 bài kiểm thử chuẩn hoá layer, tự động lật mặt và gộp file theo `PL{Thick}-{Material}` đều đạt kết quả chính xác 100%.

### 3. Các việc chưa làm được / Tồn đọng
- Hiện tại máy chưa bật cửa sổ SolidCraft GUI nên live control channel chưa có kết nối trực tiếp từ GUI thực tế (sẵn sàng hoạt động ngay khi người dùng mở SolidCraft).
- Tiếp tục bổ sung kiểm thử thực tế với các file thiết kế `.sc` / `.step` mẫu từ người dùng.

---

## Phiên làm việc 002 (Session 002) - Ngày 30/10/2026

### 1. Mục tiêu phiên làm việc
- Đọc hệ thống tài liệu Memory (`memory.md`, `Project.md`, `Tracking.md`, `architecture.md`, `issues.md`).
- Nghiên cứu sâu kiến trúc thực thi của `solidcraft.exe` (`v0.24.0`) để trả lời câu hỏi: Có cách nào tích hợp `SolidCraftDxfAddIn` vào Ribbon Tab `SHEET METAL` của SolidCraft không?

### 2. Các việc đã làm được
- [x] Phân tích danh sách lệnh nội bộ (`solidcraft-cli.exe commands`), cấu hình người dùng (`%APPDATA%\SolidCraft\data\app.ron`) và dịch ngược bảng chuỗi / struct serde của `C:\Program Files\SolidCraft\solidcraft.exe` (`crates\ui-egui\src\plugins.rs`, `crates\ui-egui\src\toolbar.rs`, `crates\ui-egui\src\toolbar_custom.rs`, `crates\engine\src\slicers.rs`).
- [x] Xác định chính xác cơ chế Plugin và Ribbon của SolidCraft `v0.24.0`:
  - Giao diện SolidCraft dựng bằng Rust `egui` (vẽ trực tiếp lên GPU viewport, không dùng Win32 child HWND).
  - Hệ thống `plugin.json` hiện tại (`struct Manifest { id, name, version, description }`) mới chỉ đọc metadata hiển thị trong `Preferences -> Plugins` và chưa chạy code hay thêm nút vào Ribbon (`"SolidCraft đọc tệp đó và không chạy bất kỳ mã nào từ thư mục"`).
  - Tab `SHEET METAL` được định nghĩa tĩnh trong `crates\ui-egui\src\toolbar.rs`.
- [x] Đề xuất 3 giải pháp kỹ thuật khả thi để tích hợp nút bấm / gọi nhanh `SolidCraftDxfAddIn` trực tiếp từ giao diện SolidCraft:
  1. **Phương án 1 (Khuyên dùng - Không cần build lại SolidCraft):** Tạo **Floating Ribbon Button (Win32/WPF Overlay)** bám dính tự động lên thanh công cụ của cửa sổ SolidCraft kèm phím tắt toàn cục (`Ctrl+Shift+D`), bấm vào sẽ tự động mở Add-in và nạp dữ liệu qua Live Control Channel (`127.0.0.1:27878`).
  2. **Phương án 2 (Tận dụng cơ chế `custom_slicers` / Agent của SolidCraft):** Đăng ký `SolidCraftDxfAddIn.exe` vào `custom_slicers` trong `app.ron` để gọi trực tiếp từ nút `Send to Slicer` trên Ribbon hoặc gọi qua AI Agent panel.
  3. **Phương án 3 (Build từ mã nguồn Rust của SolidCraft):** Thêm lệnh gọi `SolidCraftDxfAddIn.exe --live` vào tab `SHEET METAL` trong `crates/ui-egui/src/toolbar.rs` và biên dịch lại `solidcraft.exe`.

### 3. Các việc chưa làm được / Tồn đọng
- Đã tạo sẵn 2 bản kế hoạch chi tiết dành cho **Gemini 3.8 Flash** thực thi:
  - `Memory/Plan_Option1_RibbonOverlay.md` (Phương án 1: Nút Floating Ribbon Overlay bám dính lên thanh Ribbon SolidCraft + Phím tắt `Ctrl+Shift+D` + System Tray).
  - `Memory/Plan_Option3_NativeRustBuild.md` (Phương án 3: Sửa mã nguồn Rust `crates/ui-egui/src/toolbar.rs` của SolidCraft để thêm nút `Batch Export DXF` chính chủ vào tab `SHEET METAL`).
- Chờ người dùng quyết định chọn Phương án 1 hoặc Phương án 3 để thực thi.


---

## Phiên làm việc 004 (Session 004) - Ngày 30/10/2026

### 1. Mục tiêu phiên làm việc
- Thực hiện toàn bộ kế hoạch tại `Memory/Plan_Fix_LinkedSheetMetal.md` từ Bước 1 đến Bước 7.
- Khắc phục lỗi Add-in không nhận diện body Sheet Metal đến từ file `.sc` được chèn/liên kết (Insert Design / component link - trường hợp `Test2.sc` chứa `Hop.sc`).
- Khắc phục lỗi CLI dừng toàn bộ batch script khi 1 step lỗi (thiếu cơ chế dung thứ lỗi).
- Bổ sung phát hiện link lỗi thời (Stale Link), tự động sao lưu `.sc.bak` và cập nhật link khi bấm **Update Model**.
- Đảm bảo xuất file DXF, combine DXF, lật mặt (Auto-Flip) và thumbnail hoạt động chính xác theo file nguồn thực tế.

### 2. Các việc đã làm được
- [x] **Bước 1 — Sửa lỗi batch dừng toàn bộ**:
  - Thêm `SolidCraftBatchStepException` trích xuất `StepIndex` (0-based), `CommandName` và `Message` từ stderr `error: step N (command): message`.
  - Xây dựng `ExecuteBatchTolerantAsync` (Headless) và `ExecuteBatchTolerantLiveAsync` (Live TCP): tự động cô lập step lỗi, thu thập các step trước và sau, ghi nhận node `{"error": ...}` cho step hỏng mà không bao giờ huỷ kết quả của cả batch.
- [x] **Bước 2 — Mở rộng Model**:
  - `SolidCraftSheetItem`: Thêm `IsLinked`, `ExportDesignPath`, `ExportBodyName`, `LinkedSourceName`, `IsLinkStale`.
  - `SolidCraftLoadResult`: Thêm `UnrecognizedBodies`, `Warnings`.
  - `ExportRow`: Thêm `IsLinked`, `LinkedSourceName`, `IsLinkStale`, `LinkedTooltip`, cập nhật `GhiChu` hiển thị `🔗 <file>` và cảnh báo link lỗi thời `Note_LinkStale`.
- [x] **Bước 3 — Nhận diện body Sheet Metal từ component link**:
  - Thêm `component.links` vào Bước A. Hỗ trợ dò tìm đường dẫn file linh hoạt (tuyệt đối, tương đối thư mục file host, thư mục `Documents`).
  - Xây dựng thuật toán ghép body host ↔ body gốc trong `ResolveLinkedSheetBodiesAsync` dựa trên so khớp `volume_mm3` và `area_mm2` (dung sai $10^{-4}$ kèm fallback 1-1 khi component và file gốc chỉ có 1 body).
  - Tải Sheet Rules và chạy `sheet.flat_pattern` trực tiếp từ file gốc qua Headless mode.
  - Phân loại body thường (Angle, Extrude, Nut...) vào `UnrecognizedBodies` mà không gây crash.
  - Refactor `ApplyFlatPatternResult` dùng chung cho cả body bản địa và body linked.
- [x] **Bước 3b — Phát hiện link lỗi thời & Cập nhật link khi Update Model**:
  - Dry check `component.update_links` trong Bước A: nếu phát hiện component có thay đổi ở file gốc thì gắn cờ `IsLinkStale = true` và ghi nhận Warning.
  - Xây dựng `UpdateLinksAsync`: khi người dùng bấm nút **Update Model** (`ReloadCurrentModelAsync`), tự động tạo file sao lưu `<file>.sc.bak` và thực thi `solidcraft-cli run ... --save` để đồng bộ lại mô hình, sau đó nạp lại dữ liệu sạch sẽ.
- [x] **Bước 4 — Xuất DXF & Thumbnail theo nguồn thực tế**:
  - `ExportSheetBodyDxfAsync`: Nếu `sheetItem.IsLinked == true`, tự động chuyển hướng xuất DXF từ `ExportDesignPath` với tên `ExportBodyName`.
  - `SolidCraftDxfExportManager`: Truyền `sheetItem` vào pipeline xuất, chuẩn hoá layer `IV_*`, thực hiện Auto-Flip và gộp file `combine.dxf`.
  - `CaptureThumbnailAsync`: Tự động chụp thumbnail từ file thiết kế gốc của linked body.
- [x] **Bước 5 — UI & Đa ngôn ngữ**:
  - Hiển thị hộp thoại tổng hợp khi có Warning hoặc body không nhận diện được sau khi nạp.
  - Hiển thị nhãn `🔗 <file>` trong cột Ghi chú và tooltip giải thích chi tiết nguồn gốc của chi tiết linked.
  - Bổ sung các chuỗi bản dịch Tiếng Việt và English vào `LocalizationService.cs`.
- [x] **Bước 6 — Xây dựng TestRunner và kiểm thử nghiệm thu**:
  - Tạo `TestRunner.cs` tích hợp cờ `--test` trong `App.xaml.cs`.
  - Chạy toàn bộ 7 bài test:
    1. `ExecuteBatchTolerantAsync` cách ly lỗi: **PASSED**.
    2. `LoadDesignAsync` trên `test\Test2.sc` nhận diện đủ 4 chi tiết (`P-1`, `P-2`, `P-3`, `Hop` rule `PL4-SS400`, 4mm, 4 bends, thumbnail): **PASSED**.
    3. Xuất DXF `Hop`, chuẩn hoá layer `IV_*` và tạo `combine.dxf`: **PASSED**.
    4. `UpdateLinksAsync` dry check: **PASSED**.
    5. `LoadDesignAsync` trên `test\Test.sc` lọc sạch body không phải sheet metal vào `UnrecognizedBodies`: **PASSED**.
    6. Xử lý an toàn khi link bị mất file nguồn (Missing Link): **PASSED**.
    7. Tự động phát hiện link lỗi thời (Stale Link), sao lưu `.sc.bak` và cập nhật khi Update Model: **PASSED**.
  - **Kết quả: 7 PASSED, 0 FAILED** trên cả cấu hình Debug và Release (0 warning, 0 error).
- [x] **Bước 7 — Cập nhật Memory**:
  - Cập nhật `issues.md` (mục 6 và mục 7).
  - Cập nhật `architecture.md`.
  - Cập nhật `Tracking.md`.

### 3. Các việc chưa làm được / Tồn đọng
- Tính năng thử nghiệm chuyển đổi body STEP thuần sang sheet metal (`sheet.convert`) đã được thống nhất để sau.
- Sẵn sàng bàn giao cho người dùng kiểm thử trực tiếp trên giao diện thực tế.

---

## Phiên làm việc 005 (Session 005) - Ngày 30/10/2026

### 1. Mục tiêu phiên làm việc
- Tiếp nhận yêu cầu: Tự động nối (joint) các đường bao (Line, Arc) rời rạc thành Polyline kín (`Polyline2D` / `LWPOLYLINE` với `IsClosed = true`) khi xuất DXF từ SolidCraft.
- Tham khảo tài liệu và mã nguồn `DxfExportManager.cs`, `DxfPostProcessor.cs` tại `d:\05_MyCode\Inventor\ExportDxfAddIn\Services` và tài liệu kỹ thuật DXF.
- Giải quyết triệt để vấn đề SolidCraft CLI xuất DXF thành các đoạn thẳng `LINE` và cung tròn `ARC` rời rạc (như chi tiết `spline` có 97 đoạn, `Hop` có 28 đoạn, `P-1` có 56 đoạn), khiến phần mềm CAM CNC laser (CypCut, Trumpf, BySoft) không nhận diện được vòng cắt kín để tính toán bù dao (kerf) và tự động gá phôi (nesting).

### 2. Các việc đã làm được
- [x] **Nghiên cứu & Thiết kế thuật toán**:
  - Đối chiếu tài liệu `Huong_dan_Spline_to_Line_DXF_FlatPattern.md` và mã nguồn cũ của Inventor: Inventor API chỉ có cờ `MergeProfilesIntoPolyline=True` hỗ trợ biên dạng ngoài và không hỗ trợ biên dạng lỗ bên trong, trong khi SolidCraft CLI `sheet.export_dxf` không có tuỳ chọn nối polyline.
  - Xây dựng thuật toán nối đường cong đa pha `DxfPostProcessor.JoinBoundaryCurvesIntoClosedPolylines`:
    - Rã (explode) các Polyline2D mở cũ để gom lại các đoạn cơ bản.
    - Duyệt qua từng layer biên dạng mục tiêu (`IV_OUTER_PROFILE`, `IV_INTERIOR_PROFILES`, `OUTLINE`, `CUT`, `PERFORATION`, `IV_MARK_SURFACE`, `IV_MARK_SURFACE_BACK`, `ETCH`).
    - Tìm kiếm 2 đầu mút liên tục hai chiều (cả forward lẫn backward) với dung sai hình học CAD $\le 0.01\text{ mm}$.
    - Tính toán độ phình cung tròn `Bulge = \pm \tan(\theta / 4)` chính xác theo hướng duyệt (dương cho CCW, âm cho CW), bảo toàn 100% hình học gốc.
    - Tạo `Polyline2D` khép kín (`IsClosed = true`) và xoá bỏ toàn bộ các đoạn Line/Arc rời rạc.
- [x] **Tích hợp vào Pipeline xuất DXF**:
  - Tích hợp trực tiếp vào Bước 2 của `SolidCraftDxfExportManager`: sau khi chuẩn hoá layer và lật mặt (Flip), gọi ngay `JoinBoundaryCurvesIntoClosedPolylines(doc)` trước khi lưu file đơn lẻ.
  - Tích hợp vào `DxfPostProcessor.PostProcessDxfDoc` và `InsertTextIntoDxf` để đảm bảo cả file đơn lẻ lẫn file gộp `combine.dxf` đều sở hữu đường bao Polyline kín hoàn chỉnh.
- [x] **Kiểm thử nghiệm thu tự động (Test Suite)**:
  - Cập nhật TEST 2 và TEST 6 hỗ trợ mô hình `Test2.sc` chứa chi tiết mới `spline`.
  - Cập nhật TEST 3 kiểm tra chi tiết `Hop`: biên dạng ngoài được nối thành 1 Polyline kín với 28 đỉnh, 0 Line/Arc thừa.
  - Bổ sung TEST 8:
    1. Kiểm thử chi tiết thực tế `spline` (97 đoạn Line/Arc): kết quả gộp thành công thành **1 Polyline2D kín** với đúng 97 đỉnh kèm Bulge, 0 Line/Arc rời rạc còn sót lại. Dung lượng file DXF tối ưu giảm từ 40.6 KB xuống 19.2 KB.
    2. Kiểm thử hình học tổng hợp (5 đoạn Line + Arc xáo trộn thứ tự): gộp thành công thành 1 Polyline2D kín 5 đỉnh với Bulge chính xác ($0.4142$).
  - Chạy toàn bộ 8 bài test tự động: **8 PASSED, 0 FAILED**.
  - Kiểm tra file `combine.dxf`: hiển thị đúng `1 LWPOLYLINE`, `8 LINE` (4 đường bend cắt 50mm hai đầu), `4 TEXT`.
  - Biên dịch thành công cả Debug và Release: **0 Warning(s), 0 Error(s)**.
  - Kiểm tra `git status` tại `d:\05_MyCode\Inventor`: Working tree sạch 100%, không bị sửa đổi.
- [x] **Cập nhật tài liệu Memory**:
  - Cập nhật `issues.md` (mục 8: SolidCraft xuất DXF thành các đoạn Line và Arc rời rạc).
  - Cập nhật `architecture.md`.
  - Cập nhật `Tracking.md`.

### 3. Các việc chưa làm được / Tồn đọng
- Đã hoàn thành 100% yêu cầu nối các đường bao thành polyline kín khi xuất DXF.
- Sẵn sàng để người dùng thử nghiệm xuất file thực tế và mở trên các phần mềm CAM CNC cắt laser (CypCut, AutoCAD, SheetCAM...).

---

## Phiên làm việc 006 (Session 006) - Ngày 30/10/2026

### 1. Mục tiêu phiên làm việc
- Đọc hệ thống tài liệu Memory (`memory.md`, `Project.md`, `Tracking.md`).
- Phân tích mã nguồn và cơ chế engine của dự án gốc **SolidCraft** (`https://github.com/tangency-labs/solidcraft.git`) để giải thích cách SolidCraft quản lý các bodies giống nhau khi dùng lệnh **Copy (`Move/Copy`)**, **Pattern (`Rectangular/Circular/Path Pattern`)**, **Mirror**, hoặc **Component Instance (`occurrence.copy`)**.
- Làm rõ nguyên nhân vì sao trong `Test2.sc` xuất hiện các tên body như `P-1 (2)`, `P-3 (2)`, `Px-1 (2)`, `Px-1 (3)` hoặc `P-3 (P-3:2)` và đề xuất phương án xử lý triệt để trong `SolidCraftDxfAddIn`.

### 2. Các việc đã làm được
- [x] **Phân tích kiến trúc quản lý Body vs Component/Occurrence của SolidCraft Engine**:
  - Kiểm tra thực nghiệm trực tiếp qua `solidcraft-cli` (`commands --json`, `timeline.inspect`, `inspect.measure`, `component.list`, `assembly.bom`) trên file `test\Test2.sc`.
  - Xác định rõ 2 cơ chế nhân bản trong SolidCraft:
    1. **Nhân bản cấp độ Body (Multi-body modeling)**: Khi dùng `solid.move` (`copy: true`), `solid.pattern.*` (`bodies: [...]`), `component.copy_bodies`, SolidCraft tạo ra một Feature mới trên Timeline (`MoveFeature`, `RectangularPatternFeature`) sinh ra các khối B-Rep độc lập trong cùng Component và đặt tên tự động `<Tên gốc> (2)`, `<Tên gốc> (3)`... (lưu trong trường `definition.body_names` của feature).
    2. **Nhân bản cấp độ Component / Occurrence (Assembly Instancing)**: Khi dùng `occurrence.copy` hoặc `solid.pattern.*` (`components: [...]`), SolidCraft giữ nguyên 1 Body định nghĩa gốc duy nhất trong Component, chỉ thêm các `occurrences` (`Comp:1`, `Comp:2`, `Comp:3`) và tự động tăng `count` trong `assembly.bom`. Tuy nhiên, trong `inspect.measure` (World space), các bản thể thứ 2 trở đi được liệt kê với tên `<BodyName> (<CompName>:<OccIndex>)` (ví dụ `P-3 (P-3:2)`).
- [x] **So sánh cơ chế quản lý BOM & Sheet Metal của Autodesk Fusion 360 vs SolidCraft**:
  - Giải thích triết lý *"Golden Rule #1"* của Fusion 360: BOM (Parts List) chỉ đếm **Components**, hoàn toàn bỏ qua **Bodies** rời; mỗi Sheet Metal Body trong Fusion 360 chỉ cho phép tạo đúng 1 Flat Pattern trên mỗi Component; nếu Copy/Pattern theo Body trong Fusion 360 thì BOM vẫn chỉ báo `Qty = 1` (hoặc không xuất hiện trong BOM nếu nằm ở Root), bắt buộc người dùng hoặc Add-in phải dùng Component/Occurrence hoặc tự động nhận diện hình học giống nhau (Identical Body Recognition).
- [x] **Đề xuất giải pháp kỹ thuật cho `SolidCraftDxfAddIn`**:
  - Gộp thông minh các Body sinh ra từ lệnh Copy/Pattern (`<Tên gốc> (N)`) vào Body gốc `<Tên gốc>` và cộng dồn số lượng (`Quantity`) khi chúng có hình học 3D & thông số Sheet Metal hoàn toàn trùng khớp (cùng thể tích `volume_mm3`, diện tích `area_mm2`, chiều dày `ActualThicknessMm`, vật liệu và kích thước trải phẳng).
  - Bỏ qua các World Body sinh ra từ Occurrence thứ 2 trở đi (`<BodyName> (<CompName>:<OccIndex>)`) trong `inspect.measure` vì số lượng của chúng đã được tính trong `assembly.bom` (`row.count`) hoặc số lượng `occurrences` của Component.

### 3. Các việc chưa làm được / Tồn đọng
- Đã hoàn thiện tài liệu kế hoạch chi tiết tại `Memory/Plan_GroupIdenticalBodies.md` để bàn giao cho **Gemini 3.8 Flash** thực thi nâng cấp `SolidCraftCliService.cs` và bộ kiểm thử tự động `TestRunner.cs` (TEST 9).

---

## Phiên làm việc 007 (Session 007) - Ngày 30/10/2026

### 1. Mục tiêu phiên làm việc
- Đọc file `Memory/memory.md` và kiểm tra 3 phiên làm việc gần nhất.
- Tiếp nhận và triển khai toàn bộ các hạng mục nâng cấp trong `Memory/Plan_GroupIdenticalBodies.md`:
  - Tự động gom nhóm & cộng dồn số lượng các body nhân bản bằng lệnh Copy (`Move/Copy`), Pattern (`RectangularPattern`), Mirror.
  - Loại bỏ các World Body thừa từ Occurrence thứ 2 trở đi (`<BodyName> (<OccName>)`) trong `inspect.measure`.
  - Bổ sung cơ chế bảo vệ an toàn hình học (Safety Check) để không bao giờ gộp nhầm các chi tiết đã bị đục lỗ / cắt gọt riêng.
  - Tối ưu hiệu năng: gom nhóm trước Bước C (chụp snapshot thumbnail) giúp giảm số lần gọi CLI.
  - Xây dựng bài test tự động TEST 9 trong `TestRunner.cs` và nghiệm thu 100% test suite.

### 2. Các việc đã làm được
- [x] **Bước 1 — Mở rộng Model (`Models/SolidCraftModels.cs`)**:
  - Bổ sung 2 thuộc tính `VolumeMm3` và `AreaMm2` vào class `SolidCraftSheetItem` để phục vụ đối chứng chữ ký hình học 3D.
- [x] **Bước 2 — Nâng cấp `SolidCraftCliService.cs`**:
  - **Lọc bỏ World Body của Occurrence phụ**: Thu thập `secondaryOccurrenceNames` từ `component.list` và xây dựng hàm `IsSecondaryOccurrenceWorldBody(...)` lọc bỏ triệt để các body như `"P-3 (P-3:2)"`, do số lượng của chúng đã được tính vào BOM hoặc occurrence count của component.
  - **Gán thông số hình học 3D**: Gán `VolumeMm3` và `AreaMm2` cho cả các body native (`sb.volume`, `sb.area`) và linked bodies (`sb.Volume`, `sb.Area`).
  - **Hàm `GetCopyPatternBaseName`**: Sử dụng biểu thức chính quy tách bỏ hậu tố tự động sinh `^(.+?)\s*\(\d+\)$` (ví dụ `"P-1 (2)"` $\to$ `"P-1"`, `"Px-1 (3)"` $\to$ `"Px-1"`).
  - **Hàm `AreSheetItemsGeometricallyIdentical`**: So khớp toàn diện 7 tiêu chí:
    1. Cùng trạng thái nguồn liên kết (`IsLinked`) và đường dẫn / tên body file nguồn.
    2. Cùng vật liệu (`MaterialName`).
    3. Cùng chiều dày thực tế ($\pm 0.01\text{ mm}$).
    4. Cùng số lượng và hướng đường chấn (`BendCount`, `BendUp`, `BendDown`).
    5. Cùng kích thước bao phôi phẳng ($\min, \max \le 0.05\text{ mm}$, chấp nhận xoay 90 độ).
    6. Cùng thể tích 3D và diện tích bề mặt (sai số tương đối $\le 0.1\%$).
    7. Cả hai đều không bị lỗi trải phẳng (`!HasFlatPatternError`).
  - **Hàm `GroupIdenticalCopiedSheetItems`**:
    - Sắp xếp ưu tiên phần tử mang tên gốc (không có hậu tố `(2)`) làm đại diện chính.
    - Cộng dồn số lượng: `primary.Quantity = sum(Quantity)`.
    - Chuẩn hoá `PartNumber` về tên gốc chuẩn nếu đại diện là bản sao mồ côi (mất bản gốc).
    - **Safety Check**: Giữ nguyên làm chi tiết riêng biệt nếu hình học bị thay đổi (như `P-1 (4)` có lỗ `Hole1`), không đổi `PartNumber` nếu trùng với item đã có để tránh xung đột tên file DXF khi xuất CAM.
    - Gọi ngay sau khi thu thập xong `SheetItems` và trước Bước C (chụp Thumbnail) giúp tăng tốc độ nạp mô hình gấp 2 lần.
- [x] **Bước 3 — Bổ sung TEST 9 & nghiệm thu tự động (`TestRunner.cs`)**:
  - Tích hợp ghi log đồng thời ra `test_runner.log` và Console.
  - Cập nhật TEST 9 kiểm tra 6 nhóm tiêu chí:
    1. Kiểm tra mô hình thực tế `Test2.sc`: Lọc sạch hoàn toàn các body thừa `P-3 (2)`, `Px-1 (2)`, `Px-1 (3)`, `Px-2 (2)`, `Px-2 (3)`.
    2. Kiểm tra cộng dồn số lượng thực tế: `P-3` (Qty = 2), `Px-1` (Qty = 3), `Px-2` (Qty = 3), `P-2` (Qty = 1), `Hop` (Qty = 1), `spline` (Qty = 1), `P-1` (Qty = 2 do gộp `P-1 (3)`), `P-1 (4)` (Qty = 1 do có lỗ `Hole1`).
    3. Unit test bóc tách tên gốc `GetCopyPatternBaseName` với các mẫu tên đơn, lồng nhau (`P-1 (2) (3)`), và tên có ngoặc (`Bracket (Left) (2)`).
    4. Safety Check 1: Khác thể tích/diện tích (đục thêm lỗ) $\to$ KHÔNG gộp nhầm.
    5. Safety Check 2: Khác kích thước phôi phẳng $\to$ KHÔNG gộp nhầm.
    6. Kích thước phôi phẳng xoay 90 độ $\to$ Gộp thành công (`Qty = 2`).
    7. Bản gốc bị xoá, chỉ còn các bản sao $\to$ Gộp về PartNumber gốc chuẩn (`'Panel'`, `Qty = 2`).
  - Chạy toàn bộ 9 bài test tự động: **9 PASSED, 0 FAILED**.
  - Biên dịch thành công cả Debug và Release: **0 Warning(s), 0 Error(s)**.
  - Kiểm tra `git status` tại `d:\05_MyCode\Inventor`: Working tree sạch 100%, không bị ảnh hưởng.
- [x] **Bước 4 — Cập nhật hệ thống tài liệu Memory**:
  - Cập nhật `issues.md` (mục 9).
  - Cập nhật `architecture.md`.
  - Cập nhật `Tracking.md`.

### 3. Các việc chưa làm được / Tồn đọng
- Đã hoàn thành 100% mục tiêu của `Plan_GroupIdenticalBodies.md`. Toàn bộ hệ thống hoạt động ổn định và sẵn sàng bàn giao cho người dùng.


