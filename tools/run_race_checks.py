#!/usr/bin/env python3
"""Gerçek Unity player ile Relay duman testi ve tekrarlanabilir performans koşusu."""
import argparse, json, os, re, subprocess, time
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--mode',choices=['online','perf'],default='online')
p.add_argument('--label',default='temizlik')
p.add_argument('--seconds',type=float,default=60)
p.add_argument('--gpu',choices=['intel','nvidia'],default='intel')
p.add_argument('--api',choices=['auto','vulkan','glcore'],default='auto')
p.add_argument('--no-debris-collision',action='store_true',help='Geçici test: elenen araç enkazlarının collider\'larını kapatır.')
a=p.parse_args()
if not re.fullmatch(r'[A-Za-z0-9_-]+',a.label):p.error('Etiket yalnızca harf, rakam, alt çizgi veya tire içermeli.')
root=Path(__file__).resolve().parents[1]
out=root/'Builds'/'reports'/a.label;out.mkdir(parents=True,exist_ok=True)
env=os.environ.copy()
for key in ['__NV_PRIME_RENDER_OFFLOAD','__GLX_VENDOR_LIBRARY_NAME','__VK_LAYER_NV_optimus']:env.pop(key,None)
if a.gpu=='nvidia':env.update(__NV_PRIME_RENDER_OFFLOAD='1',__GLX_VENDOR_LIBRARY_NAME='nvidia',__VK_LAYER_NV_optimus='NVIDIA_only')
if a.api=='vulkan':
    # İki GPU varken sürücünün varsayılan sırası ölçülen kartı değiştirmesin.
    icd=Path('/usr/share/vulkan/icd.d')/('nvidia_icd.json' if a.gpu=='nvidia' else 'intel_icd.json')
    if icd.exists():
        env.pop('VK_ICD_FILENAMES',None);env.pop('VK_ADD_DRIVER_FILES',None);env['VK_DRIVER_FILES']=str(icd)
prefs=root/'Builds'/('perf-prefs' if a.mode=='perf' else 'online-prefs')/a.label
prefs.mkdir(parents=True,exist_ok=True);env['XDG_CONFIG_HOME']=str(prefs)
exe=root/'Builds/LinuxTest/Harvester.x86_64'
common=[str(exe),'-screen-fullscreen','0','-screen-width','1920' if a.mode=='perf' else '1280','-screen-height','1080' if a.mode=='perf' else '720']
if a.api!='auto':common+=['-force-'+a.api]
if a.no_debris_collision:common+=['-testEnkazCarpismaYok']
processes=[]
def launch(role,flags):
    log=out/(role+'.log')
    if log.exists():log.unlink()
    proc=subprocess.Popen(common+flags+['-logFile',str(log)],cwd=root,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    processes.append(proc);print(f'{role}: pid={proc.pid}, log={log}',flush=True);return proc,log

def read(path):return path.read_text(errors='replace') if path.exists() else ''
result={'label':a.label,'gpu_requested':a.gpu,'api_requested':a.api,'mode':a.mode,'debris_collision_disabled_requested':a.no_debris_collision}
try:
    if a.mode=='online':
        host,hlog=launch('host',['-autoHost','-autoOnline','-autoStart','2','-autoRounds','1','-autoDrive','-autoTrace','-autoShot','-autoElim','8'])
        deadline=time.monotonic()+45
        while '[AutoTest] Davet kodu:' not in read(hlog):
            if host.poll() is not None or time.monotonic()>deadline:raise RuntimeError('Host davet kodu alamadı.')
            time.sleep(.5)
        client,clog=launch('client',['-autoJoin','kod','-autoOnline','-autoDrive','-autoTrace','-autoShot'])
        deadline=time.monotonic()+max(120,a.seconds)
        while time.monotonic()<deadline:
            h=read(hlog);c=read(clog)
            ended=h.rfind('[Race] Yarış bitti:')
            returned=ended>=0 and '[Lobby] Oda açıldı:' in h[ended:]
            client_return=c.count('[Lobby] Oda açıldı:')>=2
            if returned and client_return:break
            if host.poll() is not None or client.poll() is not None:raise RuntimeError('Player beklenmeden kapandı.')
            time.sleep(1)
        result.update(host_race_started='[Race] Yarış başladı' in h,host_finished=ended>=0,host_returned=returned,client_returned=client_return)
        result['passed']=all(result[k] for k in ['host_race_started','host_finished','host_returned','client_returned'])
        result['network_mismatch']='NetworkConfig mismatch' in h+c or 'PrefabHash' in h+c
        result['passed']=result['passed'] and not result['network_mismatch']
        test_marker='[Test] GEÇİCİ: elenen araç enkazlarının'
        result['debris_collision_disabled_actual']=test_marker in h and test_marker in c
        result['passed']=result['passed'] and result['debris_collision_disabled_actual']==a.no_debris_collision
    else:
        host,hlog=launch('host',['-autoHost','-autoStart','1','-autoRounds','100','-autoBots','3','-autoDrive','-autoLaps','999','-perfProbe',str(a.seconds),'-perfLabel',a.label,'-perfOutput',str(out)])
        deadline=time.monotonic()+a.seconds*15+120
        while time.monotonic()<deadline:
            text=read(hlog)
            if '[Perf] TAMAMLANDI' in text:break
            if host.poll() is not None:raise RuntimeError('Ölçüm tamamlanmadan player kapandı.')
            time.sleep(1)
        result['passed']='[Perf] TAMAMLANDI' in text
        result['summary']=[line for line in text.splitlines() if line.startswith('[Perf]')]
        summary_path=out/(a.label+'.json')
        if summary_path.exists():
            summary=json.loads(summary_path.read_text())
            result['actual_gpu']=summary['gpu'];result['actual_api']=summary['api']
            result['actual_resolution']=[summary['width'],summary['height']]
            expected_api={'vulkan':'Vulkan','glcore':'OpenGLCore'}.get(a.api)
            result['api_matches_request']=expected_api is None or summary['api']==expected_api
            result['debris_collision_disabled_actual']=summary.get('debrisCollisionDisabled',False)
            gpu_ok=('NVIDIA' in summary['gpu']) if a.gpu=='nvidia' else ('Intel' in summary['gpu'])
            result['passed']=result['passed'] and gpu_ok and summary['width']==1920 and summary['height']==1080
            result['passed']=result['passed'] and result['debris_collision_disabled_actual']==a.no_debris_collision
            result['passed']=result['passed'] and result['api_matches_request']

    for path in out.glob('*.log'):
        text=read(path)
        for line in text.splitlines():
            if any(word in line for word in ['Renderer:','Vendor:','Vulkan API','GfxDevice:']):print(line,flush=True)
except Exception as error:
    result.update(passed=False,error=str(error))
finally:
    for process in processes:
        if process.poll() is None:process.terminate()
    for process in processes:
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
    (out/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
    print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
raise SystemExit(0 if result.get('passed') else 1)
