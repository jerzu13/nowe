# Test of the in-game app (src/acapp.py) outside Assetto Corsa: a fake "ac" module, a recorded lap played back
# at 60 frames per second, checks of what the app shows and (with Pillow) a picture of a few frames.
#   python3 check_acapp.py <app folder with data\> <lap.csv> [preview.png]
import csv
import importlib.util
import os
import sys
import types

app_dir, lap_csv = sys.argv[1], sys.argv[2]
png = sys.argv[3] if len(sys.argv) > 3 else None
data = [f for f in os.listdir(os.path.join(app_dir, "data")) if f != "settings.txt" and f.endswith(".txt")][0]
car, track = data[:-4].split("__")[:2]


class FakeAc(object):
    def __init__(self):
        self.labels, self.quads, self.color, self.state, self.render = [], [], (1, 1, 1, 1), {}, None
        self.logs = []

    def __getattr__(self, name):           # every other ac call: accepted and ignored
        return lambda *a: None

    def newApp(self, name): return 1
    def log(self, s): self.logs.append(s)
    def getCarName(self, i): return car
    def getTrackName(self, i): return track
    def getTrackConfiguration(self, i): return ""

    def addLabel(self, app, text):
        self.labels.append({"text": text, "x": 0, "y": 0, "size": 12, "align": "left", "col": (1, 1, 1, 1), "vis": 1})
        return len(self.labels) - 1

    def setText(self, i, t): self.labels[i]["text"] = t
    def setPosition(self, i, x, y): self.labels[i]["x"], self.labels[i]["y"] = x, y
    def setFontSize(self, i, s): self.labels[i]["size"] = s
    def setFontAlignment(self, i, a): self.labels[i]["align"] = a
    def setFontColor(self, i, r, g, b, a): self.labels[i]["col"] = (r, g, b, a)
    def setVisible(self, i, v): self.labels[i]["vis"] = v
    def addRenderCallback(self, app, f): self.render = f
    def glColor4f(self, r, g, b, a): self.color = (r, g, b, a)
    def glQuad(self, x, y, w, h): self.quads.append((x, y, w, h, self.color))
    def getCarState(self, car_id, what): return self.state[what]


ac = FakeAc()
cs = types.SimpleNamespace(NormalizedSplinePosition="pos", SpeedKMH="speed", Gas="gas", Brake="brake", LapTime="t", LapCount="laps")
sys.modules["ac"] = ac
sys.modules["acsys"] = types.SimpleNamespace(CS=cs)
spec = importlib.util.spec_from_file_location("ACCoach", os.path.join(app_dir, "ACCoach.py"))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
assert m.acMain(1) == "AC Coach"
assert m.coach is not None, "no data loaded: " + str(ac.logs)

rows = []
with open(lap_csv) as f:
    next(f)
    for r in csv.DictReader(f):
        rows.append((int(r["t_ms"]), float(r["pos"]), float(r["speed"]), float(r["gas"]), float(r["brake"])))

frames, fails = [], []


def frame(t_ms, laps):
    # the sample at this time (laps are recorded at the game's frame rate, close enough)
    lo, hi = 0, len(rows) - 1
    while lo < hi:
        mid = (lo + hi) // 2
        if rows[mid][0] < t_ms: lo = mid + 1
        else: hi = mid
    t, pos, spd, gas, brk = rows[lo]
    ac.state.update({"pos": pos, "speed": spd, "gas": gas, "brake": brk, "t": t_ms, "laps": laps})
    ac.quads = []
    m.acUpdate(1 / 60.0)
    if ac.render: ac.render(1 / 60.0)
    lb = {k: dict(ac.labels[v]) for k, v in m.ui.items() if isinstance(v, int)}
    return {"t": t_ms, "d": pos * m.coach.d.length, "brake": brk, "gas": gas, "speed": spd, "labels": lb, "quads": list(ac.quads), "view": dict(m.view)}


# lap 0 (no delta: did not start at the line), then the same lap again as lap 1 (delta shown)
end = rows[-1][0]
for laps in (0, 1):
    t = 0
    while t < end:
        frames.append(frame(t, laps))
        t += 1000 // 60

counts = [f for f in frames if f["labels"]["count"]["text"].endswith(" m") and f["labels"]["count"]["text"][0] in "-0123456789"]
nums = [int(f["labels"]["count"]["text"].split()[0]) for f in counts]
results = [f["labels"]["count"]["text"] for f in frames if f["labels"]["count"]["text"].split()[:1] in (["OK"], ["EARLY"], ["LATE"])]
deltas = [f["view"]["delta"] for f in frames if f["view"]["delta"] is not None]
phases = set(f["labels"]["brk_txt"]["text"] for f in frames)
gas_txt = set(f["labels"]["gas_txt"]["text"] for f in frames)


def check(ok, what):
    print(("   OK: " if ok else "   FAIL: ") + what)
    if not ok: fails.append(what)


check(len(nums) > 50 and max(nums) >= 100, "countdown to the braking point (max %s m)" % (max(nums) if nums else "-"))
check(len(set(results)) >= 1, "verdict after the brake press: " + ", ".join(sorted(set(results))))
check({"BRAKE HARD", "EASE OFF", "LET GO"} <= phases, "brake phases: " + ", ".join(sorted(p for p in phases if p)))
check("FULL THROTTLE" in gas_txt, "throttle phases: " + ", ".join(sorted(p for p in gas_txt if p)))
lap0 = [f for f in frames[:len(frames) // 2] if f["view"]["delta"] is not None]
check(not lap0 and deltas and max(abs(x) for x in deltas) < 5, "delta only after crossing the line (%d frames, last %+.2f s)" % (len(deltas), deltas[-1] if deltas else 0))
check(not ac.logs or all("loaded" in s for s in ac.logs), "no errors in the log: " + "; ".join(ac.logs))

if png:
    from PIL import Image, ImageDraw, ImageFont
    font = lambda s: ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", int(s * 0.8))
    lap1 = frames[len(frames) // 2:]

    def pick(cond):
        for f in lap1:
            if cond(f): return f

    def metres(f):
        p = f["labels"]["count"]["text"].split()
        return int(p[0]) if len(p) == 2 and p[1] == "m" and p[0].lstrip("-").isdigit() else None

    shots = [
        pick(lambda f: metres(f) is not None and 80 <= metres(f) <= 120),
        pick(lambda f: metres(f) is not None and 0 <= metres(f) <= 20),
        pick(lambda f: f["labels"]["count"]["text"].split()[:1] in (["OK"], ["EARLY"], ["LATE"])),
        pick(lambda f: f["labels"]["brk_txt"]["text"] == "BRAKE HARD"),
        pick(lambda f: f["labels"]["brk_txt"]["text"] == "EASE OFF"),
        pick(lambda f: f["labels"]["brk_txt"]["text"] == "LET GO"),
        pick(lambda f: f["labels"]["gas_txt"]["text"] == "FEED IN"),
    ]
    shots = [s for s in shots if s]
    hgt = max(q[1] + q[3] for s in shots for q in s["quads"]) + 50
    img = Image.new("RGB", (len(shots) * (m.W + 20), hgt + 30), (60, 70, 60))
    dr = ImageDraw.Draw(img)
    for n, f in enumerate(shots):
        ox, oy = n * (m.W + 20) + 10, 10
        dr.rectangle([ox, oy, ox + m.W, oy + hgt - 20], fill=(30, 30, 30))
        for x, y, w, h, c in f["quads"]:
            dr.rectangle([ox + x, oy + y, ox + x + w, oy + y + h], fill=tuple(int(255 * v) for v in c[:3]))
        for k, lb in f["labels"].items():
            if not lb["text"] or not lb["vis"]: continue
            fn = font(lb["size"])
            for i, line in enumerate(lb["text"].split("\n")):
                w = dr.textlength(line, font=fn)
                x = lb["x"] - (w / 2 if lb["align"] == "center" else w if lb["align"] == "right" else 0)
                dr.text((ox + x, oy + lb["y"] + i * lb["size"]), line, font=fn, fill=tuple(int(255 * v) for v in lb["col"][:3]))
        dr.text((ox, hgt - 2), "%.0f m, %.0f km/h, brake %.0f%%" % (f["d"], f["speed"], f["brake"] * 100), fill=(220, 220, 220))
    img.save(png)
    print("   preview: " + png)

sys.exit(1 if fails else 0)
