using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ExportDxfAddIn.Models;

namespace ExportDxfAddIn.Services
{
    /// <summary>
    /// Lịch sử "Dxf batch No" cho từng assembly tổng: 1 file SQLite riêng, đặt trong đúng
    /// folder của assembly tổng đang mở (assembly hiện hành khi add-in load, KHÔNG phải
    /// sub-assembly con), đặt tên theo PartNumber của chính assembly đó để không lẫn khi
    /// nhiều assembly tổng chung 1 folder, và không mất liên kết khi đổi tên file .iam.
    ///
    /// Nguyên tắc ghi: append-only — mỗi lần Export chỉ INSERT thêm dòng mới, không bao giờ
    /// UPDATE/DELETE dòng cũ (xem thảo luận thiết kế, không hỗ trợ "sửa lỗi chính tả" lịch sử).
    /// BatchNo rỗng là trạng thái hợp lệ ("chưa gán batch"), không tự sinh mã thay thế.
    /// </summary>
    public class DxfHistoryService
    {
        public string GetDbPath(string assemblyFolderPath, string assemblyPartNumber)
        {
            string safeName = SanitizeForFileName(assemblyPartNumber);
            return Path.Combine(assemblyFolderPath, $"KHONG_XOA_{safeName}.dxfhistory.db");
        }

        /// <summary>Trả về BatchNo gần nhất theo từng PartNumber (dùng để tự điền cột "Dxf batch No"
        /// khi mở lại Assembly). Không throw ra ngoài — DB thiếu/lỗi thì coi như chưa có lịch sử.</summary>
        public Dictionary<string, string> GetLatestBatchNoByPartNumber(string dbPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dbPath)) return result;

            try
            {
                using var conn = OpenConnection(dbPath, createIfMissing: false);
                if (conn == null) return result;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT PartNumber, BatchNo FROM (
                        SELECT PartNumber, BatchNo,
                               ROW_NUMBER() OVER (PARTITION BY PartNumber ORDER BY ExportedAtUtc DESC, Id DESC) AS rn
                        FROM ExportHistory
                    ) WHERE rn = 1;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string pn = reader.GetString(0);
                    string batchNo = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    if (!string.IsNullOrEmpty(pn))
                    {
                        result[pn] = batchNo;
                    }
                }
            }
            catch
            {
                // DB lỗi/khoá tạm thời (vd đang đồng bộ Google Drive) -> coi như chưa có lịch sử,
                // không chặn việc mở Assembly.
            }

            return result;
        }

        /// <summary>Ghi thêm (append) 1 lô bản ghi lịch sử trong 1 transaction. Ném exception ra ngoài
        /// để caller (DxfExportManager/MainViewModel) tự quyết định cách báo cho người dùng — không
        /// được để lỗi ở đây làm mất kết quả DXF đã xuất thành công trước đó.</summary>
        public void AppendExportRecords(string dbPath, IEnumerable<ExportHistoryEntry> entries)
        {
            var list = entries.ToList();
            if (list.Count == 0) return;

            using var conn = OpenConnection(dbPath, createIfMissing: true)
                ?? throw new InvalidOperationException($"Không mở được database lịch sử: {dbPath}");

            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO ExportHistory
                    (ExportSessionId, BatchNo, ExportedAtUtc, PartNumber, Revision, Quantity, Description, FileName, MachineName, UserName, AssemblyFileName)
                VALUES
                    ($sessionId, $batchNo, $exportedAt, $partNumber, $revision, $quantity, $description, $fileName, $machineName, $userName, $assemblyFileName);";

            var pSessionId = cmd.Parameters.Add("$sessionId", SqliteType.Text);
            var pBatchNo = cmd.Parameters.Add("$batchNo", SqliteType.Text);
            var pExportedAt = cmd.Parameters.Add("$exportedAt", SqliteType.Text);
            var pPartNumber = cmd.Parameters.Add("$partNumber", SqliteType.Text);
            var pRevision = cmd.Parameters.Add("$revision", SqliteType.Text);
            var pQuantity = cmd.Parameters.Add("$quantity", SqliteType.Integer);
            var pDescription = cmd.Parameters.Add("$description", SqliteType.Text);
            var pFileName = cmd.Parameters.Add("$fileName", SqliteType.Text);
            var pMachineName = cmd.Parameters.Add("$machineName", SqliteType.Text);
            var pUserName = cmd.Parameters.Add("$userName", SqliteType.Text);
            var pAssemblyFileName = cmd.Parameters.Add("$assemblyFileName", SqliteType.Text);

            foreach (var e in list)
            {
                pSessionId.Value = e.ExportSessionId;
                // BatchNo rỗng lưu là NULL thật (không phải chuỗi "") để phân biệt rõ "chưa gán batch"
                // khỏi 1 chuỗi rỗng do lỗi dữ liệu, và để tab Lịch sử dùng IS NULL/IFNULL nhất quán.
                pBatchNo.Value = string.IsNullOrWhiteSpace(e.BatchNo) ? (object)DBNull.Value : e.BatchNo;
                pExportedAt.Value = e.ExportedAtUtc.ToString("O", CultureInfo.InvariantCulture);
                pPartNumber.Value = e.PartNumber ?? "";
                pRevision.Value = e.Revision ?? "";
                pQuantity.Value = e.Quantity;
                pDescription.Value = e.Description ?? "";
                pFileName.Value = e.FileName ?? "";
                pMachineName.Value = e.MachineName ?? "";
                pUserName.Value = e.UserName ?? "";
                pAssemblyFileName.Value = e.AssemblyFileName ?? "";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>Toàn bộ lịch sử, mới nhất trước, cho tab "Lịch sử xuất".</summary>
        public List<ExportHistoryEntry> GetHistory(string dbPath)
        {
            var result = new List<ExportHistoryEntry>();
            if (!File.Exists(dbPath)) return result;

            try
            {
                using var conn = OpenConnection(dbPath, createIfMissing: false);
                if (conn == null) return result;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT Id, ExportSessionId, BatchNo, ExportedAtUtc, PartNumber, Revision, Quantity, Description, FileName, MachineName, UserName, AssemblyFileName
                    FROM ExportHistory
                    ORDER BY ExportedAtUtc DESC, Id DESC;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new ExportHistoryEntry
                    {
                        Id = reader.GetInt64(0),
                        ExportSessionId = reader.GetString(1),
                        BatchNo = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        ExportedAtUtc = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        PartNumber = reader.GetString(4),
                        Revision = reader.GetString(5),
                        Quantity = reader.GetInt32(6),
                        Description = reader.GetString(7),
                        FileName = reader.GetString(8),
                        MachineName = reader.GetString(9),
                        UserName = reader.GetString(10),
                        AssemblyFileName = reader.GetString(11),
                    });
                }
            }
            catch
            {
                // DB lỗi/khoá tạm thời -> trả về danh sách rỗng, tab Lịch sử tự hiện trạng thái trống.
            }

            return result;
        }

        /// <summary>Mở kết nối + đảm bảo bảng ExportHistory tồn tại. Nếu file DB bị hỏng (không phải
        /// SQLite hợp lệ, ví dụ do xung đột ghi qua Google Drive sync), đổi tên file cũ sang
        /// ".corrupted_{timestamp}" rồi tạo file mới, thay vì làm crash toàn bộ tính năng.</summary>
        private SqliteConnection? OpenConnection(string dbPath, bool createIfMissing)
        {
            if (!createIfMissing && !File.Exists(dbPath)) return null;

            try
            {
                return OpenAndEnsureSchema(dbPath);
            }
            catch (SqliteException)
            {
                try
                {
                    string corruptedPath = $"{dbPath}.corrupted_{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Move(dbPath, corruptedPath);
                }
                catch
                {
                    // Không đổi tên được (vd đang bị khoá) -> đành chịu, ném lỗi gốc lên cho caller.
                    throw;
                }

                if (!createIfMissing) return null;
                return OpenAndEnsureSchema(dbPath);
            }
        }

        private static SqliteConnection OpenAndEnsureSchema(string dbPath)
        {
            // Pooling=false: đóng connection là đóng hẳn handle file ngay lập tức (không giữ native
            // handle nền để tái dùng). Quan trọng vì file DB nằm trong folder đồng bộ Google Drive —
            // muốn nhả file càng sớm càng tốt sau mỗi lần đọc/ghi, tránh cản trở Drive sync.
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
            var conn = new SqliteConnection(connStr);
            try
            {
                conn.Open();

                using (var pragmaCmd = conn.CreateCommand())
                {
                    // Tránh sinh file -wal/-shm phụ (mặc định SQLite hiện đại dùng WAL) — file rời rạc
                    // như vậy dễ gây rối khi đồng bộ qua Google Drive (không có file-locking thật).
                    pragmaCmd.CommandText = "PRAGMA journal_mode=DELETE;";
                    pragmaCmd.ExecuteNonQuery();
                }

                using (var createCmd = conn.CreateCommand())
                {
                    createCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ExportHistory (
                        Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                        ExportSessionId  TEXT NOT NULL,
                        BatchNo          TEXT,
                        ExportedAtUtc    TEXT NOT NULL,
                        PartNumber       TEXT NOT NULL,
                        Revision         TEXT NOT NULL DEFAULT '',
                        Quantity         INTEGER NOT NULL DEFAULT 0,
                        Description      TEXT NOT NULL DEFAULT '',
                        FileName         TEXT NOT NULL DEFAULT '',
                        MachineName      TEXT NOT NULL DEFAULT '',
                        UserName         TEXT NOT NULL DEFAULT '',
                        AssemblyFileName TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_exporthistory_partnumber ON ExportHistory(PartNumber, ExportedAtUtc DESC);";
                    createCmd.ExecuteNonQuery();
                }

                return conn;
            }
            catch
            {
                // Phải đóng hẳn connection trước khi ném lỗi ra ngoài — nếu không, caller (vd logic
                // đổi tên file hỏng sang .corrupted_*) sẽ đụng lỗi "file đang bị dùng bởi tiến trình
                // khác" vì handle native sqlite3 vẫn còn mở trên chính file cần đổi tên.
                conn.Dispose();
                throw;
            }
        }

        /// <summary>Thay ký tự không hợp lệ trong tên file bằng "_" — cùng cách DxfExportManager
        /// đang làm với tên file DXF, áp dụng cho PartNumber dùng làm tên file DB.</summary>
        public static string SanitizeForFileName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "UnknownAssembly";

            string clean = raw;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                clean = clean.Replace(c.ToString(), "_");
            }
            return clean;
        }
    }
}
