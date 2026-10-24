# Decodes Megabonk StageData assets dumped raw by mbread (RAW=1 BIN=dir ... StageData) into JSON:
# python stages.py <dir> out.json      (pptrs are (fileId, pathId) pairs; names via mbfx '#pathId' lookups)
from mbraw import *


def arr(r, f):
    return [f(r) for _ in range(r.i())]


def col(r): return [r.f(), r.f(), r.f(), r.f()]


def rmo(r):
    d = {'amount': r.i(), 'maxAmount': r.i(), 'checkRadius': r.f(), 'scaleMin': r.f(), 'scaleMax': r.f(),
         'maxSlopeAngle': r.f(), 'upOffset': r.f()}
    d['prefabs'] = arr(r, lambda r: r.pptr()[1])
    d['randomRotation'] = [r.f(), r.f(), r.f()]
    d['alignWithNormal'] = r.bo()
    return d


PARAMS = {8160: 'ParamForest1', 8161: 'ParamForest2', 8162: 'ParamForest3', 8167: 'ParamGraveyard'}
out = {}
for p in sorted(glob.glob(os.path.join(sys.argv[1], 'StageData__*.bin'))):
    b = open(p, 'rb').read(); r = R(b); name = header(r)
    locstr(r); locstr(r)
    d = {'mapEdgeFillType': ['Island', 'Walls', 'Trees', 'None'][r.i()]}
    d['waterMaterial'] = r.pptr()[1]; d['waterSplashFx'] = r.pptr()[1]; d['grassMaterial'] = r.pptr()[1]; d['grassPerChunk'] = r.i()
    d['flatMaterials'] = arr(r, lambda r: r.pptr()[1])
    for k in 'fillMiddle fillTop fillMiddleEdge fillTopEdge stairs triplanarMaterial particles skybox'.split(): d[k] = r.pptr()[1]
    d['fogIntensity'] = r.f(); d['fogColor'] = col(r); d['ambienceColor'] = col(r); d['lightColor'] = col(r); d['lightIntensity'] = r.f()
    # stageTimeline is an inline class; skip to the MapParameters reference (a pptr to one of the MapParameters assets).
    start = r.o
    while r.o + 12 <= len(b) and not (struct.unpack_from('<i', b, r.o)[0] == 0 and struct.unpack_from('<q', b, r.o + 4)[0] in PARAMS): r.o += 4
    if r.o + 12 > len(b):
        # A noise-mesh map (Desert): mapParameters is an empty reference; find it as 12 zero bytes followed by a plausible
        # randomMapObjects array (amount, maxAmount, checkRadius, scale range, slope...).
        def plausible(o):
            n = struct.unpack_from('<i', b, o)[0]
            if not 1 <= n <= 40: return False
            amount, mx = struct.unpack_from('<ii', b, o + 4); cr, s0, s1, slope = struct.unpack_from('<ffff', b, o + 12)
            return 0 < amount < 5000 and 0 <= mx < 5000 and 0 <= cr < 100 and 0 < s0 <= s1 < 50 and 0 <= slope <= 180
        r.o = start
        while r.o + 40 <= len(b) and not (b[r.o:r.o + 12] == bytes(12) and plausible(r.o + 12)): r.o += 4
        if r.o + 40 > len(b): print(name, '(could not find its map objects)'); continue
        d['noiseMap'] = True
    d['mapParameters'] = r.pptr()[1]
    d['randomMapObjects'] = arr(r, rmo)
    d['flatTilePrefabs'] = arr(r, lambda r: r.pptr()[1]); d['populateTilesRatio'] = r.f()
    d['mapSpecificTilesPrefabs'] = arr(r, lambda r: r.pptr()[1]); d['numSpecificTiles'] = r.i()
    d['proceduralTerrainData'] = r.pptr()[1]; d['proceduralNoiseData'] = r.pptr()[1]
    d['proceduralMapScale'] = r.f(); d['farClipPlane'] = r.f()
    out[name] = d
json.dump(out, open(sys.argv[2], 'w'), indent=1)
for n, d in out.items():
    print(n, d['mapEdgeFillType'], 'params', d['mapParameters'], 'scale', d['proceduralMapScale'], 'objects', len(d['randomMapObjects']), 'tiles', len(d['flatTilePrefabs']), d['populateTilesRatio'])
