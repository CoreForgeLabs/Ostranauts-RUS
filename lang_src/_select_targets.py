import json, io, os

ROOT = r"F:\DEV2\ostra_i18n\.claude\worktrees\i18n-arch-v2-phase6"
cand = json.load(io.open(os.path.join(ROOT, "lang_src", "grammar_v2_candidates.json"), encoding="utf-8"))
a = cand["class_a"]
b = cand["class_b"]

cond_a = [x for x in a if x["file"] == "conditions.json"]
inter_a = [x for x in a if x["file"] == "interactions.json"]

inter_sorted = sorted(inter_a, key=lambda x: x["name"])
selected_inter = []
seen_names = set()
for x in inter_sorted:
    if x["name"] == "CancelAction":
        selected_inter.append(x)
        seen_names.add(x["name"])
rest = [x for x in inter_sorted if x["name"] not in seen_names]
step = max(1, len(rest) // 49)
for i in range(0, len(rest), step):
    if len(selected_inter) >= 50:
        break
    selected_inter.append(rest[i])

targets = []
for x in cond_a:
    targets.append({"file": x["file"], "name": x["name"], "field": x["field"], "class": "A"})
for x in selected_inter:
    targets.append({"file": x["file"], "name": x["name"], "field": x["field"], "class": "A"})
for x in b:
    key = (x["file"], x["name"], x["field"])
    if not any((t["file"], t["name"], t["field"]) == key for t in targets):
        targets.append({"file": x["file"], "name": x["name"], "field": x["field"], "class": "B"})

print("total targets:", len(targets))
with io.open(os.path.join(ROOT, "lang_src", "grammar_v2_selected.json"), "w", encoding="utf-8") as f:
    json.dump(targets, f, ensure_ascii=False, indent=2)
