# SWTORWrapper

Small launcher wrapper for Star Wars: The Old Republic. It starts the game launcher, waits for
the client to appear, and injects `SWTORFix.dll` into it. Both client executables are supported:
`swtor.exe` and `swtor_RC2.exe`, the DX12 renderer client a recent patch added.

The fix itself is [SWTORFix](https://git.jedi95.com/jedi95/SWTORFix).

## Usage

1. Put `SWTORWrapper.exe` and `SWTORFix.dll` in the same folder as `launcher.exe`.
2. Run `SWTORWrapper.exe` instead of `launcher.exe`.

It works the same as launching the game normally — the launcher still runs, you still log in,
and the wrapper exits once the injection is done.

## Configuration

Optional `SWTORWrapper.ini` beside `SWTORWrapper.exe`:

```ini
[launcher]
CacheHours=0
ProcessNames=swtor,swtor_RC2
```

| key | default | meaning |
|---|---|---|
| `CacheHours` | `0` | Hours a cached command line may be reused. `0` disables the cache; values above `48` are clamped to `48`. |
| `ProcessNames` | `swtor,swtor_RC2` | Comma-separated process names to inject into. `.exe` is optional and case does not matter. Set this if a future patch renames the client again — no rebuild needed. |

With the default of `0` the cache is never read and never written — the wrapper always goes
through `launcher.exe`. Set it to a positive number to skip the launcher on subsequent runs.

`-ignorecache` (also `--ignorecache`, `/ignorecache`) forces the launcher for a single run
regardless of the setting.

The ini is read from the executable's own directory, not the working directory, and is parsed
as UTF-8 or UTF-16 — encoding is detected, not assumed.

## Cached credential

When caching is enabled, the wrapper writes `swtor_cmdline_cache.txt` **beside the executable**
after a launcher run:

```
<UTC timestamp, round-trip format>
<full command line of the client, including the login token>
```

That file is a live credential until the token expires. Treat it accordingly, and delete it to
force a fresh login. It is git-ignored. The cache records whichever client the launcher started,
so it replays directly into `swtor.exe` or `swtor_RC2.exe` as appropriate.

Every cache decision is reported through `OutputDebugString`, so DebugView shows the resolved
ini path, the parsed value, whether the cache was used, and whether the write succeeded.

## Steam

Steam starts `launcher.exe` itself, so there is nothing for the wrapper to sit in front of.
Use watch mode instead:

```
SWTORWrapper.exe -watch
```

It sits in the tray, launches nothing, and injects into any supported client (`swtor.exe` or
`swtor_RC2.exe`) that appears — however it was started. Put a shortcut to it (with the `-watch` argument) in `shell:startup`, then just
press Play in Steam as usual. Exit it from its tray menu.

Do **not** replace `launcher.exe` with the wrapper to force the Steam path. The SWTOR launcher
updates itself, and Steam's "Verify integrity of game files" restores the original, so the
replacement does not survive an update.

## How it works

- Starts `launcher.exe`.
- Polls for a process named `swtor` or `swtor_RC2` every 200 ms, so whichever client the
  launcher starts gets injected.
- Allocates the DLL path inside the target process and calls `LoadLibraryA` through
  `CreateRemoteThread`.
- Exits after injecting, or 10 seconds after the launcher exits.

Every stage that can fail (open, allocate, write, create thread) reports through
`OutputDebugString` with the target image and pid, including which executable was injected.

`SWTORFix.dll` must sit beside `SWTORWrapper.exe`; the path is resolved from the executable's
directory, so a shortcut or Steam launch with a different working directory still finds it.

## Build

Visual Studio 2022, .NET Framework 4.8, x64. Output is `SWTORWrapper.exe`.

## License

MIT — see [LICENSE](LICENSE).
