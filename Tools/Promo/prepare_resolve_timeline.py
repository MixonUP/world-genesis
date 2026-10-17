"""Write an editable OTIO timeline from the measured promo edit receipt.

No Resolve project is changed by this preparation step. Video shots and the
two PCM stems remain separate items/tracks for import into DaVinci Resolve.
--plan and --outdir select another version; defaults preserve the v01 paths.
--narration (or plan.narration) records/checks one text paragraph per chapter.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / "Builds/Promo/Telegram-2026-10-17"
DEFAULT_PLAN = ROOT / "SourceAssets/Promo/Telegram-2026-10-17/edit-plan.json"


def project_path(value: str | Path) -> Path:
    path = Path(value)
    return path.resolve() if path.is_absolute() else (ROOT / path).resolve()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan", default=str(DEFAULT_PLAN), help="Edit plan JSON; project-relative or absolute path")
    parser.add_argument("--outdir", default=str(DEFAULT_OUT), help="Folder containing edit receipt, segment MP4s and audio stems")
    parser.add_argument("--narration", help="Optional UTF-8 narration file; overrides plan.narration")
    args = parser.parse_args()
    plan_path = project_path(args.plan)
    out = project_path(args.outdir)
    plan = json.loads(plan_path.read_text(encoding="utf-8"))
    receipt = json.loads((out / "edit-receipt.json").read_text(encoding="utf-8"))
    fps = receipt.get("fps", plan["fps"])
    if fps != 30 or plan["fps"] != 30:
        raise ValueError("The measured promo pipeline requires 30 fps / 48 kHz")
    if len(receipt["chapters"]) != len(plan["chapters"]):
        raise ValueError("Plan and rendered receipt have different chapter counts")
    value = args.narration or plan.get("narration")
    narration = None
    if value:
        narration_path = project_path(value)
        if not narration_path.exists() and args.narration is None and not Path(value).is_absolute():
            narration_path = (plan_path.parent / value).resolve()
        paragraphs = [" ".join(p.split()) for p in re.split(r"\n\s*\n", narration_path.read_text(encoding="utf-8-sig").strip()) if p.strip()]
        if len(paragraphs) != len(plan["chapters"]):
            raise ValueError("Narration paragraph count differs from the plan")
        narration = {"file": str(narration_path), "paragraphs": paragraphs}

    def rt(value: int) -> dict:
        return {"OTIO_SCHEMA": "RationalTime.1", "value": value, "rate": fps}

    def tr(start: int, duration: int) -> dict:
        return {"OTIO_SCHEMA": "TimeRange.1", "start_time": rt(start), "duration": rt(duration)}

    def clip(path: Path, frames: int, name: str) -> dict:
        if not path.is_file():
            raise FileNotFoundError(path)
        return {"OTIO_SCHEMA": "Clip.2", "metadata": {}, "name": name,
                "source_range": tr(0, frames), "effects": [], "markers": [],
                "media_references": {"DEFAULT_MEDIA": {"OTIO_SCHEMA": "ExternalReference.1", "metadata": {},
                    "name": path.name, "available_range": tr(0, frames), "target_url": path.resolve().as_uri()}},
                "active_media_reference_key": "DEFAULT_MEDIA"}

    def track(name: str, kind: str, children: list) -> dict:
        return {"OTIO_SCHEMA": "Track.1", "metadata": {}, "name": name, "source_range": None,
                "effects": [], "markers": [], "kind": kind, "children": children}

    total = sum(chapter["frames"] for chapter in receipt["chapters"])
    video = [clip(out / f"segment-{i+1:02d}.mp4", shot["output_frames"],
                  f"{i+1:02d} — {Path(shot['file']).stem}") for i, shot in enumerate(receipt["shots"])]
    if sum(c["source_range"]["duration"]["value"] for c in video) != total:
        raise ValueError("Video segment frame total differs from measured chapter timing")
    if abs(total/fps-receipt["duration_seconds"]) > 1/fps:
        raise ValueError("Receipt movie duration differs from its exact frame total")
    voice_receipt_path = out / "voice-receipt.json"
    voice_receipt = json.loads(voice_receipt_path.read_text(encoding="utf-8")) if voice_receipt_path.exists() else {}
    voice_name = voice_receipt.get("voice", voice_receipt.get("speaker", "Narration"))
    provider = voice_receipt.get("provider", "Voice")
    music_name = plan.get("music_title", "Dawn over the Settlement" if Path(plan["music"]).name in ("music-user-dawn.mp3", "WG_MUS_01.mp3") else Path(plan["music"]).stem)
    tracks = [track("Gameplay and titles", "Video", video),
              track(f"{voice_name} — narration", "Audio", [clip(out / "voice-stem.wav", total, f"{voice_name} / {provider}")]),
              track(f"{music_name} — music", "Audio", [clip(out / "music-stem.wav", total, music_name)])]
    version = plan.get("version", 1)
    version_label = f"{version:02d}" if isinstance(version, int) else str(version)
    default_name = f"World Genesis — Telegram {round(plan['audio']['target_seconds'])}s v{version_label}"
    timeline = {"OTIO_SCHEMA": "Timeline.1", "metadata": {"project": "World Genesis", "resolution": receipt.get("canvas", plan["canvas"]),
                "status": "Prepared; Resolve import not yet verified", "plan": str(plan_path), "narration": narration},
                "name": plan.get("timeline_name", default_name), "global_start_time": rt(0),
                "tracks": {"OTIO_SCHEMA": "Stack.1", "metadata": {}, "name": "Tracks", "source_range": None,
                           "effects": [], "markers": [], "children": tracks}}
    target = out / "WorldGenesis-Telegram-editable.otio"
    target.write_text(json.dumps(timeline, ensure_ascii=False, indent=2), encoding="utf-8")
    print(target)


if __name__ == "__main__":
    main()
