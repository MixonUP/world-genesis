"""Split the downloaded ElevenLabs narration at ASR-observed chapter boundaries."""
from pathlib import Path
import json, hashlib, subprocess, re

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Builds/Promo/Telegram-2026-10-17'
source = OUT / 'narration-elevenlabs-original.mp3'
asr = json.loads((OUT/'narration-asr.json').read_text('utf-8'))
words = [w for seg in asr['segments'] for w in seg['words']]
norm = lambda s: re.sub(r'[^а-яa-z]', '', s.lower().replace('ё','е'))
anchors = ['это', 'здесь', 'рыбаки', 'однажды', 'но', 'игра', 'исходный']
indices, cursor = [0], 0
for anchor in anchors:
    cursor = next(i for i in range(cursor+1,len(words)) if norm(words[i]['word']) == anchor)
    indices.append(cursor)
boundaries = [0.0] + [(words[i-1]['end']+words[i]['start'])/2 for i in indices[1:]] + [asr['duration']]
records = []
for i, (start,end) in enumerate(zip(boundaries,boundaries[1:]),1):
    target=OUT/f'narration-{i:02d}.wav'
    subprocess.run(['C:/FFmpeg_exe/ffmpeg.exe','-hide_banner','-loglevel','error','-y','-i',str(source),'-ss',f'{start:.6f}','-t',f'{end-start:.6f}','-ar','48000','-ac','1','-c:a','pcm_s16le',str(target)],check=True)
    records.append({'file':target.name,'source_start':start,'source_end':end,'seconds':end-start,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
receipt = {'provider':'ElevenLabs','model':'Eleven v4','voice':'Victoria - Warm & Cinematic','voice_id':'D5RRIJYa9pFwxiSpbGbR','language':'Russian','direction':'Soft, warm voice, gentle wonder, intimate storytelling','generation_date':'2026-10-17','generation':'Generation 1; one synthesis request, UI produced two variants','source_file':source.name,'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'source_seconds':asr['duration'],'credits_used':795,'ui_settings':{'stability':0.5,'similarity':0.75,'output':'MP3 44.1 kHz 128 kbps'},'segmentation':'Eight chapters cut halfway between observed word timings, no words removed','segments':records,'verification':{'ASR_word_match_ratio':asr['word_match_ratio'],'ASR_notes':'All eight paragraphs present; differences include Latin product names and one grammatical-ending recognition uncertainty. Direction tag not spoken.','human_artistic_acceptance':'pending'}}
(OUT/'voice-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'boundaries':boundaries,'provider':receipt['provider'],'duration':asr['duration']},ensure_ascii=False))
