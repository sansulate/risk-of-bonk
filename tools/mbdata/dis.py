# usage: dis.py Class$$Method [maxInstr]   (names as in script.json, e.g. WeaponBase$$GetCooldown)
import json,sys,struct,pickle,os,re
from capstone import *
S=os.environ.get("MBDUMP_CACHE", os.path.dirname(os.path.abspath(__file__)))
GA=os.environ.get("MEGABONK_GA", r"C:Program Files (x86)SteamsteamappsmmonMegabonkGameAssembly.dll")
cache=os.path.join(S,'syms.pkl')
if os.path.exists(cache): syms,byname,meta=pickle.load(open(cache,'rb'))
else:
    d=json.load(open(os.path.join(os.environ.get("MBDUMP", os.path.join(S, "mbdump")), "script.json")))
    syms={};byname={}
    for m in d['ScriptMethod']:
        syms.setdefault(m['Address'],m['Name']); byname.setdefault(m['Name'],[]).append(m['Address'])
    meta={}
    for m in d['ScriptMetadata']: meta[m['Address']]=m['Name']
    for m in d['ScriptMetadataMethod']: meta[m['Address']]=m['Name']
    for m in d['ScriptString']: meta[m['Address']]='"'+m['Value'][:60]+'"'
    pickle.dump((syms,byname,meta),open(cache,'wb'))
pe=open(GA,'rb').read()
# section mapping
peo=struct.unpack_from('<I',pe,0x3c)[0]; nsec=struct.unpack_from('<H',pe,peo+6)[0]; opt=struct.unpack_from('<H',pe,peo+20)[0]
secs=[]
for i in range(nsec):
    o=peo+24+opt+40*i; name=pe[o:o+8].rstrip(b'\0'); vs,va,rs,ra=struct.unpack_from('<IIII',pe,o+8); secs.append((va,vs,ra,rs))
def rva2off(r):
    for va,vs,ra,rs in secs:
        if va<=r<va+max(vs,rs): return r-va+ra
def rd(r,n):
    o=rva2off(r); return pe[o:o+n] if o is not None else None
BASE=0x180000000
name=sys.argv[1]; maxn=int(sys.argv[2]) if len(sys.argv)>2 else 400
addrs=byname.get(name) or [a for k,v in byname.items() if k.endswith('.'+name) for a in v] or ([int(name,16)-BASE] if name.startswith('0x') else [])
for a in addrs:
    print(f"== {name} rva {a:#x}")
    md=Cs(CS_ARCH_X86,CS_MODE_64); md.detail=False
    code=rd(a,maxn*8); n=0
    for ins in md.disasm(code,BASE+a):
        n+=1
        s=f"{ins.address-BASE:8x}  {ins.mnemonic:7} {ins.op_str}"
        m=re.search(r'\[rip ([+-]) (0x[0-9a-f]+)\]',ins.op_str)
        if m:
            tgt=ins.address+ins.size+(int(m.group(2),16)*(1 if m.group(1)=='+' else -1))-BASE
            if tgt in meta: s+=f"   ; {meta[tgt]}"
            else:
                b=rd(tgt,8)
                if b: s+=f"   ; [{tgt:#x}] f={struct.unpack('<f',b[:4])[0]:.6g} d={struct.unpack('<d',b)[0]:.6g}"
        if ins.mnemonic in('call','jmp') and ins.op_str.startswith('0x'):
            t=int(ins.op_str,16)-BASE
            if t in syms: s+=f"   ; {syms[t]}"
        print(s)
        if ins.mnemonic in ('ret','int3') and n>3 and ins.mnemonic=='int3': break
        if n>=maxn: break
