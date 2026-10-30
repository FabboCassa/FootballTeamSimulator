"""Event reference v2 (docs/specs/free-ball-real-movement.md R1) from StatsBomb open data.

Downloads the StatsBomb match lists and event files of six league seasons into
tools/.tracking-data/ (git-ignored), derives the R1 event metrics per team-match, groups them by
league, team tier and opponent tier, and prints the markdown tables of
docs/research/football-reference.md section 11. Only those aggregates are committed.

Run from the repo root:  python -I tools/event_reference.py [--check] [--pin]
--check exits 1 unless every printed table line is in the doc verbatim.
--pin downloads a file missing from tools/event_reference.sha256 and records its SHA-256 there;
without it a file that is not in the manifest stops the run. Definitions are in the doc, §11.3.
"""
import importlib.util
import json
import math
import os
import sys
import urllib.request

import numpy as np

TOOLS = os.path.dirname(os.path.abspath(__file__))


def _load_tracking():
    # -I keeps the script directory off sys.path, so the sibling script is loaded by its path.
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location("tracking_reference", os.path.join(TOOLS, "tracking_reference.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TR = _load_tracking()
MR = TR.MR

# Every URL is under one pinned commit, and every file is pinned by name and SHA-256 in the
# committed manifest (887 event files would not fit in the script). File names are built here
# from integer ids; nothing remote decides a file name or a checksum.
SB_COMMIT = "4b73468fc5b0f1950f9f66fada70ad3a4f9327cb"
SB_BASE = "https://raw.githubusercontent.com/statsbomb/open-data/" + SB_COMMIT + "/data/"
SB_DIR = os.path.join(TR.ROOT_DATA, "statsbomb-" + SB_COMMIT[:7])
MANIFEST = os.path.join(TOOLS, "event_reference.sha256")

# (competition id, season id, league, season), most recent first, with the final league table in
# StatsBomb's team names (doc §11.1 for the sources). A club StatsBomb has no match of keeps a
# placeholder name: only its position matters.
SEASONS = (
    (9, 281, "Bundesliga", "2023/24", (
        "Bayer Leverkusen", "VfB Stuttgart", "Bayern Munich", "RB Leipzig", "Borussia Dortmund",
        "Eintracht Frankfurt", "Hoffenheim", "FC Heidenheim", "Werder Bremen", "Freiburg", "Augsburg",
        "Wolfsburg", "FSV Mainz 05", "Borussia Mönchengladbach", "Union Berlin", "Bochum", "FC Köln",
        "Darmstadt 98")),
    (7, 235, "Ligue 1", "2022/23", (
        "Paris Saint-Germain", "Lens", "Olympique de Marseille", "Rennes", "Lille", "AS Monaco", "Lyon",
        "Clermont Foot", "OGC Nice", "Lorient", "Stade de Reims", "Montpellier", "Toulouse",
        "Stade Brestois", "Strasbourg", "Nantes", "Auxerre", "AC Ajaccio", "Troyes", "Angers")),
    (7, 108, "Ligue 1", "2021/22", (
        "Paris Saint-Germain", "Olympique de Marseille", "AS Monaco", "Rennes", "OGC Nice", "Strasbourg",
        "Lens", "Lyon", "Nantes", "Lille", "Stade Brestois", "Stade de Reims", "Montpellier", "Angers",
        "Troyes", "Lorient", "Clermont Foot", "Saint-Étienne", "Metz", "Bordeaux")),
    (11, 90, "La Liga", "2020/21", (
        "Atlético Madrid", "Real Madrid", "Barcelona", "Sevilla", "Real Sociedad", "Real Betis",
        "Villarreal", "Celta Vigo", "Athletic Club", "Granada", "Osasuna", "Cádiz", "Valencia",
        "Levante UD", "Getafe", "Deportivo Alavés", "Elche", "Huesca", "Real Valladolid", "Eibar")),
    (2, 27, "Premier League", "2015/16", (
        "Leicester City", "Arsenal", "Tottenham Hotspur", "Manchester City", "Manchester United",
        "Southampton", "West Ham United", "Liverpool", "Stoke City", "Chelsea", "Everton", "Swansea City",
        "Watford", "West Bromwich Albion", "Crystal Palace", "AFC Bournemouth", "Sunderland",
        "Newcastle United", "Norwich City", "Aston Villa")),
    (12, 27, "Serie A", "2015/16", (
        "Juventus", "Napoli", "AS Roma", "Inter Milan", "Fiorentina", "Sassuolo", "AC Milan", "Lazio",
        "Chievo", "Genoa", "Empoli", "Torino", "Atalanta", "Bologna", "Sampdoria", "Udinese", "Palermo",
        "Carpi", "Frosinone", "Hellas Verona")),
)
TIERS = ("top", "middle", "bottom")

# StatsBomb's 120 x 80 pitch: every event is seen from the acting team, attacking toward x = 120.
X_M, Y_M = 105.0 / 120.0, 68.0 / 80.0   # metres per unit on a 105 x 68 m pitch
FINAL_THIRD_X = 80.0
BOX_X, BOX_Y0, BOX_Y1 = 102.0, 18.0, 62.0   # StatsBomb's own penalty-area lines (doc §2.2)
WIDE_M = 13.84   # spec R2 wide channel: within 13.84 m of a touchline, in metres (Y_M), not box lines
LONG_M = 30.0
SET_PIECES = {"Corner", "Free Kick", "Goal Kick", "Kick Off", "Throw-in"}
MIN_SPLIT = 10   # team-matches; a smaller split is printed in parentheses and is not in the band


def tier(position, teams):
    return "top" if position <= 6 else "bottom" if position > teams - 6 else "middle"


# ---------- download

def read_manifest():
    pins = {}
    if os.path.exists(MANIFEST):
        with open(MANIFEST, encoding="ascii") as f:
            for line in f:
                sha256, name = line.split()
                pins[name] = sha256
    return pins


def write_manifest(pins):
    with open(MANIFEST + ".part", "w", encoding="ascii", newline="\n") as f:
        for name in sorted(pins):
            f.write("%s  %s\n" % (pins[name], name))
    os.replace(MANIFEST + ".part", MANIFEST)


def fetch(rel_url, name, pins, pin_new):
    """Make SB_DIR/name the pinned file; with pin_new, download an unpinned one and pin its hash."""
    if name in pins:
        TR.fetch(SB_BASE + rel_url, SB_DIR, name, pins[name])
        return
    if not pin_new:
        raise RuntimeError("%s is not pinned in %s: run with --pin" % (name, MANIFEST))
    path = os.path.join(SB_DIR, name)
    print("downloading and pinning", name, file=sys.stderr)
    urllib.request.urlretrieve(SB_BASE + rel_url, path + ".part")
    pins[name] = TR._sha256(path + ".part")
    os.replace(path + ".part", path)


def load_json(name):
    with open(os.path.join(SB_DIR, name), encoding="utf8") as f:
        return json.load(f)


def download(pin_new):
    """Fetch or re-verify every match list and event file; return [(season row, matches)]."""
    os.makedirs(SB_DIR, exist_ok=True)
    pins = read_manifest()
    out = []
    try:
        for row in SEASONS:
            comp, season = row[0], row[1]
            name = "matches_%d_%d.json" % (comp, season)
            fetch("matches/%d/%d.json" % (comp, season), name, pins, pin_new)
            matches = load_json(name)
            for m in matches:
                if type(m["match_id"]) is not int:
                    raise RuntimeError("match id is not an integer: %r" % (m["match_id"],))
                fetch("events/%d.json" % m["match_id"], "events_%d.json" % m["match_id"], pins, pin_new)
            out.append((row, matches))
    finally:
        if pin_new:
            write_manifest(pins)
    return out


# ---------- one team-match

def is_open_play(p):
    return (p["pass"].get("type") or {}).get("name") not in SET_PIECES


def is_cross(p):
    """Open-play pass from the wide channel of the final third that ends inside the box."""
    (x0, y0), (x1, y1) = p["location"], p["pass"]["end_location"][:2]
    return (is_open_play(p) and x0 >= FINAL_THIRD_X and (y0 * Y_M <= WIDE_M or (80.0 - y0) * Y_M <= WIDE_M)
            and x1 >= BOX_X and BOX_Y0 <= y1 <= BOX_Y1)


def team_match(events, team_id):
    mine = [e for e in events if e["team"]["id"] == team_id and e["period"] <= 4]
    passes = [e for e in mine if e["type"]["name"] == "Pass"]
    high = lambda p: p["pass"]["height"]["name"] == "High Pass"
    length = lambda p: math.hypot((p["pass"]["end_location"][0] - p["location"][0]) * X_M,
                                  (p["pass"]["end_location"][1] - p["location"][1]) * Y_M)
    crosses = [p for p in passes if is_cross(p)]
    aerial_won = sum(1 for e in mine for v in e.values() if isinstance(v, dict) and v.get("aerial_won"))
    aerial_lost = sum(1 for e in mine if e["type"]["name"] == "Duel"
                      and e["duel"].get("type", {}).get("name") == "Aerial Lost")
    offsides = sum(1 for p in passes if (p["pass"].get("outcome") or {}).get("name") == "Pass Offside")
    offsides += sum(1 for e in mine if e["type"]["name"] == "Offside")
    return dict(
        passes=len(passes),
        long=100.0 * sum(1 for p in passes if length(p) > LONG_M) / len(passes),
        high=100.0 * sum(1 for p in passes if high(p)) / len(passes),
        high_cross=sum(1 for p in crosses if high(p)),
        low_cross=sum(1 for p in crosses if not high(p)),
        flag_cross=sum(1 for p in passes if is_open_play(p) and p["pass"].get("cross")),
        through=sum(1 for p in passes if p["pass"].get("through_ball")
                    or (p["pass"].get("technique") or {}).get("name") == "Through Ball"),
        aerial=aerial_won + aerial_lost,
        offside=offsides)


def season_rows(row, matches):
    """One entry per team-match of the season: (league, season, team, team tier, opponent tier, metrics)."""
    _, _, league, season, table = row
    position = {name: k + 1 for k, name in enumerate(table)}
    check_table(row, matches)
    out = []
    for m in sorted(matches, key=lambda m: m["match_id"]):
        events = load_json("events_%d.json" % m["match_id"])
        sides = ((m["home_team"]["home_team_id"], m["home_team"]["home_team_name"]),
                 (m["away_team"]["away_team_id"], m["away_team"]["away_team_name"]))
        tiers = [tier(position[name], len(table)) for _, name in sides]
        for k, (team_id, name) in enumerate(sides):
            out.append((league, season, name, tiers[k], tiers[1 - k], team_match(events, team_id)))
    return out


def check_table(row, matches):
    """A season the data covers in full re-derives its tiers from the results (points, goal
    difference, goals): a typo in the static table stops the run."""
    _, _, league, season, table = row
    if len(matches) != len(table) * (len(table) - 1):
        return
    pts = {name: [0, 0, 0] for name in table}
    for m in matches:
        h, a = m["home_team"]["home_team_name"], m["away_team"]["away_team_name"]
        hs, as_ = m["home_score"], m["away_score"]
        for team, gf, ga in ((h, hs, as_), (a, as_, hs)):
            pts[team][0] += 3 if gf > ga else 1 if gf == ga else 0
            pts[team][1] += gf - ga
            pts[team][2] += gf
    order = sorted(table, key=lambda t: [-v for v in pts[t]])
    for k, name in enumerate(order):
        if tier(k + 1, len(table)) != tier(table.index(name) + 1, len(table)):
            raise RuntimeError("%s %s: %s is %s by the results" % (league, season, name, tier(k + 1, len(table))))


# ---------- tables

METRICS = [   # label, constant (None: information only), key, cap, digits
    ("Passes per team-match", "PassesPerTeamMatch", "passes", None, 1),
    ("Long passes (> 30 m), % of passes", "LongPassPercent", "long", 100.0, 2),
    ("High passes, % of passes", "HighPassPercent", "high", 100.0, 2),
    ("High crosses per team-match", "HighCrossesPerTeamMatch", "high_cross", None, 2),
    ("Low crosses and cut-backs per team-match", "LowCrossesPerTeamMatch", "low_cross", None, 2),
    ("Crosses by StatsBomb's own flag, open play, per team-match", None, "flag_cross", None, 2),
    ("Through balls per team-match", "ThroughBallsPerTeamMatch", "through", None, 2),
    ("Aerial duels per team-match", "AerialDuelsPerTeamMatch", "aerial", None, 2),
    ("Offsides per team-match", "OffsidesPerTeamMatch", "offside", None, 2),
]
HEADS = ["Passes", "Long %", "High %", "High crosses", "Low crosses", "SB cross flag", "Through balls",
         "Aerial duels", "Offsides"]


def means(rows):
    return {key: float(np.mean([r[5][key] for r in rows])) for _, _, key, _, _ in METRICS}


def cells(values):
    return " | ".join("%.*f" % (digits, values[key]) for _, _, key, _, digits in METRICS)


def tables(seasons, rows):
    head = " | ".join(HEADS) + " |"
    rule = "---|" * len(HEADS)
    lines = ["| League, season | Matches | Team-matches | Coverage | " + head,
             "|---|---|---|---|" + rule]
    for row, matches in seasons:
        league, season = row[2], row[3]
        mine = [r for r in rows if r[0] == league and r[1] == season]
        count = {}
        for r in mine:
            count[r[2]] = count.get(r[2], 0) + 1
        full = len(matches) == len(row[4]) * (len(row[4]) - 1)
        clubs = "full season" if full else ", ".join(t for t in row[4] if count.get(t, 0) == len(matches))
        lines.append("| %s %s | %d | %d | %s | %s |" % (league, season, len(matches), len(mine), clubs, cells(means(mine))))
    lines += ["", "| League, season | Team tier | Team-matches | " + head, "|---|---|---|" + rule]
    for row, _ in seasons:
        for t in TIERS:
            mine = [r for r in rows if r[0] == row[2] and r[1] == row[3] and r[3] == t]
            lines.append("| %s %s | %s | %d | %s |" % (row[2], row[3], t, len(mine), cells(means(mine))))
    lines += ["", "| Split | League, season | Team tier - opponent tier | Team-matches | " + head,
              "|---|---|---|---|" + rule]
    splits = []
    for row, _ in seasons:
        for t in TIERS:
            for o in TIERS:
                mine = [r for r in rows if r[0] == row[2] and r[1] == row[3] and r[3] == t and r[4] == o]
                if not mine:
                    continue
                name = "E%d" % (len(splits) + 1)
                values = means(mine)
                splits.append((name, len(mine), values))
                lines.append("| %s | %s %s | %s-%s | %d | %s |" % (
                    name if len(mine) >= MIN_SPLIT else "(%s)" % name, row[2], row[3], t, o, len(mine), cells(values)))
    lines += ["", "| Metric (constant) | Lowest split | Highest split | Band |", "|---|---|---|---|"]
    banded = [s for s in splits if s[1] >= MIN_SPLIT]
    for label, const, key, cap, digits in METRICS:
        low = min(banded, key=lambda s: s[2][key])
        high = max(banded, key=lambda s: s[2][key])
        lo, hi = MR.band([s[2][key] for s in banded], cap, digits)
        name = "%s (`%s`)" % (label, const) if const else "%s (information, no constant)" % label
        lines.append("| %s | %.*f (%s) | %.*f (%s) | %.*f-%.*f |" % (
            name, digits, low[2][key], low[0], digits, high[2][key], high[0], digits, lo, digits, hi))
    return lines


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    seasons = download("--pin" in sys.argv)
    rows = []
    for row, matches in seasons:
        print("%s %s: %d matches ..." % (row[2], row[3], len(matches)), file=sys.stderr)
        rows += season_rows(row, matches)
    lines = tables(seasons, rows)
    print("\n".join(lines))
    if "--check" in sys.argv:
        with open(TR.DOC, encoding="utf8") as f:
            doc = set(f.read().splitlines())
        missing = [line for line in lines if line.startswith("|") and line not in doc]
        for line in missing:
            print("NOT IN DOC: " + line, file=sys.stderr)
        print("doc check: %s" % ("FAILED" if missing else "OK, every table line is in the doc"), file=sys.stderr)
        sys.exit(1 if missing else 0)


if __name__ == "__main__":
    main()
