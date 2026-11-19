# Flyweight

A tiny video compressor for Windows 10 and 11: drop a video in, get a smaller one out. It's C# and WPF on .NET Framework 4.8, which ships with Windows, built with the .NET SDK, and it uses the ffmpeg already on the PC. One small exe installs itself, and `docs/` is its website, getflyweight.com. The README says what it does and how to build it; this file is for working on it.

## Layout

- `src/Flyweight/Core`: the engine. `CommandBuilder.cs` decides everything passed to ffmpeg when compressing, `FrameGrabber.cs` the pictures you trim by. `Seeking.cs` and `CommandBuilder.cs` explain how cuts land on the exact frame.
- `src/Flyweight/UI`: the windows. `tests/Flyweight.Tests`: the xUnit tests.
- `tests/e2e`: checks that drive the real program. `tests/hidden-desktop`: the private desktop the tests run on.
- `docs/`: the site. `redirects/`: a small Worker that sends http and www to https://getflyweight.com.

## Never disturb the PC you're on

Someone is using this PC while you work. Nothing you run may open a window where they can see it, take focus, move the mouse, or make a sound.

- Run tests with `pwsh tests/run-tests.ps1`, which takes about 3 minutes, or add `-Filter "FullyQualifiedName~TrimSelectionTests"` for some of them. They run on a hidden desktop called `VideoCompressorTests`, and the log lands in `TestResults/logs`. Plain `dotnet test` and `build.ps1` are safe but incomplete: tests marked `[OnHiddenDesktopFact]` skip themselves anywhere else.
- A pass is nothing failed and nothing skipped. The runners exit with 1 otherwise; don't judge a run by its log alone.
- A test that shows a window or opens the sound output must be `[OnHiddenDesktopFact]`, so it can never run on a real desktop. Tests hand the player `PretendSound`; the few that need the real sound output play silent clips with the test process muted (`Hush`).
- Test classes that build windows go in `[Collection(WindowTests.Name)]`, because WPF can't load two windows' layouts at once. Ones that time things without a window go in `[Collection(ClockTests.Name)]`.
- Read modifier keys from the key event's `KeyboardDevice`. `Keyboard.Modifiers` also sees keys the person at the PC is pressing elsewhere.
- One run at a time: runs share the hidden desktop, the build folders, the logs and the clip folders.

## Checks on the real program

`pwsh tests/run-e2e.ps1 <name> [<name> ...]` runs scripts from `tests/e2e` on the hidden desktop against `dist\FlyweightSetup.exe`, so build first. Each one passes with `checks: N, failed: 0`.

| Script | Checks | Changes |
|---|---|---|
| `trim` | trimming in the window: exact cuts, cancelling, several videos, an already open window | nothing; runs `dist` without installing it |
| `right-click-trim` | the real right-click menu, and Trim or crop first from it | installs `dist` over the installed app |
| `basics` | installing, compressing from the menu, updating while open, no CPU when idle | installs `dist`, three times |
| `install` | nothing; it only installs `dist`, so it can be tried | installs `dist` |

- Run `trim` after changing the window or trimming, and all three checks before a release.
- Ask the owner before running the ones that install: they replace the installed app with the build under test, and put back its Start menu and desktop shortcuts. Afterwards, say which build is installed. Reinstalling a release's `FlyweightSetup.exe` puts that one back.
- Each takes up to about two minutes, so run them in the background. The runner says how long each took, and stops one still going at 15 minutes and reports it as not finished. In the trim scripts each section says how far in it started, to show where a run stalled.
- They refuse to start while Flyweight is open, and check again before each step: copies of Flyweight share one channel, so a start from the checks would go to that window. They only ever stop copies they started themselves. Never close someone's Flyweight window.
- Never press Play (real speakers), Show in folder (opens an Explorer window, which can appear on the real desktop) or the button that installs ffmpeg (runs winget in a console window).
- Never run `--uninstall` on a PC someone uses: it deletes their settings. Nothing may write or delete `%APPDATA%\Flyweight`; the checks compare it before and after.
- Windows PowerShell runs these scripts, so keep them ASCII.

## Testing habits

- Write the failing test first. Then `python tests/mutate.py <mutations file>` breaks the code one way at a time, in a copy, and says whether the tests noticed. Its docstring shows the file's format.
- For anything about cuts, use times that fall between frames, and count what's really in the file (`ffprobe -count_frames`, and `-ignore_editlist 1` for sound). Whole seconds and ordinary playback hide cuts a frame out and sound left over from before the cut.
- On the hidden desktop a window is redrawn only about five times a second, so judge timing without one, as `PartPlayerTests` does. Real drag-and-drop can't be done there without the real mouse, so `TrimDropTests` hands WPF a `DragEventArgs` it made itself.
- Layouts must fit 824 px of height: a 1080p screen at 125% scaling, less the taskbar.

## What matters

- Idle costs nothing: nothing runs in the background or at startup, and an open window uses no CPU. Full speed while compressing is fine.
- The levels differ by what they keep: Light keeps full resolution, Balanced goes down to 1080p, Maximum to 720p at 30 fps. Every level must compress well, including videos that are already compressed, like downloads.
- Size: keep the program under 300 KB; 1.1.0 is 232 KB. Give the size cost when proposing a feature (about 29 bytes per line of C#, though estimates have missed both ways). About 28 KB of it is embedded debug info, for line numbers in error details.
- Plain words everywhere: the window, the README, the site, release notes and commit messages.
- Propose a design and get a yes before building it.
- No license, on purpose. Don't add one, and don't call the project open source; say its code is on GitHub.
- Ask before putting the owner's own recordings, or pictures of their screen, on a public page.

## Building and releasing

- `pwsh tests/run-build.ps1` runs every test on the hidden desktop, then writes `dist\FlyweightSetup.exe` and prints its SHA-256. The same source always builds the same bytes.
- Commit straight to `main`. The repo is public, so pushing publishes: ask before any push, release or deploy.
- A release:
  1. Raise `<Version>` in `src/Flyweight/Flyweight.csproj` to the new number, build with `pwsh tests/run-build.ps1`, and with the owner's OK run `pwsh tests/run-e2e.ps1 trim right-click-trim basics`.
  2. Update the size: "about N KB" in `README.md` and in the description in `docs/index.html`, and the exact KB in two places further down that page.
  3. If the window changed, retake the site's pictures. `pwsh tests/run-tests.ps1 -Filter "FullyQualifiedName~Snapshots_when_asked" -SnapshotDir <folder>` writes them; the site uses `site-app-*`, `site-corner-*` and `site-presets-*`, light and dark. Convert each like `ffmpeg -y -i site-app-light.png -c:v libwebp -quality 90 -compression_level 6 docs/img/app-light.webp`, and if a picture's size changed, update its `width` and `height` in `docs/index.html`.
  4. Commit and push, tag that commit `vX.Y.Z`, then `gh release create` with `dist\FlyweightSetup.exe` and notes like the last release's, ending with its SHA-256.
  5. Deploy the site with `npx wrangler deploy`. The download link always serves the latest release, so site copy about a new feature goes live with its release.
- `redirects/` deploys on its own: `npx wrangler deploy --config redirects/wrangler.jsonc`. Wrangler's sign-in expires, and only the owner can renew it (`npx wrangler login`).
- It's tested on Windows 10 with an NVIDIA card. AMD and Intel graphics fall back to the processor.

## Status

For work that runs past one sitting, keep `NOW.md` current so another session or another AI (Claude, Codex) can pick up at any moment. Rewrite it rather than appending to it: when a piece of work finishes, before a long run, and when usage is running low. Keep it to about 20 lines: the goal, the branch, what's done, what's next and open questions. Commit it with the work it describes. Skip it for tasks that finish in one sitting.

This repo is public, so keep `NOW.md` free of anything private, and delete it once the work is done.
