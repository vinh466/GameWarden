using System.Text.Json;
using GameWarden.Core.AgentHost;
using GameWarden.Core.Data;
using GameWarden.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Tests;

/// <summary>Kernel contracts that do not need a real model or Discord connection (docs/21).</summary>
public sealed class AgentKernelTests
{
    [Fact]
    public async Task Built_in_crash_runbook_loads()
    {
        await using var harness = await CoreHarness.StartAsync();
        var runbooks = harness.Get<AgentRunbookService>();

        var runbook = runbooks.Find("generic.crash-triage");

        Assert.NotNull(runbook);
        Assert.Contains(runbook!.Steps, step => step.Use == "core.status");
        Assert.Contains(runbook.Steps, step => step.Use == "model.summarize");
    }

    [Fact]
    public async Task Crash_activity_opens_an_incident()
    {
        await using var harness = await CoreHarness.StartAsync();
        var incidents = harness.Get<AgentIncidentService>();
        await incidents.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await incidents.ProcessAsync(new ActivityRecord
            {
                At = DateTimeOffset.UtcNow,
                Kind = "instance.crashed",
                Level = "error",
                InstanceId = "valheim-main",
                Message = "Crashed: exited with code 3.",
            }, TestContext.Current.CancellationToken);

            AgentIncidentRecord? incident = null;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            await using var db = await harness.Get<IDbContextFactory<GameWardenDb>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
            while (DateTime.UtcNow < deadline)
            {
                incident = await db.AgentIncidents.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.InstanceId == "valheim-main" && row.Trigger == "instance.crashed", TestContext.Current.CancellationToken);
                if (incident is not null)
                {
                    break;
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.NotNull(incident);
            Assert.Equal("open", incident!.State);
            Assert.Contains("Crashed", incident.Title);
        }
        finally
        {
            await incidents.StopAsync(TestContext.Current.CancellationToken);
        }
    }


    [Fact]
    public async Task The_activity_event_queue_opens_an_incident()
    {
        await using var harness = await CoreHarness.StartAsync();
        var incidents = harness.Get<AgentIncidentService>();
        await incidents.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            harness.Get<CoreEvents>().RaiseActivityAdded(new ActivityRecord
            {
                At = DateTimeOffset.UtcNow,
                Kind = "instance.faulted",
                Level = "error",
                InstanceId = "valheim-main",
                Message = "Faulted: crash loop.",
            });

            AgentIncidentRecord? incident = null;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            await using var db = await harness.Get<IDbContextFactory<GameWardenDb>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
            while (DateTime.UtcNow < deadline)
            {
                incident = await db.AgentIncidents.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.InstanceId == "valheim-main" && row.Trigger == "instance.faulted", TestContext.Current.CancellationToken);
                if (incident is not null)
                {
                    break;
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.NotNull(incident);
        }
        finally
        {
            await incidents.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Writes_require_approval_by_default()
    {
        await using var harness = await CoreHarness.StartAsync();
        var tools = harness.Get<AgentTools>();
        var policy = harness.Get<AgentPolicyService>();
        var tool = tools.Find("control_instance");
        Assert.NotNull(tool);

        using var args = JsonDocument.Parse("""{"instanceId":"valheim-main","action":"restart"}""");
        var decision = await policy.EvaluateAsync(
            tool!,
            args.RootElement,
            "valheim-main",
            "test-turn",
            "test-trace",
            TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
        Assert.True(decision.RequiresApproval);
        Assert.Equal("instance-restart", decision.Rule);
    }
}
