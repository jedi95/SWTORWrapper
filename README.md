# SWTORWrapper

Small launcher wrapper for Star Wars: The Old Republic. It starts the game launcher, waits for
`swtor.exe` to appear, and injects `SWTORFix.dll` into it.

The fix itself is [SWTORFix](https://git.jedi95.com/jedi95/SWTORFix).

## Usage

1. Put `SWTORWrapper.exe` and `SWTORFix.dll` in the same folder as `launcher.exe`.
2. Run `SWTORWrapper.exe` instead of `launcher.exe`.

It works the same as launching the game normally — the launcher still runs, you still log in,
and the wrapper exits once the injection is done.

## Steam

Steam starts `launcher.exe` itself, so there is nothing for the wrapper to sit in front of.
Use watch mode instead:

```
SWTORWrapper.exe -watch
```

It sits in the tray, launches nothing, and injects into any `swtor.exe` that appears — however
it was started. Put a shortcut to it (with the `-watch` argument) in `shell:startup`, then just
press Play in Steam as usual. Exit it from its tray menu.

Do **not** replace `launcher.exe` with the wrapper to force the Steam path. The SWTOR launcher
updates itself, and Steam's "Verify integrity of game files" restores the original, so the
replacement does not survive an update.

## How it works

- Starts `launcher.exe`.
- Polls for a process named `swtor` every 200 ms.
- Allocates the DLL path inside the target process and calls `LoadLibraryA` through
  `CreateRemoteThread`.
- Exits after injecting, or 10 seconds after the launcher exits.

`SWTORFix.dll` must sit beside `SWTORWrapper.exe`; the path is built from the working directory.

## Build

Visual Studio 2022, .NET Framework 4.8, x64. Output is `SWTORWrapper.exe`.

## License

MIT — see [LICENSE](LICENSE).
