# Boxwright.Qmp

A small, **dependency-free** C# client for the **QEMU Machine Protocol (QMP)** and the
**QEMU Guest Agent (QGA)**. It talks to a running `qemu-system-*` over a TCP or
Unix-domain socket — connect, run correlated commands, observe events — using only
`System.Text.Json` and the base class library (no third-party packages, no UI framework).

It's the protocol layer of [Boxwright](https://github.com/eko1495/Boxwright), a
cross-platform QEMU GUI, and is designed to stand on its own.

## Install

```
dotnet add package Boxwright.Qmp
```

## QMP — connect and run a command

```csharp
using System.Text.Json;
using Boxwright.Qmp;

await using var client = new QmpClient();

// Performs the greeting + qmp_capabilities handshake.
await client.ConnectAsync(QmpEndpoint.Tcp("127.0.0.1", 4444));

// Correlated request/response.
JsonElement status = await client.ExecuteAsync("query-status");
Console.WriteLine(status.GetProperty("status").GetString()); // e.g. "running"

// Asynchronous events are exposed as IObservable<QmpEvent> on client.Events.
```

Launch QEMU with a QMP server, e.g.:

```
qemu-system-x86_64 ... -qmp tcp:127.0.0.1:4444,server,nowait
```

## QEMU Guest Agent

```csharp
using Boxwright.Qmp;

await using var agent = new QgaClient();
await agent.ConnectAsync("127.0.0.1", 4445);
if (await agent.PingAsync())
{
    foreach (string ip in await agent.GetIpAddressesAsync())
        Console.WriteLine(ip);
}
```

## Scope

- Connect to a QMP endpoint (TCP or Unix socket) and run the `qmp_capabilities` handshake.
- Send `execute` commands with correlated, awaitable replies.
- Surface asynchronous QMP events (`SHUTDOWN`, `RESET`, `STOP`, …) as a stream.
- Probe `query-qmp-schema` for capability detection.
- A minimal QEMU Guest Agent client (ping, shutdown, guest IP addresses).

Connection retry/policy logic lives in the consuming application, not here. Tested against
an in-memory loopback fake server — no live QEMU required.

## License

MIT.
