# Flyweight

**Heavyweight videos. Flyweight files.** A tiny video compressor for Windows 10 and 11.

Drop a video in and get a smaller one out. Or right-click any video and pick a level, or a preset you made.
If you only want part of a video, or part of the picture, it can trim or crop it on the way.

**[Website](https://getflyweight.com/) · [Download](https://github.com/NessDan/flyweight/releases/latest/download/FlyweightSetup.exe)**

It's a single small program, about 230 KB. It needs no admin rights, nothing runs in the background or at
startup, and the right-click menu is plain registry entries rather than a shell extension, so Explorer
doesn't load anything extra. When idle it costs nothing.

The original video is never changed. Results are saved next to it as `name_compressed.mp4`,
`name_trimmed.mp4` when you kept only part of it, or `name_cropped.mp4` when you kept only part of the
picture.

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

## Trimming and cropping

To keep only part of a video, drop it on **Trim or crop first** instead of the main area, or right-click
it and choose **Compress video > Trim or crop first…**. The drop zones make way for a picture of the video
and a strip of pictures from the whole of it. Drag the two handles to the part you want, or type the
times, and click **Trim and compress**. The button always says what it will do. Nothing is compressed
until you click it.

The levels, your presets and Advanced options stay where they always are, underneath, and the video is
compressed with whichever one is selected.

- **Cuts are exact to the frame, and the sound stays in step.** Tools that cut without re-encoding can only
  cut at keyframes, which can be seconds apart. Flyweight re-encodes anyway, so it cuts where you say.
- **Nothing from outside the part you kept is left in the file.** The sound is always re-encoded in a
  trimmed copy, because copied sound drags along what came before the cut. Subtitles and chapters are left
  out, since their times and words belong to the whole video.
- **The picture is the frame the cut lands on:** the first frame kept for the start, the last one for the
  end. In a time box, Up and Down step one frame, or one second with Shift held. On recordings with an
  uneven frame rate, such as screen recordings, a step is an average frame, so the picture doesn't always
  change with each one.
- **Play it where it is.** **Play** on the picture, or the space bar, plays the video in place with its
  sound, and pauses it. It plays from the white line on the strip. Moving the start handle puts the
  line there, and moving the end handle puts it three seconds before the end, so checking where a part
  stops doesn't mean watching all of it. Click between the handles to play from anywhere else. Once
  it has played to the end, Play starts the part over. A still picture is always an exact frame.
- **Start here, end here.** The **[** and **]** either side of Play, or the I and O keys, put the start
  or the end on the picture that's playing or paused, or on the white line when nothing is. Start here
  keeps it playing. End here stops on the new end, with that picture kept last. On recordings with an
  uneven frame rate it can land a frame out; Up and Down in the time box put it right.
- **Crop by the corners.** Four corners sit on the picture. Drag one in and what's outside the box is
  dimmed, which is what gets cut away, and drag inside the box to move it. Any shape works, and the size
  is rounded to even pixels, which video needs. The crop is taken from the full-size video first, so a
  1080p piece of a 4K video keeps its full detail on Balanced. **Reset crop**, beside the time boxes,
  puts back the whole picture. Cropping alone keeps subtitles and chapters; only trimming leaves them out.
- **Hold a corner still to zoom in.** Half a second without moving and the picture comes four times
  nearer around that corner, at full detail, so a hand's movement moves it a quarter as far. Let go and it
  goes back. Zoomed in, a thin strip along an edge, a few pixels wide, can be cropped away too.
- **Levels work as they always do.** Changing one while you trim changes that level for good, not just
  for this video.
- **The window makes room while you trim,** and goes back to its size afterwards. Advanced options open
  beside the picture rather than under it, where they'd be off the bottom of most screens.
- **One part per video.** It doesn't cut a piece out of the middle or join clips together. Send several
  videos at once and each gets its turn, with **Skip them all** as the way out. A video dropped on the
  window while you're trimming waits its turn too.
- **With a Fit under preset**, the part you keep gets the whole size. If it would fit anyway, it's
  compressed at normal quality instead of being padded up to the limit.
- **A trimmed or cropped copy is always made,** even from a video that's already small, since that's
  what was asked for.

With nothing trimmed or cropped, the button just says **Compress**, and compresses the whole video as
usual.

## How it behaves

- **The window** is always as tall as what's in it, and remembers how wide you made it. Make it wide
  and Advanced options move beside everything else. Maximized, or snapped to a side of the screen, the
  queue gets the extra room.
- **Speed:** H.265 and AV1 use both of the graphics card's encoder chips where it has two. On an
  RTX 5080 that's about 10x real time for 1080p60 and 3x for 4K60.
- **Priority:** compression runs at low priority. That doesn't slow it down on an idle PC; it only
  means whatever you're doing goes first.
- **Several videos** queue up in one window and run one at a time.
- **Right-click jobs** open a small window in the corner without taking focus from whatever you're
  doing. It closes itself when finished, unless something failed. **Trim or crop first…** is the
  exception: it opens the whole window in front, since it needs you, and goes to the corner once you've
  chosen.
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
and some encode with NVENC when an NVIDIA card is present. Tests that show a window or play sound
skip themselves unless they're on a hidden desktop. `pwsh tests/run-build.ps1` does the same build
with every test run on one, so nothing appears on screen.

The engine is in `src/Flyweight/Core` and the windows are in `src/Flyweight/UI`.
`CommandBuilder.cs` holds every decision about what gets passed to ffmpeg when compressing, and
`FrameGrabber.cs` does the same for the pictures you trim by.

The website is the static page in `docs`, served from Cloudflare. `npx wrangler deploy` publishes it. The small
Worker in `redirects` sends `http://` and `www.` addresses to the main one, and
`npx wrangler deploy --config redirects/wrangler.jsonc` publishes that.
