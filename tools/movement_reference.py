"""Movement reference (docs/specs/believable-match.md R1) from Metrica Sports open sample data.

Downloads the three sample games at a pinned commit into tools/.tracking-data/ (git-ignored),
derives the R1 movement metrics per match and prints them with their bands as the markdown
table of docs/research/football-reference.md section 9. Only those aggregates are committed.

Run from the repo root:  python -I tools/movement_reference.py
Needs numpy. Definitions and the smoothing filter are documented in the doc, section 9.
"""
import json
import math
import os
import sys
import urllib.request
import xml.etree.ElementTree as ET

import numpy as np

COMMIT = "e706dd506b360d69d9d123d5b8026e7294b13996"
BASE = "https://raw.githubusercontent.com/metrica-sports/sample-data/" + COMMIT + "/data/"
DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".tracking-data", "metrica-" + COMMIT[:7])
FILES = {
    1: ["Sample_Game_1_RawEventsData.csv", "Sample_Game_1_RawTrackingData_Home_Team.csv",
        "Sample_Game_1_RawTrackingData_Away_Team.csv"],
    2: ["Sample_Game_2_RawEventsData.csv", "Sample_Game_2_RawTrackingData_Home_Team.csv",
        "Sample_Game_2_RawTrackingData_Away_Team.csv"],
    3: ["Sample_Game_3_events.json", "Sample_Game_3_metadata.xml", "Sample_Game_3_tracking.txt"],
}
FPS = 25
DT = 1.0 / FPS
LENGTH, WIDTH = 105.0, 68.0
SG_WINDOW, SG_ORDER = 13, 2     # Savitzky-Golay: 0.52 s window, quadratic fit
ACC_THRESHOLD, ACC_MIN_FRAMES = 2.0, 13   # > 2 m/s2 held for >= 0.5 s counts as one effort
SPEED_EDGES = [0.2, 2.0, 4.0, 5.5, 7.0]
BALL_GLITCH = 45.0   # m/s between two ball samples: a tracking jump, not a kick


def download():
    os.makedirs(DATA, exist_ok=True)
    for game, names in FILES.items():
        for name in names:
            path = os.path.join(DATA, name)
            if not os.path.exists(path):
                print("downloading", name, file=sys.stderr)
                urllib.request.urlretrieve(BASE + "Sample_Game_%d/%s" % (game, name), path)


# ---------- loading: every game becomes X, Y [frames, players] in metres, plus teams/periods/events

def load_csv_game(game):
    ev_name, home_name, away_name = FILES[game]
    cols, teams, xs, ys = [], [], [], []
    for team, name in (("Home", home_name), ("Away", away_name)):
        with open(os.path.join(DATA, name)) as f:
            header = [f.readline() for _ in range(3)][2].strip().split(",")
        raw = np.loadtxt(os.path.join(DATA, name), delimiter=",", skiprows=3)
        if team == "Home":
            period = raw[:, 0].astype(int)
            ball = raw[:, -2:]
        for c in range(3, len(header) - 2, 2):
            cols.append(header[c].replace(" ", ""))
            teams.append(team)
            xs.append(raw[:, c])
            ys.append(raw[:, c + 1])
    events = []
    with open(os.path.join(DATA, ev_name)) as f:
        f.readline()
        for line in f:
            p = line.rstrip("\n").split(",")
            events.append(dict(team=p[0], type=p[1], sub=p[2], start=int(p[4]), end=int(p[6])))
    return build(np.array(xs).T, np.array(ys).T, teams, period, ball, events)


def load_epts_game():
    ev_name, meta_name, track_name = FILES[3]
    root = ET.parse(os.path.join(DATA, meta_name)).getroot()
    team_of = {p.get("id"): p.get("teamId") for p in root.iter("Player")}
    channel = {c.get("id"): c.get("playerId") for c in root.iter("PlayerChannel")}
    params = {p.findtext("Name"): p.findtext("Value") for p in root.iter("ProviderParameter")}
    specs = []
    for spec in root.iter("DataFormatSpecification"):
        refs = [channel[r.get("playerChannelId")] for r in spec.iter("PlayerChannelRef")]
        specs.append((int(spec.get("startFrame")), int(spec.get("endFrame")), refs[::2]))
    players = sorted(team_of)
    index = {p: i for i, p in enumerate(players)}
    n = max(s[1] for s in specs)
    x = np.full((n, len(players)), np.nan)
    y = np.full((n, len(players)), np.nan)
    ball = np.full((n, 2), np.nan)
    with open(os.path.join(DATA, track_name)) as f:
        for line in f:
            frame_s, body, ball_s = line.strip().split(":")
            frame = int(frame_s)
            order = next(s[2] for s in specs if s[0] <= frame <= s[1])
            for pid, xy in zip(order, body.split(";")):
                px, py = xy.split(",")
                x[frame - 1, index[pid]], y[frame - 1, index[pid]] = float(px), float(py)
            bx, by = ball_s.split(",")
            ball[frame - 1] = float(bx), float(by)
    period = np.zeros(n, dtype=int)
    for k, name in ((1, "first_half"), (2, "second_half")):
        period[int(params[name + "_start"]) - 1:int(params[name + "_end"])] = k
    with open(os.path.join(DATA, ev_name)) as f:
        raw = json.load(f)["data"]
    events = []
    for e in raw:
        subs = e.get("subtypes") or []
        subs = [subs] if isinstance(subs, dict) else subs
        end = (e.get("end") or {}).get("frame") or 0
        events.append(dict(team=e["team"]["id"], type=e["type"]["name"],
                           sub="-".join(s["name"] for s in subs), start=e["start"]["frame"], end=end))
    return build(x, y, [team_of[p] for p in players], period, ball, events)


def build(x, y, teams, period, ball, events):
    bx, by = ball[:, 0] * LENGTH, ball[:, 1] * WIDTH
    jump = np.hypot(np.diff(bx), np.diff(by)) * FPS > BALL_GLITCH
    glitch = np.concatenate((jump, [False])) | np.concatenate(([False], jump))
    bx[glitch], by[glitch] = np.nan, np.nan
    return dict(x=x * LENGTH, y=y * WIDTH, nx=x, teams=np.array(teams), period=period,
                bx=bx, by=by, events=events)


# ---------- smoothing

def sg_kernel(deriv):
    m = SG_WINDOW // 2
    a = np.vander(np.arange(-m, m + 1, dtype=float), SG_ORDER + 1, increasing=True)
    return np.linalg.pinv(a)[deriv] * math.factorial(deriv) / DT ** deriv


KERNELS = {0: sg_kernel(0), 1: sg_kernel(1)}


def sg(values, deriv):
    """Savitzky-Golay over each finite run; frames within half a window of a gap stay NaN."""
    out = np.full(len(values), np.nan)
    finite = np.isfinite(values)
    edges = np.flatnonzero(np.diff(np.concatenate(([0], finite.astype(int), [0]))))
    m = SG_WINDOW // 2
    for start, stop in zip(edges[::2], edges[1::2]):
        if stop - start >= SG_WINDOW:
            out[start + m:stop - m] = np.correlate(values[start:stop], KERNELS[deriv], "valid")
    return out


# ---------- open play: dead from a stoppage event to the next event, and while the ball is blank >= 3 s

def in_play_mask(g):
    period = g["period"]
    mask = period > 0
    events = sorted((e for e in g["events"] if e["type"] not in ("CARD", "FAULT RECEIVED")),
                    key=lambda e: e["start"])
    for p in (1, 2):
        frames = np.flatnonzero(period == p)
        inside = [e for e in events if frames[0] < e["start"] <= frames[-1] + 1]
        mask[frames[0]:inside[0]["start"] - 1] = False
        mask[max(max(e["start"], e["end"]) for e in inside):frames[-1] + 1] = False
        for e, nxt in zip(inside, inside[1:]):
            if stoppage(e):
                mask[max(e["start"], e["end"]):nxt["start"] - 1] = False
    for a, b in runs(~np.isfinite(g["bx"])):
        # Metrica blanks the ball out of play; a long blank is a stoppage the events do not tag.
        if b - a >= 3 * FPS:
            mask[a:b] = False
    return mask


def stoppage(e):
    sub = e["sub"]
    return (e["type"] == "BALL OUT"
            or (e["type"] == "SHOT" and ("OUT" in sub or "GOAL" in sub))
            or "FAULT" in sub
            or (e["type"] == "BALL LOST" and ("OFFSIDE" in sub or "END HALF" in sub or "HAND BALL" in sub)))


def runs(mask):
    edges = np.flatnonzero(np.diff(np.concatenate(([0], mask.astype(int), [0]))))
    return list(zip(edges[::2], edges[1::2]))


def keepers(g):
    """Per team and period, the player (>= 25% of the period tracked) furthest from midfield on average."""
    keeper = np.zeros(g["x"].shape, dtype=bool)
    for p in (1, 2):
        rows = g["period"] == p
        depth = np.abs(g["nx"][rows] - 0.5)
        tracked = np.isfinite(depth)
        present = tracked.mean(axis=0)
        mean_depth = np.where(tracked, depth, 0).sum(axis=0) / np.maximum(tracked.sum(axis=0), 1)
        for team in set(g["teams"]):
            cand = np.flatnonzero((g["teams"] == team) & (present >= 0.25))
            keeper[rows, cand[np.argmax(mean_depth[cand])]] = True
    return keeper


# ---------- metrics

def metrics(g):
    play = in_play_mask(g)
    period = g["period"]
    keeper = keepers(g)
    players = g["x"].shape[1]
    px = np.column_stack([sg(g["x"][:, j], 0) for j in range(players)])
    py = np.column_stack([sg(g["y"][:, j], 0) for j in range(players)])
    vx = np.column_stack([sg(g["x"][:, j], 1) for j in range(players)])
    vy = np.column_stack([sg(g["y"][:, j], 1) for j in range(players)])
    speed = np.hypot(vx, vy)
    accel = np.column_stack([sg(speed[:, j], 1) for j in range(players)])
    outfield = ~keeper & play[:, None]

    counted = outfield & np.isfinite(speed)
    player_minutes = counted.sum() / FPS / 60
    bands = np.digitize(speed[counted], SPEED_EDGES)
    shares = [100.0 * np.mean(bands == b) for b in range(len(SPEED_EDGES) + 1)]

    efforts = 0
    for j in range(players):
        hot = outfield[:, j] & (accel[:, j] > ACC_THRESHOLD)
        efforts += sum(1 for a, b in runs(hot) if b - a >= ACC_MIN_FRAMES)

    # 1 s grid per period; a step is valid when every frame of it is open play in that period.
    starts = []
    for p in (1, 2):
        frames = np.flatnonzero(period == p)
        starts += list(range(frames[0], frames[-1] - FPS, FPS))
    starts = np.array(starts)
    cum = np.concatenate(([0], np.cumsum(play)))
    valid = (cum[starts + FPS + 1] - cum[starts] == FPS + 1) & (period[starts] == period[starts + FPS])
    dx = px[starts + FPS] - px[starts]
    dy = py[starts + FPS] - py[starts]
    step = np.hypot(dx, dy)
    ok = valid[:, None] & ~keeper[starts] & ~keeper[starts + FPS] & np.isfinite(step)

    agreement = []
    for team in set(g["teams"]):
        cols = g["teams"] == team
        moving = ok[:, cols] & (step[:, cols] > 1.0)
        ux = np.where(moving, dx[:, cols] / np.where(moving, step[:, cols], 1), 0)
        uy = np.where(moving, dy[:, cols] / np.where(moving, step[:, cols], 1), 0)
        count = moving.sum(axis=1)
        sel = count >= 6
        agreement += list(np.hypot(ux.sum(axis=1), uy.sum(axis=1))[sel] / count[sel])
    agreement = np.array(agreement)

    adjacent = starts[1:] == starts[:-1] + FPS
    both = ok[:-1] & ok[1:] & adjacent[:, None] & (step[:-1] > 0.5) & (step[1:] > 0.5)
    cos = (dx[:-1] * dx[1:] + dy[:-1] * dy[1:]) / np.where(both, step[:-1] * step[1:], 1)
    reversals = int((both & (cos < -0.5)).sum())

    bvx, bvy = sg(g["bx"], 1), sg(g["by"], 1)
    ball_speed = np.hypot(bvx, bvy)

    def flight(kind):
        frames = []
        for e in g["events"]:
            if e["type"] == kind and e["end"] > e["start"] and play[e["start"] - 1]:
                frames.append(ball_speed[e["start"] - 1:e["end"] - 1])
        s = np.concatenate(frames)
        return np.percentile(s[np.isfinite(s)], [50, 90, 99])

    return dict(
        open_play_min=play.sum() / FPS / 60, player_minutes=player_minutes, windows=len(agreement),
        r_median=float(np.median(agreement)), r_share=100.0 * float(np.mean(agreement > 0.9)),
        reversals=reversals / player_minutes, shares=shares, accels=efforts / player_minutes,
        pass_speed=flight("PASS"), shot_speed=flight("SHOT"))


ROWS = [
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
    ("Pass ball speed p50, m/s", "PassSpeedP50", lambda m: m["pass_speed"][0], None, 2),
    ("Pass ball speed p90, m/s", "PassSpeedP90", lambda m: m["pass_speed"][1], None, 2),
    ("Pass ball speed p99, m/s", "PassSpeedP99", lambda m: m["pass_speed"][2], None, 2),
    ("Shot ball speed p50, m/s", "ShotSpeedP50", lambda m: m["shot_speed"][0], None, 2),
    ("Shot ball speed p90, m/s", "ShotSpeedP90", lambda m: m["shot_speed"][1], None, 2),
    ("Shot ball speed p99, m/s", "ShotSpeedP99", lambda m: m["shot_speed"][2], None, 2),
]


def band(values, cap, digits):
    """Min-max widened by 10% of the range (5% of the value when the range is 0), rounded outward."""
    lo, hi = min(values), max(values)
    pad = 0.1 * (hi - lo)
    lo, hi = (lo - pad, hi + pad) if pad > 0 else (lo * 0.95, hi * 1.05)
    scale = 10 ** digits
    lo = max(0.0, math.floor(lo * scale) / scale)
    hi = math.ceil(hi * scale) / scale
    return lo, (min(hi, cap) if cap else hi)


def main():
    download()
    results = {1: load_csv_game(1), 2: load_csv_game(2), 3: load_epts_game()}
    per_game = {}
    for game, g in results.items():
        print("game %d ..." % game, file=sys.stderr)
        per_game[game] = metrics(g)
    games = sorted(per_game)
    print("| Metric (constant) | " + " | ".join("Game %d" % k for k in games) + " | Band |")
    print("|---|" + "---|" * len(games) + "---|")
    for label, const, get, cap, digits in ROWS:
        values = [get(per_game[k]) for k in games]
        lo, hi = band(values, cap, digits)
        cells = " | ".join("%.*f" % (digits, v) for v in values)
        print("| %s (`%s`) | %s | %.*f-%.*f |" % (label, const, cells, digits, lo, digits, hi))
    print()
    for k in games:
        m = per_game[k]
        print("game %d: open play %.1f min, outfield player-minutes %.0f, R windows %d"
              % (k, m["open_play_min"], m["player_minutes"], m["windows"]))


if __name__ == "__main__":
    main()
