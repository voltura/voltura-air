"""Optional narration refresh: Python + edge-tts 7.2.8; sends the public script to Edge TTS."""

import asyncio
import json
import argparse
from pathlib import Path

import edge_tts

output = Path(__file__).resolve().parents[2] / "assets" / "branding" / "youtube"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("guide", nargs="?", default="getting-started", choices=["getting-started", "cloud-relay"])
guide = parser.parse_args().guide
shots = json.loads((output / f"{guide}.json").read_text(encoding="utf-8"))


async def main():
    narration = output / ("narration" if guide == "getting-started" else f"{guide}-narration")
    narration.mkdir(exist_ok=True)
    for index, shot in enumerate(shots, start=1):
        stem = narration / f"{index:02d}"
        communicator = edge_tts.Communicate(
            shot["narration"], "en-US-AriaNeural", rate="-3%"
        )
        subtitles = edge_tts.SubMaker()
        with stem.with_suffix(".mp3").open("wb") as audio:
            async for chunk in communicator.stream():
                if chunk["type"] == "audio":
                    audio.write(chunk["data"])
                elif chunk["type"] in ("WordBoundary", "SentenceBoundary"):
                    subtitles.feed(chunk)
        stem.with_suffix(".srt").write_text(subtitles.get_srt(), encoding="utf-8")
        print(f"Generated narration {index}/{len(shots)}", flush=True)


if __name__ == "__main__":
    asyncio.run(main())
