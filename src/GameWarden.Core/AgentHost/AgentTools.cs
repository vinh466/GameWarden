using System.Text.Json;
using System.Text.Json.Nodes;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Activity;
using GameWarden.Core.Agents;
using GameWarden.Core.Checks;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Jobs;
using GameWarden.Core.Metrics;
using GameWarden.Core.Mods;
using GameWarden.Core.Notifications;
using GameWarden.Core.Observability;
using GameWarden.Core.Platform;
using GameWarden.Core.Supervision;
using GameWarden.Core.Updates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.AgentHost;

public enum AgentToolRisk
{
    Read,
    Write,
}

/// <summary>One curated capability of the in-Core Agent (docs/20). The schema is what the model sees.</summary>
public sealed record AgentTool(
    string Name,
    string Description,
    JsonElement Parameters,
    AgentToolRisk Risk,
    Func<JsonElement, CancellationToken, Task<object>> Handler)
{
    public string Kind => Risk == AgentToolRisk.Read ? "query" : "command";
}

/// <summary>
/// The read and operate tools the Owner's Agent may call. This is the shared registry ADR-0030 describes: add a tool
/// here, classify its risk, and both the Discord runtime and (later) the MCP bridge see it.
/// </summary>
public sealed class AgentTools(
    Supervisor supervisor,
    JobService jobs,
    SystemLogQuery systemLog,
    AgentLogService logs,
    ActivityLog activity,
    MetricsSampler metrics,
    AgentDiagnosticsService diagnostics,
    AgentFileService files,
    AgentKnowledgeService knowledge,
    ModsService mods,
    IDbContextFactory<GameWardenDb> db,
    NotificationService notifications,
    UpdateService updates,
    IMachine machine,
    AgentRedactor redactor,
    IOptions<GameWardenOptions> options,
    TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxToolOutput = 12_000;

    private IReadOnlyList<AgentTool>? _tools;

    public IReadOnlyList<AgentTool> All => _tools ??= BuildTools();

    private IReadOnlyList<AgentTool> BuildTools() =>
    [
        new("status", "Tóm tắt sức khỏe GameWarden: Core, instance, job lỗi, RAM/disk. Gọi tool này trước khi chẩn đoán.",
            Schema(), AgentToolRisk.Read, (_, ct) => Status(ct)),
        new("list_instances", "Liệt kê game server với state, desired state, cổng và lỗi gần nhất.",
            Schema(), AgentToolRisk.Read, (_, _) => Task.FromResult<object>(Instances())),
        new("get_instance", "Đọc một game server theo id.",
            Schema(("instanceId", "string", "Instance id, ví dụ valheim-main", true, null)), AgentToolRisk.Read,
            (args, _) => Task.FromResult<object>(One(Text(args, "instanceId")))),
        new("get_console", "Đọc phần cuối console của game server; dùng tail nhỏ và lọc thay vì đọc hết.",
            Schema(("instanceId", "string", "Instance id", true, null), ("tail", "integer", "Số dòng cuối, 1-1000; mặc định 200", false, null)),
            AgentToolRisk.Read, (args, _) => Console(args)),
        new("get_settings", "Đọc settings của instance; secret chỉ trả về { set: true }.",
            Schema(("instanceId", "string", "Instance id", true, null)), AgentToolRisk.Read, (args, _) => Settings(args)),
        new("list_jobs", "Đọc các job gần đây, lọc theo instance và/hoặc state.",
            Schema(("instanceId", "string", "Instance id (tùy chọn)", false, null), ("state", "string", "queued, running, succeeded hoặc failed", false, null)),
            AgentToolRisk.Read, (args, ct) => Jobs(args, ct)),
        new("get_job_log", "Đọc phần cuối log của một job.",
            Schema(("jobId", "string", "Job id", true, null), ("tail", "integer", "Số dòng cuối, 1-1000; mặc định 200", false, null)),
            AgentToolRisk.Read, (args, _) => Task.FromResult<object>(JobLog(args))),
        new("tail_logs", "Đọc log Core/job/instance/setup; core và setup có thể cần diagnostics mode.",
            Schema(("source", "string", "core, job, instance hoặc setup", true, ["core", "job", "instance", "setup"]),
                ("id", "string", "Instance id cho source=instance, job id cho source=job", false, null),
                ("tail", "integer", "Số dòng cuối, 1-1000; mặc định 100", false, null),
                ("level", "string", "trace, debug, info, warn, error, critical", false, null),
                ("contains", "string", "Lọc text, không phân biệt hoa thường", false, null)),
            AgentToolRisk.Read, (args, _) => Task.FromResult<object>(TailLogs(args))),
        new("query_system_log", "Truy vấn system log có cấu trúc: thời gian, level, component, instance, job, request id, text.",
            Schema(("from", "string", "ISO thời gian bắt đầu (tùy chọn)", false, null),
                ("to", "string", "ISO thời gian kết thúc (tùy chọn)", false, null),
                ("level", "string", "trace, debug, info, warn, error, critical", false, null),
                ("component", "string", "Component hoặc category", false, null),
                ("instanceId", "string", "Instance id", false, null),
                ("jobId", "string", "Job id", false, null),
                ("requestId", "string", "Request id", false, null),
                ("text", "string", "Text cần tìm", false, null),
                ("limit", "integer", "1-500; mặc định 100", false, null)),
            AgentToolRisk.Read, (args, ct) => QueryLog(args, ct)),
        new("get_activity", "Đọc activity feed mới nhất.",
            Schema(("instanceId", "string", "Instance id (tùy chọn)", false, null), ("limit", "integer", "1-200; mặc định 50", false, null)),
            AgentToolRisk.Read, (args, ct) => Activity(args, ct)),
        new("search_knowledge", "Tìm trong tài liệu vận hành GameWarden (docs, adapter guide, runbook). Dùng cho câu hỏi how-to, khái niệm hoặc quy trình; trả lời kèm nguồn.",
            Schema(("query", "string", "Từ khóa hoặc câu hỏi ngắn", true, null),
                ("limit", "integer", "1-10; mặc định 5", false, null)),
            AgentToolRisk.Read, (args, _) => Task.FromResult<object>(Knowledge(args))),
        new("get_metrics", "Đọc CPU và RAM của máy và các server đang chạy.",
            Schema(), AgentToolRisk.Read, (_, _) => Task.FromResult<object>(metrics.Report())),
        new("list_incidents", "Đọc incident gần đây, lọc theo instance và state.",
            Schema(("instanceId", "string", "Instance id (tùy chọn)", false, null),
                ("state", "string", "open, diagnosing, mitigating, verifying, resolved, closed (tùy chọn)", false, null),
                ("limit", "integer", "1-100; mặc định 20", false, null)),
            AgentToolRisk.Read, (args, ct) => Incidents(args, ct)),
        new("get_incident", "Đọc một incident cùng timeline sự kiện.",
            Schema(("incidentId", "string", "Incident id", true, null)),
            AgentToolRisk.Read, (args, ct) => Incident(args, ct)),
        new("get_notification_deliveries", "Đọc delivery gần đây của notification để kiểm tra alert đã gửi chưa.",
            Schema(("limit", "integer", "1-200; mặc định 50", false, null)),
            AgentToolRisk.Read, (args, ct) => Deliveries(args, ct)),
        new("check_update", "Kiểm tra GameWarden có bản mới hơn không.",
            Schema(), AgentToolRisk.Read, (_, ct) => UpdateCheck(ct)),
        new("get_diagnostics", "Đọc chẩn đoán sâu: system, processes, storage, network, events.",
            Schema(("section", "string", "system, processes, storage, network hoặc events", true, ["system", "processes", "storage", "network", "events"]),
                ("limit", "integer", "1-300; mặc định 100", false, null),
                ("log", "string", "Application hoặc System (events)", false, null),
                ("level", "string", "critical, error, warning, information (events)", false, null),
                ("hours", "integer", "1-168; mặc định 24 (events)", false, null)),
            AgentToolRisk.Read, (args, _) => Task.FromResult<object>(Diagnostics(args))),
        new("list_files", "Liệt kê file và folder dưới instance root, tối đa 500 entry. Gọi tool này trước khi đọc một file chưa biết đường dẫn.",
            Schema(("instanceId", "string", "Instance id", true, null),
                ("path", "string", "Đường dẫn tương đối; mặc định là instance root", false, null),
                ("depth", "integer", "1-4; mặc định 2", false, null)),
            AgentToolRisk.Read, (args, _) => Task.FromResult<object>(ListFiles(args))),
        new("read_file", "Đọc một file text dưới folder instance, tối đa 1 MB và đã redacted. Nếu chưa biết đường dẫn, gọi list_files trước; log mod dùng get_mod_log.",
            Schema(("instanceId", "string", "Instance id", true, null), ("path", "string", "Đường dẫn tương đối dưới instance root", true, null)),
            AgentToolRisk.Read, (args, _) => Task.FromResult<object>(ReadFile(args))),
        new("list_mods", "Đọc mod set của một instance: loader, mods đang bật/tắt, set active/previous/pending, share code và restart required. Dùng cho câu hỏi mod nào đang active.",
            Schema(("instanceId", "string", "Instance id", true, null)),
            AgentToolRisk.Read, (args, ct) => Mods(args, ct)),
        new("get_mod_log", "Đọc log của mod loader (ví dụ BepInEx/LogOutput.log) trong set đang chạy; dùng thay vì read_file cho log mod.",
            Schema(("instanceId", "string", "Instance id", true, null),
                ("tail", "integer", "Số dòng cuối, 1-2000; mặc định 200", false, null)),
            AgentToolRisk.Read, (args, ct) => ModLog(args, ct)),
        new("control_instance", "Start, stop hoặc restart một game server. Cần Owner approve.",
            Schema(("instanceId", "string", "Instance id", true, null),
                ("action", "string", "start, stop hoặc restart", true, ["start", "stop", "restart"]),
                ("force", "boolean", "Bỏ qua kiểm tra tài nguyên (force)", false, null)),
            AgentToolRisk.Write, (args, ct) => Control(args, ct)),
        new("send_console", "Gửi một lệnh console được allow-list. Cần Owner approve.",
            Schema(("instanceId", "string", "Instance id", true, null), ("command", "string", "Lệnh console", true, null)),
            AgentToolRisk.Write, (args, ct) => SendConsole(args, ct)),
        new("install_instance", "Cài hoặc cập nhật file game bằng SteamCMD. Cần Owner approve.",
            Schema(("instanceId", "string", "Instance id", true, null), ("action", "string", "install hoặc update", true, ["install", "update"])),
            AgentToolRisk.Write, (args, ct) => Install(args, ct)),
        new("wait_job", "Chờ một job xong trong tối đa N giây rồi trả trạng thái.",
            Schema(("jobId", "string", "Job id", true, null), ("timeoutSeconds", "integer", "1-120; mặc định 60", false, null)),
            AgentToolRisk.Read, (args, ct) => WaitJob(args, ct)),
    ];

    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["core.status"] = "status",
        ["instance.list"] = "list_instances",
        ["instance.get"] = "get_instance",
        ["instance.control"] = "control_instance",
        ["instance.console.tail"] = "get_console",
        ["instance.console.send"] = "send_console",
        ["logs.tail"] = "tail_logs",
        ["logs.query"] = "query_system_log",
        ["mods.list"] = "list_mods",
        ["mods.log"] = "get_mod_log",
        ["files.list"] = "list_files",
        ["files.read"] = "read_file",
        ["jobs.list"] = "list_jobs",
        ["jobs.log"] = "get_job_log",
        ["jobs.wait"] = "wait_job",
        ["metrics.get"] = "get_metrics",
        ["diagnostics.get"] = "get_diagnostics",
        ["activity.list"] = "get_activity",
        ["knowledge.search"] = "search_knowledge",
        ["incidents.list"] = "list_incidents",
        ["incidents.get"] = "get_incident",
        ["updates.check"] = "check_update",
        ["notifications.deliveries"] = "get_notification_deliveries",
    };

    public AgentTool? Find(string name) =>
        All.FirstOrDefault(tool => tool.Name == (Aliases.TryGetValue(name, out var alias) ? alias : name));

    /// <summary>Run a read tool, or a write tool after the approval layer has allowed it.</summary>
    public async Task<string> ExecuteAsync(AgentTool tool, JsonElement args, CancellationToken cancellationToken)
    {
        var result = await tool.Handler(args, cancellationToken);
        var json = redactor.Redact(JsonSerializer.Serialize(result, Json));
        return json.Length <= MaxToolOutput
            ? json
            : json[..MaxToolOutput] + "\n… [tool output truncated; ask a narrower query]";
    }

    private async Task<object> Status(CancellationToken cancellationToken)
    {
        var snapshots = supervisor.List();
        var failed = (await jobs.ListAsync(null, cancellationToken))
            .Where(job => job.State == JobState.Failed && job.FinishedAt >= time.GetUtcNow().AddHours(-24))
            .Take(20)
            .Select(job => new { job.Id, job.Type, job.InstanceId, job.Message, job.FinishedAt })
            .ToList();
        var machineView = machine.Snapshot(options.Value.DataRoot);
        return new
        {
            version = CoreInfo.Version,
            now = time.GetUtcNow(),
            instances = snapshots.Select(Instance).ToList(),
            failedJobs = failed,
            machine = new
            {
                machineView.CpuCores,
                machineView.RamTotalMb,
                machineView.RamFreeMb,
                machineView.DiskFreeMb,
            },
        };
    }

    private object Instances() => supervisor.List().Select(Instance).ToList();

    private static object Job(JobRecord job) => new
    {
        job.Id,
        job.Type,
        job.InstanceId,
        job.State,
        job.Stage,
        job.Progress,
        job.Message,
        job.CreatedAt,
        job.FinishedAt,
    };

    private object One(string id) =>
        supervisor.Find(id) is { } actor ? Instance(actor.Snapshot) : throw new AgentToolException($"No instance '{id}'.");

    private object Instance(InstanceSnapshot snapshot) =>
        AgentInstanceView.From(snapshot) with
        {
            LastProblem = snapshot.LastProblem is null ? null : redactor.Redact(snapshot.LastProblem),
        };

    private async Task<object> Console(JsonElement args)
    {
        var id = Text(args, "instanceId");
        var actor = supervisor.Find(id) ?? throw new AgentToolException($"No instance '{id}'.");
        var lines = await actor.TailAsync(Math.Clamp(Int(args, "tail", 200), 1, 1000));
        return lines.Select(line => new
        {
            at = line.At,
            level = line.Stream == OutputStream.Stderr ? "error" : "info",
            text = redactor.Redact(line.Text),
        }).ToList();
    }

    private async Task<object> Settings(JsonElement args)
    {
        var id = Text(args, "instanceId");
        var actor = supervisor.Find(id) ?? throw new AgentToolException($"No instance '{id}'.");
        return await actor.GetSettingsAsync();
    }

    private async Task<object> Jobs(JsonElement args, CancellationToken cancellationToken)
    {
        var instanceId = OptionalText(args, "instanceId");
        var state = OptionalText(args, "state");
        var list = await jobs.ListAsync(instanceId, cancellationToken);
        return list.Where(job => state is null || string.Equals(job.State.ToString(), state, StringComparison.OrdinalIgnoreCase))
            .Select(Job)
            .ToList();
    }

    private object JobLog(JsonElement args)
    {
        var id = Text(args, "jobId");
        return logs.Tail("job", id, Int(args, "tail", 200), null, null);
    }

    private object TailLogs(JsonElement args) =>
        logs.Tail(OptionalText(args, "source") ?? "core", OptionalText(args, "id"), Int(args, "tail", 100),
            OptionalText(args, "level"), OptionalText(args, "contains"));

    private async Task<object> QueryLog(JsonElement args, CancellationToken cancellationToken) =>
        await systemLog.QueryAsync(
            Date(args, "from"),
            Date(args, "to"),
            OptionalText(args, "level"),
            OptionalText(args, "component"),
            OptionalText(args, "instanceId"),
            OptionalText(args, "jobId"),
            OptionalText(args, "requestId"),
            OptionalText(args, "text"),
            null,
            Math.Clamp(Int(args, "limit", 100), 1, 500),
            cancellationToken);

    private async Task<object> Activity(JsonElement args, CancellationToken cancellationToken) =>
        (await activity.ListAsync(OptionalText(args, "instanceId"), null, Math.Clamp(Int(args, "limit", 50), 1, 200), cancellationToken))
            .Select(item => new { item.At, item.Kind, item.Level, item.InstanceId, item.JobId, item.User, item.Message })
            .ToList();

    private async Task<object> Incidents(JsonElement args, CancellationToken cancellationToken)
    {
        var instanceId = OptionalText(args, "instanceId");
        var state = OptionalText(args, "state");
        var limit = Math.Clamp(Int(args, "limit", 20), 1, 100);
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentIncidents.AsNoTracking()
            .Where(row => (instanceId == null || row.InstanceId == instanceId)
                && (state == null || row.State == state))
            .OrderByDescending(row => row.OpenedAt)
            .Take(limit)
            .Select(row => new
            {
                row.Id,
                row.InstanceId,
                row.Severity,
                row.State,
                row.Title,
                row.Summary,
                row.Trigger,
                row.OpenedAt,
                row.ResolvedAt,
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<object> Incident(JsonElement args, CancellationToken cancellationToken)
    {
        var id = Text(args, "incidentId");
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new AgentToolException($"No incident '{id}'.");
        var timeline = await context.AgentIncidentEvents.AsNoTracking()
            .Where(row => row.IncidentId == id)
            .OrderBy(row => row.Id)
            .Take(100)
            .Select(row => new { row.At, row.Type, row.Text, row.TraceId })
            .ToListAsync(cancellationToken);
        return new
        {
            incident.Id,
            incident.InstanceId,
            incident.Severity,
            incident.State,
            incident.Title,
            incident.Summary,
            incident.Trigger,
            incident.OpenedAt,
            incident.AcknowledgedAt,
            incident.ResolvedAt,
            incident.ClosedAt,
            timeline,
        };
    }

    private async Task<object> Deliveries(JsonElement args, CancellationToken cancellationToken) =>
        await notifications.DeliveriesAsync(Math.Clamp(Int(args, "limit", 50), 1, 200), cancellationToken);

    private async Task<object> UpdateCheck(CancellationToken cancellationToken) =>
        await updates.CheckAsync(cancellationToken);

    private object Knowledge(JsonElement args) =>
        knowledge.Search(Text(args, "query"), Math.Clamp(Int(args, "limit", 5), 1, 10));

    private object Diagnostics(JsonElement args)
    {
        var section = Text(args, "section");
        return section switch
        {
            "system" => diagnostics.SystemInfo(),
            "processes" => diagnostics.Processes(Int(args, "limit", 100)),
            "storage" => diagnostics.Storage(),
            "network" => diagnostics.Network(),
            "events" => diagnostics.Events(OptionalText(args, "log"), OptionalText(args, "level"), Int(args, "hours", 24), Int(args, "limit", 100)),
            _ => throw new AgentToolException("section must be system, processes, storage, network or events."),
        };
    }

    private object ListFiles(JsonElement args) =>
        files.List(Text(args, "instanceId"), OptionalText(args, "path"), Int(args, "depth", 2));

    private object ReadFile(JsonElement args)
    {
        try
        {
            return files.Read(Text(args, "instanceId"), Text(args, "path"));
        }
        catch (AgentRequestException ex) when (ex.Code == "not_found")
        {
            throw new AgentToolException($"{ex.Message} Use list_files to find the path, or get_mod_log for the loader log.");
        }
    }

    private async Task<object> Mods(JsonElement args, CancellationToken cancellationToken)
    {
        var result = await mods.GetAsync(Text(args, "instanceId"), cancellationToken);
        return result.Mods ?? throw new AgentToolException(result.Error ?? "No mod set for this instance.");
    }

    private async Task<object> ModLog(JsonElement args, CancellationToken cancellationToken)
    {
        var result = await mods.LogAsync(
            Text(args, "instanceId"),
            Math.Clamp(Int(args, "tail", 200), 1, ModsService.LogMaxTail),
            cancellationToken);
        return result.Value ?? throw new AgentToolException(result.Error ?? "No mod log for this instance.");
    }

    private async Task<object> Control(JsonElement args, CancellationToken cancellationToken)
    {
        var id = Text(args, "instanceId");
        var action = Text(args, "action");
        var actor = supervisor.Find(id) ?? throw new AgentToolException($"No instance '{id}'.");
        var force = Bool(args, "force");
        var result = action switch
        {
            "start" => await actor.StartAsync(force),
            "stop" => await actor.StopAsync(),
            "restart" => await actor.RestartAsync(force),
            _ => throw new AgentToolException("action must be start, stop or restart."),
        };
        activity.Add("command", "info", $"{action} requested by the Operations Agent.", id, null, "agent-host");
        return new { result.Outcome, result.Message, instance = Instance(result.Instance) };
    }

    private async Task<object> SendConsole(JsonElement args, CancellationToken cancellationToken)
    {
        var id = Text(args, "instanceId");
        var command = Text(args, "command");
        var actor = supervisor.Find(id) ?? throw new AgentToolException($"No instance '{id}'.");
        var result = await actor.SendConsoleAsync(command);
        activity.Add("command", "info", "Console command sent by the Operations Agent.", id, null, "agent-host");
        return new { result.Outcome, result.Message };
    }

    private async Task<object> Install(JsonElement args, CancellationToken cancellationToken)
    {
        var id = Text(args, "instanceId");
        var action = Text(args, "action");
        var result = await jobs.StartInstallAsync(id, update: action == "update", force: false, cancellationToken);
        if (result.Job is null)
        {
            throw new AgentToolException(result.Error ?? "The job did not start.");
        }

        activity.Add("job", "info", $"{action} requested by the Operations Agent.", id, result.Job.Id, "agent-host");
        return new { jobId = result.Job.Id, result.StatusCode };
    }

    private async Task<object> WaitJob(JsonElement args, CancellationToken cancellationToken)
    {
        var id = Text(args, "jobId");
        var timeout = Math.Clamp(Int(args, "timeoutSeconds", 60), 1, 120);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var job = await jobs.FindAsync(id, cancellationToken);
            if (job is null)
            {
                throw new AgentToolException($"No job '{id}'.");
            }

            if (job.State is JobState.Succeeded or JobState.Failed)
            {
                return new { job.Id, job.State, job.Stage, job.Message, job.FinishedAt };
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return new { id, state = "stillRunning" };
    }

    private static JsonElement Schema(params (string Name, string Type, string Description, bool Required, string[]? Values)[] parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var parameter in parameters)
        {
            var property = new JsonObject
            {
                ["type"] = parameter.Type,
                ["description"] = parameter.Description,
            };
            if (parameter.Values is { Length: > 0 } values)
            {
                property["enum"] = new JsonArray(values.Select(value => (JsonNode)value!).ToArray());
            }

            properties[parameter.Name] = property;
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        });
    }

    private static string Text(JsonElement args, string name) =>
        OptionalText(args, name) ?? throw new AgentToolException($"Missing '{name}'.");

    private static string? OptionalText(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Int(JsonElement args, string name, int fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static bool Bool(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Date(JsonElement args, string name) =>
        DateTimeOffset.TryParse(OptionalText(args, name), out var at) ? at : null;
}

public sealed class AgentToolException(string message) : Exception(message);
