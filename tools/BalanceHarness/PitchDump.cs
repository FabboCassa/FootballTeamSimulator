using System.Text;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Fts.BalanceHarness;

/// <summary>
/// Writes one simulated match out as a single HTML file you can open in a browser (phase 0 of the
/// match engine rework — see docs/engine/MATCH_ENGINE_PLAN.md).
///
/// The numbers next door say the block is too narrow and everybody has an opponent on him. This is
/// how you SEE it, without building the Unity client and clicking through to a match: scrub the
/// whole ninety minutes, and turn on the overlays that draw what the readings measure — each team's
/// bounding box, the line its four deepest men are holding, and a thread from every player to the
/// opponent standing within three metres of him.
///
/// Self-contained on purpose: no server, no assets, no network. Positions are embedded as flat
/// integer arrays, exactly as the stream stores them.
/// </summary>
internal static class PitchDump
{
    public static void Write(string path, MatchReport report, Lineup home, Lineup away,
        string homeName, string awayName)
    {
        PositionStream? stream = report.Positions;
        if (stream == null) throw new InvalidOperationException("the match carries no position stream");

        string html = Template
            .Replace("__TITLE__", Escape($"{homeName} {report.HomeGoals}-{report.AwayGoals} {awayName}"))
            .Replace("__HOME_NAME__", Escape(homeName))
            .Replace("__AWAY_NAME__", Escape(awayName))
            .Replace("__PITCH_LENGTH__", Pitch.LengthDm.ToString())
            .Replace("__PITCH_WIDTH__", Pitch.WidthDm.ToString())
            .Replace("__TICKS_PER_MINUTE__", stream.TicksPerMinute.ToString())
            .Replace("__PLAYER_COUNT__", stream.PlayerCount.ToString())
            .Replace("__KEEPER_HOME__", KeeperSlot(home).ToString())
            .Replace("__KEEPER_AWAY__", KeeperSlot(away).ToString())
            .Replace("__BALL__", Ints(stream.BallXY))
            .Replace("__HOME_XY__", Ints(stream.HomeXY))
            .Replace("__AWAY_XY__", Ints(stream.AwayXY))
            .Replace("__OWNER__", Ints(stream.Owner))
            .Replace("__HOME_SHORT__", Escape(Short(homeName)))
            .Replace("__AWAY_SHORT__", Escape(Short(awayName)))
            .Replace("__HALFTIME__", HalfTimeFrame(stream).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__HOME_SHIRTS__", Ints(stream.HomeShirts))
            .Replace("__AWAY_SHIRTS__", Ints(stream.AwayShirts))
            .Replace("__HOME_PLAYERS__", Names(home))
            .Replace("__AWAY_PLAYERS__", Names(away))
            .Replace("__ACTIONS__", Actions(stream));

        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, html, new UTF8Encoding(false));
    }

    private static int KeeperSlot(Lineup lineup)
    {
        for (int i = 0; i < lineup.Slots.Count; i++)
            if (lineup.Slots[i].Role == PositionRole.Goalkeeper) return i;
        return 0;
    }

    /// <summary>
    /// A club name short enough for a scoreboard: the first three letters of its longest word, which
    /// is how a television caption abbreviates one and is enough to tell two clubs apart.
    /// </summary>
    private static string Short(string name)
    {
        string longest = "";
        foreach (string word in (name ?? "").Split(' '))
            if (word.Length > longest.Length) longest = word;
        if (longest.Length == 0) return "???";
        return longest.Substring(0, System.Math.Min(3, longest.Length)).ToUpperInvariant();
    }

    /// <summary>
    /// The frame the referee blew for half-time on, or -1 if the stream has no interval (a replay
    /// stored before engine phase 5). From this frame on the two sides have CHANGED ENDS, which is a
    /// property of the picture and not of the simulation: the model keeps both sides attacking the
    /// end they attacked all match (MovementGeometry.Direction), and this viewer mirrors the second
    /// half so the eye sees what the laws say happens at the interval.
    /// </summary>
    private static int HalfTimeFrame(PositionStream stream)
    {
        foreach (BallAction action in stream.Actions)
            if (action.Kind == BallActionKind.HalfTime) return action.Tick;
        return -1;
    }

    private static string Ints(int[] values)
    {
        var sb = new StringBuilder(values.Length * 4);
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(values[i]);
        }

        return sb.ToString();
    }

    private static string Names(Lineup lineup)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lineup.Slots.Count; i++)
        {
            if (i > 0) sb.Append(',');
            LineupSlot slot = lineup.Slots[i];
            sb.Append('"').Append(Escape(slot.Player.FullName)).Append(" (").Append(slot.Role).Append(")\"");
        }

        return sb.ToString();
    }

    /// <summary>[tick, kind, home, slot] per action, flat, so the viewer can caption the frame.</summary>
    private static string Actions(PositionStream stream)
    {
        var sb = new StringBuilder();
        foreach (BallAction a in stream.Actions)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(a.Tick).Append(',').Append((int)a.Kind).Append(',')
              .Append(a.Home ? 1 : 0).Append(',').Append(a.Slot);
        }

        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "&lt;").Replace(">", "&gt;");

    private const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__</title>
<style>
  :root { color-scheme: dark; }
  body { margin:0; background:#10131a; color:#e6e9ef;
         font:14px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif; }
  header { padding:12px 18px; display:flex; gap:18px; align-items:baseline; flex-wrap:wrap;
           border-bottom:1px solid #262b36; }
  h1 { font-size:16px; margin:0; font-weight:600; }
  .muted { color:#8b93a3; }
  main { padding:14px 18px 28px; }
  canvas { width:100%; height:auto; display:block; border-radius:8px; background:#1f6b34; }
  .bar { display:flex; gap:14px; align-items:center; flex-wrap:wrap; margin:12px 0; }
  button { background:#252b38; color:#e6e9ef; border:1px solid #39414f; border-radius:6px;
           padding:6px 14px; font:inherit; cursor:pointer; }
  button:hover { background:#2f3746; }
  button.on { background:#3d6cf0; border-color:#3d6cf0; }
  input[type=range] { flex:1; min-width:240px; accent-color:#3d6cf0; }
  .toggles { display:flex; gap:8px; flex-wrap:wrap; }
  .readout { display:flex; gap:22px; flex-wrap:wrap; margin-top:10px; padding-top:10px;
             border-top:1px solid #262b36; font-variant-numeric:tabular-nums; }
  .readout b { font-weight:600; }
  code { background:#1a1f29; padding:1px 6px; border-radius:4px; }
</style>
</head>
<body>
<header>
  <h1>__TITLE__</h1>
  <span class="muted">replay dump &mdash; <b id="clock" style="color:#e8ecf4;font-variant-numeric:tabular-nums">00:00</b></span>
  <span class="muted" id="caption"></span>
</header>
<main>
  <canvas id="pitch" width="1260" height="883"></canvas>

  <div class="bar">
    <button id="play">Pause</button>
    <button data-speed="1" class="on">1x</button>
    <button data-speed="2">2x</button>
    <button data-speed="4">4x</button>
    <button data-speed="12">12x</button>
    <input type="range" id="scrub" min="0" value="0">
  </div>

  <div class="bar toggles">
    <button data-layer="box" class="on">Team box</button>
    <button data-layer="line" class="on">Back line</button>
    <button data-layer="mark" class="on">Within 3 m</button>
    <button data-layer="trail" class="on">Ball trail</button>
    <button data-layer="names">Names</button>
  </div>

  <div class="readout">
    <span><b id="rh">__HOME_NAME__</b> width <span id="hw">-</span> m &middot; depth <span id="hd">-</span> m
      &middot; back line spread <span id="hb">-</span> m</span>
    <span><b id="ra">__AWAY_NAME__</b> width <span id="aw">-</span> m &middot; depth <span id="ad">-</span> m
      &middot; back line spread <span id="ab">-</span> m</span>
    <span class="muted">marked (within 3 m): <span id="mk">-</span> of 20</span>
  </div>
  <p class="muted">A professional block defends about 30-40 m wide and 25-35 m deep, holds its back
    four within ~3 m of a line, and leaves at most a handful of men inside three metres of an
    opponent. Read the numbers above against that while you scrub.</p>
</main>

<script>
const LEN = __PITCH_LENGTH__, WID = __PITCH_WIDTH__, TPM = __TICKS_PER_MINUTE__, N = __PLAYER_COUNT__;
const GK = [__KEEPER_HOME__, __KEEPER_AWAY__];
const BALL = [__BALL__];
const XY = [[__HOME_XY__], [__AWAY_XY__]];
const OWNER = [__OWNER__];
const SHIRTS = [[__HOME_SHIRTS__], [__AWAY_SHIRTS__]];
const NAMES = [[__HOME_PLAYERS__], [__AWAY_PLAYERS__]];
const ACTIONS = [__ACTIONS__];
const KINDS = ["kick-off","pass","long ball","cross","dribble","tackle","interception","clearance",
               "shot","save","GOAL","miss","corner","throw-in","goal kick","free kick",
               "OFFSIDE","foul","yellow card","RED CARD","PENALTY","half-time"];
const HALFTIME = __HALFTIME__;
const SHORT = ["__HOME_SHORT__", "__AWAY_SHORT__"];
const GOAL_KIND = KINDS.indexOf("GOAL");
const COLOR = ["#e8375a", "#4f6df0"];

const TICKS = BALL.length / 2;
const cv = document.getElementById("pitch"), ctx = cv.getContext("2d");
const scrub = document.getElementById("scrub");
scrub.max = TICKS - 1;

const layers = { box:true, line:true, mark:true, trail:true, names:false };
let tick = 0, playing = true, speed = 1, last = 0;

const PAD = 30;

// A strip above the pitch for the scoreboard. It is not drawn OVER the football on purpose: a
// caption in the top-left corner of the picture sits exactly where the corner flag is, and the
// corners are one of the things this dump exists to let you watch.
const TOP = 46;
const sx = t => PAD + (cv.width - 2*PAD) * t / LEN;
const sy = t => TOP + PAD + (cv.height - TOP - 2*PAD) * t / WID;

// CHANGING ENDS (Law 7). The simulation keeps every side attacking the same end for ninety
// minutes — the pitch is symmetric, so flipping it would change no football — and the second
// half is mirrored HERE, where the eye is. One rotation of the pitch through 180 degrees, so
// both axes turn: everything drawn goes through these three functions, which is why the team
// boxes, the back lines, the ball and its trail all follow without a line of their own.
const second = t => HALFTIME >= 0 && t >= HALFTIME;
const mx = (t, x) => second(t) ? LEN - x : x;
const my = (t, y) => second(t) ? WID - y : y;
const ownGoalIsLeft = (side, t) => (side === 0) !== second(t);

const px = (side, t, i) => mx(t, XY[side][(t*N + i)*2]);
const py = (side, t, i) => my(t, XY[side][(t*N + i)*2 + 1]);
const bx = t => mx(t, BALL[t*2]);
const by = t => my(t, BALL[t*2 + 1]);

function outfield(side, t) {
  const out = [];
  for (let i = 0; i < N; i++) if (i !== GK[side]) out.push([px(side,t,i), py(side,t,i)]);
  return out;
}

function shape(side, t) {
  const p = outfield(side, t);
  const xs = p.map(q => q[0]), ys = p.map(q => q[1]);

  // Depth is measured from the goal this side is defending, which after the interval is the
  // other one — so the back line reads as the back line in both halves.
  const left = ownGoalIsLeft(side, t);
  const depthOf = q => left ? q : LEN - q;
  const sorted = xs.map(depthOf).sort((a,b) => a-b);
  const meanBack = (sorted[0]+sorted[1]+sorted[2]+sorted[3])/4;
  return {
    minX: Math.min(...xs), maxX: Math.max(...xs),
    minY: Math.min(...ys), maxY: Math.max(...ys),
    width: (Math.max(...ys) - Math.min(...ys)) / 10,
    depth: (Math.max(...xs) - Math.min(...xs)) / 10,
    back: (sorted[3] - sorted[0]) / 10,
    backX: left ? meanBack : LEN - meanBack
  };
}

function pitch() {
  ctx.fillStyle = "#1f6b34"; ctx.fillRect(0, TOP, cv.width, cv.height - TOP);
  ctx.strokeStyle = "rgba(255,255,255,.65)"; ctx.lineWidth = 2;
  ctx.strokeRect(sx(0), sy(0), sx(LEN)-sx(0), sy(WID)-sy(0));
  ctx.beginPath(); ctx.moveTo(sx(LEN/2), sy(0)); ctx.lineTo(sx(LEN/2), sy(WID)); ctx.stroke();
  ctx.beginPath(); ctx.arc(sx(LEN/2), sy(WID/2), sx(91.5)-sx(0), 0, 7); ctx.stroke();
  for (const near of [true,false]) {
    const x0 = near ? 0 : LEN-165, x1 = near ? 165 : LEN;
    ctx.strokeRect(sx(x0), sy(WID/2-201), sx(x1)-sx(x0), sy(WID/2+201)-sy(WID/2-201));
    const g0 = near ? 0 : LEN-55, g1 = near ? 55 : LEN;
    ctx.strokeRect(sx(g0), sy(WID/2-91), sx(g1)-sx(g0), sy(WID/2+91)-sy(WID/2-91));
  }
}

// The clock, the half and the running score, drawn ON the pitch (a grey span in the page header is
// a number nobody finds while he is watching the football). Minutes AND seconds, because a frame is
// half a second and a match minute is 120 of them: without the seconds the number looks frozen.
function stamp(t) {
  const total = Math.floor(t * 60 / TPM);
  const mm = Math.floor(total / 60), ss = total % 60;
  return String(mm).padStart(2,"0") + ":" + String(ss).padStart(2,"0");
}

function scoreAt(t) {
  let h = 0, a = 0;
  for (let k = 0; k < ACTIONS.length; k += 4) {
    if (ACTIONS[k] > t || ACTIONS[k+1] !== GOAL_KIND) continue;
    ACTIONS[k+2] === 1 ? h++ : a++;
  }
  return [h, a];
}

function scoreboard(t) {
  const [h, a] = scoreAt(t);
  const line1 = SHORT[0] + "  " + h + " - " + a + "  " + SHORT[1];
  const line2 = second(t) ? "2nd half \u00b7 ends changed" : "1st half";

  ctx.fillStyle = "#12161e";
  ctx.fillRect(0, 0, cv.width, TOP);

  ctx.textAlign = "left";
  ctx.textBaseline = "middle";
  ctx.font = "bold 27px ui-monospace, Menlo, Consolas, monospace";
  ctx.fillStyle = "#fff";
  ctx.fillText(line1, PAD, TOP / 2 + 1);

  // The clock is the biggest number on the page, because it is the one you look for.
  ctx.font = "bold 30px ui-monospace, Menlo, Consolas, monospace";
  ctx.textAlign = "center";
  ctx.fillStyle = "#ffd166";
  ctx.fillText(stamp(t), cv.width / 2, TOP / 2 + 1);

  ctx.font = "600 15px ui-monospace, Menlo, Consolas, monospace";
  ctx.textAlign = "right";
  ctx.fillStyle = second(t) ? "#ffd166" : "rgba(255,255,255,.75)";
  ctx.fillText(line2, cv.width - PAD, TOP / 2 + 1);
  ctx.textBaseline = "alphabetic";
}

function caption(t) {
  let best = null;
  for (let a = 0; a < ACTIONS.length; a += 4)
    if (ACTIONS[a] <= t && (best === null || ACTIONS[a] >= ACTIONS[best])) best = a;
  if (best === null) return "";
  const side = ACTIONS[best+2] === 1 ? 0 : 1;
  const slot = ACTIONS[best+3];
  const kind = KINDS[ACTIONS[best+1]] || "?";
  return slot < 0 ? kind : kind + " - " + NAMES[side][slot];
}

function draw() {
  const t = tick;
  pitch();

  if (layers.trail) {
    ctx.strokeStyle = "rgba(255,255,255,.5)"; ctx.lineWidth = 2; ctx.beginPath();
    // The trail stops at the interval: a line drawn across the change of ends would be a
    // stripe across the pitch, which is the mirror and not the ball.
    const from = Math.max(0, HALFTIME >= 0 && t >= HALFTIME ? Math.max(t-24, HALFTIME) : t-24);
    for (let k = from; k <= t; k++) {
      const fx = sx(bx(k)), fy = sy(by(k));
      k === from ? ctx.moveTo(fx,fy) : ctx.lineTo(fx,fy);
    }
    ctx.stroke();
  }

  const sh = [shape(0,t), shape(1,t)];
  for (let side = 0; side < 2; side++) {
    const s = sh[side];
    if (layers.box) {
      ctx.strokeStyle = COLOR[side] + "88"; ctx.lineWidth = 1.5; ctx.setLineDash([6,5]);
      ctx.strokeRect(sx(s.minX), sy(s.minY), sx(s.maxX)-sx(s.minX), sy(s.maxY)-sy(s.minY));
      ctx.setLineDash([]);
    }
    if (layers.line) {
      ctx.strokeStyle = COLOR[side]; ctx.lineWidth = 2.5; ctx.globalAlpha = .55;
      ctx.beginPath(); ctx.moveTo(sx(s.backX), sy(0)); ctx.lineTo(sx(s.backX), sy(WID)); ctx.stroke();
      ctx.globalAlpha = 1;
    }
  }

  let marked = 0;
  for (let side = 0; side < 2; side++)
    for (let i = 0; i < N; i++) {
      if (i === GK[side]) continue;
      let bd = 1e9, bx = 0, by = 0;
      for (let j = 0; j < N; j++) {
        const dx = px(side,t,i)-px(1-side,t,j), dy = py(side,t,i)-py(1-side,t,j);
        const d = Math.hypot(dx,dy);
        if (d < bd) { bd = d; bx = px(1-side,t,j); by = py(1-side,t,j); }
      }
      if (bd <= 30) {
        marked++;
        if (layers.mark) {
          ctx.strokeStyle = "rgba(255,225,80,.85)"; ctx.lineWidth = 1.5;
          ctx.beginPath(); ctx.moveTo(sx(px(side,t,i)), sy(py(side,t,i))); ctx.lineTo(sx(bx), sy(by)); ctx.stroke();
        }
      }
    }

  const owner = OWNER[t];
  for (let side = 0; side < 2; side++)
    for (let i = 0; i < N; i++) {
      const cx = sx(px(side,t,i)), cy = sy(py(side,t,i));
      const code = (side === 0 ? 0 : N) + i + 1;
      if (owner === code) {
        ctx.strokeStyle = "#ffe14f"; ctx.lineWidth = 3;
        ctx.beginPath(); ctx.arc(cx, cy, 15, 0, 7); ctx.stroke();
      }
      ctx.fillStyle = i === GK[side] ? "#1a1a1a" : COLOR[side];
      ctx.beginPath(); ctx.arc(cx, cy, 10, 0, 7); ctx.fill();
      ctx.fillStyle = "#fff"; ctx.font = "bold 11px system-ui"; ctx.textAlign = "center";
      ctx.fillText(SHIRTS[side][i], cx, cy+4);
      if (layers.names) {
        ctx.fillStyle = "rgba(255,255,255,.85)"; ctx.font = "10px system-ui";
        ctx.fillText(NAMES[side][i].split(" (")[0], cx, cy-15);
      }
    }

  ctx.fillStyle = "#fff"; ctx.beginPath();
  ctx.arc(sx(bx(t)), sy(by(t)), 6, 0, 7); ctx.fill();
  ctx.strokeStyle = "#222"; ctx.lineWidth = 1.5; ctx.stroke();

  scoreboard(t);

  document.getElementById("clock").textContent =
    stamp(t) + (second(t) ? " \u00b7 2nd half, ends changed" : " \u00b7 1st half");
  document.getElementById("caption").textContent = caption(t);
  const set = (id,v) => document.getElementById(id).textContent = v.toFixed(1);
  set("hw", sh[0].width); set("hd", sh[0].depth); set("hb", sh[0].back);
  set("aw", sh[1].width); set("ad", sh[1].depth); set("ab", sh[1].back);
  document.getElementById("mk").textContent = marked;
  scrub.value = t;
}

// The stream is one frame per five match seconds, so "1x" here is the same 30x compression the
// client plays a replay at: twenty stream frames a second.
function frame(now) {
  if (playing && now - last >= 1000 / (20 * speed)) {
    last = now;
    tick = (tick + 1) % TICKS;
    draw();
  }
  requestAnimationFrame(frame);
}

document.getElementById("play").onclick = e => {
  playing = !playing; e.target.textContent = playing ? "Pause" : "Play";
};
scrub.oninput = () => { tick = +scrub.value; draw(); };
for (const b of document.querySelectorAll("[data-speed]"))
  b.onclick = () => {
    speed = +b.dataset.speed;
    document.querySelectorAll("[data-speed]").forEach(o => o.classList.toggle("on", o === b));
  };
for (const b of document.querySelectorAll("[data-layer]"))
  b.onclick = () => { layers[b.dataset.layer] = !layers[b.dataset.layer]; b.classList.toggle("on"); draw(); };
document.addEventListener("keydown", e => {
  if (e.key === "ArrowRight") { tick = Math.min(TICKS-1, tick+1); draw(); }
  if (e.key === "ArrowLeft") { tick = Math.max(0, tick-1); draw(); }
  if (e.key === " ") { e.preventDefault(); document.getElementById("play").click(); }
});

draw();
requestAnimationFrame(frame);
</script>
</body>
</html>
""";
}
