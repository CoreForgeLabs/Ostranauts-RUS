#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
filter_gui_unknown.py — чистит очередной дамп gui_unknown.txt от плагина:
убирает пути, dev-debug строки, сгенерированные имена NPC/станций, CJK-мусор,
уже переведённые строки (сверка с текущим langs/lang_ru/gui.json).
Пишет lang_src/gui_wave_need.en.json — вход для translate_gui_extra.py-подобного
параллельного переводчика.

Запуск: python filter_gui_unknown.py <путь_к_дампу> [langs/lang_ru/gui.json]
"""
import json, re, sys, os

ROOT = r"F:\DEV2\ostra_i18n"
GAME_DATA = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"

DUMP = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    r"F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n", "gui_unknown.txt")
GUI_JSON = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "langs", "lang_ru", "gui.json")
OUT = os.path.join(ROOT, "lang_src", "gui_wave_need.en.json")


def load_names(path):
    d = json.load(open(path, encoding="utf-8-sig"))
    out = set()
    def walk(x):
        if isinstance(x, str):
            out.add(x.strip().lower())
        elif isinstance(x, list):
            for i in x: walk(i)
        elif isinstance(x, dict):
            for v in x.values(): walk(v)
    walk(d)
    return out

first = load_names(os.path.join(GAME_DATA, "names_first", "names_first.json"))
last = load_names(os.path.join(GAME_DATA, "names_last", "names_last.json"))

DEV_KEYWORDS = ["Init ", "Parse ", "Prune NPCs", "Teleport selected", "Replace selected",
    "Respawn Ship", "Show Pax", "Emptying Scene", "Clear Scene", "Spawn Player Character",
    "Import Ship From Save", "Update Game Objects", "Beat Logs:", "Plot Logs:", "TESTING>",
    "OPTIONS>", "SHIP EDITOR>", "WE MAY NEED A STYLE GUIDE", "settings suchlike",
    "Walking to the airlock", "K-Leg_Boneyard", "Release Build:", "Directory label",
    "PDF Versions", "no tiles selected", "select tiles then press",
    "Power Switch:", "XPDR/IFF:", "They See Us As:", "New Biological",
    "RNG:", "Queue XA", "Set Camera", "Init AI Ship Manager", "Spawning "]

def is_person_name(s):
    s = s.rstrip("\u200b")  # trailing zero-width space (common in name labels)
    words = s.split()
    if not (2 <= len(words) <= 3): return False
    if not all(re.match(r"^[A-Z][a-zA-Z'\u2019-]*$", w) for w in words): return False
    lw = [w.lower() for w in words]
    return lw[0] in first and lw[-1] in last

def is_dynamic_composed(s):
    if " of the " in s: return True                          # "<Name> of the <Location>"
    if re.match(r'^[\d,]+\s*MB available$', s): return True   # live memory readout
    if s.startswith("Early Access Build:"): return True       # version-specific
    return False

# Exact whitelist, not a regex — a "2-6 uppercase letters" regex also matches real UI words
# (HOME, GOALS, ROSTER, TASKS, VIZOR, ORDERS, GIGS, ...) and silently dropped them from every
# translation wave so far. Base codes + "_SUFFIX"/"|SUFFIX" variants extracted from actual
# "Spawning Station: XXX" debug lines seen in the game.
STATION_BASE_CODES = {
    "BCER", "BCRS", "BWVN", "COHO", "EJDR", "HQCH", "JATL", "JFTS", "JPTN", "MHNG",
    "MLAB", "MSUZ", "MTAM", "MTRS", "MVOL", "OKLG", "SVIR", "VCBR", "VENC", "VLA00",
    "VLA01", "VLA02", "VLA03", "VLA04", "VLA05", "VNCA", "VORB",
}

def is_station_code(s):
    base = re.split(r'[_|]', s, 1)[0]
    return base in STATION_BASE_CODES

def keep(s):
    s = s.strip()
    if not s or len(s) <= 1: return False
    if re.search(r'[\u4e00-\u9fff]', s): return False               # CJK
    if re.search(r'[A-Za-z]:[\\/]|https?://', s): return False      # paths
    if s.count("/") + s.count("\\") >= 1: return False              # any leftover path-ish
    if is_person_name(s): return False
    if is_station_code(s): return False
    if is_dynamic_composed(s): return False
    if any(k in s for k in DEV_KEYWORDS): return False
    letters = sum(1 for c in s if c.isalpha())
    digits = sum(1 for c in s if c.isdigit())
    if letters == 0: return False
    if digits * 2 > letters: return False
    if len(s) > 150: return False
    return True

lines = open(DUMP, encoding="utf-8", errors="ignore").read().splitlines()
uniq = sorted(set(l for l in lines if keep(l)))

# Collapse typewriter-effect animation frames: the game reveals some text (news tickers,
# PDA articles) one character per frame, and each growing prefix gets logged as a distinct
# "unknown" string. Keep only the longest string in each prefix chain (closest to final text).
uniq_sorted_by_len = sorted(uniq, key=len, reverse=True)
kept = []
for s in uniq_sorted_by_len:
    if any(longer.startswith(s) for longer in kept):
        continue
    kept.append(s)
uniq = sorted(kept)

existing = json.loads(open(GUI_JSON, encoding="utf-8").read()) if os.path.exists(GUI_JSON) else {}
new = [l for l in uniq if l not in existing]

print("dump lines: %d | after filter: %d | already translated: %d | new to translate: %d" %
      (len(lines), len(uniq), len(uniq) - len(new), len(new)))

json.dump({k: "" for k in new}, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("wrote", OUT)
