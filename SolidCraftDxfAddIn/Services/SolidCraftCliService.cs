using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExportDxfAddIn.Models;

namespace ExportDxfAddIn.Services
{
    public class SolidCraftBatchStepException : Exception
    {
        public int StepIndex { get; }
        public string CommandName { get; }
        public SolidCraftBatchStepException(int stepIndex, string commandName, string message)
            : base(message)
        {
            StepIndex = stepIndex;
            CommandName = commandName;
        }
    }

    public class UpdateLinksResult
    {
        public List<string> UpdatedComponents { get; set; } = new();
        public List<string> UnchangedComponents { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string? BackupPath { get; set; }
    }

    public class SolidCraftSettings
    {
        public string? CliPath { get; set; }

        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SolidCraftDxfAddIn",
            "settings.json"
        );

        public static SolidCraftSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<SolidCraftSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new SolidCraftSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }

    public class SolidCraftCliService
    {
        private static SolidCraftCliService? _instance;
        public static SolidCraftCliService Instance => _instance ??= new SolidCraftCliService();

        private readonly SolidCraftSettings _settings;

        public SolidCraftCliService()
        {
            _settings = SolidCraftSettings.Load();
        }

        public string? CustomCliPath
        {
            get => _settings.CliPath;
            set
            {
                _settings.CliPath = value;
                _settings.Save();
            }
        }

        /// <summary>
        /// Dò tìm đường dẫn file thực thi solidcraft-cli.exe theo thứ tự ưu tiên:
        /// 1. Cài đặt người dùng trong settings.json
        /// 2. C:\Program Files\SolidCraft\solidcraft-cli.exe
        /// 3. %LOCALAPPDATA%\Programs\SolidCraft\solidcraft-cli.exe
        /// 4. Biến môi trường PATH
        /// </summary>
        public string? FindCliExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(_settings.CliPath) && File.Exists(_settings.CliPath))
            {
                return _settings.CliPath;
            }

            var defaultProgFiles = @"C:\Program Files\SolidCraft\solidcraft-cli.exe";
            if (File.Exists(defaultProgFiles))
            {
                return defaultProgFiles;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var defaultLocalAppData = Path.Combine(localAppData, "Programs", "SolidCraft", "solidcraft-cli.exe");
            if (File.Exists(defaultLocalAppData))
            {
                return defaultLocalAppData;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in paths)
                {
                    try
                    {
                        var candidate = Path.Combine(p.Trim(), "solidcraft-cli.exe");
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        /// <summary>
        /// Kiểm tra xem có phiên SolidCraft Desktop nào đang chạy và mở Control Channel không.
        /// </summary>
        public bool TryGetLiveControlEndpoint(out int port, out string token)
        {
            port = 27878;
            token = string.Empty;

            try
            {
                var controlDir = Environment.GetEnvironmentVariable("SOLIDCRAFT_CONTROL_DIR");
                if (string.IsNullOrWhiteSpace(controlDir))
                {
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    controlDir = Path.Combine(localAppData, "SolidCraft", "control");
                }

                if (!Directory.Exists(controlDir)) return false;

                var portFile = Path.Combine(controlDir, "current.port");
                if (File.Exists(portFile))
                {
                    var portText = File.ReadAllText(portFile).Trim();
                    if (int.TryParse(portText, out var parsedPort))
                    {
                        port = parsedPort;
                    }
                }

                var tokenFile = Path.Combine(controlDir, $"{port}.token");
                if (File.Exists(tokenFile))
                {
                    token = File.ReadAllText(tokenFile).Trim();
                    return !string.IsNullOrWhiteSpace(token);
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Thực thi danh sách lệnh qua TCP Control Channel trực tiếp tới SolidCraft đang mở.
        /// </summary>
        public async Task<JsonArray> ExecuteBatchLiveAsync(int port, string token, IEnumerable<object> commands, CancellationToken ct = default)
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            var reqObj = new JsonObject
            {
                ["id"] = 1,
                ["method"] = "engine.batch",
                ["params"] = new JsonObject
                {
                    ["commands"] = JsonSerializer.SerializeToNode(commands),
                    ["rollback"] = false
                },
                ["token"] = token
            };

            var reqLine = reqObj.ToJsonString();
            await writer.WriteLineAsync(reqLine.AsMemory(), ct).ConfigureAwait(false);

            var respLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(respLine))
            {
                throw new InvalidOperationException("SolidCraft Live Control Channel did not return any response.");
            }

            var respNode = JsonNode.Parse(respLine);
            if (respNode is not JsonObject respObj)
            {
                throw new InvalidOperationException($"Invalid JSON response: {respLine}");
            }

            if (respObj["ok"]?.GetValue<bool>() != true)
            {
                var err = respObj["error"]?.ToJsonString() ?? respLine;
                throw new InvalidOperationException($"SolidCraft Live error: {err}");
            }

            var resultsNode = respObj["result"]?["results"] as JsonArray;
            return resultsNode ?? new JsonArray();
        }

        /// <summary>
        /// Thực thi danh sách lệnh qua chế độ Headless (solidcraft-cli.exe run).
        /// </summary>
        public async Task<JsonArray> ExecuteBatchHeadlessAsync(string cliPath, string designFilePath, IEnumerable<object> commands, CancellationToken ct = default)
        {
            if (!File.Exists(cliPath))
            {
                throw new FileNotFoundException($"solidcraft-cli.exe not found at: {cliPath}");
            }

            if (!File.Exists(designFilePath))
            {
                throw new FileNotFoundException($"Design file not found at: {designFilePath}");
            }

            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"sc_script_{Guid.NewGuid():N}.json");
            var tempResultPath = Path.Combine(Path.GetTempPath(), $"sc_res_{Guid.NewGuid():N}.json");

            try
            {
                var scriptJson = JsonSerializer.Serialize(commands, new JsonSerializerOptions { WriteIndented = false });
                await File.WriteAllTextAsync(tempScriptPath, scriptJson, new UTF8Encoding(false), ct).ConfigureAwait(false);

                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = $"run \"{tempScriptPath}\" --in \"{designFilePath}\" --results --json-out \"{tempResultPath}\" --allow-errors",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                if (!File.Exists(tempResultPath))
                {
                    var errText = errorBuilder.ToString().Trim();
                    var match = Regex.Match(errText, @"error:\s+step\s+(\d+)\s*(?:\(([^)]+)\))?:\s*(.*)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var stepNum = int.Parse(match.Groups[1].Value);
                        var cmdName = match.Groups[2].Success ? match.Groups[2].Value : "";
                        var msg = match.Groups[3].Value.Trim();
                        throw new SolidCraftBatchStepException(stepNum - 1, cmdName, msg);
                    }
                    throw new InvalidOperationException($"solidcraft-cli exited with code {process.ExitCode} without producing result. Error: {errText}");
                }

                var resJson = await File.ReadAllTextAsync(tempResultPath, Encoding.UTF8, ct).ConfigureAwait(false);
                var node = JsonNode.Parse(resJson);
                if (node is JsonArray arr)
                {
                    return arr;
                }
                else if (node is JsonObject obj && obj["results"] is JsonArray objArr)
                {
                    return objArr;
                }

                throw new InvalidOperationException($"Unexpected JSON output format from solidcraft-cli: {resJson}");
            }
            finally
            {
                try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
                try { if (File.Exists(tempResultPath)) File.Delete(tempResultPath); } catch { }
            }
        }

        /// <summary>
        /// Thực thi danh sách lệnh trong chế độ dung thứ lỗi (Tolerant): nếu 1 step trong batch lỗi,
        /// ghi nhận lỗi cho step đó và tiếp tục chạy các step còn lại thay vì huỷ toàn bộ kết quả.
        /// </summary>
        public async Task<JsonArray> ExecuteBatchTolerantAsync(
            string cliPath,
            string designFilePath,
            IReadOnlyList<object> commands,
            CancellationToken ct = default)
        {
            var fullResults = new JsonArray();
            int startIndex = 0;

            while (startIndex < commands.Count)
            {
                ct.ThrowIfCancellationRequested();
                var remainingCommands = new List<object>();
                for (int i = startIndex; i < commands.Count; i++)
                {
                    remainingCommands.Add(commands[i]);
                }

                try
                {
                    var batchRes = await ExecuteBatchHeadlessAsync(cliPath, designFilePath, remainingCommands, ct).ConfigureAwait(false);
                    foreach (var r in batchRes)
                    {
                        fullResults.Add(r?.DeepClone());
                    }
                    startIndex += remainingCommands.Count;
                }
                catch (SolidCraftBatchStepException stepEx)
                {
                    int failedSubIndex = stepEx.StepIndex;
                    if (failedSubIndex < 0 || failedSubIndex >= remainingCommands.Count)
                    {
                        failedSubIndex = 0;
                    }

                    // Nếu có các step trước step lỗi, lấy kết quả của chúng
                    if (failedSubIndex > 0)
                    {
                        var prefixCommands = remainingCommands.GetRange(0, failedSubIndex);
                        try
                        {
                            var prefixRes = await ExecuteBatchHeadlessAsync(cliPath, designFilePath, prefixCommands, ct).ConfigureAwait(false);
                            foreach (var r in prefixRes)
                            {
                                fullResults.Add(r?.DeepClone());
                            }
                        }
                        catch
                        {
                            for (int p = 0; p < failedSubIndex; p++)
                            {
                                fullResults.Add(new JsonObject { ["error"] = "Không thể lấy kết quả bước trước đó" });
                            }
                        }
                    }

                    // Ghi nhận lỗi cho step bị hỏng
                    fullResults.Add(new JsonObject
                    {
                        ["error"] = stepEx.Message,
                        ["step"] = startIndex + failedSubIndex + 1,
                        ["command"] = stepEx.CommandName
                    });

                    // Chuyển sang step tiếp theo sau step lỗi
                    startIndex += failedSubIndex + 1;
                }
                catch (Exception ex)
                {
                    fullResults.Add(new JsonObject { ["error"] = ex.Message });
                    startIndex++;
                }
            }

            return fullResults;
        }

        /// <summary>
        /// Thực thi danh sách lệnh qua Live Control Channel với chế độ dung thứ lỗi.
        /// </summary>
        public async Task<JsonArray> ExecuteBatchTolerantLiveAsync(
            int port,
            string token,
            IReadOnlyList<object> commands,
            CancellationToken ct = default)
        {
            try
            {
                return await ExecuteBatchLiveAsync(port, token, commands, ct).ConfigureAwait(false);
            }
            catch
            {
                var fullResults = new JsonArray();
                foreach (var cmd in commands)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var singleRes = await ExecuteBatchLiveAsync(port, token, new object[] { cmd }, ct).ConfigureAwait(false);
                        if (singleRes.Count > 0)
                        {
                            fullResults.Add(singleRes[0]?.DeepClone());
                        }
                        else
                        {
                            fullResults.Add(new JsonObject { ["error"] = "Không có kết quả trả về" });
                        }
                    }
                    catch (Exception ex)
                    {
                        fullResults.Add(new JsonObject { ["error"] = ex.Message });
                    }
                }
                return fullResults;
            }
        }

        /// <summary>
        /// Cập nhật các liên kết (component.update_links) trong mô hình SolidCraft.
        /// Live mode: gửi qua TCP để SolidCraft GUI cập nhật trực tiếp.
        /// Headless mode: dry-check, nếu có link cần update thì sao lưu .sc.bak và lưu lại file .sc.
        /// </summary>
        public async Task<UpdateLinksResult> UpdateLinksAsync(string? designFilePath, bool useLiveApp, CancellationToken ct = default)
        {
            var res = new UpdateLinksResult();
            var updateCmd = new object[]
            {
                new { command = "component.update_links", @params = new { } }
            };

            if (useLiveApp)
            {
                if (TryGetLiveControlEndpoint(out var port, out var token))
                {
                    try
                    {
                        var liveResults = await ExecuteBatchLiveAsync(port, token, updateCmd, ct).ConfigureAwait(false);
                        if (liveResults.Count > 0)
                        {
                            ParseUpdateLinksOutput(liveResults[0], res);
                        }
                    }
                    catch (Exception ex)
                    {
                        res.Warnings.Add($"Lỗi cập nhật liên kết trực tiếp trên SolidCraft: {ex.Message}");
                    }
                }
                return res;
            }

            if (string.IsNullOrWhiteSpace(designFilePath) || !File.Exists(designFilePath))
            {
                return res;
            }

            var cliPath = FindCliExecutablePath();
            if (string.IsNullOrWhiteSpace(cliPath))
            {
                res.Warnings.Add("Không tìm thấy solidcraft-cli.exe để cập nhật liên kết.");
                return res;
            }

            // 1. Dry check: chạy headless không có --save để kiểm tra có link nào cần update không
            try
            {
                var dryResults = await ExecuteBatchHeadlessAsync(cliPath, designFilePath, updateCmd, ct).ConfigureAwait(false);
                if (dryResults.Count > 0)
                {
                    ParseUpdateLinksOutput(dryResults[0], res);
                }
            }
            catch (Exception ex)
            {
                res.Warnings.Add($"Không thể kiểm tra liên kết: {ex.Message}");
                return res;
            }

            // 2. Nếu có component cần update -> Sao lưu file và ghi lại file với --save
            if (res.UpdatedComponents.Count > 0)
            {
                var backupPath = designFilePath + ".bak";
                try
                {
                    File.Copy(designFilePath, backupPath, overwrite: true);
                    res.BackupPath = backupPath;

                    var tempScriptPath = Path.Combine(Path.GetTempPath(), $"sc_update_{Guid.NewGuid():N}.json");
                    var tempResultPath = Path.Combine(Path.GetTempPath(), $"sc_updateres_{Guid.NewGuid():N}.json");
                    try
                    {
                        var scriptJson = JsonSerializer.Serialize(updateCmd, new JsonSerializerOptions { WriteIndented = false });
                        await File.WriteAllTextAsync(tempScriptPath, scriptJson, new UTF8Encoding(false), ct).ConfigureAwait(false);

                        var psi = new ProcessStartInfo
                        {
                            FileName = cliPath,
                            Arguments = $"run \"{tempScriptPath}\" --in \"{designFilePath}\" --save \"{designFilePath}\" --results --json-out \"{tempResultPath}\" --allow-errors",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
                        };

                        using var process = new Process { StartInfo = psi };
                        process.Start();
                        await process.WaitForExitAsync(ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
                        try { if (File.Exists(tempResultPath)) File.Delete(tempResultPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    res.Warnings.Add($"Không thể ghi lại file sau khi cập nhật liên kết ({ex.Message}).");
                }
            }

            return res;
        }

        private static void ParseUpdateLinksOutput(JsonNode? node, UpdateLinksResult result)
        {
            if (node == null) return;
            if (node["updated"] is JsonArray upArr)
            {
                foreach (var u in upArr)
                {
                    var s = u?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) result.UpdatedComponents.Add(s);
                }
            }
            if (node["unchanged"] is JsonArray unArr)
            {
                foreach (var u in unArr)
                {
                    var s = u?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) result.UnchangedComponents.Add(s);
                }
            }
            if (node["warnings"] is JsonArray wArr)
            {
                foreach (var w in wArr)
                {
                    var s = w?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Warnings.Add(s);
                }
            }
        }

        /// <summary>
        /// Tạo ảnh Thumbnail cho một Sheet Body trong file thiết kế qua solidcraft-cli snapshot.
        /// </summary>
        public async Task<string?> CaptureThumbnailAsync(string cliPath, string designFilePath, string bodyName, CancellationToken ct = default)
        {
            try
            {
                var thumbDir = Path.Combine(Path.GetTempPath(), "SolidCraftDxfAddIn_Thumbnails");
                if (!Directory.Exists(thumbDir))
                {
                    Directory.CreateDirectory(thumbDir);
                }

                var safeName = Regex.Replace(bodyName, @"[^\w\-]", "_");
                var thumbFile = Path.Combine(thumbDir, $"{safeName}_{Guid.NewGuid():N}.png");

                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = $"snapshot --in \"{designFilePath}\" --out \"{thumbFile}\" --width 180 --height 140 --view iso --no-grid --no-axes --tool \"{bodyName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                if (File.Exists(thumbFile) && new FileInfo(thumbFile).Length > 0)
                {
                    return thumbFile;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Quét toàn bộ thiết kế SolidCraft, trả về danh sách SolidCraftSheetItem.
        /// </summary>
        public async Task<SolidCraftLoadResult> LoadDesignAsync(
            string? designFilePath,
            bool useLiveApp,
            bool captureThumbnails,
            IProgress<(int current, int total, string message)>? progress = null,
            CancellationToken ct = default)
        {
            var result = new SolidCraftLoadResult();
            int livePort = 27878;
            string liveToken = string.Empty;
            string? cliPath = FindCliExecutablePath();

            if (useLiveApp)
            {
                if (!TryGetLiveControlEndpoint(out livePort, out liveToken))
                {
                    throw new InvalidOperationException("Không tìm thấy phiên làm việc của phần mềm SolidCraft đang mở (thiếu control port/token).");
                }
                result.IsFromLiveApp = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(designFilePath) || !File.Exists(designFilePath))
                {
                    throw new FileNotFoundException("Chưa chọn file thiết kế SolidCraft hợp lệ (.sc, .solidcraft, .step).");
                }
                if (string.IsNullOrWhiteSpace(cliPath))
                {
                    throw new FileNotFoundException("Không tìm thấy solidcraft-cli.exe trên máy. Vui lòng cấu hình đường dẫn.");
                }
                result.DesignPath = designFilePath;
                result.DesignName = Path.GetFileNameWithoutExtension(designFilePath);
                result.IsFromLiveApp = false;
            }

            progress?.Report((1, 5, "Đang quét cấu trúc mô hình, BOM và các quy tắc Sheet Metal..."));

            // BƯỚC A: inspect.measure, document.inspect, component.list, assembly.bom, sheet.rules, component.links (+ component.update_links nếu headless)
            var stepACommandsList = new List<object>
            {
                new { command = "inspect.measure", @params = new { } },
                new { command = "document.inspect", @params = new { measure = false } },
                new { command = "component.list", @params = new { compact = false } },
                new { command = "assembly.bom", @params = new { levels = "flat" } },
                new { command = "sheet.rules", @params = new { } },
                new { command = "component.links", @params = new { } }
            };

            if (!useLiveApp)
            {
                // Kiểm tra liên kết lỗi thời không kèm --save (dry check)
                stepACommandsList.Add(new { command = "component.update_links", @params = new { } });
            }

            JsonArray stepAResults;
            if (useLiveApp)
            {
                stepAResults = await ExecuteBatchTolerantLiveAsync(livePort, liveToken, stepACommandsList, ct).ConfigureAwait(false);
            }
            else
            {
                stepAResults = await ExecuteBatchTolerantAsync(cliPath!, designFilePath!, stepACommandsList, ct).ConfigureAwait(false);
            }

            if (stepAResults.Count < 5 || stepAResults[0]?["error"] != null)
            {
                var errDetail = stepAResults.Count > 0 ? stepAResults[0]?["error"]?.ToString() : "không có phản hồi";
                throw new InvalidOperationException($"Không thể quét thông tin thiết kế từ SolidCraft: {errDetail}");
            }

            var measureRes = stepAResults[0];
            var docInspectRes = stepAResults[1];
            var compListRes = stepAResults[2];
            var bomRes = stepAResults[3];
            var rulesRes = stepAResults[4];
            var linksRes = stepAResults.Count > 5 && stepAResults[5]?["error"] == null ? stepAResults[5] : null;
            var updateLinksRes = stepAResults.Count > 6 && stepAResults[6]?["error"] == null ? stepAResults[6] : null;

            if (stepAResults.Count > 6 && stepAResults[6]?["error"] != null)
            {
                result.Warnings.Add($"Cảnh báo liên kết: {stepAResults[6]?["error"]}");
            }

            if (useLiveApp)
            {
                result.DesignName = docInspectRes?["name"]?.ToString() ?? "LiveDesign";
                result.DesignPath = docInspectRes?["path"]?.ToString() ?? string.Empty;
            }

            // 0. Phân tích component lỗi thời từ updateLinksRes (Headless)
            var staleComponentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (updateLinksRes?["updated"] is JsonArray updatedArr)
            {
                foreach (var u in updatedArr)
                {
                    var uName = u?.ToString();
                    if (!string.IsNullOrWhiteSpace(uName))
                    {
                        staleComponentNames.Add(uName);
                        result.Warnings.Add($"Component '{uName}' khác với file gốc – bấm Update Model để cập nhật.");
                    }
                }
            }

            // 1. Phân tích sheet rules của host
            var rulesDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (rulesRes?["rules"] is JsonArray rulesArr)
            {
                foreach (var rItem in rulesArr)
                {
                    var ruleName = rItem?["rule"]?["name"]?.ToString();
                    var thicknessVal = rItem?["values"]?["thickness"]?.GetValue<double>() ?? 0.0;
                    if (!string.IsNullOrWhiteSpace(ruleName))
                    {
                        rulesDict[ruleName] = thicknessVal;
                    }
                }
            }

            // 2. Phân tích component list (mapping bodyName -> component)
            var bodyToCompMap = new Dictionary<string, (ulong compId, string compName)>(StringComparer.OrdinalIgnoreCase);
            var secondaryOccurrenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var compOccurrenceCount = new Dictionary<ulong, int>();

            if (compListRes?["components"] is JsonArray compArr)
            {
                foreach (var c in compArr)
                {
                    var cId = c?["id"]?.GetValue<ulong>() ?? 0;
                    var cName = c?["name"]?.ToString() ?? string.Empty;
                    var occArr = c?["occurrences"] as JsonArray;
                    int occCount = occArr?.Count ?? 0;
                    if (cId > 0)
                    {
                        compOccurrenceCount[cId] = Math.Max(1, occCount);
                    }

                    if (occArr != null)
                    {
                        // Occurrence đầu tiên (index 0) dùng tên body gốc; từ index 1 trở đi inspect.measure gắn hậu tố " (<occName>)"
                        for (int occIdx = 1; occIdx < occArr.Count; occIdx++)
                        {
                            var occName = occArr[occIdx]?["name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(occName))
                            {
                                secondaryOccurrenceNames.Add(occName);
                            }
                        }
                    }

                    if (c?["bodies"] is JsonArray cBodies)
                    {
                        foreach (var b in cBodies)
                        {
                            var bName = b?.ToString();
                            if (!string.IsNullOrWhiteSpace(bName))
                            {
                                bodyToCompMap[bName] = (cId, cName);
                            }
                        }
                    }
                }
            }

            // 3. Phân tích BOM (mapping compId -> PartNumber, Qty, Desc, Revision)
            var compToBomMap = new Dictionary<ulong, (string partNum, string desc, string rev, int qty)>();
            if (bomRes?["rows"] is JsonArray bomRows)
            {
                foreach (var row in bomRows)
                {
                    var partNum = row?["part_number"]?.ToString() ?? string.Empty;
                    var desc = row?["description"]?.ToString() ?? string.Empty;
                    var rev = row?["revision"]?.ToString() ?? string.Empty;
                    var qty = row?["count"]?.GetValue<int>() ?? 1;

                    if (row?["components"] is JsonArray rowComps)
                    {
                        foreach (var compNode in rowComps)
                        {
                            var cId = compNode?.GetValue<ulong>() ?? 0;
                            compToBomMap[cId] = (partNum, desc, rev, qty);
                        }
                    }
                }
            }

            // 4. Phân tích component.links
            var compIdToLink = new Dictionary<ulong, (string name, string file, string path, bool found)>();
            if (linksRes?["links"] is JsonArray linksArr)
            {
                foreach (var l in linksArr)
                {
                    var cId = l?["component"]?.GetValue<ulong>() ?? 0;
                    var lName = l?["name"]?.ToString() ?? string.Empty;
                    var lFile = l?["file"]?.ToString() ?? string.Empty;
                    var lPath = l?["path"]?.ToString() ?? string.Empty;
                    var lFound = l?["found"]?.GetValue<bool>() ?? false;
                    if (cId > 0)
                    {
                        compIdToLink[cId] = (lName, lFile, lPath, lFound);
                    }
                }
            }

            // 5. Phân loại bodies từ inspect.measure
            var nativeSheetBodies = new List<(string bodyName, string material, double area, double volume)>();
            var candidateLinkedBodies = new Dictionary<ulong, List<HostBodyInfo>>();

            if (measureRes?["bodies"] is JsonArray bodiesArr)
            {
                foreach (var b in bodiesArr)
                {
                    var bName = b?["name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(bName)) continue;

                    // Bỏ qua các World Body của Occurrence phụ (VD: "P-3 (P-3:2)") vì số lượng đã được tính trong BOM / Occurrences
                    if (IsSecondaryOccurrenceWorldBody(bName, secondaryOccurrenceNames, bodyToCompMap))
                    {
                        continue;
                    }

                    var isSheet = b?["sheet_metal"]?.GetValue<bool>() ?? false;
                    var mat = b?["material"]?.ToString() ?? "Steel";
                    var area = b?["area_mm2"]?.GetValue<double>() ?? 0.0;
                    var vol = b?["volume_mm3"]?.GetValue<double>() ?? 0.0;

                    if (isSheet)
                    {
                        nativeSheetBodies.Add((bName, mat, area, vol));
                    }
                    else
                    {
                        // Kiểm tra xem body này có thuộc component có link .sc không
                        if (bodyToCompMap.TryGetValue(bName, out var compInfo) && compIdToLink.ContainsKey(compInfo.compId))
                        {
                            if (!candidateLinkedBodies.TryGetValue(compInfo.compId, out var list))
                            {
                                list = new List<HostBodyInfo>();
                                candidateLinkedBodies[compInfo.compId] = list;
                            }
                            list.Add(new HostBodyInfo
                            {
                                Name = bName,
                                Material = mat,
                                Area = area,
                                Volume = vol,
                                CompId = compInfo.compId,
                                CompName = compInfo.compName
                            });
                        }
                        else
                        {
                            result.UnrecognizedBodies.Add($"Body '{bName}' ({mat}, volume: {vol:0.##} mm³)");
                        }
                    }
                }
            }

            // 6. Xử lý các linked bodies từ file .sc gốc
            var linkedFileCache = new Dictionary<string, LinkedFileInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in candidateLinkedBodies)
            {
                var compId = kvp.Key;
                var hostBodies = kvp.Value;
                var link = compIdToLink[compId];

                string linkFilePath = link.file;
                bool linkFound = link.found && File.Exists(linkFilePath);

                if (!linkFound)
                {
                    // Candidate 1: tương đối với thư mục file host
                    if (!string.IsNullOrEmpty(result.DesignPath))
                    {
                        var hostDir = Path.GetDirectoryName(result.DesignPath);
                        if (!string.IsNullOrEmpty(hostDir))
                        {
                            if (!string.IsNullOrEmpty(link.path))
                            {
                                var cand = Path.GetFullPath(Path.Combine(hostDir, link.path));
                                if (File.Exists(cand)) { linkFilePath = cand; linkFound = true; }
                            }
                            if (!linkFound && !string.IsNullOrEmpty(link.file))
                            {
                                var cand = Path.Combine(hostDir, Path.GetFileName(link.file));
                                if (File.Exists(cand)) { linkFilePath = cand; linkFound = true; }
                            }
                        }
                    }

                    // Candidate 2: trong thư mục MyDocuments
                    if (!linkFound && !string.IsNullOrEmpty(link.file))
                    {
                        var docDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        var cand = Path.Combine(docDir, Path.GetFileName(link.file));
                        if (File.Exists(cand)) { linkFilePath = cand; linkFound = true; }
                    }
                }

                if (!linkFound || !linkFilePath.EndsWith(".sc", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"Không tìm thấy file nguồn của component liên kết '{link.name}': {link.file}");
                    foreach (var hb in hostBodies)
                    {
                        result.UnrecognizedBodies.Add($"Body '{hb.Name}' (không tìm thấy file nguồn: {link.name})");
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(cliPath))
                {
                    result.Warnings.Add($"Không tìm thấy solidcraft-cli.exe để nạp chi tiết từ component liên kết '{link.name}'.");
                    foreach (var hb in hostBodies)
                    {
                        result.UnrecognizedBodies.Add($"Body '{hb.Name}' (thiếu solidcraft-cli)");
                    }
                    continue;
                }

                try
                {
                    var resolvedItems = await ResolveLinkedSheetBodiesAsync(
                        cliPath,
                        linkFilePath,
                        hostBodies,
                        compToBomMap,
                        staleComponentNames,
                        depth: 0,
                        visitedFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        fileCache: linkedFileCache,
                        resultWarnings: result.Warnings,
                        resultUnrecognized: result.UnrecognizedBodies,
                        ct: ct
                    ).ConfigureAwait(false);

                    foreach (var it in resolvedItems)
                    {
                        result.SheetItems.Add(it);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Lỗi nạp component liên kết '{link.name}' ({linkFilePath}): {ex.Message}");
                    foreach (var hb in hostBodies)
                    {
                        result.UnrecognizedBodies.Add($"Body '{hb.Name}' (lỗi nạp: {ex.Message})");
                    }
                }
            }

            // 7. BƯỚC B: Gửi batch lệnh sheet.flat_pattern cho các body kim loại tấm gốc (native)
            if (nativeSheetBodies.Count > 0)
            {
                progress?.Report((2, 5, $"Tìm thấy {nativeSheetBodies.Count} chi tiết kim loại tấm gốc. Đang trích xuất bản trải phẳng..."));

                var flatCommands = new List<object>();
                foreach (var sb in nativeSheetBodies)
                {
                    flatCommands.Add(new { command = "sheet.flat_pattern", @params = new { body = sb.bodyName } });
                }

                JsonArray flatResults;
                if (useLiveApp)
                {
                    flatResults = await ExecuteBatchTolerantLiveAsync(livePort, liveToken, flatCommands, ct).ConfigureAwait(false);
                }
                else
                {
                    flatResults = await ExecuteBatchTolerantAsync(cliPath!, designFilePath!, flatCommands, ct).ConfigureAwait(false);
                }

                for (int i = 0; i < nativeSheetBodies.Count; i++)
                {
                    var sb = nativeSheetBodies[i];
                    var sheetItem = new SolidCraftSheetItem
                    {
                        BodyName = sb.bodyName,
                        ExportBodyName = sb.bodyName,
                        ExportDesignPath = result.DesignPath,
                        SourceDesignPath = result.DesignPath,
                        MaterialName = sb.material,
                        VolumeMm3 = sb.volume,
                        AreaMm2 = sb.area,
                        IsLinked = false
                    };

                    if (bodyToCompMap.TryGetValue(sb.bodyName, out var compInfo))
                    {
                        sheetItem.ComponentId = compInfo.compId;
                        sheetItem.ComponentName = compInfo.compName;

                        if (compToBomMap.TryGetValue(compInfo.compId, out var bomInfo))
                        {
                            sheetItem.PartNumber = !string.IsNullOrWhiteSpace(bomInfo.partNum) ? bomInfo.partNum : sb.bodyName;
                            sheetItem.DescriptionProp = bomInfo.desc;
                            sheetItem.Revision = bomInfo.rev;
                            sheetItem.Quantity = bomInfo.qty > 0 ? bomInfo.qty : 1;
                        }
                        else
                        {
                            sheetItem.PartNumber = sb.bodyName;
                            sheetItem.Quantity = compOccurrenceCount.TryGetValue(compInfo.compId, out var occC) ? occC : 1;
                        }
                    }
                    else
                    {
                        sheetItem.PartNumber = sb.bodyName;
                        sheetItem.Quantity = 1;
                    }

                    if (i < flatResults.Count)
                    {
                        ApplyFlatPatternResult(sheetItem, flatResults[i], rulesDict);
                    }
                    else
                    {
                        sheetItem.HasFlatPatternError = true;
                        sheetItem.FlatPatternErrorMessage = "Không nhận được kết quả trải phẳng";
                    }

                    result.SheetItems.Add(sheetItem);
                }
            }

            // Gộp các body được nhân bản bằng lệnh Copy / Pattern (VD: P-1 (2) -> P-1, Px-1 (2),(3) -> Px-1)
            result.SheetItems = GroupIdenticalCopiedSheetItems(result.SheetItems);

            if (result.SheetItems.Count == 0)
            {
                return result;
            }

            // BƯỚC C: Xuất ảnh thumbnail nếu yêu cầu
            if (captureThumbnails && !string.IsNullOrEmpty(cliPath))
            {
                progress?.Report((4, 5, "Đang chụp ảnh xem trước (Thumbnails)..."));
                for (int i = 0; i < result.SheetItems.Count; i++)
                {
                    var item = result.SheetItems[i];
                    var thumbDesignPath = item.IsLinked ? item.ExportDesignPath : result.DesignPath;
                    var thumbBodyName = item.IsLinked ? item.ExportBodyName : item.BodyName;

                    if (!string.IsNullOrEmpty(thumbDesignPath) && File.Exists(thumbDesignPath))
                    {
                        item.ThumbnailPath = await CaptureThumbnailAsync(cliPath, thumbDesignPath, thumbBodyName, ct).ConfigureAwait(false);
                    }
                }
            }

            progress?.Report((5, 5, "Hoàn tất nạp dữ liệu từ SolidCraft."));
            return result;
        }

        private class HostBodyInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Material { get; set; } = string.Empty;
            public double Area { get; set; }
            public double Volume { get; set; }
            public ulong CompId { get; set; }
            public string CompName { get; set; } = string.Empty;
        }

        private class SourceBodyInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Material { get; set; } = string.Empty;
            public double Area { get; set; }
            public double Volume { get; set; }
            public bool IsSheet { get; set; }
        }

        private class LinkedFileInfo
        {
            public Dictionary<string, double> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<SourceBodyInfo> Bodies { get; set; } = new();
            public Dictionary<ulong, (string name, string file, string path, bool found)> Links { get; set; } = new();
        }

        private async Task<List<SolidCraftSheetItem>> ResolveLinkedSheetBodiesAsync(
            string cliPath,
            string sourceFilePath,
            List<HostBodyInfo> hostBodies,
            Dictionary<ulong, (string partNum, string desc, string rev, int qty)> hostCompToBomMap,
            HashSet<string> staleComponentNames,
            int depth,
            HashSet<string> visitedFiles,
            Dictionary<string, LinkedFileInfo> fileCache,
            List<string> resultWarnings,
            List<string> resultUnrecognized,
            CancellationToken ct)
        {
            var resolvedItems = new List<SolidCraftSheetItem>();
            var fullPath = Path.GetFullPath(sourceFilePath);

            if (depth > 5 || visitedFiles.Contains(fullPath))
            {
                return resolvedItems;
            }
            visitedFiles.Add(fullPath);

            var cacheKey = $"{fullPath}|{File.GetLastWriteTimeUtc(fullPath).Ticks}";
            if (!fileCache.TryGetValue(cacheKey, out var fileInfo))
            {
                fileInfo = new LinkedFileInfo();
                var scanCommands = new object[]
                {
                    new { command = "inspect.measure", @params = new { } },
                    new { command = "sheet.rules", @params = new { } },
                    new { command = "component.links", @params = new { } },
                    new { command = "component.list", @params = new { compact = false } }
                };

                var scanResults = await ExecuteBatchHeadlessAsync(cliPath, fullPath, scanCommands, ct).ConfigureAwait(false);
                if (scanResults.Count >= 2)
                {
                    // 1. sheet.rules
                    if (scanResults[1]?["rules"] is JsonArray rArr)
                    {
                        foreach (var rItem in rArr)
                        {
                            var rName = rItem?["rule"]?["name"]?.ToString();
                            var thk = rItem?["values"]?["thickness"]?.GetValue<double>() ?? 0.0;
                            if (!string.IsNullOrWhiteSpace(rName))
                            {
                                fileInfo.Rules[rName] = thk;
                            }
                        }
                    }

                    // 2. inspect.measure
                    if (scanResults[0]?["bodies"] is JsonArray bArr)
                    {
                        foreach (var b in bArr)
                        {
                            var name = b?["name"]?.ToString() ?? "";
                            var mat = b?["material"]?.ToString() ?? "Steel";
                            var area = b?["area_mm2"]?.GetValue<double>() ?? 0.0;
                            var vol = b?["volume_mm3"]?.GetValue<double>() ?? 0.0;
                            var isSheet = b?["sheet_metal"]?.GetValue<bool>() ?? false;
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                fileInfo.Bodies.Add(new SourceBodyInfo
                                {
                                    Name = name,
                                    Material = mat,
                                    Area = area,
                                    Volume = vol,
                                    IsSheet = isSheet
                                });
                            }
                        }
                    }

                    // 3. component.links
                    if (scanResults.Count > 2 && scanResults[2]?["links"] is JsonArray nestedLinksArr)
                    {
                        foreach (var l in nestedLinksArr)
                        {
                            var cId = l?["component"]?.GetValue<ulong>() ?? 0;
                            var lName = l?["name"]?.ToString() ?? "";
                            var lFile = l?["file"]?.ToString() ?? "";
                            var lPath = l?["path"]?.ToString() ?? "";
                            var lFound = l?["found"]?.GetValue<bool>() ?? false;
                            if (cId > 0)
                            {
                                fileInfo.Links[cId] = (lName, lFile, lPath, lFound);
                            }
                        }
                    }
                }

                fileCache[cacheKey] = fileInfo;
            }

            var matchedPairs = new List<(HostBodyInfo host, SourceBodyInfo src, bool isStale)>();
            var sourceSheetBodies = fileInfo.Bodies.FindAll(b => b.IsSheet);

            foreach (var hb in hostBodies)
            {
                SourceBodyInfo? matchedSrc = null;
                bool isStale = false;

                // 1. Khớp theo Volume và Area (dung sai 1e-4)
                foreach (var sb in sourceSheetBodies)
                {
                    double relVolDiff = Math.Abs(hb.Volume - sb.Volume) / Math.Max(1.0, hb.Volume);
                    double relAreaDiff = Math.Abs(hb.Area - sb.Area) / Math.Max(1.0, hb.Area);
                    if (relVolDiff <= 1e-4 && relAreaDiff <= 1e-4)
                    {
                        matchedSrc = sb;
                        break;
                    }
                }

                // 2. Fallback nếu file gốc chỉ có đúng 1 body và host component chỉ có đúng 1 body
                if (matchedSrc == null && fileInfo.Bodies.Count == 1 && hostBodies.Count == 1)
                {
                    var single = fileInfo.Bodies[0];
                    if (single.IsSheet)
                    {
                        matchedSrc = single;
                        double relVolDiff = Math.Abs(hb.Volume - single.Volume) / Math.Max(1.0, hb.Volume);
                        if (relVolDiff > 1e-4)
                        {
                            isStale = true;
                            staleComponentNames.Add(hb.CompName);
                            resultWarnings.Add($"Component '{hb.CompName}' có hình học lệch với file gốc '{Path.GetFileName(fullPath)}' (volume lệch {relVolDiff * 100:0.##}%) – bấm Update Model để cập nhật.");
                        }
                    }
                }

                if (matchedSrc != null)
                {
                    matchedPairs.Add((hb, matchedSrc, isStale));
                }
                else
                {
                    resultUnrecognized.Add($"Body '{hb.Name}' (trong '{Path.GetFileName(fullPath)}') không khớp chi tiết kim loại tấm.");
                }
            }

            if (matchedPairs.Count == 0)
            {
                return resolvedItems;
            }

            // Gửi batch sheet.flat_pattern lên file gốc cho các body đã ghép được
            var flatCommands = new List<object>();
            foreach (var pair in matchedPairs)
            {
                flatCommands.Add(new { command = "sheet.flat_pattern", @params = new { body = pair.src.Name } });
            }

            var flatResults = await ExecuteBatchTolerantAsync(cliPath, fullPath, flatCommands, ct).ConfigureAwait(false);

            for (int i = 0; i < matchedPairs.Count; i++)
            {
                var (hb, sb, isStale) = matchedPairs[i];
                var sheetItem = new SolidCraftSheetItem
                {
                    BodyName = hb.Name,
                    ExportBodyName = sb.Name,
                    ExportDesignPath = fullPath,
                    SourceDesignPath = fullPath,
                    LinkedSourceName = Path.GetFileName(fullPath),
                    IsLinked = true,
                    IsLinkStale = isStale || staleComponentNames.Contains(hb.CompName),
                    ComponentId = hb.CompId,
                    ComponentName = hb.CompName,
                    VolumeMm3 = sb.Volume,
                    AreaMm2 = sb.Area,
                    MaterialName = !string.IsNullOrWhiteSpace(sb.Material) && !string.Equals(sb.Material, "Default", StringComparison.OrdinalIgnoreCase)
                        ? sb.Material
                        : (!string.IsNullOrWhiteSpace(hb.Material) && !string.Equals(hb.Material, "Default", StringComparison.OrdinalIgnoreCase)
                            ? hb.Material
                            : "Default")
                };

                if (hostCompToBomMap.TryGetValue(hb.CompId, out var bomInfo))
                {
                    sheetItem.PartNumber = !string.IsNullOrWhiteSpace(bomInfo.partNum) ? bomInfo.partNum : GetCopyPatternBaseName(hb.Name);
                    sheetItem.DescriptionProp = bomInfo.desc;
                    sheetItem.Revision = bomInfo.rev;
                    sheetItem.Quantity = bomInfo.qty > 0 ? bomInfo.qty : 1;
                }
                else
                {
                    sheetItem.PartNumber = GetCopyPatternBaseName(hb.Name);
                    sheetItem.Quantity = 1;
                }

                if (i < flatResults.Count)
                {
                    ApplyFlatPatternResult(sheetItem, flatResults[i], fileInfo.Rules);
                }
                else
                {
                    sheetItem.HasFlatPatternError = true;
                    sheetItem.FlatPatternErrorMessage = "Không nhận được kết quả trải phẳng từ file liên kết";
                }

                resolvedItems.Add(sheetItem);
            }

            return resolvedItems;
        }

        private static void ApplyFlatPatternResult(SolidCraftSheetItem sheetItem, JsonNode? fRes, Dictionary<string, double> rulesDict)
        {
            if (fRes == null || fRes["error"] != null)
            {
                sheetItem.HasFlatPatternError = true;
                sheetItem.FlatPatternErrorMessage = fRes?["error"]?.ToString() ?? "Lỗi tạo bản trải phẳng (Flat Pattern Error)";
                return;
            }

            sheetItem.RuleName = fRes["rule"]?.ToString() ?? string.Empty;
            sheetItem.ActualThicknessMm = fRes["thickness"]?.GetValue<double>() ?? 0.0;

            if (rulesDict.TryGetValue(sheetItem.RuleName, out var ruleThick))
            {
                sheetItem.RuleThicknessMm = ruleThick;
            }
            else
            {
                sheetItem.RuleThicknessMm = sheetItem.ActualThicknessMm;
            }

            if (fRes["flat_size_mm"] is JsonArray flatSize && flatSize.Count >= 2)
            {
                sheetItem.FlatWidthMm = flatSize[0]?.GetValue<double>() ?? 0.0;
                sheetItem.FlatHeightMm = flatSize[1]?.GetValue<double>() ?? 0.0;
                if (flatSize.Count >= 3 && sheetItem.ActualThicknessMm <= 0)
                {
                    sheetItem.ActualThicknessMm = flatSize[2]?.GetValue<double>() ?? 0.0;
                }
            }

            // Phân tích đường chấn (Bends)
            if (fRes["bends"] is JsonArray bendsArr)
            {
                sheetItem.BendCount = bendsArr.Count;
                foreach (var b in bendsArr)
                {
                    var dir = b?["direction"]?.ToString()?.ToLowerInvariant();
                    var len = b?["length"]?.GetValue<double>() ?? 0.0;
                    if (dir == "up")
                    {
                        sheetItem.BendUpCount++;
                        sheetItem.BendUpTotalLength += len;
                    }
                    else if (dir == "down")
                    {
                        sheetItem.BendDownCount++;
                        sheetItem.BendDownTotalLength += len;
                    }
                }
            }

            // Phân tích pockets / etch
            if (fRes["pockets"] is JsonArray pocketsArr)
            {
                foreach (var p in pocketsArr)
                {
                    var side = p?["side"]?.ToString()?.ToLowerInvariant();
                    if (side == "bottom")
                    {
                        sheetItem.HasBottomPocket = true;
                    }
                    else if (side == "top")
                    {
                        sheetItem.HasTopPocketOrEtch = true;
                    }
                }
            }

            // Kiểm tra sai lệch Chiều dày vs Sheet Rule
            if (sheetItem.RuleThicknessMm > 0 && Math.Abs(sheetItem.ActualThicknessMm - sheetItem.RuleThicknessMm) > 0.05)
            {
                sheetItem.ThicknessRuleMismatch = true;
                sheetItem.ThicknessRuleMismatchDetail = $"Rule: {sheetItem.RuleThicknessMm:0.##}mm != Model: {sheetItem.ActualThicknessMm:0.##}mm";
            }

            // Kiểm tra sai lệch Description (đối chiếu PL{Thickness}-{Material})
            sheetItem.DescriptionMismatch = CheckDescriptionMismatch(sheetItem.DescriptionProp, sheetItem.ActualThicknessMm, sheetItem.MaterialName);
        }

        /// <summary>
        /// Xuất file DXF cho một Sheet Body từ SolidCraft qua lệnh sheet.export_dxf.
        /// </summary>
        public async Task ExportSheetBodyDxfAsync(
            string? designFilePath,
            bool useLiveApp,
            string bodyName,
            string destinationDxfPath,
            SolidCraftSheetItem? sheetItem = null,
            CancellationToken ct = default)
        {
            string targetDesignPath = designFilePath ?? string.Empty;
            string targetBodyName = bodyName;
            bool targetUseLive = useLiveApp;

            if (sheetItem != null && sheetItem.IsLinked)
            {
                targetDesignPath = sheetItem.ExportDesignPath;
                targetBodyName = sheetItem.ExportBodyName;
                targetUseLive = false; // Luôn dùng Headless trên file thiết kế gốc
            }

            var exportCmd = new
            {
                command = "sheet.export_dxf",
                @params = new
                {
                    body = targetBodyName,
                    path = destinationDxfPath,
                    curves = "arcs",
                    text = "etch",
                    bend_notes = false,
                    bend_lines = true,
                    layers = "roles"
                }
            };

            int livePort = 27878;
            string liveToken = string.Empty;
            string? cliPath = FindCliExecutablePath();

            JsonArray resArr;
            if (targetUseLive)
            {
                if (!TryGetLiveControlEndpoint(out livePort, out liveToken))
                {
                    throw new InvalidOperationException("Mất kết nối tới SolidCraft Live Control Channel.");
                }
                resArr = await ExecuteBatchLiveAsync(livePort, liveToken, new object[] { exportCmd }, ct).ConfigureAwait(false);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(cliPath))
                {
                    throw new FileNotFoundException("Không tìm thấy solidcraft-cli.exe.");
                }
                resArr = await ExecuteBatchHeadlessAsync(cliPath, targetDesignPath, new object[] { exportCmd }, ct).ConfigureAwait(false);
            }

            if (resArr.Count > 0)
            {
                var itemRes = resArr[0];
                if (itemRes?["error"] != null)
                {
                    throw new InvalidOperationException($"Lỗi xuất DXF từ SolidCraft: {itemRes["error"]}");
                }

                // Nếu SolidCraft trả về chuỗi DXF trực tiếp thay vì ghi ra file
                var dxfText = itemRes?["dxf"]?.ToString();
                if (!string.IsNullOrWhiteSpace(dxfText) && (!File.Exists(destinationDxfPath) || new FileInfo(destinationDxfPath).Length == 0))
                {
                    var dir = Path.GetDirectoryName(destinationDxfPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    await File.WriteAllTextAsync(destinationDxfPath, dxfText, new UTF8Encoding(false), ct).ConfigureAwait(false);
                }
            }

            if (!File.Exists(destinationDxfPath) || new FileInfo(destinationDxfPath).Length == 0)
            {
                throw new FileNotFoundException($"File DXF không được tạo tại đường dẫn: {destinationDxfPath}");
            }
        }

        private static bool CheckDescriptionMismatch(string description, double actualThickMm, string material)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;

            // Mẫu kiểm tra PL{Thick}-{Material} hoặc PL {Thick}
            var match = Regex.Match(description, @"PL\s*([0-9]+(?:\.[0-9]+)?)(?:[-_\s]*([A-Za-z0-9]+))?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var descThick))
                {
                    if (Math.Abs(descThick - actualThickMm) > 0.05)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsSecondaryOccurrenceWorldBody(
            string bodyName,
            HashSet<string> secondaryOccurrenceNames,
            Dictionary<string, (ulong compId, string compName)> bodyToCompMap)
        {
            if (secondaryOccurrenceNames.Count == 0) return false;
            if (bodyToCompMap.ContainsKey(bodyName)) return false;

            int openParen = bodyName.LastIndexOf(" (", StringComparison.Ordinal);
            if (openParen <= 0 || !bodyName.EndsWith(")", StringComparison.Ordinal)) return false;

            string baseBody = bodyName.Substring(0, openParen).Trim();
            string suffix = bodyName.Substring(openParen + 2, bodyName.Length - openParen - 3).Trim();

            return secondaryOccurrenceNames.Contains(suffix) && bodyToCompMap.ContainsKey(baseBody);
        }

        private static readonly Regex CopySuffixRegex = new(@"^(.+?)\s*\(\d+\)$", RegexOptions.Compiled);

        /// <summary>
        /// Tách tên gốc của Body nếu tên có hậu tố nhân bản tự động của SolidCraft: "P-1 (2)" -> "P-1".
        /// </summary>
        public static string GetCopyPatternBaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string current = name.Trim();
            while (true)
            {
                var m = CopySuffixRegex.Match(current);
                if (m.Success)
                {
                    current = m.Groups[1].Value.Trim();
                }
                else
                {
                    break;
                }
            }
            return current;
        }

        private static bool AreSheetItemsGeometricallyIdentical(SolidCraftSheetItem a, SolidCraftSheetItem b)
        {
            if (a.HasFlatPatternError || b.HasFlatPatternError) return false;
            if (a.IsLinked != b.IsLinked) return false;

            if (a.IsLinked)
            {
                if (!string.Equals(a.ExportDesignPath, b.ExportDesignPath, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(a.ExportBodyName, b.ExportBodyName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.Equals(a.MaterialName, b.MaterialName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (Math.Abs(a.ActualThicknessMm - b.ActualThicknessMm) > 0.01)
                return false;

            if (a.BendCount != b.BendCount ||
                a.BendUpCount != b.BendUpCount ||
                a.BendDownCount != b.BendDownCount)
                return false;

            // So sánh kích thước phôi trải phẳng 2D (không phụ thuộc góc xoay 90 độ trên mặt phẳng)
            double aMin = Math.Min(a.FlatWidthMm, a.FlatHeightMm);
            double aMax = Math.Max(a.FlatWidthMm, a.FlatHeightMm);
            double bMin = Math.Min(b.FlatWidthMm, b.FlatHeightMm);
            double bMax = Math.Max(b.FlatWidthMm, b.FlatHeightMm);
            if (Math.Abs(aMin - bMin) > 0.05 || Math.Abs(aMax - bMax) > 0.05)
                return false;

            // So sánh thể tích 3D và diện tích bề mặt (dung sai 0.1%)
            if (a.VolumeMm3 > 0 && b.VolumeMm3 > 0)
            {
                double relVol = Math.Abs(a.VolumeMm3 - b.VolumeMm3) / Math.Max(1.0, a.VolumeMm3);
                if (relVol > 1e-3) return false;
            }

            if (a.AreaMm2 > 0 && b.AreaMm2 > 0)
            {
                double relArea = Math.Abs(a.AreaMm2 - b.AreaMm2) / Math.Max(1.0, a.AreaMm2);
                if (relArea > 1e-3) return false;
            }

            return true;
        }

        /// <summary>
        /// Gom nhóm thông minh các Body sinh ra từ lệnh Copy / Pattern (VD: P-1 (2) -> P-1, Px-1 (2),(3) -> Px-1)
        /// khi có cùng chữ ký hình học 3D và thông số trải phẳng 2D.
        /// </summary>
        public static List<SolidCraftSheetItem> GroupIdenticalCopiedSheetItems(List<SolidCraftSheetItem> items)
        {
            if (items.Count <= 1) return items;

            var groupedResult = new List<SolidCraftSheetItem>();
            var visited = new bool[items.Count];

            // Sắp xếp ưu tiên phần tử có tên gốc (không có hậu tố "(2)") đứng trước để làm đại diện chính
            var orderedIndices = new List<int>();
            for (int i = 0; i < items.Count; i++) orderedIndices.Add(i);
            orderedIndices.Sort((i, j) =>
            {
                bool iHasSuffix = CopySuffixRegex.IsMatch(items[i].BodyName);
                bool jHasSuffix = CopySuffixRegex.IsMatch(items[j].BodyName);
                if (iHasSuffix != jHasSuffix) return iHasSuffix ? 1 : -1;
                return i.CompareTo(j);
            });

            foreach (int idx in orderedIndices)
            {
                if (visited[idx]) continue;
                visited[idx] = true;

                var primary = items[idx];
                string primaryBaseName = GetCopyPatternBaseName(primary.BodyName);
                int totalQty = Math.Max(1, primary.Quantity);

                foreach (int otherIdx in orderedIndices)
                {
                    if (visited[otherIdx]) continue;
                    var candidate = items[otherIdx];
                    string candidateBaseName = GetCopyPatternBaseName(candidate.BodyName);

                    if (string.Equals(primaryBaseName, candidateBaseName, StringComparison.OrdinalIgnoreCase) &&
                        AreSheetItemsGeometricallyIdentical(primary, candidate))
                    {
                        visited[otherIdx] = true;
                        totalQty += Math.Max(1, candidate.Quantity);
                    }
                }

                primary.Quantity = totalQty;

                // Nếu đại diện được chọn vẫn mang tên có hậu tố "(2)" (VD bản gốc "P-1" đã bị xoá, chỉ còn "P-1 (2)" và "P-1 (3)")
                // hoặc PartNumber đang trùng với BodyName có hậu tố thì chuẩn hoá PartNumber về BaseName
                if (string.Equals(primary.PartNumber, primary.BodyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!groupedResult.Any(r => string.Equals(r.PartNumber, primaryBaseName, StringComparison.OrdinalIgnoreCase)))
                    {
                        primary.PartNumber = primaryBaseName;
                    }
                }

                groupedResult.Add(primary);
            }

            return groupedResult;
        }
    }
}

