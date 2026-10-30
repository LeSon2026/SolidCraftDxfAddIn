# KẾ HOẠCH PHÁT TRIỂN: SOLIDCRAFT DXF EXPORTER (PHƯƠNG ÁN 1 — GỌI SOLIDCRAFT-CLI & CONTROL CHANNEL)

> **Dành cho AI thực thi (Gemini Flash 3.8):**  
> Tài liệu này hướng dẫn chi tiết từng bước từ A → Z để xây dựng dự án **SolidCraftDxfAddIn** — công cụ xuất hàng loạt bản trải phẳng kim loại tấm (Sheet Metal Flat Pattern DXF), tự động lật mặt (Flip), gắn nhãn sản xuất, gộp file `combine.dxf` phân loại theo **(Chiều dày + Vật liệu)** và xuất báo cáo Excel cho phần mềm **SolidCraft**.  
> Dự án mới được đặt tại thư mục độc lập **`d:\05_MyCode\SolidCraftDxfAddIn`** (nằm ngoài thư mục `d:\05_MyCode\Inventor`), **tách biệt hoàn toàn** với Add-in Inventor cũ để không làm ảnh hưởng tới mã nguồn hiện tại.
> Mã nguồn của dự án SolidCraft tại: https://github.com/tangency-labs/solidcraft.git
> Máy tính đã cài đặt SolidCraft.
---

## 1. TỔNG QUAN KIẾN TRÚC & NGUYÊN LÝ HOẠT ĐỘNG

### 1.1. Vị trí thư mục dự án mới
- **Thư mục dự án cũ (Inventor — giữ nguyên, chỉ đọc để copy file dùng chung):** `d:\05_MyCode\Inventor`
- **Thư mục dự án mới (SolidCraft — nơi khởi tạo và làm việc):** `d:\05_MyCode\SolidCraftDxfAddIn`

```text
d:\05_MyCode\
├── Inventor\                     <-- Dự án cũ cho Autodesk Inventor (KHÔNG SỬA ĐỔI)
└── SolidCraftDxfAddIn\           <-- Dự án mới độc lập cho SolidCraft
    ├── SolidCraftDxfAddIn.sln
    └── SolidCraftDxfAddIn\
        ├── SolidCraftDxfAddIn.csproj   (WinExe - Ứng dụng WPF .NET 8 chạy độc lập / Sidecar)
        ├── App.xaml / App.xaml.cs
        ├── Models\
        │   ├── ExportRow.cs
        │   ├── ExportedItem.cs
        │   ├── ExportHistoryEntry.cs
        │   ├── LayerSettingItem.cs
        │   └── SolidCraftModels.cs     (DTO hứng JSON từ solidcraft-cli)
        ├── Services\
        │   ├── SolidCraftCliService.cs (Gọi solidcraft-cli.exe & TCP Control Channel)
        │   ├── SolidCraftDxfExportManager.cs (Điều phối xuất DXF, map layer, flip, combine)
        │   ├── SolidCraftFlipService.cs (Lật mặt Flat Pattern 2D & đảo BEND_UP <-> BEND_DOWN)
        │   ├── DxfPostProcessor.cs     (Tái sử dụng 100%: cắt bend, gắn nhãn, gộp theo Chiều dày + Vật liệu)
        │   ├── LayerConfigService.cs   (Tái sử dụng 100%)
        │   ├── ExcelReportService.cs   (Tái sử dụng 100%)
        │   ├── DxfHistoryService.cs    (Tái sử dụng 100%)
        │   └── LocalizationService.cs  (Mở rộng thêm chuỗi cho SolidCraft)
        ├── ViewModels\
        │   ├── MainViewModel.cs
        │   └── LayerConfigViewModel.cs
        └── Views\
            ├── MainWindow.xaml / MainWindow.xaml.cs
            ├── LayerConfigWindow.xaml / LayerConfigWindow.xaml.cs
            └── DxfBatchPromptWindow.xaml / DxfBatchPromptWindow.xaml.cs
```

---

## 2. CÁCH THỨC GỌI `solidcraft-cli.exe` VÀ CONTROL CHANNEL (ĐÃ XÁC MINH TỪ SOURCE CODE SOLIDCRAFT)

Qua phân tích trực tiếp mã nguồn Rust của SolidCraft (`apps/solidcraft-cli/src/main.rs`, `crates/engine/src/cmd/sheet.rs`, `crates/engine/src/cmd/bom.rs`, `crates/engine/src/cmd/inspect.rs`), dưới đây là cơ chế giao tiếp chính xác:

### 2.1. Vị trí file thực thi `solidcraft-cli.exe`
Ứng dụng cần tự động dò tìm `solidcraft-cli.exe` theo thứ tự ưu tiên:
1. Đường dẫn lưu trong `%AppData%\SolidCraftDxfAddIn\settings.json` (do người dùng chọn nếu để ở thư mục riêng).
2. Tìm trong biến môi trường `PATH` của Windows (`solidcraft-cli.exe`).
3. Đường dẫn cài đặt mặc định của bộ cài MSI Windows:
   - `C:\Program Files\SolidCraft\solidcraft-cli.exe`
   - `%LOCALAPPDATA%\Programs\SolidCraft\solidcraft-cli.exe`

---

### 2.2. Hai chế độ làm việc với SolidCraft

Ứng dụng hỗ trợ song song **2 chế độ lấy dữ liệu thiết kế**:

#### Chế độ 1: Mở trực tiếp file thiết kế (`.sc`, `.solidcraft`, `.step`, `.stp`) qua `solidcraft-cli run` (Headless Mode)
Người dùng chọn file `.sc` / `.step` ngay trên giao diện WPF (hoặc kéo thả file vào) mà không cần bật cửa sổ SolidCraft 3D.

- **Cú pháp lệnh chuẩn của `solidcraft-cli.exe`:**
  ```powershell
  solidcraft-cli.exe run "<đường_dẫn_script.json>" --in "<đường_dẫn_file.sc>" --results --json-out "<đường_dẫn_ket_qua.json>" --allow-errors
  ```
- **Giải thích các cờ (flags) bắt buộc:**
  - `--in "<file.sc>"`: Nạp file thiết kế `.sc` (hoặc `.solidcraft`, `.step`) làm phiên làm việc gốc trước khi chạy script.
  - `<script.json>`: File JSON chứa mảng các lệnh (`[{"command": "...", "params": {...}}, ...]`) cần thực thi tuần tự trên thiết kế đó.
  - `--results`: Yêu cầu `solidcraft-cli` thu thập toàn bộ kết quả trả về của từng lệnh trong `script.json` thành một mảng JSON `[...]`.
  - `--json-out "<ket_qua.json>"`: **Cực kỳ quan trọng trên Windows!** Ghi kết quả JSON trực tiếp ra file bằng chuẩn UTF-8 không BOM (tránh lỗi hỏng ký tự tiếng Việt / Unicode khi đọc qua `stdout` pipe trên Windows).
  - `--allow-errors`: Vẫn trả kết quả của các body lành lặn ngay cả khi trong cây lịch sử (timeline) của model có 1 feature khác đang bị cảnh báo lỗi.

#### Chế độ 2: Kết nối trực tiếp với cửa sổ SolidCraft đang mở (Live Control Channel Mode)
Khi người dùng đang mở bản vẽ trên phần mềm SolidCraft Desktop và bấm nút **"Đọc từ SolidCraft đang mở"**:

1. **Đọc cổng (Port) và mã xác thực (Token):**
   - Thư mục điều khiển mặc định của SolidCraft trên Windows:
     `%LOCALAPPDATA%\SolidCraft\control\` (hoặc biến môi trường `SOLIDCRAFT_CONTROL_DIR`).
   - Đọc số cổng từ file: `%LOCALAPPDATA%\SolidCraft\control\current.port` (mặc định là `27878`).
   - Đọc chuỗi hex token từ file: `%LOCALAPPDATA%\SolidCraft\control\<port>.token` (ví dụ `27878.token`).
2. **Giao thức TCP JSON-lines (`127.0.0.1:<port>`):**
   - Kết nối `TcpClient("127.0.0.1", port)`.
   - Gửi 1 dòng JSON (kết thúc bằng `\n`). Dòng đầu tiên trên kết nối **phải** đính kèm `"token"`:
     ```json
     {"id": 1, "method": "engine.batch", "params": {"commands": [...], "rollback": false}, "token": "<chuỗi_token>"}
     ```
   - Nhận về 1 dòng JSON phản hồi:
     ```json
     {"id": 1, "ok": true, "result": {"completed": 4, "results": [...]}}
     ```

---

### 2.3. Chi tiết kịch bản lệnh JSON (Command Script) để quét BOM & Xuất DXF từ SolidCraft

Để lấy đầy đủ dữ liệu cho bảng danh sách Sheet Metal, `SolidCraftCliService` thực hiện **2 bước gọi lệnh**:

#### Bước A: Quét cấu trúc thiết kế, BOM, Vật liệu và Danh sách Sheet Metal
Gửi danh sách 5 lệnh sau trong 1 lần chạy (`run` hoặc `engine.batch`):

```json
[
  {
    "command": "inspect.measure",
    "params": {}
  },
  {
    "command": "document.inspect",
    "params": { "measure": false }
  },
  {
    "command": "component.list",
    "params": { "compact": false }
  },
  {
    "command": "assembly.bom",
    "params": { "levels": "flat" }
  },
  {
    "command": "sheet.rules",
    "params": {}
  }
]
```

**Cấu trúc JSON trả về của từng lệnh (tương ứng `results[0]` → `results[4]`):**

1. **`results[0]` (`inspect.measure`)**:
   ```json
   {
     "body_count": 3,
     "bodies": [
       {
         "name": "Bracket_Left",
         "closed": true,
         "sheet_metal": true,
         "material": "S275JR",
         "volume_mm3": 45000.0,
         "area_mm2": 18500.0
       }
     ]
   }
   ```
   $\rightarrow$ Lọc ra tất cả các phần tử trong `"bodies"` có `"sheet_metal": true`. Từ đây ta biết chính xác tên từng `SheetBody` (`name`) và vật liệu của nó (`material`).

2. **`results[1]` (`document.inspect`)**:
   - Cho biết `"name"` (tên thiết kế), `"path"` (đường dẫn file `.sc`), `"metadata"` (thuộc tính của thiết kế gốc).

3. **`results[2]` (`component.list`)**:
   ```json
   {
     "active": 0,
     "components": [
       {
         "id": 0,
         "name": "MainAssembly",
         "parent": null,
         "bodies": ["BasePlate"],
         "occurrences": []
       },
       {
         "id": 1,
         "name": "Bracket_Comp",
         "parent": 0,
         "bodies": ["Bracket_Left"],
         "occurrences": [{ "id": 1, "name": "Bracket_Comp:1", "parent": 0 }]
       }
     ]
   }
   ```
   $\rightarrow$ Ánh xạ ngược từ `bodyName` xem nó thuộc về `component.id` nào.

4. **`results[3]` (`assembly.bom`)**:
   ```json
   {
     "rows": [
       {
         "item": 1,
         "part_number": "BRK-001",
         "description": "PL6-S275JR",
         "revision": "A",
         "bom_type": "normal",
         "count": 4,
         "components": [1]
       }
     ],
     "total_occurrences": 4
   }
   ```
   $\rightarrow$ Đối chiếu `component.id` vào mảng `"components"` của từng dòng BOM để lấy:
   - `PartNumber`: nếu Component có `part_number` thì lấy `part_number`, nếu rỗng hoặc thuộc Root Component (`id = 0`) thì lấy tên `Body` (`bodyName`).
   - `SoLuong` (`Quantity`): lấy `row.count` (nếu thuộc Root Component `id = 0` không có occurrence thì `SoLuong = 1`).
   - `Description` gốc: lấy `row.description` để kiểm tra đối chiếu với `PL{ChiềuDày}-{VậtLiệu}`.

5. **`results[4]` (`sheet.rules`)**:
   ```json
   {
     "active": "Sheet 2 mm",
     "rules": [
       {
         "rule": { "name": "Sheet 2 mm", "thickness": "2 mm", "k_factor": "0.5", "bend_radius": "1 mm" },
         "values": { "thickness": 2.0, "k_factor": 0.5, "bend_radius": 1.0 }
       }
     ]
   }
   ```
   $\rightarrow$ Dùng để tra cứu chiều dày theo Rule (`values.thickness`) và kiểm tra sai lệch giữa Chiều dày thực tế của tấm với Rule.

---

#### Bước B: Đọc thông tin trải phẳng (`sheet.flat_pattern`) cho từng `SheetBody`
Với danh sách các `bodyName` có `"sheet_metal": true` thu được ở Bước A, gửi batch lệnh:

```json
[
  { "command": "sheet.flat_pattern", "params": { "body": "Bracket_Left" } },
  { "command": "sheet.flat_pattern", "params": { "body": "BasePlate" } }
]
```

**Kết quả trả về của mỗi lệnh `sheet.flat_pattern`:**
```json
{
  "body": "Bracket_Left",
  "rule": "Sheet 6 mm",
  "thickness": 6.0,
  "k_factor": 0.44,
  "flat_size_mm": [250.0, 120.0, 6.0],
  "bends": [
    {
      "bend": 1,
      "angle_deg": 90.0,
      "included_angle_deg": 90.0,
      "radius": 6.0,
      "direction": "up",
      "allowance": 10.5,
      "length": 120.0,
      "start": [50.0, 0.0],
      "end": [50.0, 120.0]
    }
  ],
  "pockets": [
    {
      "side": "bottom",
      "depth_mm": 1.0,
      "z0_mm": 0.0,
      "z1_mm": 1.0
    }
  ]
}
```
- Nếu `sheet.flat_pattern` trả lỗi cho một body $\rightarrow$ gán `HasFlatPatternError = true` cho dòng đó.
- Nếu thành công $\rightarrow$ lấy:
  - `ActualThicknessMm = thickness` (hoặc `flat_size_mm[2]`).
  - Đếm số đường chấn `direction == "up"` và `direction == "down"`, cộng tổng chiều dài đường chấn `up` vs `down`, và kiểm tra `pockets` xem có rãnh khắc ở `"bottom"` hay `"top"` để phục vụ **Auto-Flip**.

---

#### Bước C: Xuất ảnh Thumbnail (Tuỳ chọn) và Xuất file DXF (`sheet.export_dxf`)
1. **Xuất ảnh Thumbnail cho bảng UI (chạy qua `solidcraft-cli snapshot`):**
   ```powershell
   solidcraft-cli.exe snapshot --in "<design.sc>" --out "<temp_thumb_dir>\<bodyName>.png" --width 180 --height 140 --view iso --no-grid --no-axes --tool "<bodyName>"
   ```
   *(Cờ `--tool "<bodyName>"` tô màu nổi bật đúng body đó và làm trong suốt các chi tiết xung quanh).*

2. **Xuất nội dung DXF chuẩn CNC cho từng `SheetBody`:**
   Gửi lệnh:
   ```json
   {
     "command": "sheet.export_dxf",
     "params": {
       "body": "Bracket_Left",
       "curves": "arcs",
       "text": "etch",
       "bend_notes": false,
       "bend_lines": true,
       "layers": "roles"
     }
   }
   ```
   - `"curves": "arcs"`: Tự động chuyển toàn bộ Spline/Ellipse tự do thành cung tròn (`ARC`) và đoạn thẳng (`LINE`) với sai số `0.001 mm` (tương đương `SimplifySplines=True` trên Inventor).
   - Kết quả trả về chứa trường `"dxf"` (chuỗi nội dung file AutoCAD 2000 DXF) hoặc nếu truyền thêm `"path": "<sFullDxfPath>"` thì SolidCraft tự ghi thẳng file `.dxf` xuống ổ cứng!

---

## 3. ĐỒNG BỘ LAYER & LẬT MẶT TRẢI PHẲNG (AUTO-FLIP) CHO SOLIDCRAFT

### 3.1. Bảng ánh xạ Layer từ SolidCraft sang chuẩn `DxfPostProcessor`
SolidCraft xuất DXF với tên layer riêng (`OUTLINE`, `CUT`, `BEND_UP`, `BEND_DOWN`, `ETCH`...). Để tái sử dụng **100%** `DxfPostProcessor.cs` và `LayerConfigService.cs` (màn hình "Cấu hình Layer" quen thuộc của người dùng), ngay khi nhận DXF từ SolidCraft, chúng ta thực hiện bước chuẩn hoá tên Layer (`NormalizeSolidCraftLayers`):

| Tên Layer gốc của SolidCraft | Đổi sang tên Layer chuẩn trong Add-in | Ý nghĩa kỹ thuật |
| :--- | :--- | :--- |
| `OUTLINE` | `IV_OUTER_PROFILE` | Biên dạng cắt ngoài cùng của tấm |
| `CUT`, `TEXT`, `PERFORATION` | `IV_INTERIOR_PROFILES` | Các lỗ cắt bên trong tấm |
| `BEND_UP` | `IV_BEND` | Đường tâm chấn dương (Up) |
| `BEND_DOWN` | `IV_BEND_DOWN` | Đường tâm chấn âm (Down) |
| `ETCH`, `POCKET_TOP_...` | `IV_MARK_SURFACE` | Đường khắc laser / hạ bậc mặt trên |
| `POCKET_BOTTOM_...` | `IV_MARK_SURFACE_BACK` | Đường khắc laser / hạ bậc mặt dưới (mặt ẩn) |
| `BEND_NOTES` | `IV_TANGENT` (hoặc giữ riêng) | Ghi chú chấn |

Nhờ bước ánh xạ này:
- Hàm `ProcessBendLines(doc, "IV_BEND_DOWN")` và `ProcessBendLines(doc, "IV_BEND")` trong `DxfPostProcessor.cs` sẽ **tự động cắt các đường tâm chấn dài > 100 mm thành 2 đoạn 50 mm ở 2 đầu**!
- Bảng màu ACI và tuỳ chọn xoá layer trong `LayerConfigWindow` hoạt động **nguyên vẹn 100%**!

### 3.2. Logic Tự động lật mặt trải phẳng (`SolidCraftFlipService.cs`)
Trên Inventor, chúng ta gọi `FlipBaseFace()` trong môi trường 3D. Trên SolidCraft, vì bản trải phẳng là hình chiếu 2D phẳng tuyệt đối trên mặt phẳng XY, phép lật mặt (`FlipBaseFace`) tương đương toán học 100% với **phép lấy đối xứng gương (Mirror) qua trục đứng $X = X_{\text{center}}$ trên bản vẽ DXF 2D** kèm theo **đảo chiều đường chấn và mặt khắc**:

1. **Quy tắc quyết định có lật (Flip) hay không (giống hệt iLogic / `FlatPatternFlipService`):**
   - **Trường hợp 1 (Có rãnh khắc / Pocket nhưng không có đường chấn):**
     - Nếu có entity trên layer `IV_MARK_SURFACE_BACK` (`POCKET_BOTTOM_...`) mà không có trên `IV_MARK_SURFACE` $\rightarrow$ **CẦN LẬT (`shouldFlip = true`)** để đưa vết khắc lên mặt trên cho máy laser khắc.
   - **Trường hợp 2 (Có đường chấn Bend, không có rãnh khắc):**
     - So sánh tổng số lượng (hoặc tổng chiều dài) của `BEND_DOWN` so với `BEND_UP`.
     - Nếu `downCount > upCount` $\rightarrow$ **CẦN LẬT (`shouldFlip = true`)** để ưu tiên chấn dương hướng lên trên.
2. **Thực thi phép lật trực tiếp trên `DxfDocument` (`netDxf`):**
   - Với mọi entity trong `doc.Entities.All`:
     - Lật toạ độ $X' = -X$:
       - `Line`: $(x_1, y_1) \rightarrow (-x_1, y_1)$, $(x_2, y_2) \rightarrow (-x_2, y_2)$.
       - `Circle`: tâm $(c_x, c_y) \rightarrow (-c_x, c_y)$.
       - `Arc`: tâm $(c_x, c_y) \rightarrow (-c_x, c_y)$; góc `StartAngle` và `EndAngle` đổi thành:
         $$\text{newStart} = (180^\circ - \text{oldEnd}) \pmod{360^\circ}, \quad \text{newEnd} = (180^\circ - \text{oldStart}) \pmod{360^\circ}$$
       - `Polyline2D`: mỗi đỉnh $(x, y) \rightarrow (-x, y)$ và đảo dấu độ phình cung `vertex.Bulge = -vertex.Bulge`.
       - `Ellipse` / `Spline`: lật dấu hoành độ $X$ của tâm, trục chính (`MajorAxis`) và các điểm điều khiển (`ControlPoints`).
     - Hoán đổi Layer:
       - `IV_BEND` $\leftrightarrow$ `IV_BEND_DOWN`
       - `IV_MARK_SURFACE` $\leftrightarrow$ `IV_MARK_SURFACE_BACK`

---

## 4. HƯỚNG DẪN THỰC HIỆN TỪNG BƯỚC CHO AI (BƯỚC 1 → BƯỚC 7)

---

### BƯỚC 1: Khởi tạo cấu trúc thư mục và Solution tại `d:\05_MyCode\SolidCraftDxfAddIn`

1. Tạo thư mục gốc và các thư mục con:
   ```powershell
   New-Item -ItemType Directory -Force -Path "d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Models"
   New-Item -ItemType Directory -Force -Path "d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Services"
   New-Item -ItemType Directory -Force -Path "d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\ViewModels"
   New-Item -ItemType Directory -Force -Path "d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Views"
   New-Item -ItemType Directory -Force -Path "d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Helpers"
   New-Item -ItemType Directory -Force -Path "d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Resources"
   ```

2. Tạo file `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\SolidCraftDxfAddIn.csproj`:
   - Đặt `<OutputType>WinExe</OutputType>` và `<TargetFramework>net8.0-windows</TargetFramework>`.
   - **Không** tham chiếu `Autodesk.Inventor.Interop` hay `AssemblyReaderLib_v3`.
   - Giữ các gói NuGet: `HandyControl (3.5.1)`, `netDxf.netstandard (2.4.0)`, `ClosedXML (0.105.0)`, `Microsoft.Data.Sqlite (8.0.11)`.

   Nội dung file `SolidCraftDxfAddIn.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

     <PropertyGroup>
       <OutputType>WinExe</OutputType>
       <TargetFramework>net8.0-windows</TargetFramework>
       <UseWPF>true</UseWPF>
       <UseWindowsForms>true</UseWindowsForms>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <Platforms>AnyCPU;x64</Platforms>
       <PlatformTarget>x64</PlatformTarget>
       <AssemblyName>SolidCraftDxfAddIn</AssemblyName>
       <RootNamespace>ExportDxfAddIn</RootNamespace>
     </PropertyGroup>

     <ItemGroup>
       <PackageReference Include="ClosedXML" Version="0.105.0" />
       <PackageReference Include="HandyControl" Version="3.5.1" />
       <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.11" />
       <PackageReference Include="netDxf.netstandard" Version="2.4.0" />
     </ItemGroup>

   </Project>
   ```
   *(Mẹo quan trọng: Giữ `<RootNamespace>ExportDxfAddIn</RootNamespace>` để toàn bộ các file XAML, Models, Services, ViewModels copy từ dự án cũ sang biên dịch được ngay mà không phải sửa namespace trên từng file XAML!).*

3. Tạo Solution tại `d:\05_MyCode\SolidCraftDxfAddIn`:
   ```powershell
   cd d:\05_MyCode\SolidCraftDxfAddIn
   dotnet new sln -n SolidCraftDxfAddIn
   dotnet sln add SolidCraftDxfAddIn\SolidCraftDxfAddIn.csproj
   ```

---

### BƯỚC 2: Sao chép các thành phần dùng chung từ `d:\05_MyCode\Inventor\ExportDxfAddIn`

Copy nguyên vẹn các file không phụ thuộc Inventor API từ `d:\05_MyCode\Inventor\ExportDxfAddIn` sang `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn`:

1. **Models:**
   - `Models\ExportRow.cs`
   - `Models\ExportedItem.cs`
   - `Models\ExportHistoryEntry.cs`
   - `Models\LayerSettingItem.cs`
2. **Services (Tái sử dụng 100%):**
   - `Services\DxfPostProcessor.cs` *(đã bao gồm nâng cấp gộp file theo Chiều dày + Vật liệu)*
   - `Services\LayerConfigService.cs`
   - `Services\ExcelReportService.cs`
   - `Services\DxfHistoryService.cs`
   - `Services\LocalizationService.cs`
3. **Helpers & Resources:**
   - Toàn bộ thư mục `Helpers\` và `Resources\` (nếu có).
4. **ViewModels & Views:**
   - `ViewModels\LayerConfigViewModel.cs`
   - `Views\LayerConfigWindow.xaml` & `Views\LayerConfigWindow.xaml.cs`
   - `Views\DxfBatchPromptWindow.xaml` & `Views\DxfBatchPromptWindow.xaml.cs`
   - `Views\MainWindow.xaml` & `Views\MainWindow.xaml.cs`

---

### BƯỚC 3: Tạo `Models/SolidCraftModels.cs` (Cấu trúc dữ liệu hứng JSON từ SolidCraft)

Tạo file `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Models\SolidCraftModels.cs` để chứa kết quả đọc được từ SolidCraft:

```csharp
using System.Collections.Generic;

namespace ExportDxfAddIn.Models
{
    /// <summary>
    /// Thông tin tổng hợp của một chi tiết kim loại tấm đọc từ SolidCraft (qua CLI hoặc Control Channel).
    /// </summary>
    public class SolidCraftSheetItem
    {
        public string BodyName { get; set; } = string.Empty;
        public ulong ComponentId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string DescriptionProp { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public string MaterialName { get; set; } = "Steel";
        public string RuleName { get; set; } = string.Empty;
        public double ActualThicknessMm { get; set; }
        public double RuleThicknessMm { get; set; }
        public double FlatWidthMm { get; set; }
        public double FlatHeightMm { get; set; }
        public int BendCount { get; set; }
        public int BendUpCount { get; set; }
        public int BendDownCount { get; set; }
        public double BendUpTotalLength { get; set; }
        public double BendDownTotalLength { get; set; }
        public bool HasTopPocketOrEtch { get; set; }
        public bool HasBottomPocket { get; set; }
        public bool HasFlatPatternError { get; set; }
        public string FlatPatternErrorMessage { get; set; } = string.Empty;
        public bool ThicknessRuleMismatch { get; set; }
        public string ThicknessRuleMismatchDetail { get; set; } = string.Empty;
        public bool DescriptionMismatch { get; set; }
        public string? ThumbnailPath { get; set; }
        public string SourceDesignPath { get; set; } = string.Empty;
    }

    public class SolidCraftLoadResult
    {
        public string DesignName { get; set; } = string.Empty;
        public string DesignPath { get; set; } = string.Empty;
        public bool IsFromLiveApp { get; set; }
        public List<SolidCraftSheetItem> SheetItems { get; set; } = new();
    }
}
```

---

### BƯỚC 4: Xây dựng `Services/SolidCraftCliService.cs` (Giao tiếp `solidcraft-cli.exe` & TCP Control Channel)

Tạo file `d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn\Services\SolidCraftCliService.cs` thực hiện các nhiệm vụ:

1. **Dò tìm `solidcraft-cli.exe` (`FindCliExecutablePath()`):**
   - Kiểm tra đường dẫn cấu hình hoặc `C:\Program Files\SolidCraft\solidcraft-cli.exe`, `%LOCALAPPDATA%\Programs\SolidCraft\solidcraft-cli.exe`, hoặc tìm trong biến `PATH`.
2. **Dò tìm cổng và token của cửa sổ SolidCraft đang mở (`TryGetLiveControlEndpoint(out int port, out string token)`):**
   - Kiểm tra thư mục `%LOCALAPPDATA%\SolidCraft\control\`.
   - Đọc `current.port` $\rightarrow$ lấy `port` (mặc định `27878`).
   - Đọc `<port>.token` $\rightarrow$ lấy `token` (chuỗi 64 ký tự hex).
3. **Hàm thực thi Batch lệnh qua TCP Control Channel (`ExecuteBatchLiveAsync`):**
   - Kết nối `TcpClient("127.0.0.1", port)`.
   - Gửi dòng JSON:
     ```json
     {"id": 1, "method": "engine.batch", "params": {"commands": [...], "rollback": false}, "token": "..."}
     ```
   - Đọc dòng JSON trả về, kiểm tra `"ok": true` và lấy mảng `result.results`.
4. **Hàm thực thi Batch lệnh qua `solidcraft-cli.exe run` (`ExecuteBatchHeadlessAsync`):**
   - Ghi mảng lệnh ra file tạm `Path.Combine(Path.GetTempPath(), $"sc_script_{Guid.NewGuid():N}.json")`.
   - Chạy tiến trình `ProcessStartInfo`:
     - `FileName = cliPath`
     - `Arguments = $"run \"{scriptPath}\" --in \"{designFilePath}\" --results --json-out \"{outJsonPath}\" --allow-errors"`
     - `CreateNoWindow = true`, `UseShellExecute = false`
   - Đọc kết quả từ `outJsonPath` và xoá file tạm trong khối `finally`.
5. **Hàm tổng hợp dữ liệu BOM & Sheet Metal (`LoadDesignAsync(string? designFilePath, bool useLiveApp, bool captureThumbnails)`):**
   - Chạy **Bước A** (5 lệnh: `inspect.measure`, `document.inspect`, `component.list`, `assembly.bom`, `sheet.rules`).
   - Lọc các body có `"sheet_metal": true` trong kết quả `inspect.measure`.
   - Chạy **Bước B** (gọi `sheet.flat_pattern` cho từng sheet body).
   - Tính toán `ActualThicknessMm`, `RuleThicknessMm`, kiểm tra chéo với `Description` (dùng cùng thuật toán `FormatThicknessMm` và kiểm tra lệch `ThicknessRuleMismatch`, `DescriptionMismatch` như trong `AssemblyReader.cs` của dự án Inventor).
   - Nếu `captureThumbnails == true` và có `designFilePath` + `cliPath`: gọi `solidcraft-cli snapshot` tạo ảnh PNG thumbnail cho từng sheet body.
6. **Hàm xuất chuỗi DXF của một `SheetBody` (`ExportSheetBodyDxfAsync`):**
   - Gọi lệnh `sheet.export_dxf` với tham số:
     ```json
     {
       "body": "<bodyName>",
       "curves": "arcs",
       "text": "etch",
       "bend_notes": false,
       "bend_lines": true,
       "layers": "roles"
     }
     ```
   - Nhận chuỗi nội dung DXF từ trường `"dxf"` trong JSON kết quả và ghi ra file `.dxf` đích.

---

### BƯỚC 5: Xây dựng `Services/SolidCraftFlipService.cs` & `Services/SolidCraftDxfExportManager.cs`

#### 5.1. `Services/SolidCraftFlipService.cs`
Thực hiện:
1. **`NormalizeSolidCraftLayers(DxfDocument doc)`**:
   - Đổi tên layer của từng entity trong `doc.Entities.All`:
     - `"OUTLINE"` $\rightarrow$ `"IV_OUTER_PROFILE"`
     - `"CUT"`, `"TEXT"`, `"PERFORATION"` $\rightarrow$ `"IV_INTERIOR_PROFILES"`
     - `"BEND_UP"` $\rightarrow$ `"IV_BEND"`
     - `"BEND_DOWN"` $\rightarrow$ `"IV_BEND_DOWN"`
     - `"ETCH"`, `"POCKET_TOP_*"`, `"POCKET_INTERNAL_*"` $\rightarrow$ `"IV_MARK_SURFACE"`
     - `"POCKET_BOTTOM_*"` $\rightarrow$ `"IV_MARK_SURFACE_BACK"`
     - `"BEND_NOTES"` $\rightarrow$ `"IV_TANGENT"`
2. **`FlipIfNeeded(DxfDocument doc, SolidCraftSheetItem item)`**:
   - Kiểm tra điều kiện lật:
     - Có rãnh khắc mặt dưới (`item.HasBottomPocket && !item.HasTopPocketOrEtch && item.BendCount == 0`) $\rightarrow$ Lật.
     - Hoặc không có rãnh khắc nhưng `item.BendDownCount > item.BendUpCount` (hoặc khi số lượng bằng nhau mà `item.BendDownTotalLength > item.BendUpTotalLength`) $\rightarrow$ Lật.
   - Khi lật:
     - Gọi `entity.TransformBy(Matrix3.Scale(-1.0, 1.0, 1.0), Vector3.Zero)` của `netDxf` (hỗ trợ chuẩn xác mọi loại entity từ `Line`, `Arc`, `Circle`, `Polyline2D`, `Ellipse` đến `Spline`!).
     - Hoán đổi layer `IV_BEND` $\leftrightarrow$ `IV_BEND_DOWN` và `IV_MARK_SURFACE` $\leftrightarrow$ `IV_MARK_SURFACE_BACK`.
     - Trả về chuỗi ghi chú `"Flipped (BendDown > BendUp)"` hoặc `"Flipped (Bottom Pocket/Etch)"`.

#### 5.2. `Services/SolidCraftDxfExportManager.cs`
Thay thế cho `DxfExportManager.cs` của Inventor:
- Nhận danh sách `List<ExportRow>` đã được người dùng tích chọn.
- Với mỗi dòng:
  1. Gọi `SolidCraftCliService.ExportSheetBodyDxfAsync(...)` để lấy nội dung DXF thô từ SolidCraft.
  2. Nạp vào `DxfDocument` (`netDxf`), gọi `NormalizeSolidCraftLayers(doc)`.
  3. Nếu `isFlip == true`, gọi `SolidCraftFlipService.FlipIfNeeded(doc, item)`.
  4. Lưu file `.dxf` theo đúng quy ước tên: `{PartNumber}_{SoLuong}_PL{ChieuDay}-{VatLieu}.dxf`.
  5. Gọi `DxfPostProcessor`:
     - Nếu `isCombine == true`: gọi `postProcessor.InsertTextIntoDxf(sFullDxfPath, sPartNum, sDesc, qty, 30, 45, 15)`.
     - Nếu `isCombine == false`: gọi `postProcessor.ProcessSingleFileNoCombine(sFullDxfPath, sPartNum, 15)`.
- Sau khi xuất xong toàn bộ các dòng:
  - Ghi lịch sử vào SQLite (`DxfHistoryService`).
  - Nếu `isCombine == true`: gọi `postProcessor.CombineDxfFiles(saveDir, logOK, 1000)` — tự động phân cột theo cặp **`PL{ChiềuDày}-{VậtLiệu}`** (sắp xếp chiều dày tăng dần $\rightarrow$ tên vật liệu A-Z).
  - Nếu `isExportExcel == true`: gọi `new ExcelReportService().CreateExcelReport(...)`.

---

### BƯỚC 6: Cập nhật Giao diện (`MainWindow.xaml`) và `MainViewModel.cs`

1. **Trong `App.xaml` & `App.xaml.cs`:**
   - Khởi tạo `MainWindow` khi chạy ứng dụng (`StartupUri="Views/MainWindow.xaml"`).
   - Cho phép nhận tham số dòng lệnh (ví dụ truyền đường dẫn file `.sc`: `SolidCraftDxfAddIn.exe "D:\Designs\TrayAssembly.sc"` thì tự động mở luôn file đó).
2. **Trên thanh công cụ đầu cửa sổ `MainWindow.xaml`:**
   - Bổ sung thanh chọn nguồn dữ liệu SolidCraft (nhỏ gọn, hiện đại ở phía trên bảng):
     - Nút **"Kết nối SolidCraft đang mở (Live)"**: tự động đọc từ cổng `27878` của cửa sổ SolidCraft đang chạy.
     - Nút **"Mở file thiết kế (.sc / .step)..."**: mở `OpenFileDialog` chọn file `.sc`, `.solidcraft`, `.step`, `.stp` để quét bằng `solidcraft-cli.exe` ở chế độ Headless.
     - Nút **"Cấu hình đường dẫn CLI"**: cho phép chọn đường dẫn `solidcraft-cli.exe` nếu cài ở thư mục tuỳ chỉnh.
3. **Trong `MainViewModel.cs`:**
   - Thay thế lời gọi `AssemblyReader` và `Inventor.Application` bằng `SolidCraftCliService` và `SolidCraftDxfExportManager`.
   - Giữ nguyên toàn bộ tính năng:
     - Gộp dòng theo `PartNumber` (`MergeRowsByPartNumber`).
     - Bộ lọc theo cột kiểu Excel (`ChieuDayFilter`, `VatLieuFilter`, tìm kiếm từ khoá).
     - Nút **"Kiểm tra Flat Pattern"** (`ValidateFlatPatterns`).
     - Nút **"Cấu hình Layer"** (`LayerConfigWindow`).
     - Checkbox **"Tự động lật mặt (Flip)"**, **"Gộp file (Combine DXF)"**, **"Xuất báo cáo Excel"**.
     - Chuyển đổi song ngữ **Tiếng Việt / English**.

---

### BƯỚC 7: Biên dịch & Kiểm thử nghiệm thu (Build & Verification)

1. **Chạy lệnh biên dịch:**
   ```powershell
   dotnet build d:\05_MyCode\SolidCraftDxfAddIn\SolidCraftDxfAddIn.sln -c Debug
   ```
   - Yêu cầu kết quả: **`Build succeeded. 0 Error(s)`**.
2. **Kiểm tra tính độc lập:**
   - Chạy `git status` tại `d:\05_MyCode\Inventor` để đảm bảo dự án Inventor cũ không bị thay đổi bất kỳ file code nào.
3. **Kiểm thử luồng xử lý DXF giả lập (Self-Test):**
   - Kiểm tra hàm `NormalizeSolidCraftLayers` và `SolidCraftFlipService` với mẫu DXF đầu ra chuẩn của SolidCraft (gồm các layer `OUTLINE`, `CUT`, `BEND_UP`, `BEND_DOWN`, `ETCH`).
   - Kiểm tra hàm `CombineDxfFiles` tách đúng các cột theo tổ hợp `PL{ChiềuDày}-{VậtLiệu}` (`PL2-STEEL`, `PL2-SUS304`, `PL6-S275JR`).

---

## 5. CHECKLIST NGHIỆM THU DÀNH CHO AI THỰC THI
- [ ] Dự án mới nằm hoàn toàn trong `d:\05_MyCode\SolidCraftDxfAddIn`, biên dịch ra file thực thi `SolidCraftDxfAddIn.exe` (`0 Error(s)`).
- [ ] Hỗ trợ cả **2 chế độ**: đọc trực tiếp từ cửa sổ SolidCraft đang mở (qua `%LOCALAPPDATA%\SolidCraft\control\current.port` + `.token`) và đọc từ file `.sc` / `.step` qua `solidcraft-cli.exe run ... --results --json-out ...`.
- [ ] Ánh xạ đầy đủ các layer của SolidCraft (`OUTLINE`, `CUT`, `BEND_UP`, `BEND_DOWN`, `ETCH`, `POCKET_*`) sang các layer chuẩn để `DxfPostProcessor` cắt đường bend 50 mm, đổi màu ACI và xoá layer thừa.
- [ ] Tự động lật mặt (`SolidCraftFlipService`) bằng `Matrix3.Scale(-1, 1, 1)` và đảo `IV_BEND` $\leftrightarrow$ `IV_BEND_DOWN` khi số đường chấn âm nhiều hơn chấn dương hoặc có khắc ở mặt đáy.
- [ ] Giữ nguyên tính năng gộp file `combine.dxf` phân loại theo cả **Chiều dày và Vật liệu** (`PL{ChiềuDày}-{VậtLiệu}`), xuất báo cáo Excel `.xlsx` và lưu lịch sử SQLite.

