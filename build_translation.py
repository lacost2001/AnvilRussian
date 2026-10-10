"""Build deterministic UE locres and a data-only update feed; no game files needed."""
import collections, hashlib, json, re, struct, zlib
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent
out = root / 'dist'
out.mkdir(exist_ok=True)
config = json.loads((root / 'release-config.json').read_text(encoding='utf-8-sig'))
records = json.loads((root / 'translations.json').read_text(encoding='utf-8-sig'))
rows = {}
def fstr(s):
    if not s: return struct.pack('<i', 0)
    data = (s + '\0').encode('utf-16le')
    return struct.pack('<i', -len(data)//2) + data
def source_hash(s):
    data = s.encode('utf-16le')
    return zlib.crc32(b''.join(data[i:i+2]+b'\0\0' for i in range(0,len(data),2)))
for record in records:
    en, ru = record['source'], record['russian']
    for pattern in (r'\{[^{}]*\}', r'<[^>]+>'):
        assert collections.Counter(re.findall(pattern,en)) == collections.Counter(re.findall(pattern,ru)), en
    namespace = rows.setdefault(record['namespace'], {})
    value = (source_hash(en),ru)
    assert record['key'] not in namespace or namespace[record['key']] == value
    namespace[record['key']] = value
for key in ('CallForReinforcementsAlert','GatheringResourceTextFormat','UseEntityPromptTextFormat','BountyComplete','BountyItemsMissing'):
    assert key in rows['Anvil'] and re.search('[А-Яа-я]',rows['Anvil'][key][1])
body=bytearray(struct.pack('<i',len(rows))); strings=[]
for ns, entries in rows.items():
    body += fstr(ns)+struct.pack('<i',len(entries))
    for key,(crc,ru) in entries.items():
        body += fstr(key)+struct.pack('<Ii',crc,len(strings)); strings.append(ru)
locres = bytes.fromhex('0e147475674a03fc4a15909dc3377f1b')+b'\1'+struct.pack('<q',25+len(body))+body+struct.pack('<i',len(strings))+b''.join(fstr(s) for s in strings)
(out/'Game.locres').write_bytes(locres)
def ansi(s): return struct.pack('<i',len(s)+1)+s.encode()+b'\0'
meta = bytes.fromhex('4fee4ca1684855836c4c46bd70da507c')+b'\1'+ansi('en')+ansi('en/Game.locres')+struct.pack('<i',2)+ansi('en')+ansi('ru')
(out/'Game.locmeta').write_bytes(meta)
import base64
feed=ET.Element('TranslationUpdate',schema='1',version=config['version'],gameVersion=config['gameVersion'],exeHash=config['exeHash'],pakSize=str(config['pakSize']),pakTailHash=config['pakTailHash'])
for name,data in [('Game.locres',locres),('Game.locmeta',meta),('Fonts.pak',(root/'Fonts.pak').read_bytes())]:
    node=ET.SubElement(feed,'File',name=name,sha256=hashlib.sha256(data).hexdigest())
    node.text=base64.b64encode(data).decode()
for digest in config['previousLocresHashes']:
    ET.SubElement(feed,'PreviousLocres',sha256=digest)
ET.ElementTree(feed).write(out/'translation.xml',encoding='utf-8',xml_declaration=True)
print(f"Built {len(strings)} translations, version {config['version']}; placeholders verified.")

