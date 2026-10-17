"""Offline narration using installed Qwen3-TTS. Outputs stay in this project."""
from pathlib import Path
import os, json, time, argparse, faulthandler

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Builds/Promo/Telegram-2026-10-17'
OUT.mkdir(parents=True, exist_ok=True)
os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1', TOKENIZERS_PARALLELISM='false', HF_HOME=str(OUT/'hf-cache'), PYTHONDONTWRITEBYTECODE='1')
os.environ.pop('TRANSFORMERS_CACHE', None)
for cache_name in ['numba-cache','temp']:
    (OUT/cache_name).mkdir(parents=True,exist_ok=True)
os.environ['NUMBA_CACHE_DIR']=str(OUT/'numba-cache')
os.environ['TEMP']=str(OUT/'temp')
os.environ['TMP']=str(OUT/'temp')
os.environ['OMP_NUM_THREADS']='8'
os.environ['MKL_NUM_THREADS']='8'
MODEL = Path('E:/Krea_tren_AI/GamesDev/Tools/voice/models/Qwen3-TTS-12Hz-1.7B-CustomVoice')
LINES = [
    'Сначала — берег, лес и маленький огонь. Потом — первые дома, дороги и чьи-то новые истории.',
    'Это Уорлд Дженезис — моя пиксельная песочница, в которой я собираю живой мир из множества маленьких деталей.',
    'Здесь жители несут добытые ресурсы к поселению. В мастерских сырьё превращается в полезные вещи.',
    'Рыбаки выходят к воде, а корабли отправляются в море с экипажем и провизией.',
    'Однажды от города отделяется маленький отряд. Несколько человек, доски, немного еды — и впереди место, где появится новый огонь.',
    'Но природа тоже меняет эту историю: приходит снег, поднимается вода, распространяется пожар.',
    'Игра ещё растёт. Сейчас я развиваю её системы, баланс и характер.',
    'Исходный код и версия для Виндоус уже на Гитхаб. Ссылка — под видео.',
]
INSTRUCT = ('Speak fluent native Russian with clear, natural pronunciation. A soft, warm, beautiful female narrator, '
            'telling a magical fairy tale with gentle wonder. Captivating and intimate. Calm and measured, medium-slow pace, '
            'gentle expressive pauses. Confident, sincere, never shouting. No exaggerated advertising delivery.')

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--sample', action='store_true'); parser.add_argument('--speaker',default='Serena'); args=parser.parse_args()
    faulthandler.dump_traceback_later(120,repeat=True)
    print('Importing torch',flush=True)
    import torch, soundfile as sf
    print('Importing Qwen',flush=True)
    from qwen_tts import Qwen3TTSModel
    print('Loading local weights on CPU',flush=True)
    torch.set_num_threads(8); torch.manual_seed(17010)
    started=time.time()
    model=Qwen3TTSModel.from_pretrained(str(MODEL), device_map='cpu', dtype=torch.bfloat16, attn_implementation='sdpa',local_files_only=True)
    print(json.dumps({'loaded_seconds':round(time.time()-started,2),'speaker':args.speaker}),flush=True)
    receipt={'model':str(MODEL),'speaker':args.speaker,'device':'cpu','dtype':'bfloat16','seed':17010,'instruction':INSTRUCT,'language':'Russian','takes':[]}
    texts=['Сначала — берег, лес и маленький огонь. Так рождается новая история.'] if args.sample else LINES
    for i,line in enumerate(texts):
        name=f'sample-{args.speaker}.wav' if args.sample else f'narration-{i+1:02d}.wav'
        path=OUT/name
        if path.exists() and not args.sample:
            audio, sr=sf.read(path); receipt['takes'].append({'file':name,'text':line,'duration':len(audio)/sr,'reused':True}); continue
        t=time.time(); torch.manual_seed(17010+i)
        wavs,sr=model.generate_custom_voice(text=line,language='Russian',speaker=args.speaker,instruct=INSTRUCT,max_new_tokens=500,temperature=0.7,top_p=0.8,repetition_penalty=1.05)
        sf.write(path,wavs[0],sr,subtype='PCM_16')
        entry={'file':name,'text':line,'duration':len(wavs[0])/sr,'generation_seconds':round(time.time()-t,2),'sample_rate':sr}
        receipt['takes'].append(entry); print(json.dumps(entry,ensure_ascii=False),flush=True)
        (OUT/('voice-sample-receipt.json' if args.sample else 'voice-receipt.json')).write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')

if __name__=='__main__': main()
