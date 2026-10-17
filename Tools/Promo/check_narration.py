"""Transcribe a supplied narration locally and retain word timing for editing.

ASR detects omissions; it does not certify voice beauty or artistic acceptance.
"""
from pathlib import Path
import os, json, argparse, ast, re, difflib

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Builds/Promo/Telegram-2026-10-17'
for name in ('temp','numba-cache','hf-cache'):
    (OUT/name).mkdir(parents=True,exist_ok=True)
os.environ.update(HF_HUB_OFFLINE='1',TRANSFORMERS_OFFLINE='1',HF_HOME=str(OUT/'hf-cache'),NUMBA_CACHE_DIR=str(OUT/'numba-cache'),TEMP=str(OUT/'temp'),TMP=str(OUT/'temp'))
os.environ.pop('TRANSFORMERS_CACHE',None)

def main():
    p=argparse.ArgumentParser(); p.add_argument('audio'); p.add_argument('--output',default='narration-asr.json'); p.add_argument('--outdir',type=Path,default=OUT); p.add_argument('--narration',type=Path); args=p.parse_args()
    from faster_whisper import WhisperModel
    import soundfile as sf
    import numpy as np
    from scipy.signal import resample_poly
    import math
    model=WhisperModel('E:/Krea_tren_AI/GamesDev/Tools/voice/models/faster-whisper-small',device='cpu',compute_type='int8',cpu_threads=6,local_files_only=True)
    # Decode with libsndfile; installed PyAV does not accept metadata_errors.
    samples,sr=sf.read(args.audio,dtype='float32')
    if samples.ndim>1: samples=samples.mean(axis=1)
    if sr!=16000:
        gcd=math.gcd(sr,16000); samples=resample_poly(samples,16000//gcd,sr//gcd).astype(np.float32)
    segments,info=model.transcribe(samples,language='ru',beam_size=5,word_timestamps=True,vad_filter=False)
    entries=[]
    for s in segments:
        entries.append({'start':s.start,'end':s.end,'text':s.text,'words':[{'start':w.start,'end':w.end,'word':w.word,'probability':w.probability} for w in s.words]})
        print(f'{s.start:.2f} {s.end:.2f}: {s.text}',flush=True)
    actual=' '.join(s['text'] for s in entries)
    tree=ast.parse((ROOT/'Tools/Promo/generate_narration.py').read_text('utf-8'))
    lines=next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='LINES' for t in n.targets))
    if args.narration:
        lines=[x.strip() for x in re.split(r'\n\s*\n',args.narration.read_text('utf-8')) if x.strip()]
    normalize=lambda t: re.sub(r'[^а-яёa-z0-9 ]','',t.lower().replace('ё','е'))
    expected=normalize(' '.join(lines)).split(); heard=normalize(actual).split()
    sm=difflib.SequenceMatcher(a=expected,b=heard)
    result={'input':str(Path(args.audio).resolve()),'language':info.language,'duration':info.duration,'expected':' '.join(lines),'asr':actual,'word_match_ratio':sm.ratio(),'mismatches':[{'kind':tag,'expected':expected[i:j],'asr':heard[k:l]} for tag,i,j,k,l in sm.get_opcodes() if tag!='equal'],'segments':entries,'human_listening':'not performed by this script'}
    args.outdir.mkdir(parents=True,exist_ok=True)
    (args.outdir/args.output).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print('word match',round(sm.ratio(),4),flush=True)

if __name__=='__main__': main()
