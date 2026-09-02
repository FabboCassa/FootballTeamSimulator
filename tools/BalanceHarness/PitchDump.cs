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
  <span class="muted">phase 0 replay dump &mdash; <span id="clock">0'</span></span>
  <span class="muted" id="caption"></span>
</header>
<main>
  <canvas id="pitch" width="1260" height="837"></canvas>

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
               "shot","save","GOAL","miss","corner","throw-in","goal kick","free kick"];
const COLOR = ["#e8375a", "#4f6df0"];

const TICKS = BALL.length / 2;
const cv = document.getElementById("pitch"), ctx = cv.getContext("2d");
const scrub = document.getElementById("scrub");
scrub.max = TICKS - 1;

const layers = { box:true, line:true, mark:true, trail:true, names:false };
let tick = 0, playing = true, speed = 1, last = 0;

const PAD = 30;
const sx = t => PAD + (cv.width - 2*PAD) * t / LEN;
const sy = t => PAD + (cv.height - 2*PAD) * t / WID;
const px = (side, t, i) => XY[side][(t*N + i)*2];
const py = (side, t, i) => XY[side][(t*N + i)*2 + 1];

function outfield(side, t) {
  const out = [];
  for (let i = 0; i < N; i++) if (i !== GK[side]) out.push([px(side,t,i), py(side,t,i)]);
  return out;
}

function shape(side, t) {
  const p = outfield(side, t);
  const xs = p.map(q => q[0]), ys = p.map(q => q[1]);
  const depthOf = q => side === 0 ? q : LEN - q;
  const sorted = xs.map(depthOf).sort((a,b) => a-b);
  return {
    minX: Math.min(...xs), maxX: Math.max(...xs),
    minY: Math.min(...ys), maxY: Math.max(...ys),
    width: (Math.max(...ys) - Math.min(...ys)) / 10,
    depth: (Math.max(...xs) - Math.min(...xs)) / 10,
    back: (sorted[3] - sorted[0]) / 10,
    backX: side === 0 ? (sorted[0]+sorted[1]+sorted[2]+sorted[3])/4
                      : LEN - (sorted[0]+sorted[1]+sorted[2]+sorted[3])/4
  };
}

function pitch() {
  ctx.fillStyle = "#1f6b34"; ctx.fillRect(0,0,cv.width,cv.height);
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

function caption(t) {
  let best = null;
  for (let a = 0; a < ACTIONS.length; a += 4)
    if (ACTIONS[a] <= t && (best === null || ACTIONS[a] >= ACTIONS[best])) best = a;
  if (best === null) return "";
  const side = ACTIONS[best+2] === 1 ? 0 : 1;
  return KINDS[ACTIONS[best+1]] + " - " + NAMES[side][ACTIONS[best+3]];
}

function draw() {
  const t = tick;
  pitch();

  if (layers.trail) {
    ctx.strokeStyle = "rgba(255,255,255,.5)"; ctx.lineWidth = 2; ctx.beginPath();
    for (let k = Math.max(0, t-24); k <= t; k++) {
      const fx = sx(BALL[k*2]), fy = sy(BALL[k*2+1]);
      k === Math.max(0, t-24) ? ctx.moveTo(fx,fy) : ctx.lineTo(fx,fy);
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
  ctx.arc(sx(BALL[t*2]), sy(BALL[t*2+1]), 6, 0, 7); ctx.fill();
  ctx.strokeStyle = "#222"; ctx.lineWidth = 1.5; ctx.stroke();

  document.getElementById("clock").textContent = Math.floor(t/TPM) + "'";
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
