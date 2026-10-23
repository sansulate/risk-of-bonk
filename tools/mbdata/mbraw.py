import struct,sys,glob,os,json
class R:
    def __init__(s,b): s.b=b; s.o=0
    def i(s): v=struct.unpack_from('<i',s.b,s.o)[0]; s.o+=4; return v
    def l(s): v=struct.unpack_from('<q',s.b,s.o)[0]; s.o+=8; return v
    def f(s): v=struct.unpack_from('<f',s.b,s.o)[0]; s.o+=4; return round(v,4)
    def bo(s): v=s.b[s.o]; s.o+=4; return bool(v)
    def st(s):
        n=s.i(); v=s.b[s.o:s.o+n].decode('utf8','replace'); s.o+=n; s.o=(s.o+3)&~3; return v
    def pptr(s): fid=s.i(); pid=s.l(); return (fid,pid)
def header(r):
    r.pptr(); r.bo(); r.pptr(); return r.st()
def locstr(r):
    r.st(); r.l(); r.st(); r.i(); r.bo(); n=r.i()
    assert n==0, "local variables"
def unlockable(r):
    d={}
    d['isEnabled']=r.bo(); r.bo(); r.bo(); r.st(); d['price']=r.i(); r.i(); locstr(r); locstr(r)
    for _ in range(2):
        n=r.i(); assert n==0,"loc keys %d"%n
    return d
