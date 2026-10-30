# Bộ nhớ Dự án (Project Memory)

**⚠️ LUẬT BẮT BUỘC ĐỐI VỚI AI (STRICT RULE):**
1. Sau mỗi phiên làm việc, **bắt buộc** phải ghi chép lại toàn bộ những việc đã làm được và chưa làm được vào file `Tracking.md`. theo dạng "Phiên làm việc XXX (Session) - Ngày dd/mm/yyy"
2. Luôn luôn phải xem lại file `Project.md` để đối chiếu tiến độ và biết chính xác công việc cần thực hiện tiếp theo.
3. Đọc 3 phiên làm việc gần nhất trong file `Tracking.md` để biết tiến độ đã thực hiện
4. File này đóng vai trò là sơ đồ gốc (index) kết nối tới các tài liệu chuyên sâu của dự án để AI và người dùng cùng theo dõi.

## Cây cấu trúc tài liệu Memory

- 📄 [memory.md](./memory.md): File trung tâm này.
- 📄 [Project.md](./Project.md): Tài liệu đặc tả yêu cầu, thiết kế kiến trúc và lộ trình gốc của dự án.
- 📄 [Plan_Option1_RibbonOverlay.md](./Plan_Option1_RibbonOverlay.md): Kế hoạch chi tiết cho Gemini 3.8 Flash triển khai Phương án 1 (Gắn nút bấm lên thanh Ribbon của SolidCraft bằng Win32/WPF Overlay & Hotkey, không cần build lại SolidCraft).
- 📄 [Plan_Option3_NativeRustBuild.md](./Plan_Option3_NativeRustBuild.md): Kế hoạch chi tiết cho Gemini 3.8 Flash triển khai Phương án 3 (Thêm nút `Batch Export DXF` trực tiếp vào mã nguồn Rust `toolbar.rs` của SolidCraft tab `SHEET METAL` và build lại `solidcraft.exe`).
- 📄 [Plan_Fix_LinkedSheetMetal.md](./Plan_Fix_LinkedSheetMetal.md): Kế hoạch fix lỗi Add-in không nhận diện body Sheet Metal đến từ file `.sc` được Insert/Link (body bị lưu dạng STEP blob) + lỗi CLI dừng cả batch khi 1 step lỗi.
- 📄 [Plan_GroupIdenticalBodies.md](./Plan_GroupIdenticalBodies.md): Kế hoạch chi tiết cho Gemini 3.8 Flash nâng cấp thuật toán tự động gom nhóm & cộng dồn số lượng các body sinh ra từ lệnh Copy/Pattern (`P-1 (2)` → `P-1`, `Px-1 (2),(3)` → `Px-1`) và lọc bỏ World Body của Occurrence phụ trong SolidCraft.
- 📄 [Tracking.md](./Tracking.md): Nhật ký tiến độ các phiên làm việc và các tính năng đã hoàn thành.
- 📄 [issues.md](./issues.md): Ghi chép các vấn đề kỹ thuật, giới hạn công nghệ đã gặp phải và cách giải quyết (VD: vấn đề bảng trong bản vẽ CAD).
- 📄 [architecture.md](./architecture.md): Cây thư mục mã nguồn và giải thích chi tiết chức năng của từng file code hiện có.


*Lưu ý: Bất kỳ cập nhật lớn nào về kiến trúc hoặc lỗi phát sinh trong tương lai sẽ được ghi chép vào các file con tương ứng.*
