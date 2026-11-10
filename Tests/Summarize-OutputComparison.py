"""Offline only: phase-normalized rates, per-stage timings and IPC transit correlations.
No claim of bandwidth exhaustion or CPU scheduler diagnosis is made from application events.
Use scheduling.etl in WPA for Ready/CSwitch/DPC/ISR and correlate QPC + native thread IDs.
"""
import argparse, csv, json, math, re
from collections import defaultdict
from pathlib import Path

def percentile(values, p):
    return sorted(values)[min(len(values)-1, math.ceil(len(values)*p)-1)] if values else None

def summarize(root):
    records=[json.loads(x) for x in (root/'comparison.jsonl').read_text(encoding='utf-8-sig').splitlines() if x.strip()]
    phases=[x['Value'] for x in records if x['Kind']=='phase']
    invalid=[x for x in records if x['Kind'] in ('failed','restore-failed')]
    issues=[]
    if len(phases)!=4 or invalid: issues.append('Incomplete/failed experiment: do not treat as a valid A/B/A/B result.')
    durations=defaultdict(list); arrivals={}; receives=[]; metadata=[]
    for path in root.glob('timing-*.csv'):
        with path.open(encoding='utf-8-sig',newline='') as f:
            header=f.readline(); m=re.search(r'frequency=(\d+),pid=(\d+)',header)
            if not m: issues.append(f'{path.name}: missing clock metadata');continue
            frequency,pid=map(int,m.groups());footer=False
            reader=csv.DictReader(f)
            for e in reader:
                if e['begin_qpc'].startswith('#'):
                    # CSV splits footer at its comma, so inspect both columns.
                    footer=True
                    text=','.join(str(v) for v in e.values() if v is not None)
                    d=re.search(r'dropped=(\d+)',text);err=re.search(r'errors=(\d+)',text)
                    if not d or not err or int(d[1]) or int(err[1]):issues.append(f'{path.name}: trace drops/errors or invalid footer')
                    continue
                begin,end=int(e['begin_qpc']),int(e['end_qpc']);key=e['key'];kind=e['kind']
                phase=next((p for p in phases if p['Start']['Qpc']<=end<=p['End']['Qpc']),None)
                if phase is None:continue
                name=phase['Name'];durations[(name,key,kind)].append((end-begin)*1000/frequency)
                address=re.search(r'&([0-9a-f]{12})_',key,re.I)
                if address and kind in ('capture-complete','ipc-received'):
                    identity=(name,address[1].upper(),int(e['a']))
                    if kind=='capture-complete':arrivals[identity]=(end,frequency)
                    else:receives.append((identity,end))
            if not footer:issues.append(f'{path.name}: no final footer; completeness unknown')
            metadata.append({'file':path.name,'pid':pid,'frequency':frequency})
    if not metadata: issues.append('No per-process timing files: scheduling/IPC evidence is unavailable.')
    expected_pids={int(identity.split(':')[0]) for p in phases for identity in p['Start']['Phones'].values()}
    actual_pids={m['pid'] for m in metadata}
    if expected_pids-actual_pids: issues.append('Missing phone timing files for PIDs: '+str(sorted(expected_pids-actual_pids)))
    if len({m['frequency'] for m in metadata})>1: issues.append('Clock frequencies differ; cross-process timing correlation is invalid.')
    for identity,end in receives:
        if identity in arrivals:
            start,freq=arrivals[identity]
            durations[(identity[0],identity[1],'capture-to-parent')].append((end-start)*1000/freq)
    rows=[]
    for p in phases:
        elapsed=(p['End']['Qpc']-p['Start']['Qpc'])/p['Start']['Frequency']
        before={s['Source']:s for s in p['Start']['Routes']}
        for s in p['End']['Routes']:
            key=s['Source']
            if key not in p['Start']['Phones'] or key not in before:continue
            b=before[key];frames=s['PhoneFrames']-b['PhoneFrames']
            rows.append({'phase':p['Name'],'source':key,'seconds':elapsed,'packets':frames,'packets_per_second':frames/elapsed,
                         'audio_seconds':frames/100,'padding_ms':s['UnderflowMilliseconds']-b['UnderflowMilliseconds'],
                         'queue_clears':s['QueueClearCount']-b['QueueClearCount'],'dropped_bytes':s['QueueDroppedBytes']-b['QueueDroppedBytes'],
                         'signal_samples':s.get('NonZeroSamples',0)-b.get('NonZeroSamples',0) if 'NonZeroSamples' in s and 'NonZeroSamples' in b else None})
    timing=[{'phase':p,'key':k,'stage':stage,'count':len(v),'p50_ms':percentile(v,.5),'p95_ms':percentile(v,.95),'p99_ms':percentile(v,.99),'max_ms':max(v)} for (p,k,stage),v in sorted(durations.items())]
    result={'issues':issues,'rates':rows,'timings':timing,'files':metadata,
            'interpretation':'Application timestamps cannot prove radio bandwidth exhaustion. Low supply + short application work still requires ETW/driver analysis. Zero packets may mean pause. Read-completion observation includes scheduler delay.'}
    (root/'summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path);args=parser.parse_args()
    result=summarize(args.directory)
    print(json.dumps({'issues':result['issues'],'rates':result['rates']},ensure_ascii=False,indent=2))
