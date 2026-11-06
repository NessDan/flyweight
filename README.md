# Flyweight

**Heavyweight videos. Flyweight files.** A tiny video compressor for Windows 10 and 11.

Drop a video in and get a smaller one out. Or right-click any video and pick a level, or a preset you made.

**[Website](https://nessdan.github.io/flyweight/) · [Download](https://github.com/NessDan/flyweight/releases/latest/download/FlyweightSetup.exe)**

It's a single small program, about 160 KB. It needs no admin rights, nothing runs in the background or at
startup, and the right-click menu is plain registry entries rather than a shell extension, so Explorer
doesn't load anything extra. When idle it costs nothing.

The original video is never changed. Results are saved next to it as `name_compressed.mp4`.

## Install

Download `FlyweightSetup.exe`, run it and click Install. To remove it, use Windows' Apps & features.

The program isn't code-signed, so Windows may show a warning the first time. Click **More info**, then
**Run anyway**. Each [release](https://github.com/NessDan/flyweight/releases) lists a SHA-256 checksum, so you
can check your copy with `Get-FileHash .\FlyweightSetup.exe`.

Flyweight uses the ffmpeg already on your PC, and offers to install it if there isn't one (about 550 MB on
disk). Running setup again updates the app, even while it's open; the new version takes over the next time
it starts.

## The three levels

Every level compresses at the same quality. They differ in what they keep:

| Level | Keeps |
|---|---|
| **Light** | Full resolution and frame rate |
| **Balanced** | Frame rate, and brings anything larger down to 1080p |
| **Maximum** | Brings video down to 720p at 30 fps |

So on a video that's already 1080p or smaller, Light and Balanced give the same result.

How much smaller each level made real footage, measured through the app:

| Source | Light | Balanced | Maximum |
|---|---|---|---|
| Game capture, 1080p120, 121 Mbps | 92% | 92% | 98% |
| OBS capture, 1080p60, 33 Mbps | 84% | 84% | 95% |
| Phone video, 4K60, 64 Mbps | 67% | 91% | 97% |
| X video, 4K30, 13 Mbps | 61% | 87% | 93% |
| X video, vertical 1080p30, 8 Mbps | 69% | 69% | 85% |
| OBS recording, 1080p30, 4 Mbps | 66% | 66% | 86% |
| Downloaded video, 1080p60, 5 Mbps | 39% | 39% | 81% |

All levels use H.265, quality 34 and 128 kbps audio. The quality number was chosen by scoring encodes
of footage like this with [VMAF](https://github.com/Netflix/vmaf). H.265 came out about a third
smaller than H.264 at the same quality, though some sites and apps still expect H.264. If a video won't play
or upload somewhere, change **Format** to H.264.

## Your own presets

Click **+ New preset**. It starts as a copy of the selected level and opens **Advanced options** so you
can name and adjust it. It gets its own card in the window and its own entry in the right-click menu,
and it's saved as you go.

For a site with an upload limit, set **Fit under**. A "Discord" preset is just Fit under 10 MB, Format H.264
and a name. If a video can't fit at its current size without looking terrible, the app lowers the frame rate
and then the resolution to make it work.

Each built-in level can be changed under **Advanced options** too, and the right-click menu follows
those changes. **Reset this level** puts one back.

For the smallest possible files, try **Format: AV1** with **Encode with: Processor**. In testing that
was roughly a quarter smaller at the same quality and about 4-5x slower, and AV1 plays on fewer devices.

## How it behaves

- **The window** can be resized and remembers its size. Make it wide and Advanced options move beside
  everything else; make it tall and the queue gets the extra room.
- **Speed:** H.265 and AV1 use both of the graphics card's encoder chips where it has two. On an
  RTX 5080 that's about 10x real time for 1080p60 and 3x for 4K60.
- **Priority:** compression runs at low priority. That doesn't slow it down on an idle PC; it only
  means whatever you're doing goes first.
- **Several videos** queue up in one window and run one at a time.
- **Right-click jobs** open a small window in the corner without taking focus from whatever you're
  doing. It closes itself when finished, unless something failed.
- **Videos that are already tightly compressed** get a second, slightly harder pass if the first
  barely helped. If the result still isn't smaller, the original is left as is and no new file is
  created. A quality number you set yourself under Advanced is never overridden.
- **No NVIDIA graphics card, or the card can't handle a video?** It falls back to the processor
  automatically. AMD and Intel graphics aren't used yet.
- **Windows 11** keeps the right-click entries under **Show more options**. Holding Shift while you
  right-click goes straight there.
- **`.ts` and `.mts` videos** don't get the right-click menu, because TypeScript uses those
  extensions too. Drop them on the window instead.
- **Your videos stay on your PC.** The app has no accounts or tracking, and never connects to the internet
  itself.

## Building from source

Needs the .NET SDK. The app itself targets .NET Framework 4.8, which ships with Windows.

```bash
pwsh ./build.ps1
```

That runs the tests and writes `dist\FlyweightSetup.exe`. The tests need ffmpeg on the PATH,
and some encode with NVENC when an NVIDIA card is present.

The engine is in `src/Flyweight/Core` and the windows are in `src/Flyweight/UI`.
`CommandBuilder.cs` holds every decision about what gets passed to ffmpeg.

The website is the static page in `docs`, published with GitHub Pages.
