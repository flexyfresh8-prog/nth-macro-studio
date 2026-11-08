# UI ⇄ Engine bridge protocol

The identical HTML/CSS/JS interface runs in two places:

- **Browser preview** (`preview/index.html`): mock data, in-page input only.
- **Windows app**: WebView2 loads `wwwroot/index.html` and this bridge connects it to the native engine.

Detection: the UI checks `window.chrome.webview` — when present it is running inside WebView2.

## Transport

| Direction | Mechanism |
| --- | --- |
| JS → C# | `window.chrome.webview.postMessage({type, payload})` |
| C# → JS | `CoreWebView2.PostWebMessageAsString(json)` |

Envelope (both directions): `{ "type": "domain:action", "payload": { ... } }`, camelCase JSON.

## JS → C#

| Type | Payload | Purpose |
| --- | --- | --- |
| `app:hello` | `{}` | Handshake |
| `macro:list` | `{}` | Request all macros |
| `macro:save` | `{macro}` | Upsert one macro |
| `macro:delete` | `{id}` | Delete one macro |
| `macro:export` | `{id}` | Get raw JSON of one macro |
| `macro:import` | `{json}` | Import a macro JSON string |
| `record:start` | `{captureKeyboard, captureMouseClicks, captureMouseMoves, mouseMoveSampleMs, delayMode}` | Arm system-wide recording |
| `record:pause` / `record:resume` | `{}` | Pause/resume capture |
| `record:stop` | `{}` | Stop + save, returns the macro |
| `play:start` | `{macroId, loops, speed}` | Start playback (`loops: 0` = infinite) |
| `play:pause` / `play:resume` | `{}` | Pause/resume playback |
| `play:stop` | `{}` | Stop playback |
| `play:emergency` | `{}` | Emergency stop |
| `settings:save` | `{settings}` | Persist settings |
| `hotkey:set` | `{slot, combo}` | Register global hotkey. `slot`: `startStop`, `pauseResume`, `emergency`, `record`. `combo` format: `Ctrl+Alt+M` (parts Ctrl/Alt/Shift/Win + one final key) |

## C# → JS

| Type | Payload |
| --- | --- |
| `app:ready` | `{version, platform:"windows", macrosPath, preview:false}` |
| `macro:list` | `{macros:[Macro]}` |
| `macro:saved` | `{macro}` |
| `macro:deleted` | `{id}` |
| `macro:export` | `{id, json}` |
| `macro:imported` | `{macro}` |
| `record:started` / `record:paused` / `record:resumed` | `{}` |
| `record:stopped` | `{macro, saved:true}` |
| `record:event` | `{event:{kind:"keyDown"\|"keyUp"\|"mouseDown"\|"mouseUp"\|"mouseMove", key?, mods?, button?, x?, y?, tMs}}` |
| `play:started` / `play:paused` / `play:resumed` | `{}` |
| `play:progress` | `{macroId, index, total, loop, loops, elapsedMs, action, status:"running"\|"paused"\|"done"\|"stopped"}` |
| `play:stopped` | `{reason:"user"\|"emergency"}` |
| `sys:stats` | `{cpu, ram}` — pushed every second |
| `hotkey:set:result` | `{slot, ok, combo}` |
| `hotkey:triggered` | `{slot}` — global hotkey fired |
| `app:error` | `{message}` |

## Macro JSON schema

```json
{
  "id": "m_9f2c1e7a4",
  "name": "Example Macro",
  "description": "What it automates",
  "tags": ["workflow"],
  "hotkey": "Ctrl+Alt+7",
  "speed": 1,
  "loops": 1,
  "createdAt": "2026-01-01T09:00:00.000Z",
  "updatedAt": "2026-01-01T09:30:00.000Z",
  "actions": [
    { "id": "a_1", "kind": "key",   "tMs": 0,   "key": "h", "mods": ["ctrl"], "button": "left", "x": 0,   "y": 0,   "delayMs": 120, "durationMs": 45 },
    { "id": "a_2", "kind": "click", "tMs": 200, "key": "",  "mods": [],       "button": "left", "x": 412, "y": 236, "delayMs": 250, "durationMs": 60 },
    { "id": "a_3", "kind": "delay", "tMs": 510, "key": "",  "mods": [],       "button": "left", "x": 0,   "y": 0,   "delayMs": 500, "durationMs": 0 }
  ]
}
```

`kind` values: `key`, `click`, `move`, `delay`. Delay semantics: `delayMs` is the wait *before* the action fires; `durationMs` is how long the key/button is held.

## Safety model

- Hooks (`SetWindowsHookEx` WH_KEYBOARD_LL / WH_MOUSE_LL) are installed **only while a recording is armed** and removed on stop/pause-exit.
- Playback injects input exclusively via `SendInput` — the standard user-session automation channel. No DLL injection, no memory reads, no driver-level interposition, ever.
- `EmergencyStop` cancels the playback task between input slices and releases any held key.
