# SWTORWrapper

Small launcher wrapper for Star Wars: The Old Republic. It starts the game launcher, waits for
`swtor.exe` to appear, and injects `SWTORFix.dll` into it.

## Usage

1. Put `SWTORWrapper.exe` and `SWTORFix.dll` in the same folder as `launcher.exe`.
2. Run `SWTORWrapper.exe` instead of `launcher.exe`.

It works the same as launching the game normally — the launcher still runs, you still log in,
and the wrapper exits once the injection is done.

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
