# LinkManager — Automatic Network Adapter Failover

> [!CAUTION]
> Contains vibecoded source code, use with caution. I uh...</br>
> I'm not responsible for anything that may happen...</br>
> It works on my machine, that's all I'll say.</br>

A Windows system-tray application that monitors multiple network adapters and automatically adjusts their routing priority based on actual internet connectivity — without ever disabling an adapter. If your primary connection drops, it silently falls back to a backup. When the primary recovers, it switches back.

## Features

- **Multi-adapter failover** — configure 2+ adapters in priority order; failover and restore happen automatically
- **Priority groups** — give two adapters the same priority number to load-balance between them instead of strict failover
- **Dual-mode connectivity probe** — tests each adapter via ICMP ping *and* HTTP, so it works even when ICMP is firewalled
- **Hysteresis / anti-flap** — requires N consecutive failures to declare an adapter dead, and M consecutive successes to restore it; prevents route flapping on unstable connections
- **Hold-down timer** — after a switch, waits a configurable cooldown period before polling again, giving the OS time to flush DNS and services like Tailscale time to re-authenticate
- **Flexible adapter identification** — identify adapters by interface name, hardware description, or static IP address; description matching handles USB tethering devices that Windows renames on replug
- **Event hooks** — run any process/script automatically after each failover (e.g. restart Tailscale)
- **Tray icon** — color-coded icon shows system state at a glance (green = primary, orange = backup, red = all dead, blue = switching)
- **Hot-reload config** — edit `config.json` while the app is running; changes apply within seconds without restarting
- **Auto-start** — registers itself in Task Scheduler to run at logon with elevated privileges

## Requirements

- Windows 10 / 11
- .NET 8 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Must run as Administrator** (required to change interface metrics via `netsh`)

## Setup

### 1. Build

```
dotnet build -c Release
```

The output will be in `LinkManager\bin\Release\net8.0-windows\`.

### 2. Configure `config.json`

Edit `config.json` next to the executable before first run. The file is created with defaults if it doesn't exist.

```jsonc
{
  "adapters": [
    { "identifier": "vEthernet (VMSwitch)", "priority": 0 },  // primary
    { "identifier": "Wi-Fi",                "priority": 1 },  // first backup
    { "identifier": "Remote NDIS Compatible Device", "priority": 2 }  // last resort (USB tether)
  ],
  "testEndpoints":     ["8.8.8.8", "1.1.1.1", "9.9.9.9"],
  "httpTestEndpoints": [
    "http://www.msftconnecttest.com/connecttest.txt",
    "https://www.google.com/generate_204",
    "https://cp.cloudflare.com"
  ],
  "pollIntervalMs":    5000,
  "switchCooldownMs":  15000,
  "failThreshold":     3,
  "restoreThreshold":  5,
  "probeTimeoutMs":    2000,
  "hooks": [],
  "enableNotifications": true,
  "startWithWindows":    true
}
```

### 3. Run

Right-click the exe → **Run as administrator**, or let Task Scheduler handle elevation if `startWithWindows` is enabled.

---

## Adapter Identifier Formats

The `identifier` field accepts any of these:

| Format | Example | Use case |
|---|---|---|
| Interface name | `"Wi-Fi"`, `"Ethernet"` | Stable adapters |
| Description substring | `"Remote NDIS Compatible Device"` | USB tethering (survives rename on replug) |
| IP address | `"192.168.88.200"` | Adapters with a known static IP |

## Priority Groups

Adapters with the **same** priority number are treated as a load-balanced group — both get metric `10` simultaneously and Windows balances traffic between them. Failover only occurs when the **entire group** loses connectivity.

```jsonc
{ "identifier": "Ethernet 1", "priority": 0 },  // ─┐ load-balanced
{ "identifier": "Ethernet 2", "priority": 0 },  // ─┘
{ "identifier": "Wi-Fi",      "priority": 1 }   //   strict fallback
```

## Hooks (e.g. Restart Tailscale)

Add entries to the `hooks` array. Hooks run sequentially after every group switch:

```jsonc
"hooks": [
    {
      "Type": "process",
      "Path": "C:\\Program Files\\Tailscale\\tailscale.exe",
      "Args": "down"
    },
    {
      "Type": "process",
      "Path": "C:\\Program Files\\Tailscale\\tailscale.exe",
      "Args": "up"
    }
]
```

## Tray Icon

| Color | Meaning |
|---|---|
| 🟢 Green | Primary group (priority 0) is active |
| 🟠 Orange | Running on a backup group |
| 🔴 Red | All adapters have no internet; metrics reset to Windows defaults |
| 🔵 Blue | Switch in progress / hold-down cooldown |
| ⚫ Gray | Paused |

Right-click the tray icon for options: force re-evaluate, pause, toggle notifications, toggle auto-start, open/reload config, exit.
