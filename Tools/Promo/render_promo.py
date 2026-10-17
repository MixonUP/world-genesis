"""CPU-only, reproducible Telegram edit using existing native Player captures.

python Tools/Promo/render_promo.py --preview-only
python Tools/Promo/render_promo.py --threads 4

Requires Pillow, ffmpeg/ffprobe on PATH and one narration-NN.wav per chapter.
Use --plan/--outdir for another version; --narration or plan.narration supplies
one text paragraph per chapter. Without it the original LINES literal is used.
Plan media paths are relative to the project root. Source media is read-only.
The output filename is a delivery name; actual duration is in edit-receipt.json.
"""
from __future__ import annotations

import argparse
import ast
import hashlib
import inspect
import json
import math
import re
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Builds/Promo/Telegram-2026-10-17"
PLAN_PATH = ROOT / "SourceAssets/Promo/Telegram-2026-10-17/edit-plan.json"
FFMPEG = shutil.which("ffmpeg")
FFPROBE = shutil.which("ffprobe")
FONT_REGULAR = Path("C:/Windows/Fonts/segoeui.ttf")
FONT_BOLD = Path("C:/Windows/Fonts/segoeuib.ttf")
COMMANDS: list[dict] = []


def run(cmd: list[str], label: str) -> subprocess.CompletedProcess:
    """Every external process is checked; logs/commands stay beside the export."""
    proc = subprocess.run([str(x) for x in cmd], capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    COMMANDS.append({"label": label, "argv": [str(x) for x in cmd], "returncode": proc.returncode})
    (OUT / "render-commands.json").write_text(json.dumps(COMMANDS, ensure_ascii=False, indent=2), encoding="utf-8")
    if proc.returncode:
        (OUT / "render-error.log").write_text(proc.stderr, encoding="utf-8")
        raise RuntimeError(f"{label} failed ({proc.returncode}):\n{proc.stderr[-6000:]}")
    return proc


def probe(path: Path) -> dict:
    return json.loads(run([FFPROBE, "-v", "error", "-show_streams", "-show_format", "-of", "json", path], "probe " + path.name).stdout)


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as src:
        for block in iter(lambda: src.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def project_path(value: str | Path) -> Path:
    path = Path(value)
    return path.resolve() if path.is_absolute() else (ROOT / path).resolve()


def narration_path(plan: dict, override: str | None = None) -> Path | None:
    value = override or plan.get("narration")
    if not value:
        return None
    path = project_path(value)
    # A bare filename beside a custom plan is convenient; project-relative
    # paths remain the first choice, matching the existing source convention.
    if not path.exists() and not Path(value).is_absolute() and override is None:
        path = (PLAN_PATH.parent / value).resolve()
    return path


def narration_lines(plan: dict | None = None, override: str | None = None) -> list[str]:
    path = narration_path(plan or {}, override)
    if path is not None:
        text = path.read_text(encoding="utf-8-sig").strip()
        return [" ".join(part.split()) for part in re.split(r"\n\s*\n", text) if part.strip()]
    # Parse the literal; importing the voice generator would change env/start jobs.
    module = ast.parse((ROOT / "Tools/Promo/generate_narration.py").read_text(encoding="utf-8"))
    for statement in module.body:
        if isinstance(statement, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "LINES" for t in statement.targets):
            return ast.literal_eval(statement.value)
    raise ValueError("LINES not found in voice script")


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONT_BOLD if bold else FONT_REGULAR), size)


def draw_fit(draw: ImageDraw.ImageDraw, text: str, xy: tuple[int, int], size: int,
             max_width: int, color: str, bold: bool = False, minimum: int = 24) -> None:
    while size > minimum and draw.textlength(text, font=font(size, bold)) > max_width:
        size -= 1
    if draw.textlength(text, font=font(size, bold)) > max_width:
        raise ValueError(f"Title does not fit: {text}")
    draw.text(xy, text, font=font(size, bold), fill=color)


def card(plan: dict, chapter: dict, index: int) -> Image.Image:
    w, h = plan["canvas"]
    p = plan["palette"]
    im = Image.new("RGB", (w, h), p["navy"])
    d = ImageDraw.Draw(im)
    # Understated navy gradient; all pixels here are typography/frame, never game art.
    for y in range(h):
        t = y / h
        d.line((0, y, w, y), fill=(int(10 + 4*t), int(22 + 9*t), int(36 + 13*t)))
    d.text((96, 48), "WORLD GENESIS", font=font(35, True), fill=p["gold"])
    d.text((1510, 57), "ДНЕВНИК РАЗРАБОТКИ", font=font(21), fill=p["muted"])
    draw_fit(d, chapter["title"], (96, 113), 61, 1728, p["ivory"], True, 46)
    d.line((96, 208, 1824, 208), fill="#314253", width=2)
    x, y, mw, mh = plan["media_box"]
    d.rectangle((x-2, y-2, x+mw+2, y+mh+2), fill="#42515d")
    d.rectangle((x, y, x+mw, y+mh), fill="#08121e")
    chapter_count = len(plan["chapters"])
    d.text((1402, 280), f"{index+1:02d} / {chapter_count:02d}", font=font(23), fill=p["gold"])
    draw_fit(d, chapter["label"], (1402, 330), 21, 420, p["muted"], True, 17)
    for i, line in enumerate(chapter["side"]):
        draw_fit(d, line, (1398, 400+i*77), 52, 425, p["ivory"], True, 40)
    d.line((1402, 710, 1514, 710), fill=p["gold"], width=3)
    if index == chapter_count-1:
        d.text((1402, 749), "Скачать. Изучить.", font=font(27), fill=p["muted"])
        d.text((1402, 791), "Следить за развитием.", font=font(27), fill=p["muted"])
        draw_fit(d, "github.com/MixonUP/world-genesis", (96, 929), 36, 1250, p["gold"], True)
    else:
        d.text((1402, 749), "UNITY / WINDOWS", font=font(21), fill=p["muted"])
        d.text((96, 940), "Маленький огонь. Большая история.", font=font(29), fill=p["ivory"])
    draw_fit(d, plan["disclosure"], (96, 1008), 23, 1728, p["muted"])
    # Thin progress motif: an editorial chapter indicator, not fake game UI.
    progress_step = 400/chapter_count
    for i in range(chapter_count):
        left = 1402+i*progress_step
        d.rectangle((round(left), 860, round(left+progress_step*0.72), 865), fill=p["gold"] if i <= index else "#344353")
    return im


def source_frame(shot: dict, index: int) -> Image.Image:
    path = ROOT / shot["file"]
    if shot["kind"] == "still":
        image = Image.open(path).convert("RGB")
    else:
        extracted = OUT / f"preview-source-{index:02d}.png"
        run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-ss", str(shot.get("in", 0)),
             "-i", path, "-frames:v", "1", "-threads", "1", extracted], "preview " + path.name)
        image = Image.open(extracted).convert("RGB")
    x, y, w, h = shot["crop"]
    if x < 0 or y < 0 or x+w > image.width or y+h > image.height:
        raise ValueError(f"Crop outside {path.name}: {shot['crop']} / {image.size}")
    return image.crop((x, y, x+w, y+h))


def fit_size(source_width: int, source_height: int, box: list[int]) -> tuple[int, int, int, int]:
    x, y, w, h = box
    scale = min(w/source_width, h/source_height)
    sw, sh = int(source_width*scale)//2*2, int(source_height*scale)//2*2
    return sw, sh, x+(w-sw)//2, y+(h-sh)//2


def prepare_previews(plan: dict) -> list[Path]:
    previews, n = [], 0
    for ci, chapter in enumerate(plan["chapters"]):
        background = card(plan, chapter, ci)
        background.save(OUT / f"chapter-{ci+1:02d}-card.png")
        for shot in chapter["shots"]:
            n += 1
            frame = source_frame(shot, n)
            sw, sh, px, py = fit_size(*frame.size, plan["media_box"])
            # Nearest keeps native sprite pixels; GitHub text uses Lanczos.
            resample = Image.Resampling.LANCZOS if shot["version"] == "repository" else Image.Resampling.NEAREST
            preview = background.copy()
            preview.paste(frame.resize((sw, sh), resample), (px, py))
            path = OUT / f"edit-preview-{n:02d}.jpg"
            preview.save(path, quality=94)
            previews.append(path)
    contact = Image.new("RGB", (1280, math.ceil(len(previews)/2)*384), "#0b1828")
    d = ImageDraw.Draw(contact)
    for i, path in enumerate(previews):
        x, y = i % 2 * 640, i // 2 * 384
        contact.paste(Image.open(path).resize((640, 360), Image.Resampling.LANCZOS), (x, y))
        d.text((x+12, y+362), f"{i+1:02d} / {path.name}", font=font(15), fill="#f2ead9")
    contact.save(OUT / "edit-contact-sheet.jpg", quality=92)
    shutil.copyfile(previews[0], OUT / "cover.jpg")
    return previews


def timing(plan: dict) -> tuple[list[dict], float, float]:
    config = plan["audio"]
    count = len(plan["chapters"])
    durations = [float(probe(OUT / f"narration-{i+1:02d}.wav")["format"]["duration"]) for i in range(count)]
    if any(seconds <= 0 for seconds in durations):
        raise ValueError("Every narration WAV must have positive duration")
    gaps = config["first_lead"] + (count-1)*config["chapter_lead"] + (count-1)*config["chapter_tail"] + config["last_tail"]
    if config["target_seconds"] <= gaps:
        raise ValueError("Audio target_seconds must exceed the sum of chapter pauses")
    tempo = min(config["tempo_max"], max(config["tempo_min"], sum(durations)/(config["target_seconds"]-gaps)))
    chapters, current = [], 0.0
    fps = plan["fps"]
    for i, seconds in enumerate(durations):
        lead = config["first_lead"] if i == 0 else config["chapter_lead"]
        tail = config["last_tail"] if i == count-1 else config["chapter_tail"]
        frames = math.ceil((lead+seconds/tempo+tail)*fps)
        duration = frames/fps
        chapters.append({"index": i+1, "start": current, "duration": duration, "frames": frames,
                         "source_audio_seconds": seconds, "voice_start": current+lead,
                         "voice_end": current+lead+seconds/tempo, "lead": lead,
                         "tail": duration-lead-seconds/tempo})
        current += duration
    return chapters, tempo, current


def srt_time(seconds: float) -> str:
    millis = round(seconds*1000)
    h, millis = divmod(millis, 3600000)
    m, millis = divmod(millis, 60000)
    s, ms = divmod(millis, 1000)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def subtitles(lines: list[str], times: list[dict], output_stem: str = "WorldGenesis-Telegram-60s") -> None:
    # Sentence/phrase times are estimates within each supplied voice take.
    # The receipt explicitly records this; no forced alignment is claimed.
    cues = []
    for line, t in zip(lines, times):
        words = line.split()
        chunks = [" ".join(words[i:i+8]) for i in range(0, len(words), 8)]
        weights = [len(chunk) for chunk in chunks]
        start = t["voice_start"]
        available = t["voice_end"]-start
        for chunk, weight in zip(chunks, weights):
            end = start+available*weight/sum(weights)
            cues.append(f"{len(cues)+1}\n{srt_time(start)} --> {srt_time(end)}\n{chunk}\n")
            start = end
    (OUT / f"{output_stem}.srt").write_text("\n".join(cues), encoding="utf-8-sig")


def render_shots(plan: dict, times: list[dict], threads: int) -> tuple[list[Path], list[dict]]:
    segments, receipts, global_frame = [], [], 0
    fps = plan["fps"]
    total_shots = sum(len(c["shots"]) for c in plan["chapters"])
    for ci, (chapter, chapter_time) in enumerate(zip(plan["chapters"], times)):
        remaining = chapter_time["frames"]
        total_weight = sum(s["weight"] for s in chapter["shots"])
        for si, shot in enumerate(chapter["shots"]):
            index = len(segments)+1
            frames = remaining if si == len(chapter["shots"])-1 else round(chapter_time["frames"]*shot["weight"]/total_weight)
            remaining -= frames
            duration = frames/fps
            path = ROOT / shot["file"]
            x, y, cw, ch = shot["crop"]
            sw, sh, px, py = fit_size(cw, ch, plan["media_box"])
            used = duration if shot["kind"] == "still" else min(duration, shot["max_seconds"])
            start = shot.get("in", 0)
            hold = max(0.0, duration-used) if shot["kind"] == "video" else 0.0
            output = OUT / f"segment-{index:02d}.mp4"
            command = [FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-threads", str(threads),
                       "-loop", "1", "-framerate", str(fps), "-i", OUT / f"chapter-{ci+1:02d}-card.png"]
            if shot["kind"] == "still":
                command += ["-loop", "1", "-framerate", str(fps), "-i", path]
            else:
                command += ["-ss", f"{start:.6f}", "-t", f"{used:.6f}", "-i", path]
            resampler = "lanczos" if shot["version"] == "repository" else "neighbor"
            # A 4-frame fade on the native image only; typography remains calm.
            fade = min(4/fps, duration/4)
            filters = (f"[1:v]fps={fps},setpts=PTS-STARTPTS,crop={cw}:{ch}:{x}:{y},"
                       f"scale={sw}:{sh}:flags={resampler},setsar=1,format=rgba,"
                       f"tpad=stop_mode=clone:stop_duration={duration:.6f},trim=duration={duration:.6f},"
                       f"fade=t=in:st=0:d={fade:.6f}:alpha=1,"
                       f"fade=t=out:st={max(0,duration-fade):.6f}:d={fade:.6f}:alpha=1[media];"
                       f"[0:v][media]overlay={px}:{py}:shortest=1,format=yuv420p[v]")
            command += ["-filter_complex_threads", "1", "-filter_complex", filters,
                        "-map", "[v]", "-an", "-frames:v", str(frames), "-r", str(fps),
                        "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-threads", str(threads),
                        "-pix_fmt", "yuv420p", "-movflags", "+faststart", output]
            print(f"Shot {index}/{total_shots}: {path.name}, {duration:.2f}s", flush=True)
            run(command, f"render segment {index:02d}")
            segments.append(output)
            receipts.append({**shot, "chapter": ci+1, "edit_start": global_frame/fps,
                             "edit_end": (global_frame+frames)/fps, "source_in": start,
                             "source_out": start+used if shot["kind"] == "video" else None,
                             "tail_freeze_seconds": hold, "output_frames": frames,
                             "source_sha256": sha(path), "fit_rectangle": [px, py, sw, sh]})
            global_frame += frames
    return segments, receipts


def mix_audio(plan: dict, times: list[dict], tempo: float, duration: float) -> Path:
    command = [FFMPEG, "-hide_banner", "-loglevel", "error", "-y"]
    count = len(times)
    for i in range(count):
        command += ["-i", OUT / f"narration-{i+1:02d}.wav"]
    command += ["-stream_loop", "-1", "-i", ROOT / plan["music"]]
    filters = []
    total_samples = sum(chapter["frames"] for chapter in times) * 1600
    for i, chapter in enumerate(times):
        delay = round(chapter["lead"]*1000)
        samples = chapter["frames"] * 1600  # 48 kHz / 30 fps: exact chapter length.
        filters.append(f"[{i}:a]aresample=48000,aformat=channel_layouts=stereo,atempo={tempo:.9f},"
                       f"asetpts=N/SR/TB,adelay={delay}|{delay},apad=whole_len={samples},"
                       f"atrim=end_sample={samples},asetpts=N/SR/TB[a{i}]")
    filters.append("".join(f"[a{i}]" for i in range(count))+f"concat=n={count}:v=0:a=1,loudnorm=I=-16:TP=-1.5:LRA=7,aresample=48000,apad=whole_len={total_samples},atrim=end_sample={total_samples},asetpts=N/SR/TB[voice]")
    filters.append(f"[{count}:a]aresample=48000,aformat=channel_layouts=stereo,atrim=duration={duration:.9f},"
                   f"asetpts=PTS-STARTPTS,volume={plan['audio']['music_volume']},afade=t=in:d=2,"
                   f"afade=t=out:st={max(0,duration-3):.9f}:d=3[music]")
    filters.append(f"[voice]asplit=2[vstem][vout];[music]asplit=2[mstem][mout];[vout][mout]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95:level=0:latency=1,aresample=48000,apad=whole_len={total_samples},atrim=end_sample={total_samples},asetpts=N/SR/TB[mix]")
    output = OUT / "final-audio.wav"
    command += ["-filter_complex_threads", "1", "-filter_complex", ";".join(filters), "-map", "[mix]",
                "-t", f"{duration:.9f}", "-c:a", "pcm_s16le", output,
                "-map", "[vstem]", "-t", f"{duration:.9f}", "-c:a", "pcm_s24le", OUT / "voice-stem.wav",
                "-map", "[mstem]", "-t", f"{duration:.9f}", "-c:a", "pcm_s24le", OUT / "music-stem.wav"]
    run(command, "mix narration and project music")
    for stem in (output, OUT / "voice-stem.wav", OUT / "music-stem.wav"):
        if abs(float(probe(stem)["format"]["duration"])-duration) > 1/48000:
            raise RuntimeError(f"Audio length differs from picture: {stem.name}")
    return output


def video_fingerprint(plan: dict, times: list[dict]) -> str:
    """Audio gain/music may change; picture, measured timing and sources may not."""
    visual = {key: plan[key] for key in ("canvas", "fps", "media_box", "palette", "disclosure", "chapters")}
    visual["timing"] = times
    visual["sources"] = {s["file"]: sha(ROOT/s["file"]) for c in plan["chapters"] for s in c["shots"]}
    visual["fonts"] = [sha(FONT_REGULAR), sha(FONT_BOLD)]
    visual["drawing_code"] = "\n".join(inspect.getsource(fn) for fn in (card, draw_fit, fit_size, render_shots))
    encoded = json.dumps(visual, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def reuse_video(plan: dict, times: list[dict], fingerprint: str) -> tuple[Path, list[dict]]:
    receipt_path = OUT / "video-receipt.json"
    video = OUT / "video-only.mp4"
    if not receipt_path.is_file() or not video.is_file():
        raise ValueError("--reuse-video requires video-only.mp4 and video-receipt.json from a full render with this script. Render once without --reuse-video.")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    if receipt.get("visual_fingerprint") != fingerprint or receipt.get("chapters") != times:
        raise ValueError("Cannot reuse video: picture plan, source files, fonts or measured chapter timings changed. Render without --reuse-video.")
    if sha(video) != receipt.get("video_sha256"):
        raise ValueError("Cannot reuse video: video-only.mp4 differs from its receipt")
    expected_shots = sum(len(chapter["shots"]) for chapter in plan["chapters"])
    shots = receipt.get("shots", [])
    if len(shots) != expected_shots or sum(s["output_frames"] for s in shots) != sum(t["frames"] for t in times):
        raise ValueError("Cannot reuse video: shot receipt does not match chapter frames")
    return video, shots


def main() -> None:
    global OUT, PLAN_PATH
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan", default=str(PLAN_PATH), help="JSON edit plan; relative paths resolve from the project root")
    parser.add_argument("--outdir", default=str(OUT), help="Folder containing narration WAVs and receiving this version's exports")
    parser.add_argument("--narration", help="UTF-8 text, one paragraph per chapter; overrides plan.narration")
    parser.add_argument("--preview-only", action="store_true", help="Create all shot cards/contact sheet/cover without requiring voice or rendering video")
    parser.add_argument("--reuse-video", "--audio-only", dest="reuse_video", action="store_true", help="Reuse verified picture and shot receipt; remix stems and mux final output without rendering images/video")
    parser.add_argument("--threads", type=int, default=4, help="CPU encoder threads; no GPU encoder is used")
    args = parser.parse_args()
    OUT = project_path(args.outdir)
    PLAN_PATH = project_path(args.plan)
    OUT.mkdir(parents=True, exist_ok=True)
    if not FFMPEG or not FFPROBE:
        raise SystemExit("ffmpeg and ffprobe must be on PATH")
    if args.threads < 1:
        raise SystemExit("--threads must be positive")
    if args.preview_only and args.reuse_video:
        raise SystemExit("--preview-only cannot be combined with --reuse-video")
    plan = json.loads(PLAN_PATH.read_text(encoding="utf-8"))
    lines = narration_lines(plan, args.narration)
    chapter_count = len(plan["chapters"])
    if chapter_count < 1 or len(lines) != chapter_count:
        raise ValueError(f"One narration paragraph per chapter is required: {len(lines)} paragraphs / {chapter_count} chapters")
    if plan["fps"] != 30:
        raise ValueError("This renderer uses exact 48 kHz / 30 fps timing; plan.fps must be 30")
    output_name = plan.get("output_filename", f"WorldGenesis-Telegram-{round(plan['audio']['target_seconds'])}s.mp4")
    if Path(output_name).name != output_name or Path(output_name).suffix.lower() != ".mp4":
        raise ValueError("plan.output_filename must be an MP4 filename without a directory")
    for chapter in plan["chapters"]:
        for shot in chapter["shots"]:
            if not (ROOT / shot["file"]).is_file():
                raise FileNotFoundError(ROOT / shot["file"])
    if args.preview_only:
        previews = prepare_previews(plan)
        receipt = {"status": "preview-only; video not rendered", "created_utc": datetime.now(timezone.utc).isoformat(),
                   "plan_sha256": sha(PLAN_PATH), "previews": [p.name for p in previews], "no_voice_required": True}
        (OUT / "preview-receipt.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding="utf-8")
        print(str(OUT / "edit-contact-sheet.jpg"), flush=True)
        return
    missing = [str(OUT / f"narration-{i:02d}.wav") for i in range(1, chapter_count+1) if not (OUT / f"narration-{i:02d}.wav").is_file()]
    if missing:
        raise SystemExit("Voice is not ready; full render was not started. Missing:\n"+"\n".join(missing))
    times, tempo, duration = timing(plan)
    print(f"Voice tempo {tempo:.4f}; finished duration {duration:.2f}s", flush=True)
    if duration > plan["audio"]["target_seconds"]+10:
        print("NOTE: voice exceeds target by more than 10s at allowed tempo. Preserving speech; actual duration recorded.", flush=True)
    fingerprint = video_fingerprint(plan, times)
    if args.reuse_video:
        video_only, shot_receipts = reuse_video(plan, times, fingerprint)
        print("Reusing verified video-only.mp4; images and video will not be rendered.", flush=True)
    else:
        prepare_previews(plan)
        segments, shot_receipts = render_shots(plan, times, args.threads)
        concat_list = OUT / "segments.txt"
        concat_list.write_text("\n".join(f"file '{p.name}'" for p in segments)+"\n", encoding="utf-8")
        video_only = OUT / "video-only.mp4"
        run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", concat_list,
             "-c", "copy", "-movflags", "+faststart", video_only], "join segments")
        video_receipt = {"visual_fingerprint": fingerprint, "video_sha256": sha(video_only),
                         "chapters": times, "shots": shot_receipts, "duration_seconds": duration}
        (OUT / "video-receipt.json").write_text(json.dumps(video_receipt, ensure_ascii=False, indent=2), encoding="utf-8")
    subtitles(lines, times, Path(output_name).stem)
    audio = mix_audio(plan, times, tempo, duration)
    output = OUT / output_name
    run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", video_only, "-i", audio,
         "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
         "-t", f"{duration:.9f}", "-movflags", "+faststart", output], "mux Telegram delivery")
    info = probe(output)
    video = next(s for s in info["streams"] if s["codec_type"] == "video")
    sound = next(s for s in info["streams"] if s["codec_type"] == "audio")
    actual_duration = float(info["format"]["duration"])
    if (video["width"], video["height"]) != tuple(plan["canvas"]) or video["r_frame_rate"] != "30/1":
        raise RuntimeError("Unexpected export dimensions/frame rate")
    if abs(actual_duration-duration) > 0.12:
        raise RuntimeError(f"Export duration mismatch: {actual_duration} / {duration}")
    # Full CPU decode catches truncated/corrupt exports; no visual acceptance claim.
    run([FFMPEG, "-hide_banner", "-loglevel", "error", "-threads", "2", "-i", output, "-f", "null", "-"], "decode final video and audio")
    voice_receipt = OUT / "voice-receipt.json"
    receipt = {"status": "rendered; technical decode verified; artistic acceptance pending",
               "created_utc": datetime.now(timezone.utc).isoformat(), "output": output.name,
               "output_sha256": sha(output), "output_bytes": output.stat().st_size,
               "duration_seconds": actual_duration, "canvas": plan["canvas"], "fps": plan["fps"],
               "video_codec": video["codec_name"], "audio_codec": sound["codec_name"],
               "voice_atempo": tempo, "voice_tempo_bounds": [plan["audio"]["tempo_min"], plan["audio"]["tempo_max"]],
               "music": {"file": plan["music"], "sha256": sha(ROOT/plan["music"]), "volume_multiplier": plan["audio"]["music_volume"]},
               "voice_receipt": {"file": voice_receipt.name, "sha256": sha(voice_receipt)} if voice_receipt.exists() else None,
               "narration_files": [{"file": f"narration-{i+1:02d}.wav", "sha256": sha(OUT/f"narration-{i+1:02d}.wav"), "text": line} for i, line in enumerate(lines)],
               "chapters": times, "shots": shot_receipts, "notes": plan["notes"],
               "narration_source": str(narration_path(plan, args.narration)) if narration_path(plan, args.narration) else "Tools/Promo/generate_narration.py:LINES (legacy)",
               "video_reused": args.reuse_video, "visual_fingerprint": fingerprint,
               "srt_timing": "Estimated word-length cues inside each measured narration take, not forced alignment.",
               "plan_sha256": sha(PLAN_PATH), "renderer_sha256": sha(Path(__file__)),
               "validation": {"ffmpeg_returncodes_checked": True, "full_decode": "pass", "duration_and_format": "pass", "visual_and_audio_review": "pending"}}
    (OUT / "edit-receipt.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding="utf-8")
    print(str(output), flush=True)


if __name__ == "__main__":
    main()
