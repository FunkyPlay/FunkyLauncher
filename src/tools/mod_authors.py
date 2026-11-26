# Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
# 0.1.24 (Legal 26.11): writes each mod's author into addons/<modlist>-about.json ("author"), taken from the public
# Nexus Mods API (GraphQL v2, no key). The launcher shows it in the "Mods of the modlist" window.
# Run from the repository root:  python -I src/tools/mod_authors.py neon-nights starfield fallout4
import json
import sys
import time
import urllib.request

API = "https://api.nexusmods.com/v2/graphql"
Q = "query($ids:[CompositeDomainWithIdInput!]!){legacyModsByDomain(ids:$ids){nodes{modId author uploader{name} game{domainName}}}}"


def fetch(pairs):
    body = json.dumps({"query": Q, "variables": {"ids": [{"gameDomain": g, "modId": m} for g, m in pairs]}}).encode()
    req = urllib.request.Request(API, body, {"Content-Type": "application/json", "User-Agent": "FunkyLauncher-tools", "Application-Name": "Funky Launcher", "Application-Version": "tools-1"})
    with urllib.request.urlopen(req, timeout=60) as r:
        data = json.load(r)
    if data.get("errors"):
        raise RuntimeError(data["errors"])
    out = {}
    for n in data["data"]["legacyModsByDomain"]["nodes"]:
        name = (n.get("author") or "").strip() or ((n.get("uploader") or {}).get("name") or "").strip()
        if name:
            out[(n["game"]["domainName"], n["modId"])] = name
    return out


def run(modlist):
    main = json.load(open(f"addons/{modlist}-main.json", encoding="utf-8"))
    about_path = f"addons/{modlist}-about.json"
    raw = open(about_path, "rb").read().decode("utf-8")
    nl = "\r\n" if "\r\n" in raw else "\n"
    tail = "\n" if raw.replace("\r\n", "\n").endswith("\n") else ""
    about = json.loads(raw)
    mods = about.setdefault("mods", {})
    want = {}
    for x in main["mods"]:
        folder = x.get("modFolder") or ""
        nx = x.get("nexus") or {}
        if not folder or not nx.get("modId") or not nx.get("game"):
            continue
        mid = (mods.get(folder) or {}).get("nexusModId") or nx["modId"]
        want.setdefault(folder, (nx["game"], int(mid)))
    pairs = sorted(set(want.values()))
    found = {}
    # the API returns at most 20 mods per question (more ids → the rest silently missing)
    for i in range(0, len(pairs), 20):
        found.update(fetch(pairs[i:i + 20]))
        time.sleep(0.5)
    added = 0
    missing = []
    for folder, key in want.items():
        name = found.get(key)
        if not name:
            missing.append(f"{folder} ({key[0]}/{key[1]})")
            continue
        entry = mods.setdefault(folder, {})
        if entry.get("author") != name:
            entry["author"] = name
            added += 1
    # keep the file's own style (indent 1, its line ends), so the diff shows only the new lines
    text = (json.dumps(about, ensure_ascii=False, indent=1) + tail).replace("\n", nl)
    with open(about_path, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print(f"{modlist}: {len(want)} mods, {added} authors written, {len(missing)} not found")
    for m in missing:
        print("  not found:", m)


if __name__ == "__main__":
    for m in sys.argv[1:] or ["neon-nights", "starfield", "fallout4"]:
        run(m)
