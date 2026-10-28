using Microsoft.AspNetCore.SignalR;

namespace GameWarden.Core.Realtime;

/// <summary>
/// <c>/hubs/events</c> (docs/05-api.md §3). Everyone signed in gets instance, job and activity events; console lines go
/// only to connections that asked for that instance's console. The server's messages are sent by <see cref="RealtimeBridge"/>.
/// </summary>
public sealed class EventsHub : Hub
{
    public Task SubscribeConsole(string instanceId) => Groups.AddToGroupAsync(Context.ConnectionId, ConsoleGroup(instanceId));

    public Task UnsubscribeConsole(string instanceId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, ConsoleGroup(instanceId));

    public static string ConsoleGroup(string instanceId) => $"console:{instanceId}";
}
