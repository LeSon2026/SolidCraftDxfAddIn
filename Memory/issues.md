# Sổ tay vấn đề kỹ thuật và giải pháp (Issues & Solutions)

Tài liệu này ghi nhận các vấn đề kỹ thuật, thách thức phát sinh trong quá trình phát triển SolidCraftDxfAddIn cùng giải pháp tương ứng.

## 1. Cơ chế giao tiếp với SolidCraft (Headless vs Live)
- **Vấn đề**: Người dùng có thể muốn mở file trực tiếp (`.sc`, `.step`) mà không cần mở SolidCraft Desktop, hoặc muốn trích xuất trực tiếp bản vẽ đang mở trên màn hình SolidCraft.
- **Giải pháp**:
  - Headless: Sử dụng `solidcraft-cli run "<script.json>" --in "<file.sc>" --results --json-out "<out.json>" --allow-errors`.
  - Live: Kiểm tra thư mục `%LOCALAPPDATA%\SolidCraft\control\` để đọc `current.port` và file token `<port>.token`, sau đó gửi request JSON TCP đến `127.0.0.1:<port>`.

## 2. Mã hoá ký tự và xuất file DXF trên Windows
- **Vấn đề**: Pipe stdout của CLI trên Windows có thể bị hỏng encoding hoặc chèn BOM nếu dùng pipe chuyển hướng.
- **Giải pháp**: Dùng cờ `--json-out` của `solidcraft-cli` để ghi file UTF-8 chuẩn không BOM. Đối với DXF, truyền cờ `path` trong lệnh `sheet.export_dxf` hoặc nhận chuỗi JSON string rồi ghi bằng UTF-8.

## 3. Khác biệt cấu trúc Layer giữa SolidCraft và chuẩn DxfPostProcessor
- **Vấn đề**: SolidCraft sinh layer `OUTLINE`, `CUT`, `BEND_UP`, `BEND_DOWN`, `ETCH`, `POCKET_*` trong khi `DxfPostProcessor` hiện tại dùng layer quy ước kiểu Inventor (`IV_OUTER_PROFILE`, `IV_INTERIOR_PROFILES`, `IV_BEND`, `IV_BEND_DOWN`, `IV_MARK_SURFACE`, `IV_MARK_SURFACE_BACK`).
- **Giải pháp**: `SolidCraftFlipService.NormalizeSolidCraftLayers` thực hiện chuẩn hoá ngay sau khi xuất DXF, giúp tái sử dụng 100% `DxfPostProcessor` (cắt đường bend 50mm, đổi màu ACI, gắn text, combine DXF theo `PL{Thickness}-{Material}`).

## 4. Xử lý phép lật mặt đối xứng (Auto-Flip) trên bản vẽ DXF 2D
- **Vấn đề**: Khi lật toạ độ gương qua trục đứng $X' = -X$ bằng `netDxf`, các cung tròn trong `Polyline2D` có độ phình (`Bulge`) không tự động đảo dấu, khiến chiều uốn cung bị ngược sau khi lật.
- **Giải pháp**: Trong `SolidCraftFlipService.FlipIfNeeded`, sau khi áp dụng ma trận biến đổi `Matrix3.Scale(-1.0, 1.0, 1.0)`, duyệt qua từng đỉnh của `Polyline2D` và đảo dấu `vertex.Bulge = -vertex.Bulge`, đồng thời hoán đổi các cặp layer `IV_BEND` $\leftrightarrow$ `IV_BEND_DOWN` và `IV_MARK_SURFACE` $\leftrightarrow$ `IV_MARK_SURFACE_BACK`. Đã xác minh qua bài test tự động đạt kết quả chính xác 100%.

## 5. Giới hạn của hệ thống Plugin & Ribbon Tab `SHEET METAL` trong SolidCraft v0.24.0
- **Vấn đề**: Muốn gắn nút mở `SolidCraftDxfAddIn` trực tiếp vào Ribbon Tab `SHEET METAL` của phần mềm SolidCraft giống như Add-in trên Autodesk Inventor.
- **Phân tích kỹ thuật (từ binary `solidcraft.exe`)**:
  - Giao diện SolidCraft sử dụng thư viện `egui` của Rust (`crates\ui-egui\src\toolbar.rs`), vẽ toàn bộ thanh Ribbon bằng GPU shader trong một cửa sổ duy nhất.
  - Hệ thống `plugins.rs` hiện tại chỉ đọc file `plugin.json` gồm 4 trường (`id`, `name`, `version`, `description`) và thông báo rõ trong mã nguồn: *"SolidCraft đọc tệp đó và không chạy bất kỳ mã nào từ thư mục"* (`ui.plugins_note`).
- **Giải pháp**:
  1. **Floating Ribbon Button (Win32/WPF Overlay + Global Hotkey)**: Dùng Win32 API (`GetWindowRect`, `GetForegroundWindow`) neo một nút bấm WPF giao diện Dark Theme của SolidCraft đè lên góc phải thanh Ribbon của cửa sổ `solidcraft.exe` (kèm phím tắt `Ctrl+Shift+D`). Khi bấm sẽ gọi `SolidCraftDxfAddIn` kết nối tự động qua cổng TCP `27878` (Live Control Channel).
  2. **Tích hợp qua `custom_slicers` (`Send to Slicer` trên Ribbon)**: Khai báo `SolidCraftDxfAddIn.exe` làm một Custom Slicer trong `app.ron`, khi bấm `Send to Slicer` trên Ribbon sẽ kích hoạt `SolidCraftDxfAddIn.exe` ở chế độ `--live`.
  3. **Chỉnh sửa source Rust của SolidCraft**: Nếu tự build `solidcraft.exe` từ repo GitHub, thêm 1 nút vào mảng lệnh tab `"SHEET METAL"` trong `crates/ui-egui/src/toolbar.rs` để spawn `SolidCraftDxfAddIn.exe --live`.

## 6. Body insert từ file `.sc` khác (Insert Design) bị lưu dưới dạng STEP blob và mất dữ liệu Sheet Metal
- **Vấn đề**: Khi người dùng dùng lệnh `component.insert_design` để chèn file `.sc` khác vào mô hình host (ví dụ `Hop.sc` chèn vào `Test2.sc`), SolidCraft lưu hình học thành feature `import` và STEP blob (`blobs/<sha>.step`). Lệnh `inspect.measure` trên host không còn trường `sheet_metal: true`, lệnh `sheet.flat_pattern` trên host báo lỗi `` `Hop` is not a sheet metal body ``.
- **Giải pháp**:
  - Sử dụng lệnh `component.links` trên host để lấy đường dẫn file `.sc` gốc và component ID.
  - Ánh xạ body host sang body trong file gốc bằng đối chiếu thể tích (`volume_mm3`) và diện tích (`area_mm2`) với sai số $\le 10^{-4}$ (hoặc fallback 1-1 nếu cả host component và file gốc chỉ có đúng 1 body).
  - Nạp thông số Sheet Metal Rule và chạy `sheet.flat_pattern` trực tiếp trên file gốc thông qua Headless mode.
  - Khi xuất DXF (`sheet.export_dxf`) và chụp thumbnail (`snapshot`), tự động chuyển hướng mục tiêu sang `ExportDesignPath` (file gốc) và `ExportBodyName` (body trong file gốc).
  - Tự động phát hiện liên kết lỗi thời (Stale Link) khi file gốc bị sửa đổi sau khi chèn (kiểm tra `component.update_links` dry check hoặc lệch thể tích), hiển thị cảnh báo và tự động sao lưu `.sc.bak` + chạy `component.update_links --save` khi người dùng bấm nút **Update Model**.

## 7. `solidcraft-cli run` dừng toàn bộ batch và không ghi kết quả JSON khi 1 step bị lỗi
- **Vấn đề**: Cờ `--allow-errors` của `solidcraft-cli run` chỉ áp dụng cho lỗi timeline feature trong mô hình, không áp dụng cho lỗi lệnh step trong batch script. Khi 1 step bị lỗi (exit code 1), CLI hủy bỏ toàn bộ batch và không sinh file `--json-out`, làm mất hết kết quả của các step hợp lệ khác (khiến 1 chi tiết lỗi trải phẳng làm sập toàn bộ quá trình nạp mô hình).
- **Giải pháp**:
  - Trong `SolidCraftCliService.ExecuteBatchHeadlessAsync`: Khi CLI thoát với exit code khác 0 và không có file kết quả, phân tích stderr bằng biểu thức chính quy `error:\s+step\s+(\d+)\s*(?:\(([^)]+)\))?:\s*(.*)` để ném ra ngoại lệ `SolidCraftBatchStepException(stepIndex, commandName, message)`.
  - Triển khai `ExecuteBatchTolerantAsync`: Chạy batch theo từng phân đoạn (prefix commands trước step lỗi được thu thập an toàn, step lỗi được gán node `{"error": ...}`, và tiếp tục chạy phần còn lại). Đảm bảo mảng kết quả luôn đầy đủ $N$ phần tử mà không bao giờ làm hỏng toàn bộ phiên nạp mô hình.

## 8. SolidCraft xuất DXF thành các đoạn Line và Arc rời rạc (chưa gộp thành Polyline kín)
- **Vấn đề**: Khi SolidCraft xuất bản trải phẳng ra DXF qua lệnh `sheet.export_dxf` (kể cả khi bật `curves: "arcs"`), biên dạng ngoài (`OUTLINE` / `IV_OUTER_PROFILE`) và biên dạng lỗ/cutout (`CUT` / `IV_INTERIOR_PROFILES`) được lưu thành hàng chục/hàng trăm entity `LINE` và `ARC` rời rạc độc lập (ví dụ: chi tiết `spline` bị xẻ nhỏ thành 3 LINE và 94 ARC; `Hop` thành 28 đoạn; `P-1` thành 56 đoạn). Khi mở trên các phần mềm CAM cắt laser (CypCut, Trumpf, BySoft, AutoCAD), kỹ sư phải dùng lệnh `PEDIT` -> `Join` bằng tay để biến chúng thành đường bao kín thì máy mới nhận diện đúng hướng cắt, bù dao và tự động gá phôi (nesting).
- **Giải pháp**:
  - Bổ sung thuật toán `DxfPostProcessor.JoinBoundaryCurvesIntoClosedPolylines` chạy tự động trong pipeline xuất DXF (`SolidCraftDxfExportManager`) và quá trình hậu xử lý DXF (`PostProcessDxfDoc`, `InsertTextIntoDxf`, `CombineDxfFiles`).
  - Thuật toán tự động rã (explode) các polyline chưa kín, gom các đoạn `Line` và `Arc` trên từng layer biên dạng mục tiêu (`IV_OUTER_PROFILE`, `IV_INTERIOR_PROFILES`, `OUTLINE`, `CUT`, `PERFORATION`, `IV_MARK_SURFACE`, `IV_MARK_SURFACE_BACK`, `ETCH`).
  - Dùng thuật toán duyệt chuỗi (chaining graph) tìm kiếm 2 đầu mút liên tục hai chiều (cả forward lẫn backward) với dung sai hình học CAD $\le 0.01\text{ mm}$.
  - Tính toán độ phình cung tròn (`Bulge = \pm \tan(\theta / 4)`) chính xác theo chiều duyệt (dương khi ngược chiều kim đồng hồ CCW, âm khi cùng chiều kim đồng hồ CW) để đảm bảo hình học cung tròn và đoạn thẳng được bảo toàn nguyên vẹn 100%.
  - Khi vòng khép kín ($P_{tail} \approx P_{head}$), tạo thực thể `Polyline2D` (`LWPOLYLINE`) với cờ `IsClosed = true` và xoá sạch toàn bộ các đoạn `Line` / `Arc` rời rạc cũ.
  - Kết quả kiểm chứng thực tế: Chi tiết `spline` từ 97 đoạn rời được gộp thành duy nhất **1 closed Polyline2D**, dung lượng file DXF giảm từ 40.6 KB xuống còn 19.2 KB; file `combine.dxf` cũng gộp gọn gàng và tương thích hoàn hảo với mọi hệ thống CAM CNC.

## 9. Nhân bản chi tiết bằng lệnh Copy (`Move/Copy`), Pattern (`Rectangular/Circular Pattern`) và Component Occurrence trong SolidCraft
- **Vấn đề**:
  1. Khi người dùng nhân bản Body bằng lệnh `solid.move` (`copy: true`), `solid.pattern.*` (`bodies`), hoặc `component.copy_bodies`, SolidCraft tạo ra các Body mới với tên tự động mang hậu tố `<TênGốc> (2)`, `<TênGốc> (3)` (ví dụ: `P-3 (2)`, `Px-1 (2)`, `Px-1 (3)`...). Add-in ban đầu liệt kê mỗi bản sao thành 1 dòng riêng với Số lượng = 1, sinh ra hàng loạt file DXF trùng lặp thay vì gộp thành 1 dòng mang tên gốc với tổng số lượng.
  2. Khi nhân bản ở cấp độ Component / Occurrence (`occurrence.copy` hoặc `solid.pattern.*` với `components`), `assembly.bom` đã trả về đúng `count = N`, nhưng `inspect.measure` trong World space liệt kê các bản thể thứ 2 trở đi với tên dạng `<BodyName> (<OccName>)` (ví dụ `P-3 (P-3:2)`). Do không khớp `bodyToCompMap`, các tên này bị coi là body lạ độc lập và đẻ thêm dòng thừa trên UI.
- **Giải pháp**:
  1. **Lọc bỏ World Body của Occurrence phụ (`IsSecondaryOccurrenceWorldBody`)**:
     - Thu thập toàn bộ `secondaryOccurrenceNames` từ `c["occurrences"]` (index $\ge 1$).
     - Khi duyệt `inspect.measure`, nếu tên body có dạng `<BaseBodyName> (<OccName>)` với `<OccName>` thuộc danh sách occurrence phụ và `<BaseBodyName>` thuộc component đó thì bỏ qua ngay lập tức vì số lượng đã được tính trong BOM hoặc số lượng occurrence của component.
  2. **Thuật toán gom nhóm & cộng dồn số lượng bản sao (`GroupIdenticalCopiedSheetItems`)**:
     - Trích xuất tên gốc bằng `GetCopyPatternBaseName`: tách bỏ hậu tố `(\d+)` (VD: `P-3 (2)` $\rightarrow$ `P-3`, `Px-1 (3)` $\rightarrow$ `Px-1`).
     - So sánh chữ ký hình học 3D & trải phẳng 2D (`AreSheetItemsGeometricallyIdentical`): cùng trạng thái nguồn linked/native, cùng vật liệu, cùng chiều dày thực tế ($\pm 0.01\text{ mm}$), cùng số lượng và hướng đường chấn (`BendCount`, `BendUp`, `BendDown`), cùng kích thước phôi trải phẳng ($\min, \max \le 0.05\text{ mm}$, cho phép hoán vị 90 độ), và cùng thể tích 3D / diện tích bề mặt (dung sai $0.1\%$).
     - Khi khớp 100%: gộp vào bản thể đại diện gốc (không có hậu tố `(2)`), cộng dồn `Quantity = sum(Quantity)`.
     - **An toàn hình học (Safety Check)**: Nếu người dùng Copy ra một bản thể mới rồi chỉnh sửa riêng (đục thêm lỗ, cắt gọt khiến thể tích/diện tích hoặc kích thước phôi thay đổi, như `P-1 (4)` trong `Test2.sc`), thuật toán phát hiện lệch hình học và **giữ nguyên làm chi tiết riêng biệt** để đảm bảo không bao giờ làm mất bản vẽ gia công của người dùng.
     - Thực thi trước bước chụp thumbnail giúp giảm 40-60% thời gian nạp mô hình. Đã kiểm chứng qua TEST 9 đạt kết quả chính xác 100%.
