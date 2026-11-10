import importlib.util, json, tempfile
from pathlib import Path

spec=importlib.util.spec_from_file_location('summary',Path(__file__).with_name('Summarize-OutputComparison.py'))
summary=importlib.util.module_from_spec(spec);spec.loader.exec_module(summary)
with tempfile.TemporaryDirectory(prefix='AirBridge-offline-summary-') as folder:
    root=Path(folder);records=[];key='BTHENUM&001122334455_device'
    for i in range(4):
        start=1000+i*20000
        a={'Qpc':start,'Frequency':1000,'Phones':{key:'1:1'},'Routes':[{'Source':key,'PhoneFrames':0,'UnderflowMilliseconds':0,'QueueClearCount':0,'QueueDroppedBytes':0,'NonZeroSamples':0}]}
        b={'Qpc':start+10000,'Frequency':1000,'Phones':{key:'1:1'},'Routes':[{'Source':key,'PhoneFrames':900,'UnderflowMilliseconds':1000,'QueueClearCount':0,'QueueDroppedBytes':0,'NonZeroSamples':900}]}
        records.append({'Kind':'phase','Value':{'Name':str(i),'Start':a,'End':b}})
    (root/'comparison.jsonl').write_text('\n'.join(json.dumps(x) for x in records),encoding='utf-8')
    for pid,tick,kind in [(1,1100,'capture-complete'),(2,1130,'ipc-received')]:
        (root/f'timing-{pid}-test.csv').write_text(f'# frequency=1000,pid={pid},utc=test\nbegin_qpc,end_qpc,thread,kind,key,a,b,c\n{tick},{tick},1,{kind},{key},1,1764,256\n# dropped=0,errors=0\n',encoding='utf-8')
    result=summary.summarize(root)
    assert not result['issues'],result['issues']
    assert all(x['packets_per_second']==90 and x['padding_ms']==1000 for x in result['rates'])
    assert next(x for x in result['timings'] if x['stage']=='capture-to-parent')['max_ms']==30
    path=root/'timing-2-test.csv';path.write_text(path.read_text(encoding='utf-8').replace('# dropped=0,errors=0','# dropped=2,errors=0'),encoding='utf-8')
    assert summary.summarize(root)['issues']
print('PASS exact phase rate, 10% deficit, 30ms transit, trace loss rejection; no hardware')
