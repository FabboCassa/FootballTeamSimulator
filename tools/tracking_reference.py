"""Tracking reference v2 (docs/specs/free-ball-real-movement.md R1) from open tracking data.

Downloads DFL/IDSSE open data (7 matches), SkillCorner open data (9 matches) and Metrica Sports
sample games 1-3 into tools/.tracking-data/ (git-ignored), derives the R1 tracking metrics per
match, groups the matches by source, league and tier pairing, and prints the markdown tables of
docs/research/football-reference.md section 10. Only those aggregates are committed.

Run from the repo root:  python -I tools/tracking_reference.py [--check]
--check exits 1 unless every printed table line is in the doc verbatim.
Needs numpy. Definitions are in the doc, section 10.3.
"""
import hashlib
import importlib.util
import json
import math
import os
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET
from datetime import datetime

import numpy as np

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT_DATA = os.path.join(TOOLS, ".tracking-data")
DOC = os.path.join(TOOLS, "..", "docs", "research", "football-reference.md")


def _load_movement():
    # -I keeps the script directory off sys.path, so the sibling script is loaded by its path
    # (without leaving a __pycache__ in tools/).
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location("movement_reference", os.path.join(TOOLS, "movement_reference.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


MR = _load_movement()

# Every downloaded file is pinned by name and SHA-256 (computed from the files this script was
# first run on; the DFL ones had matched figshare's md5). Nothing remote decides a file name or
# a checksum, and a file is re-verified on every run before it is parsed.
DFL_URL = "https://ndownloader.figshare.com/files/%d"   # figshare article 28196177, v1
DFL_DIR = os.path.join(ROOT_DATA, "dfl")
_D = "_DFL-COM-00000%d_DFL-MAT-%s.xml"
DFL_FILES = {   # name: (figshare file id, sha256)
    "DFL_02_01_matchinformation" + _D % (1, "J03WMX"): (51643475, "a884728647a564cf8b20e5223a1d04a5da6f6725e1e506c8651505327c197ba9"),
    "DFL_02_01_matchinformation" + _D % (1, "J03WN1"): (51643472, "5490286436c86e6f80a538d2b1b379d0efd016e0defcaf594bc59d9ef5079f4c"),
    "DFL_02_01_matchinformation" + _D % (2, "J03WOH"): (51643478, "f23b3ee13b988b1ff9a0bce438c416dac47e71355bbc7ee1dab21aaf3e33c18c"),
    "DFL_02_01_matchinformation" + _D % (2, "J03WOY"): (51643481, "4c497fac683eee8292901c18ca02c0be1d9609d0b9d4bd8aa092c61f9e0b19e9"),
    "DFL_02_01_matchinformation" + _D % (2, "J03WPY"): (51643487, "566dd5e4c86e124ab90fcdba51100881f0436e22afa71832945a451a2d95a420"),
    "DFL_02_01_matchinformation" + _D % (2, "J03WQQ"): (51643484, "e976aa0c5dba862cfa898ea57ec226efce9d1bc04b23b8d775908db86aa6fa41"),
    "DFL_02_01_matchinformation" + _D % (2, "J03WR9"): (51643490, "429b584e89df3e3e8b81f4bd0d004b38bd0b4298bb390b8fb43efccb60963e01"),
    "DFL_03_02_events_raw" + _D % (1, "J03WMX"): (51643493, "6f9741ea62e9dee35b604942e1dad2ac0a4dc3fce14ef2f3858aa12b74dafe00"),
    "DFL_03_02_events_raw" + _D % (1, "J03WN1"): (51643496, "d7fa1d4445e3215eb79ec62fabefa4779b4974e496195c2fdc25624c375a9b9b"),
    "DFL_03_02_events_raw" + _D % (2, "J03WOH"): (51643499, "abc27e3268e6126fb3e6e90bff5cca8d4586a5e0cefe9bd59bf32871e89d8544"),
    "DFL_03_02_events_raw" + _D % (2, "J03WOY"): (51643502, "dbb014df1b80d5c99b7fb806a278432c445a5fdd99f59d379d601f20eaf7e142"),
    "DFL_03_02_events_raw" + _D % (2, "J03WPY"): (51643505, "e440a3adf9a38d3e4ce4289343a59a3b5f76018a78e97c765114f7817591fb3c"),
    "DFL_03_02_events_raw" + _D % (2, "J03WQQ"): (51643508, "b79ccc1ee3de5ba662546a9a21304784642d00a791ccc1175550eaa463ea0074"),
    "DFL_03_02_events_raw" + _D % (2, "J03WR9"): (51643511, "31735a2d7b9008c44cae9b6ac43ab38ddeaa435d0416265ec0ebfebf53b3c982"),
    "DFL_04_03_positions_raw_observed" + _D % (1, "J03WMX"): (51643514, "d3dd92e509421a6466acb0a083c009035f1751bc5fd9ef42ffa8f20d01cf7d49"),
    "DFL_04_03_positions_raw_observed" + _D % (1, "J03WN1"): (51643517, "56ef948961a18e150dba0b7b16869d0008ff66f3050a0c355d31fa5b8ec2fcec"),
    "DFL_04_03_positions_raw_observed" + _D % (2, "J03WOH"): (51643520, "ec4dcf30200aad694dea263d5289485fcd0ca42badddaeb7c81f5cd5a4229d4f"),
    "DFL_04_03_positions_raw_observed" + _D % (2, "J03WOY"): (51643523, "a228ef0e676d009cefff1580fcb721b53f598454eb120ec347e0a628a3a4646b"),
    "DFL_04_03_positions_raw_observed" + _D % (2, "J03WPY"): (51643526, "845202c29ad93d696653436da426a7cdaefb848298e07b7343de6529581671e1"),
    "DFL_04_03_positions_raw_observed" + _D % (2, "J03WQQ"): (51643529, "cbefcb0bd7f39f3b94ee69a7bc97aac0f936425062c1cc1b144733df36804d6f"),
    "DFL_04_03_positions_raw_observed" + _D % (2, "J03WR9"): (51643532, "70b63069189019066fb7e3d9db891122edfe787f3a43ba522ebe45ed89ea0269"),
}
DFL_MATCHES = ("J03WMX", "J03WN1", "J03WOH", "J03WOY", "J03WPY", "J03WQQ", "J03WR9")
SC_COMMIT = "1ed50b775f93075c2a6d00d0d795f13b7b89709e"
SC_BASE = "https://raw.githubusercontent.com/SkillCorner/opendata/" + SC_COMMIT + "/data/"
SC_DIR = os.path.join(ROOT_DATA, "skillcorner-" + SC_COMMIT[:7])
SC_FILES = {   # (match id, file): sha256
    (2068, "match_data.json"): "e4949e16bd9df670addd9ee910560157f53e67c120ac9a52f1b201fe255919e3",
    (2068, "structured_data.json"): "33ab72ab53373317ae70866210ea69119cb4f9a5c635f97af64b5ad10c1226c4",
    (2269, "match_data.json"): "e122f866df72fbf09793932bad64e1680d46ffe36c3d06c849c793622fa930e1",
    (2269, "structured_data.json"): "b4b30ffa8a66d0da603d6b0cc9869ea63cc52e16b4c113f2261f9d62c767905c",
    (2417, "match_data.json"): "ee026be81bf15ca756276a6fa446a72bcd43c155dd0aff5ddd0221f9d8f1d639",
    (2417, "structured_data.json"): "051316dee5f1c08053375eeecd3389b829297d63f20fe88e7ac153a409102c5e",
    (2440, "match_data.json"): "f5734cbb481ca3417e4be88ced5a4869d35c00c2a562fdcc8a5060433e5db70b",
    (2440, "structured_data.json"): "ef39601e54489775d6920c5bb0a9d51c1e7228be5a40a71e0091d2bb3e2c70be",
    (2841, "match_data.json"): "9fb0185520f721dcfc9804f2a06915dad0989d11cf1380be0e65079c15790707",
    (2841, "structured_data.json"): "2ad88ced61d6c2b8c3689cef3328de6b52aaa15209dcb6284a64caaf37fe16ef",
    (3442, "match_data.json"): "5d20851a60fe0284ad01b69fdbf6bf2295684ed7839f300b223f2a10f7a918f9",
    (3442, "structured_data.json"): "badc64a5dd43aa7b2a58ae109c2378d95f81f74be55e0aafa4c089e8d12a0102",
    (3518, "match_data.json"): "3075cb8345dce9e242f90072c388e2960f3aa9f40cbf7497da75624c4da4f09f",
    (3518, "structured_data.json"): "7e3e6362204fe99a4311c06bd553d5f9d7fbad54b28eb6df73384670dcc45dec",
    (3749, "match_data.json"): "a5d6e66978e20614b435e1e62dfcdd3ed73b21bea3017195455bea9ca7c51663",
    (3749, "structured_data.json"): "02f21708d463f68dde25310d0ac4c83afffd6970c3511b9746414c14334fcecf",
    (4039, "match_data.json"): "87d52a56f94b04060553f8bffb70759a29f02cb2efb52bbf09862504ec7ac96e",
    (4039, "structured_data.json"): "f42a98795e223925629a9db4fa1ddb616cadc046a19b151e4e6a71378a297ac2",
}
SC_MATCHES = (2068, 2269, 2417, 2440, 2841, 3442, 3518, 3749, 4039)
METRICA_FILES = {   # name under tools/movement_reference.py's pinned commit: sha256
    "Sample_Game_1_RawEventsData.csv": "097dda6345e17390dca095331458fd00e47960c480c76f4356fd4edf2575ef05",
    "Sample_Game_1_RawTrackingData_Home_Team.csv": "0a3d0eff7785950379c587d3597b86a2d91c2492281b4652e1abb97c99b6b364",
    "Sample_Game_1_RawTrackingData_Away_Team.csv": "0a3df68a92af0290ede472e62de3b6489ddee29223da09bdf0cf76c8e0ab0618",
    "Sample_Game_2_RawEventsData.csv": "edf31a18599265b77a8baf150f2ce6d89456fb0324b62ea7a391229657a619ba",
    "Sample_Game_2_RawTrackingData_Home_Team.csv": "3f9990ac204b07133e4202441d6fcbb386897c7c7f81e1bc3ce4d35bc8644694",
    "Sample_Game_2_RawTrackingData_Away_Team.csv": "aa639f40762eade46c389020fe4458266b5ba14027b1e96e22e75db62803718b",
    "Sample_Game_3_events.json": "28a53bfe9eaf32535362335553d517cdce0eabb588af6166009081753751f4e0",
    "Sample_Game_3_metadata.xml": "009c7dcfcb66192014917ca1559c158f612353e390eafb85f8214b06a1c7dec1",
    "Sample_Game_3_tracking.txt": "570e6ce82c2d93bc92723af331d93c31110bec1d6dc0be07c6d44ca3f981d658",
}
SC_BALL = 55
SC_MIN_PLAYERS = 10      # broadcast frames with fewer tracked players are close-ups: not measured

REACH = 1.5              # m: a player within it can play the ball; nobody within it = ball free
TURN_STEP = 0.2          # s
TURN_SPEED, TURN_CLEAR, TURN_DEG = 5.0, 2.0, 20.0
OFFSIDE_MARGIN = 0.3     # m beyond the second-last opponent
CONTROL_MIN = 0.2        # s a holder must keep the ball within reach for a tracking-derived pass
FLIGHT_MAX = 6.0         # s
SPACE_M = 3.0
LOOSE_MIN, DUEL_LOOK, TOUCH_MAX = 1.0, 0.5, 0.4   # s

# Final league tables (position) of the teams in the data. 2022/23: computed from the results
# matrices of en.wikipedia "2022-23 Bundesliga" and "2022-23 2. Bundesliga"; 2019/20: the
# en.wikipedia season articles (all SkillCorner teams finished 1st or 2nd).
TIERS = {
    ("Bundesliga", "2022/23"): {"FC Bayern München": 1, "Bayer 04 Leverkusen": 6, "1. FC Köln": 11,
                                "VfL Bochum 1848": 14},
    ("2. Bundesliga", "2022/23"): {"Fortuna Düsseldorf": 4, "FC St. Pauli": 5, "1. FC Kaiserslautern": 9,
                                   "F.C. Hansa Rostock": 13, "1. FC Nürnberg": 14, "SSV Jahn Regensburg": 17},
    ("Premier League", "2019/20"): {"Liverpool": 1, "Manchester City": 2},
    ("La Liga", "2019/20"): {"Real Madrid": 1, "FC Barcelona": 2},
    ("Serie A", "2019/20"): {"Juventus": 1, "Inter": 2},
    ("Bundesliga", "2019/20"): {"Bayern Munchen": 1, "Dortmund": 2},
    ("Ligue 1", "2019/20"): {"Paris": 1, "Marseille": 2},
}
TIER_ORDER = ("top", "middle", "bottom", "unknown")


def tier(position):
    if position is None:
        return "unknown"
    return "top" if position <= 6 else "bottom" if position >= 13 else "middle"


# ---------- download

def fetch(url, folder, name, sha256):
    """Make folder/name the pinned file: reuse it only if its hash matches, else download to .part,
    verify, then move into place. A mismatch after download stops the run."""
    if os.path.basename(name) != name or name in ("", ".", ".."):
        raise RuntimeError("refusing file name: %r" % name)
    path = os.path.join(folder, name)
    if os.path.exists(path) and _sha256(path) == sha256:
        return
    print("downloading", name, file=sys.stderr)
    urllib.request.urlretrieve(url, path + ".part")
    got = _sha256(path + ".part")
    if got != sha256:
        os.remove(path + ".part")
        raise RuntimeError("sha256 mismatch for %s: %s" % (name, got))
    os.replace(path + ".part", path)


def _sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def download():
    """Download or re-verify every pinned file of the three sources."""
    for folder in (DFL_DIR, SC_DIR, MR.DATA):
        os.makedirs(folder, exist_ok=True)
    for name, (file_id, sha256) in DFL_FILES.items():
        fetch(DFL_URL % file_id, DFL_DIR, name, sha256)
    for (match, name), sha256 in SC_FILES.items():
        fetch(SC_BASE + "matches/%d/%s" % (match, name), SC_DIR, "%d_%s" % (match, name), sha256)
    for game, names in MR.FILES.items():
        for name in names:
            fetch(MR.BASE + "Sample_Game_%d/%s" % (game, name), MR.DATA, name, METRICA_FILES[name])


# ---------- DFL: positions XML -> cached npz (one timeline, both halves)

FRAME = re.compile(r'<Frame N="(\d+)" T="[^"]*" X="([-\d.]+)" Y="([-\d.]+)"')
BALL_EXTRA = re.compile(r'Z="([-\d.]+)".*BallPossession="(\d)" BallStatus="(\d)"')
FIRST_T = re.compile(r'<Frame N="\d+" T="([^"]+)"')


def dfl_positions(match_id, pos_path):
    cache = os.path.join(DFL_DIR, "cache-%s-%s.npz" % (match_id, DFL_FILES[os.path.basename(pos_path)][1][:16]))
    if os.path.exists(cache):
        with np.load(cache) as z:
            return {k: z[k] for k in z.files}
    sets, cur, rows = {}, None, None
    with open(pos_path, encoding="utf8") as f:
        for line in f:
            if line.startswith("<Frame "):
                if cur is None:
                    continue
                m = FRAME.match(line)
                if cur[1] == "BALL":
                    e = BALL_EXTRA.search(line)
                    if not rows:
                        sets[cur + ("t0",)] = FIRST_T.match(line).group(1)
                    rows.append((int(m.group(1)), float(m.group(2)), float(m.group(3)),
                                 float(e.group(1)), int(e.group(2)), int(e.group(3))))
                else:
                    rows.append((int(m.group(1)), float(m.group(2)), float(m.group(3))))
            elif line.startswith("<FrameSet "):
                attrs = dict(re.findall(r'(\w+)="([^"]*)"', line))
                cur = None if attrs["TeamId"] == "referee" else (attrs["GameSection"], attrs["TeamId"], attrs["PersonId"])
                rows = []
                if cur is not None:
                    sets[cur] = rows
    people = sorted({k[2] for k in sets if len(k) == 3 and k[1] != "BALL"})
    out_xy, out_ball, period, t0 = [], [], [], []
    for p, half in ((1, "firstHalf"), (2, "secondHalf")):
        ball_key = next(k for k in sets if len(k) == 3 and k[0] == half and k[1] == "BALL")
        ball = np.array(sets[ball_key])
        n0, n = int(ball[0, 0]), int(ball[-1, 0]) - int(ball[0, 0]) + 1
        b = np.full((n, 5), np.nan)
        b[ball[:, 0].astype(int) - n0] = ball[:, 1:]
        xy = np.full((n, len(people), 2), np.nan, np.float32)
        for k, rows in sets.items():
            if len(k) == 3 and k[0] == half and k[1] != "BALL" and rows:
                a = np.array(rows)
                xy[a[:, 0].astype(int) - n0, people.index(k[2])] = a[:, 1:3]
        out_xy.append(xy)
        out_ball.append(b)
        period.append(np.full(n, p))
        t0.append(sets[ball_key + ("t0",)])
    team_of = {k[2]: k[1] for k in sets if len(k) == 3 and k[1] != "BALL"}
    data = dict(xy=np.concatenate(out_xy), ball=np.concatenate(out_ball), period=np.concatenate(period),
                people=np.array(people), people_team=np.array([team_of[p] for p in people]),
                t0=np.array(t0), half_len=np.array([len(x) for x in out_xy]))
    np.savez_compressed(cache, **data)
    return data


def dfl_match(match_id):
    path = lambda kind: os.path.join(DFL_DIR, next(n for n in DFL_FILES if kind in n and match_id in n))
    info = ET.parse(path("matchinformation")).getroot()
    general = info.find(".//General")
    league = general.get("CompetitionName")
    season = general.get("Season").replace("/20", "/")
    role, name, players = {}, {}, {}
    for t in info.iter("Team"):
        role[t.get("TeamId")] = "away" if t.get("Role") == "guest" else "home"
        name[t.get("TeamId")] = t.get("TeamName")
        for p in t.iter("Player"):
            players[p.get("PersonId")] = p.get("PlayingPosition") or ""
    d = dfl_positions(match_id, path("positions_raw"))
    people = list(d["people"])
    position = [players.get(p, "") for p in people]
    team = np.array([role[t] for t in d["people_team"]])
    ball = d["ball"]
    n = len(ball)
    poss = np.where(ball[:, 3] == 1, 1, np.where(ball[:, 3] == 2, 2, 0)).astype(np.int8)
    play = ball[:, 4] == 1
    keeper = np.broadcast_to(np.array([p == "TW" for p in position]), (n, len(people)))
    forward = np.array([p in ("STZ", "STL", "STR") for p in position])
    # Events: completed passes and crosses (restarts included) with their recipient.
    starts = [datetime.fromisoformat(t) for t in d["t0"]]
    offsets = np.concatenate(([0], np.cumsum(d["half_len"])))
    passes = []
    for ev in ET.parse(path("events_raw")).getroot().iter("Event"):
        play_el = ev.find(".//Play")
        if play_el is None or ev.find(".//ShotAtGoal") is not None:
            continue
        if play_el.find("Pass") is None and play_el.find("Cross") is None:
            continue
        if play_el.get("Evaluation") != "successfullyCompleted" or not play_el.get("Recipient"):
            continue
        if play_el.get("Player") not in people or play_el.get("Recipient") not in people:
            continue
        t = datetime.fromisoformat(ev.get("EventTime"))
        for h in (0, 1):
            k = int(round((t - starts[h]).total_seconds() * 25))
            if 0 <= k < d["half_len"][h]:
                passes.append((int(offsets[h] + k), people.index(play_el.get("Player")),
                               people.index(play_el.get("Recipient"))))
    home = next(name[t] for t in role if role[t] == "home")
    away = next(name[t] for t in role if role[t] == "away")
    table = TIERS[(league, season)]
    return dict(source="DFL", league=league, season=season, home=home, away=away,
                tiers=(tier(table[home]), tier(table[away])), fps=25,
                x=d["xy"][:, :, 0], y=d["xy"][:, :, 1], team=team, keeper=keeper, forward=forward,
                period=d["period"], bx=ball[:, 0], by=ball[:, 1], bz=ball[:, 2],
                play=play, valid=play & np.isfinite(ball[:, 0]), poss=poss, passes=passes)


# ---------- SkillCorner

def sc_match(match_id):
    with open(os.path.join(SC_DIR, "%d_match_data.json" % match_id), encoding="utf8") as f:
        info = json.load(f)
    with open(os.path.join(SC_DIR, "%d_structured_data.json" % match_id), encoding="utf8") as f:
        frames = [fr for fr in json.load(f) if fr["period"]]
    team_id = {info["home_team"]["id"]: "home", info["away_team"]["id"]: "away"}
    people = sorted(p["trackable_object"] for p in info["players"])
    col = {p: j for j, p in enumerate(people)}
    meta = {p["trackable_object"]: p for p in info["players"]}
    role = [meta[p]["player_role"]["name"] for p in people]
    n, players = len(frames), len(people)
    referees = {r["trackable_object"] for r in info["referees"]}
    # Unidentified player tracks count for proximity, in extra columns with no team.
    other = lambda o: o.get("trackable_object") not in col and o.get("trackable_object") not in referees         and o.get("trackable_object") != SC_BALL
    extra = max(sum(1 for o in fr["data"] if other(o)) for fr in frames)
    x = np.full((n, players + extra), np.nan, np.float32)
    y = np.full((n, players + extra), np.nan, np.float32)
    bx, by = np.full(n, np.nan), np.full(n, np.nan)
    period, poss = np.zeros(n, dtype=int), np.zeros(n, dtype=np.int8)
    tracked = np.zeros(n, dtype=int)
    for i, fr in enumerate(frames):
        period[i] = fr["period"]
        group = (fr.get("possession") or {}).get("group")
        poss[i] = 1 if group == "home team" else 2 if group == "away team" else 0
        u = players
        for o in fr["data"]:
            obj = o.get("trackable_object")
            if obj == SC_BALL:
                bx[i], by[i] = o["x"], o["y"]
                continue
            if obj in referees:
                continue
            j = col.get(obj)
            if j is None:
                j, u = u, u + 1
            x[i, j], y[i, j] = o["x"], o["y"]
            tracked[i] += 1
    team = np.array([team_id[meta[p]["team_id"]] for p in people] + [""] * extra)
    keeper = np.broadcast_to(np.array([r == "Goalkeeper" for r in role] + [False] * extra), x.shape)
    forward = np.array([r in ("Center Forward", "Left Forward", "Right Forward") for r in role] + [False] * extra)
    league = info["competition_edition"]["competition"]["name"].replace("LaLiga", "La Liga")
    season = info["competition_edition"]["season"]["name"].replace("/20", "/")
    home, away = info["home_team"]["short_name"], info["away_team"]["short_name"]
    table = TIERS[(league, season)]
    # SkillCorner sets the possession group only while the ball is in play (every gap is >= 10 s).
    play = poss > 0
    return dict(source="SkillCorner", league=league, season=season, home=home, away=away,
                tiers=(tier(table[home]), tier(table[away])), fps=10, x=x, y=y, team=team,
                keeper=keeper, forward=forward, period=period, bx=bx, by=by, bz=None, play=play,
                valid=play & np.isfinite(bx) & (tracked >= SC_MIN_PLAYERS), poss=poss, passes=None)


# ---------- Metrica (loaders of tools/movement_reference.py; events re-read for From/To)

def metrica_match(game):
    g = MR.load_csv_game(game) if game < 3 else MR.load_epts_game()
    play = MR.in_play_mask(g)
    keeper = MR.keepers(g)
    teams = np.where(g["teams"] == g["teams"][0], "home", "away")
    n = len(g["period"])
    if game < 3:
        ev_name, home_name, away_name = MR.FILES[game]
        cols = []
        for name in (home_name, away_name):
            with open(os.path.join(MR.DATA, name)) as f:
                header = [f.readline() for _ in range(3)][2].strip().split(",")
            cols += [header[c].replace(" ", "") for c in range(3, len(header) - 2, 2)]
        with open(os.path.join(MR.DATA, ev_name)) as f:
            f.readline()
            raw = [line.rstrip("\n").split(",") for line in f]
        moves = [(p[0], p[1], int(p[4]), p[8], p[9]) for p in raw]
    else:
        ev_name, meta_name, _ = MR.FILES[3]
        root = ET.parse(os.path.join(MR.DATA, meta_name)).getroot()
        cols = sorted(p.get("id") for p in root.iter("Player"))
        with open(os.path.join(MR.DATA, ev_name)) as f:
            raw = json.load(f)["data"]
        moves = [(e["team"]["id"], e["type"]["name"], e["start"]["frame"],
                  (e.get("from") or {}).get("id"), (e.get("to") or {}).get("id")) for e in raw]
    poss = np.zeros(n, dtype=np.int8)
    passes, marks = [], []
    for team_name, kind, start, frm, to in sorted(moves, key=lambda m: m[2]):
        if kind in ("PASS", "RECOVERY", "SET PIECE", "SHOT"):
            marks.append((start - 1, team_name))
        if kind == "PASS" and frm in cols and to in cols:
            passes.append((start - 1, cols.index(frm), cols.index(to)))
    first_team = g["teams"][0]
    for (f0, t), nxt in zip(marks, marks[1:] + [(n, None)]):
        poss[f0:nxt[0]] = 1 if t == first_team else 2
    x, y = g["x"] - MR.LENGTH / 2, g["y"] - MR.WIDTH / 2
    bx, by = g["bx"] - MR.LENGTH / 2, g["by"] - MR.WIDTH / 2
    return dict(source="Metrica", league="unknown", season="unknown", home="Game %d home" % game,
                away="Game %d away" % game, tiers=("unknown", "unknown"), fps=25, x=x, y=y,
                team=teams, keeper=keeper, forward=None, period=g["period"], bx=bx, by=by, bz=None,
                play=play, valid=play & np.isfinite(bx), poss=np.where(play, poss, 0), passes=passes)


# ---------- frame helpers

def runs_of(labels):
    """(start, stop, label) for every maximal run of equal labels."""
    edges = np.flatnonzero(np.diff(labels)) + 1
    starts = np.concatenate(([0], edges))
    stops = np.concatenate((edges, [len(labels)]))
    return [(int(a), int(b), int(labels[a])) for a, b in zip(starts, stops)]


def all_true(mask, a, b):
    """mask[a:b+1] all true, for arrays of window starts a and ends b."""
    cum = np.concatenate(([0], np.cumsum(mask)))
    return cum[b + 1] - cum[a] == b - a + 1


def prepare(g):
    dist = np.hypot(g["x"] - g["bx"][:, None], g["y"] - g["by"][:, None])
    dist = np.where(np.isfinite(dist), dist, np.inf)
    g["dist"] = dist
    g["nearest"] = dist.min(axis=1)
    g["V"] = g["valid"] & np.isfinite(g["nearest"])
    holder = dist.argmin(axis=1)
    label = np.where(g["nearest"] <= REACH, holder, -1)
    label = np.where(g["team"][holder] == "", -2, label)
    g["label"] = np.where(g["V"], label, -2)
    g["attack"] = attack_directions(g)


def attack_directions(g):
    """+1 when the team attacks toward +x in that period: its keeper's mean x is negative."""
    out = {}
    for p in (1, 2):
        rows = g["period"] == p
        for t in ("home", "away"):
            cols = (g["team"] == t)[None, :] & g["keeper"][rows]
            xs = np.where(cols, g["x"][rows], np.nan)
            out[(t, p)] = 1 if np.nanmean(xs) < 0 else -1
    return out


# ---------- ball metrics

def ball_metrics(g):
    V, nearest, dist, fps = g["V"], g["nearest"], g["dist"], g["fps"]
    out = dict(free15=100.0 * np.mean(nearest[V] > REACH), free3=100.0 * np.mean(nearest[V] > 3.0),
               near5=float(np.mean((dist[V] < 5.0).sum(axis=1))),
               near10=float(np.mean((dist[V] < 10.0).sum(axis=1))),
               air=None if g["bz"] is None else 100.0 * np.mean(g["bz"][V] > 1.0))
    s = int(round(TURN_STEP * fps))
    bx, by = g["bx"], g["by"]
    vx, vy = (bx[s:] - bx[:-s]) / TURN_STEP, (by[s:] - by[:-s]) / TURN_STEP
    t = np.arange(s, len(bx) - s)
    before, after = t - s, t
    ok = all_true(V, t - s, t + s) & (nearest[t] > TURN_CLEAR)
    sp0, sp1 = np.hypot(vx[before], vy[before]), np.hypot(vx[after], vy[after])
    ok &= (sp0 > TURN_SPEED) & (sp1 > TURN_SPEED)
    if g["bz"] is not None:
        ok &= g["bz"][t] < 1.0
    cos = (vx[before] * vx[after] + vy[before] * vy[after]) / np.where(ok, sp0 * sp1, 1.0)
    turned = ok & (cos < math.cos(math.radians(TURN_DEG)))
    out["turns"] = 100.0 * turned.sum() / max(1, ok.sum())
    out["turn_steps"] = int(ok.sum())
    return out


def striker_offside(g):
    """Share of the striker's in-possession frames spent in an offside position (None: not measured)."""
    if g["source"] == "SkillCorner":
        return None
    on, off = 0, 0
    x, team, V = g["x"], g["team"], g["V"]
    for p in (1, 2):
        rows = g["period"] == p
        for t, code, opp in (("home", 1, "away"), ("away", 2, "home")):
            a = g["attack"][(t, p)]
            have = V & rows & (g["poss"] == code)
            ox = np.where(np.isfinite(x[:, team == opp]), x[:, team == opp] * a, -np.inf)
            second_last = np.sort(ox, axis=1)[:, -2]
            for j in strikers(g, t, p, a):
                sx = x[:, j] * a
                ok = have & np.isfinite(sx)
                on += ok.sum()
                off += (ok & (sx > second_last + OFFSIDE_MARGIN) & (sx > g["bx"] * a) & (sx > 0)).sum()
    return 100.0 * off / on


def strikers(g, t, p, a):
    """Declared forwards of the team on the pitch in that period, else its most advanced outfielder."""
    rows = g["period"] == p
    seen = np.isfinite(g["x"][rows]).mean(axis=0)
    mine = (g["team"] == t) & (seen > 0)
    if g["forward"] is not None and (mine & g["forward"]).any():
        return list(np.flatnonzero(mine & g["forward"]))
    cand = np.flatnonzero(mine & (seen >= 0.25) & ~g["keeper"][rows].any(axis=0))
    depth = np.nanmean(g["x"][rows][:, cand] * a, axis=0)
    return [int(cand[np.argmax(depth)])]


# ---------- passes and loose balls

def tracking_passes(g):
    """Holder runs: a run of >= CONTROL_MIN within reach, then free frames only, then a teammate's run."""
    ctrl = int(round(CONTROL_MIN * g["fps"]))
    controls, prev, clean = [], None, True
    for a, b, lab in runs_of(g["label"]):
        if lab == -1:
            continue
        if lab == -2 or b - a < ctrl:
            prev, clean = (None if lab == -2 else prev), False
            continue
        if prev is not None and clean and prev[2] != lab and g["team"][prev[2]] == g["team"][lab]:
            controls.append((prev[1] - 1, a, prev[2], lab))
        prev, clean = (a, b, lab), True
    return controls


def event_passes(g):
    """Event passes: release = the passer's last frame within reach near the event, arrival = the
    receiver's first frame within reach after it, with the ball valid throughout."""
    fps, dist, V = g["fps"], g["dist"], g["V"]
    out = []
    for f0, p, q in g["passes"]:
        lo, hi = max(0, f0 - fps // 2), min(len(V), f0 + fps + 1)
        near = np.flatnonzero((dist[lo:hi, p] <= REACH) & V[lo:hi])
        if len(near) == 0:
            continue
        r = lo + int(near[-1])
        end = min(len(V), r + int(FLIGHT_MAX * fps) + 1)
        hit = np.flatnonzero(dist[r + 1:end, q] <= REACH)
        if len(hit) == 0:
            continue
        arrival = r + 1 + int(hit[0])
        if V[r:arrival + 1].all():
            out.append((r, arrival, p, q))
    return out


def pass_metrics(g, passes):
    fps = g["fps"]
    flight, space = [], []
    for r, a, p, q in passes:
        if a - r > FLIGHT_MAX * fps or not np.isfinite(g["x"][r, q]):
            continue
        flight.append((a - r) / fps)
        space.append(math.hypot(g["x"][r, q] - g["bx"][a], g["y"][r, q] - g["by"][a]))
    flight, space = np.array(flight), np.array(space)
    return dict(passes=len(flight), flight50=float(np.percentile(flight, 50)),
                flight90=float(np.percentile(flight, 90)), space50=float(np.percentile(space, 50)),
                space_share=100.0 * float(np.mean(space >= SPACE_M)))


def loose_balls(g):
    """Free runs >= LOOSE_MIN that start with a short touch (< TOUCH_MAX within reach) either in a
    duel (both teams within reach just before) or by the other team than the previous holder."""
    fps, dist, team = g["fps"], g["dist"], g["team"]
    duel = (dist[:, team == "home"] <= REACH).any(axis=1) & (dist[:, team == "away"] <= REACH).any(axis=1)
    look, touch = int(round(DUEL_LOOK * fps)), int(round(TOUCH_MAX * fps))
    count = {"duel": 0, "deflection": 0}
    holders = []   # (length, team) of the holder runs since the last invalid frame
    for a, b, lab in runs_of(g["label"]):
        if lab == -2:
            holders = []
            continue
        if lab >= 0:
            holders.append((b - a, team[lab]))
            continue
        # A controlled release (the last holder kept it >= TOUCH_MAX) is a pass, not a loose ball.
        if b - a < LOOSE_MIN * fps or not holders or holders[-1][0] >= touch:
            continue
        if duel[max(0, a - look):a].any():
            count["duel"] += 1
        elif len(holders) >= 2 and holders[-1][1] != holders[-2][1]:
            count["deflection"] += 1
    measured = g["V"].sum() / fps / 60
    in_play = g["play"].sum() / fps / 60
    return (count["duel"] + count["deflection"]) / measured * in_play, count


# ---------- movement (believable-match R1, doc section 9.2, at the source's frame rate)

def movement(g):
    fps = g["fps"]
    window = int(round(0.52 * fps)) | 1
    acc_frames = int(round(0.52 * fps))
    m = window // 2
    vander = np.vander(np.arange(-m, m + 1, dtype=float), MR.SG_ORDER + 1, increasing=True)
    kernels = {d: np.linalg.pinv(vander)[d] * math.factorial(d) * fps ** d for d in (0, 1)}

    def sg(values, deriv):
        out = np.full(len(values), np.nan)
        for start, stop in MR.runs(np.isfinite(values)):
            if stop - start >= window:
                out[start + m:stop - m] = np.correlate(values[start:stop], kernels[deriv], "valid")
        return out

    play, period = g["play"], g["period"]
    cols = np.flatnonzero(g["team"] != "")
    teams = g["team"][cols]
    keeper = g["keeper"][:, cols]
    raw_x, raw_y = g["x"][:, cols].astype(float), g["y"][:, cols].astype(float)
    px = np.column_stack([sg(raw_x[:, j], 0) for j in range(len(cols))])
    py = np.column_stack([sg(raw_y[:, j], 0) for j in range(len(cols))])
    vx = np.column_stack([sg(raw_x[:, j], 1) for j in range(len(cols))])
    vy = np.column_stack([sg(raw_y[:, j], 1) for j in range(len(cols))])
    speed = np.hypot(vx, vy)
    accel = np.column_stack([sg(speed[:, j], 1) for j in range(len(cols))])
    outfield = ~keeper & play[:, None]
    counted = outfield & np.isfinite(speed)
    player_minutes = counted.sum() / fps / 60
    bands = np.digitize(speed[counted], MR.SPEED_EDGES)
    shares = [100.0 * np.mean(bands == b) for b in range(len(MR.SPEED_EDGES) + 1)]
    efforts = 0
    for j in range(len(cols)):
        hot = outfield[:, j] & (accel[:, j] > MR.ACC_THRESHOLD)
        efforts += sum(1 for a, b in MR.runs(hot) if b - a >= acc_frames)
    starts = []
    for p in (1, 2):
        frames = np.flatnonzero(period == p)
        starts += list(range(frames[0], frames[-1] - fps, fps))
    starts = np.array(starts)
    cum = np.concatenate(([0], np.cumsum(play)))
    valid = (cum[starts + fps + 1] - cum[starts] == fps + 1) & (period[starts] == period[starts + fps])
    dx, dy = px[starts + fps] - px[starts], py[starts + fps] - py[starts]
    step = np.hypot(dx, dy)
    ok = valid[:, None] & ~keeper[starts] & ~keeper[starts + fps] & np.isfinite(step)
    agreement = []
    for t in ("home", "away"):
        c = teams == t
        moving = ok[:, c] & (step[:, c] > 1.0)
        ux = np.where(moving, dx[:, c] / np.where(moving, step[:, c], 1), 0)
        uy = np.where(moving, dy[:, c] / np.where(moving, step[:, c], 1), 0)
        count = moving.sum(axis=1)
        sel = count >= 6
        agreement += list(np.hypot(ux.sum(axis=1), uy.sum(axis=1))[sel] / count[sel])
    agreement = np.array(agreement)
    adjacent = starts[1:] == starts[:-1] + fps
    both = ok[:-1] & ok[1:] & adjacent[:, None] & (step[:-1] > 0.5) & (step[1:] > 0.5)
    cos = (dx[:-1] * dx[1:] + dy[:-1] * dy[1:]) / np.where(both, step[:-1] * step[1:], 1)
    reversals = int((both & (cos < -0.5)).sum())
    return dict(r_median=float(np.median(agreement)), r_share=100.0 * float(np.mean(agreement > 0.9)),
                reversals=reversals / player_minutes, shares=shares, accels=efforts / player_minutes,
                windows=len(agreement), player_minutes=player_minutes)


# ---------- one match

def measure(g):
    prepare(g)
    out = ball_metrics(g)
    out["offside"] = striker_offside(g)
    out.update(pass_metrics(g, tracking_passes(g)))
    out["loose"], out["loose_by"] = loose_balls(g)
    out.update(movement(g))
    out["in_play_min"] = g["play"].sum() / g["fps"] / 60
    out["measured_min"] = g["V"].sum() / g["fps"] / 60
    if g["passes"] is not None:
        out["event"] = pass_metrics(g, event_passes(g))
    return out


def loaders():
    out = [(lambda m=m: dfl_match(m)) for m in DFL_MATCHES]
    out += [(lambda m=m: sc_match(m)) for m in SC_MATCHES]
    out += [(lambda k=k: metrica_match(k)) for k in (1, 2, 3)]
    return out


# ---------- splits, bands, tables

ROWS = [
    ("Nobody within 1.5 m of the ball, % of time", "BallFree15Percent", lambda m: m["free15"], 100.0, 2),
    ("Nobody within 3 m of the ball, % of time", "BallFree3Percent", lambda m: m["free3"], 100.0, 2),
    ("Players within 5 m of the ball", "PlayersWithin5m", lambda m: m["near5"], None, 2),
    ("Players within 10 m of the ball", "PlayersWithin10m", lambda m: m["near10"], None, 2),
    ("Ball above 1 m, % of time (DFL only)", "BallAbove1mPercent", lambda m: m["air"], 100.0, 2),
    ("Fast free ball turning > 20 deg per 0.2 s, % of steps", "FastFreeBallTurnPercent",
     lambda m: m["turns"], 100.0, 2),
    ("Striker in an offside position, % of his team's possession", "StrikerOffsidePercent",
     lambda m: m["offside"], 100.0, 2),
    ("Pass flight time p50, s", "PassFlightP50Seconds", lambda m: m["flight50"], None, 2),
    ("Pass flight time p90, s", "PassFlightP90Seconds", lambda m: m["flight90"], None, 2),
    ("Receiver to arrival point at release p50, m", "ReceiverToArrivalP50Meters", lambda m: m["space50"], None, 2),
    ("Passes into space (receiver >= 3 m from arrival), %", "PassIntoSpacePercent",
     lambda m: m["space_share"], 100.0, 2),
    ("Loose-ball episodes per match", "LooseBallsPerMatch", lambda m: m["loose"], None, 1),
    ("Direction agreement R, median", "AgreementMedian", lambda m: m["r_median"], 1.0, 3),
    ("Direction agreement R > 0.9, % of windows", "AgreementAbove09Percent", lambda m: m["r_share"], 100.0, 2),
    ("Heading reversals per player-minute", "ReversalsPerPlayerMinute", lambda m: m["reversals"], None, 3),
    ("Speed < 0.2 m/s, % of frames", "SpeedBelow02Percent", lambda m: m["shares"][0], 100.0, 2),
    ("Speed 0.2-2 m/s, % of frames", "Speed02To2Percent", lambda m: m["shares"][1], 100.0, 2),
    ("Speed 2-4 m/s, % of frames", "Speed2To4Percent", lambda m: m["shares"][2], 100.0, 2),
    ("Speed 4-5.5 m/s, % of frames", "Speed4To55Percent", lambda m: m["shares"][3], 100.0, 2),
    ("Speed 5.5-7 m/s, % of frames", "Speed55To7Percent", lambda m: m["shares"][4], 100.0, 2),
    ("Speed > 7 m/s, % of frames", "SpeedAbove7Percent", lambda m: m["shares"][5], 100.0, 2),
    ("Accelerations > 2 m/s2 per player-minute", "AccelerationsPerPlayerMinute", lambda m: m["accels"], None, 3),
]
SOURCES = ("DFL", "SkillCorner", "Metrica")
# Rows that read tracking jitter: SkillCorner's 10 fps broadcast tracking would widen them past any
# engine defect, so their band comes from the optical 25 fps sources only (doc section 10.4).
NOISE_SENSITIVE = {"FastFreeBallTurnPercent", "ReversalsPerPlayerMinute", "Speed55To7Percent",
                   "SpeedAbove7Percent", "AccelerationsPerPlayerMinute"}
OPTICAL = ("DFL", "Metrica")


def pairing(tiers):
    return "-".join(sorted(tiers, key=TIER_ORDER.index))


def tables(results):
    """Markdown lines: the match list, the split list and the band table."""
    keyed = {}
    for g, m in results:
        key = (SOURCES.index(g["source"]), g["league"] == "unknown", g["league"], g["season"],
               [TIER_ORDER.index(t) for t in sorted(g["tiers"], key=TIER_ORDER.index)])
        keyed.setdefault(repr(key), (key, []))[1].append((g, m))
    splits = [rows for _, rows in sorted(keyed.values(), key=lambda kv: kv[0])]
    split_of = {id(g): "S%d" % (k + 1) for k, rows in enumerate(splits) for g, _ in rows}
    lines = ["| Match | Source | League, season | Tiers (home-away) | Split | Ball in play, min | Measured, min |",
             "|---|---|---|---|---|---|---|"]
    for rows in splits:
        for g, m in rows:
            lines.append("| %s - %s | %s | %s %s | %s-%s | %s | %.1f | %.1f |" % (
                g["home"], g["away"], g["source"], g["league"], g["season"], g["tiers"][0], g["tiers"][1],
                split_of[id(g)], m["in_play_min"], m["measured_min"]))
    lines += ["", "| Split | Source | League, season | Tier pairing | Matches |", "|---|---|---|---|---|"]
    for k, rows in enumerate(splits):
        g = rows[0][0]
        lines.append("| S%d | %s | %s %s | %s | %d |" % (k + 1, g["source"], g["league"], g["season"],
                                                       pairing(g["tiers"]), len(rows)))
    names = ["S%d" % (k + 1) for k in range(len(splits))]
    lines += ["", "| Metric (constant) | " + " | ".join(names) + " | Band |", "|---|" + "---|" * len(names) + "---|"]
    for label, const, get, cap, digits in ROWS:
        cells, values = [], []
        for rows in splits:
            vals = [get(m) for _, m in rows]
            if any(v is None for v in vals):
                cells.append("n/a")
                continue
            value = float(np.mean(vals))
            if const in NOISE_SENSITIVE and rows[0][0]["source"] not in OPTICAL:
                cells.append("(%.*f)" % (digits, value))   # printed for information, not in the band
                continue
            values.append(value)
            cells.append("%.*f" % (digits, value))
        lo, hi = MR.band(values, cap, digits)
        lines.append("| %s (`%s`) | %s | %.*f-%.*f |" % (label, const, " | ".join(cells), digits, lo, digits, hi))
    return lines


def main():
    # The doc is UTF-8 (club names); a Windows console or redirect would default to cp1252.
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    download()
    results = []
    for load in loaders():
        g = load()
        print("%s %s - %s ..." % (g["source"], g["home"], g["away"]), file=sys.stderr)
        m = measure(g)
        for key in ("dist", "x", "y", "label", "keeper"):
            g.pop(key, None)
        results.append((g, m))
    lines = tables(results)
    print("\n".join(lines))
    for g, m in results:
        event = m.get("event")
        print("%s - %s: tracking passes %d%s, loose balls %s, turn steps %d, R windows %d" % (
            g["home"], g["away"], m["passes"],
            "" if event is None else " (event passes %d: flight p50 %.2f s, p90 %.2f s, into space %.1f%%)" % (
                event["passes"], event["flight50"], event["flight90"], event["space_share"]),
            m["loose_by"], m["turn_steps"], m["windows"]), file=sys.stderr)
    if "--check" in sys.argv:
        with open(DOC, encoding="utf8") as f:
            doc = set(f.read().splitlines())
        missing = [line for line in lines if line.startswith("|") and line not in doc]
        for line in missing:
            print("NOT IN DOC: " + line, file=sys.stderr)
        print("doc check: %s" % ("FAILED" if missing else "OK, every table line is in the doc"), file=sys.stderr)
        sys.exit(1 if missing else 0)


if __name__ == "__main__":
    main()
