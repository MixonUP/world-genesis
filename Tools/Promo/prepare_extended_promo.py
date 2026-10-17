"""Prepare revision 2: third-person story, verified mechanics and production facts."""
from pathlib import Path
import json, re, copy, hashlib, subprocess
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Builds/Promo/Telegram-2026-10-17/v02'
SRC=ROOT/'SourceAssets/Promo/Telegram-2026-10-17'
OUT.mkdir(parents=True,exist_ok=True)
font=lambda size,bold=False: ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf' if bold else 'C:/Windows/Fonts/segoeui.ttf',size)
def graphic(name,title,rows,footer):
    im=Image.new('RGB',(1240,660),'#0b1828');d=ImageDraw.Draw(im)
    d.text((42,32),title,font=font(34,True),fill='#d7b876')
    for i,(number,label,detail) in enumerate(rows):
        y=110+i*142
        d.rounded_rectangle((30,y,1210,y+122),radius=12,fill='#172b3b')
        d.text((52,y+14),number,font=font(61,True),fill='#f2ead9')
        d.text((520,y+16),label,font=font(32,True),fill='#f2ead9')
        d.text((520,y+64),detail,font=font(24),fill='#a4b1bc')
    for i,line in enumerate(footer):d.text((42,558+i*34),line,font=font(22),fill='#a4b1bc')
    im.save(OUT/name)

graphic('technology.png','ИНЖЕНЕРНАЯ ОСНОВА',[
    ('Unity 6 / C#','Движок и язык','6000.0.45f1 • Windows x64'),
    ('Simulation','Собственный мир','Потребности • ресурсы • решения жителей'),
    ('Codex + Git','Разработка и история','Специализированные агенты • открытый код')],
    ['Процедурная генерация • дискретная симуляция • воспроизводимый seed',
     'Озвучка: ElevenLabs • музыка: Suno • монтаж: DaVinci Resolve'])
graphic('development-stats.png','12–14 ОКТЯБРЯ 2026 • ВЕРСИЯ 0.10',[
    ('57 ч 46 м','До опубликованной версии','От первой проектной сессии • календарное время'),
    ('22,9 млн','Токенов без кеша','721,2 млн с учётом кешированного контекста'),
    ('466','Пройденных автотестов','398 EditMode + 68 PlayMode')],
    ['По локальным журналам Codex и отчётам проекта; не активные человеко-часы.',
     'Учтены 46 сессий со счётчиками; в 9 дополнительных счётчиков нет.'])

base=json.loads((SRC/'edit-plan.json').read_text('utf-8'))
p=copy.deepcopy(base);p['version']=2;p['narration']='SourceAssets/Promo/Telegram-2026-10-17/narration-v02.txt'
p['output_filename']='WorldGenesis-Telegram-Extended.mp4'
p['audio'].update(target_seconds=105,tempo_min=0.98,tempo_max=1.02,music_volume=0.25,first_lead=0.8,chapter_lead=0.22,chapter_tail=0.3,last_tail=2.0)
c=copy.deepcopy(base['chapters'])
c[2]['title']='Жизнь складывается из маленьких дел'
c[3]['title']='Рыбалка, караваны и морская торговля'
c[3]['shots'].insert(1,{'file':'Evidence/Captures/20261012-151758-rural-caravan.mp4','crop':[200,80,850,465],'weight':0.35,'kind':'video','in':0,'max_seconds':6.0,'version':'0.4','conditions':'Prepared caravan showcase; actual finite cargo and travel. Native Player capture.'})
c[3]['shots'][0]['weight']=0.3;c[3]['shots'][-1]['weight']=0.35
war={'title':'За землю и поселения','side':['Походы.','Территории.','Новый правитель.'],'label':'КОГДА ОБЪЯВЛЕНА ВОЙНА','nominal_seconds':9,'shots':[]}
for name in ['occupation','conquest']:
    file='20261014-081217-occupation-final.png' if name=='occupation' else '20261014-081235-conquest-final.png'
    war['shots'].append({'file':'Evidence/Captures/'+file,'crop':[310,110,930,690],'weight':0.5,'kind':'still','version':'0.10','conditions':'Prepared conquest showcase with equipped attackers and defenders away from center; real capture timer/ownership. Does not show a siege.'})
def info_chapter(title,side,label,file):
    return {'title':title,'side':side,'label':label,'nominal_seconds':11,'shots':[{'file':'Builds/Promo/Telegram-2026-10-17/v02/'+file,'crop':[0,0,1240,660],'weight':1,'kind':'still','version':'editorial','conditions':'Editorial information card. Facts from project documents and project-usage-audit.json; not a game screenshot.'}]}
tech=info_chapter('Архитектура живого мира',['Unity.','C#.','Своя симуляция.'],'ТЕХНОЛОГИИ','technology.png')
stats=info_chapter('Масштаб, который можно проверить',['12–14','октября','2026.'],'РАЗРАБОТКА В ЦИФРАХ','development-stats.png')
c[6]['title']='Мир, который продолжает расти'
p['chapters']=c[:4]+[war]+c[4:6]+[tech,stats]+c[6:]
p['notes'].append('Revision 2: third-person voice; extended narrative; development usage excludes promo work. User-supplied Dawn variant is the music source. Tech/stat cards are editorial graphics.')
(SRC/'edit-plan-v02.json').write_text(json.dumps(p,ensure_ascii=False,indent=2),encoding='utf-8')

asr=json.loads((OUT/'narration-asr.json').read_text('utf-8'));words=[w for s in asr['segments'] for w in s['words']]
norm=lambda s: re.sub(r'[^а-яa-z]','',s.lower().replace('ё','е'))
tokens=[norm(w['word']) for w in words]
anchors=['это','жители','рыбаки','возводятся','но','природа','в основе','от первого','игра продолжает','исходный']
indices=[0];cursor=0
for anchor in anchors:
    seq=anchor.split();cursor=next(i for i in range(cursor+1,len(tokens)-len(seq)+1) if tokens[i:i+len(seq)]==seq);indices.append(cursor)
boundaries=[0.0]+[(words[i-1]['end']+words[i]['start'])/2 for i in indices[1:]]+[asr['duration']]
source=OUT/'narration-elevenlabs-original.mp3';records=[]
for i,(start,end) in enumerate(zip(boundaries,boundaries[1:]),1):
    target=OUT/f'narration-{i:02d}.wav'
    subprocess.run(['C:/FFmpeg_exe/ffmpeg.exe','-v','error','-y','-i',str(source),'-ss',str(start),'-t',str(end-start),'-ar','48000','-ac','1','-c:a','pcm_s16le',str(target)],check=True)
    records.append({'file':target.name,'source_start':start,'source_end':end,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
receipt={'revision':2,'provider':'ElevenLabs','model':'Eleven v4','voice':'Victoria - Warm & Cinematic','voice_id':'D5RRIJYa9pFwxiSpbGbR','direction':'Soft, warm voice, gentle wonder, intimate storytelling','point_of_view':'third-person narrator','generation_date':'2026-10-17','generation':'Generation 1 from one request producing two variants','credits_used':1518,'source_file':source.name,'source_seconds':asr['duration'],'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'segments':records,'verification':{'ASR_word_match_ratio':asr['word_match_ratio'],'notes':'All 11 paragraphs present; ASR renders numbers/products in digits/Latin. No spoken direction tag or first-person author claims.','human_artistic_acceptance':'pending'}}
(OUT/'voice-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'chapters':len(p['chapters']),'voice_seconds':asr['duration'],'boundaries':boundaries}))
