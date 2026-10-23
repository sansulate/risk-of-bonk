#!/usr/bin/env python3
"""Preflight the design sheets and generate src/Generated/Sheets.g.cs from them.

  python3 tools/sheets.py preflight   # list unfilled cells, unverified cells and broken references
  python3 tools/sheets.py generate    # preflight, then write the C# (fails on errors, not on unverified cells)
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "sheets")
OUT = os.path.join(ROOT, "src", "Generated", "Sheets.g.cs")

REQUIRED = {
    "characters": ["id", "displayName", "mbNameHints", "mbPortrait", "movesetBody", "primaryColor", "sortPosition",
                   "heightMeters", "yawOffsetDeg", "description", "mbAssetsVerified"],
    "moveset_bodies": ["bodyName", "source", "verified"],
    "menu_style": ["id", "target", "color", "alpha", "mbSpriteHints", "verified"],
    "stage_modes": ["id", "label", "description", "color", "implementedIn", "verified"],
    "hooks": ["id", "system", "kind", "purpose", "verified"],
    "tuning": ["id", "value", "unit", "system", "note", "verified"],
    "upgrades": ["id", "displayName", "mbIcon", "stat", "perPoint", "description", "verified"],
    "rarities": ["id", "label", "color", "weight", "points", "verified"],
    "weapons": ["id", "displayName", "mbIcon", "kind", "damage", "interval", "cooldown", "count", "range", "description", "verified"],
    "bosses": ["id", "displayName", "subtitle", "role", "mbNameHints", "rorBody", "rorMaster", "airborne", "healthBoost", "verified"],
    "animations": ["id", "clipWords", "plays", "verified"],
    "sounds": ["id", "mbSound", "verified"],
}
STATS = {"damage", "cooldown", "crit", "health", "regen", "armor", "speed", "shield", "evasion", "luck"}


def load():
    return {n: json.load(open(os.path.join(SHEETS, n + ".json"), encoding="utf-8"))["rows"] for n in REQUIRED}


def preflight(s):
    errors, unverified = [], []
    for name, cols in REQUIRED.items():
        ids = set()
        for i, row in enumerate(s[name]):
            key = row.get("id") or row.get("bodyName") or i
            if key in ids:
                errors.append(f"{name}[{key}]: duplicate id")
            ids.add(key)
            for c in cols:
                v = row.get(c)
                if v is None or v == "" or (isinstance(v, list) and c != "mbSpriteHints" and not v):
                    errors.append(f"{name}[{key}].{c}: unfilled")
            for c in ("verified", "mbAssetsVerified", "startWeaponVerified"):
                if c in row and row[c] is not True:
                    unverified.append(f"{name}[{key}].{c}: not verified yet")
            if name == "tuning" and not isinstance(row.get("value"), (int, float)):
                errors.append(f"tuning[{key}].value: not a number")
            for c in ("primaryColor", "color"):
                v = row.get(c)
                if isinstance(v, str) and v.startswith("#") and not re.fullmatch(r"#[0-9A-Fa-f]{6}", v):
                    errors.append(f"{name}[{key}].{c}: bad color {v}")
    bodies = {r["bodyName"] for r in s["moveset_bodies"]}
    for r in s["characters"]:
        if r["movesetBody"] not in bodies:
            errors.append(f"characters[{r['id']}].movesetBody -> moveset_bodies: '{r['movesetBody']}' does not resolve")
    style_ids = {r["id"] for r in s["menu_style"]}
    for r in s["stage_modes"]:
        if r["color"] not in style_ids:
            errors.append(f"stage_modes[{r['id']}].color -> menu_style: '{r['color']}' does not resolve")
    weapon_ids = {r["id"] for r in s["weapons"]}
    for r in s["weapons"]:
        if r["kind"] not in ("bullet", "projectile", "melee", "aura", "lightning"):
            errors.append(f"weapons[{r['id']}].kind: '{r['kind']}' unknown")
        if r["kind"] == "projectile" and not r.get("prefab"):
            errors.append(f"weapons[{r['id']}].prefab: a projectile weapon needs a projectile address")
    for r in s["characters"]:
        if r.get("startWeapon") not in weapon_ids:
            errors.append(f"characters[{r['id']}].startWeapon -> weapons: '{r.get('startWeapon')}' does not resolve")
    for r in s["bosses"]:
        if r["role"] not in ("teleporter", "final"):
            errors.append(f"bosses[{r['id']}].role: '{r['role']}' is not teleporter or final")
        if r["role"] == "final" and not r.get("replaces"):
            errors.append(f"bosses[{r['id']}].replaces: a final boss needs the bodies it replaces")
    for r in s["animations"]:
        if r["id"] not in ("idle", "run", "jump", "fall", "slide"):
            errors.append(f"animations[{r['id']}]: not a role MegabonkAnimator knows (idle, run, jump, fall, slide)")
    for r in s["upgrades"]:
        if r["stat"] not in STATS:
            errors.append(f"upgrades[{r['id']}].stat: '{r['stat']}' is not one of {sorted(STATS)}")
    for b in bodies:
        if not any(r["movesetBody"] == b for r in s["characters"]):
            errors.append(f"moveset_bodies[{b}]: not used by any character")
    return errors, unverified


def cs(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return f"{float(v)}f"
    if isinstance(v, list):
        return "new[] { " + ", ".join(cs(x) for x in v) + " }" if v else "new string[0]"
    return json.dumps(v, ensure_ascii=False)


def generate(s):
    L = ["// <auto-generated> by tools/sheets.py from sheets/*.json. Edit the sheets, not this file. </auto-generated>",
         "namespace RiskOfBonk.Generated", "{",
         "    public static class Sheets", "    {"]
    L.append("        public static readonly CharacterRow[] Characters =\n        {")
    for r in s["characters"]:
        L.append(f"            new CharacterRow({cs(r['id'])}, {cs(r['displayName'])}, {cs(r['mbNameHints'])}, {cs(r['mbPortrait'])}, {cs(r['movesetBody'])}, "
                 f"{cs(r['primaryColor'])}, {cs(r['sortPosition'])}, {cs(r['heightMeters'])}, {cs(r['yawOffsetDeg'])}, {cs(r['description'])}, {cs(r.get('mbTexture', ''))}, {cs(r.get('startWeapon', ''))}, "
                 f"{cs(r.get('mbPassive', ''))}, {cs(float(r.get('mbMoveSpeed', 0)))}, {cs(float(r.get('mbJumpHeight', 0)))}, {int(r.get('mbExtraJumps', 0))}),")
    L.append("        };")
    L.append("        public static readonly StyleRow[] MenuStyle =\n        {")
    for r in s["menu_style"]:
        L.append(f"            new StyleRow({cs(r['id'])}, {cs(r['color'])}, {cs(r['alpha'])}, {cs(r['mbSpriteHints'])}),")
    L.append("        };")
    L.append("        public static readonly ModeRow[] StageModes =\n        {")
    for r in s["stage_modes"]:
        L.append(f"            new ModeRow({cs(r['id'])}, {cs(r['label'])}, {cs(r['description'])}, {cs(r['color'])}),")
    L.append("        };")
    L.append("        public static readonly UpgradeRow[] Upgrades =\n        {")
    for r in s["upgrades"]:
        L.append(f"            new UpgradeRow({cs(r['id'])}, {cs(r['displayName'])}, {cs(r['mbIcon'])}, {cs(r['stat'])}, {cs(r['perPoint'])}, {cs(r['description'])}),")
    L.append("        };")
    L.append("        public static readonly RarityRow[] Rarities =\n        {")
    for r in s["rarities"]:
        L.append(f"            new RarityRow({cs(r['id'])}, {cs(r['label'])}, {cs(r['color'])}, {cs(r['weight'])}, {int(r['points'])}),")
    L.append("        };")
    L.append("        public static readonly WeaponRow[] Weapons =\n        {")
    for r in s["weapons"]:
        L.append(f"            new WeaponRow({cs(r['id'])}, {cs(r['displayName'])}, {cs(r['mbIcon'])}, {cs(r['kind'])}, {cs(r['damage'])}, {cs(r['interval'])}, "
                 f"{cs(r['cooldown'])}, {int(r['count'])}, {cs(r.get('spread', 0))}, {cs(r['range'])}, {cs(r.get('prefab', ''))}, {cs(r.get('effect', ''))}, "
                 f"{cs(r.get('damageType', 'none'))}, {cs(r.get('pierce', False))}, {cs(r.get('sound', ''))}, {cs(r['description'])}, "
                 f"{cs(r.get('mbSound', ''))}, {cs(r.get('mbProjectile', ''))}, {cs(r.get('projScale', 1))}, {cs(r.get('projSpin', 0))}, "
                 f"{cs(float(r.get('mbDamage', 0)))}, {int(r.get('mbProjectiles', 1))}, {cs(float(r.get('mbBurstTime', 0)))}, {cs(float(r.get('mbMinBurstInterval', 0)))}, "
                 f"{cs(float(r.get('mbEndCooldown', r['interval'])))}, {cs(float(r.get('mbRange', 0)))}, {cs(float(r.get('mbCritChance', 0)))}, {cs(float(r.get('mbProc', 1)))}, "
                 f"{cs(r.get('mbOnlyClose', False))}),")
    L.append("        };")
    L.append("        public static readonly BossRow[] Bosses =\n        {")
    for r in s["bosses"]:
        L.append(f"            new BossRow({cs(r['id'])}, {cs(r['displayName'])}, {cs(r['subtitle'])}, {cs(r['mbNameHints'])}, {cs(r.get('mbTexture', ''))}, "
                 f"{cs(r['rorBody'])}, {cs(r['rorMaster'])}, {cs(r['airborne'])}, {int(r['healthBoost'])}, {cs(r['role'])}, {cs(r.get('replaces', []))}, {cs(r.get('retarget', True))}),")
    L.append("        };")
    L.append("        public static readonly SoundRow[] Sounds =\n        {")
    for r in s["sounds"]:
        L.append(f"            new SoundRow({cs(r['id'])}, {cs(r['mbSound'])}),")
    L.append("        };")
    L.append("        public static readonly AnimRow[] Animations =\n        {")
    for r in s["animations"]:
        L.append(f"            new AnimRow({cs(r['id'])}, {cs(r['clipWords'])}),")
    L.append("        };")
    L.append("    }")
    L.append("    /// <summary>sheets/tuning.json, one field per row.</summary>")
    L.append("    public static class Tune")
    L.append("    {")
    for r in s["tuning"]:
        name = "".join(w.capitalize() for w in r["id"].split("_"))
        L.append(f"        /// <summary>{r['note']} ({r['unit']})</summary>")
        L.append(f"        public static float {name} = {cs(r['value'])};")
    L += ["    }", "}", ""]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, "w", encoding="utf-8", newline="\n").write("\n".join(L))


if __name__ == "__main__":
    s = load()
    errors, unverified = preflight(s)
    for e in errors:
        print("ERROR  " + e)
    for u in unverified:
        print("CHECK  " + u)
    print(f"preflight: {len(errors)} errors, {len(unverified)} unverified cells")
    if errors:
        sys.exit(1)
    if len(sys.argv) > 1 and sys.argv[1] == "generate":
        generate(s)
        print("wrote " + os.path.relpath(OUT, ROOT))
