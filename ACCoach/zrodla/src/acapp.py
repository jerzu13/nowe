# AC Coach - in-game app for Assetto Corsa: metres to the braking point, brake and throttle bars
# (how much to press and when to let go) and the time to the best lap.
# AC Coach (the Windows program) copies this file to assettocorsa\apps\python\ACCoach\ACCoach.py and writes
# data\<car>__<track>.txt (the ideal lap, rewritten as you get faster) and data\settings.txt.
# Changes made here are overwritten by AC Coach - edit src\acapp.py in its sources instead.
# The track map of the analysis replays the same logic (appSim in map_template.html): change both together.
# Python 3.3 (the one inside AC): no f-strings. Texts without Polish letters (the AC font may lack them).
import os

try:
    import ac
    import acsys
except ImportError:          # tests outside the game
    ac = None
    acsys = None

APP_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.path.join(APP_DIR, "data")

W = 320                     # app width, px
BAR_X, BAR_W, BAR_H = 34, 278, 24
ROW_CD, ROW_BAR, ROW_DELTA = 58, 30, 34

lang_pl = False


def T(pl, en):
    return pl if lang_pl else en


def safe_name(s):
    # the same as Lap.SafeName in AC Coach: characters Windows does not allow in file names -> _
    return "".join("_" if c in '<>:"/\\|?*' or ord(c) < 32 else c for c in s)


def wrap(x, length):
    # distance along a closed track, in (-length/2, length/2]
    x = x % length
    return x - length if x > length / 2 else x


class Data(object):
    """The ideal lap written by AC Coach for one car + track."""

    def __init__(self, path):
        self.corners = []           # (num, brake m, apex m, speed at brake km/h, apex km/h, margin m, lead s)
        self.length = 0.0
        self.lead = 0.15
        self.step = 2.0
        t, spd, gas, brk, ph = [], [], [], [], []
        grid = False
        with open(path, encoding="utf-8") as f:
            for line in f:
                p = line.split()
                if not p or p[0].startswith("#"):
                    continue
                if grid:
                    t.append(float(p[0])); spd.append(float(p[1])); gas.append(float(p[2]))
                    brk.append(float(p[3])); ph.append(float(p[4]))
                elif p[0] == "length":
                    self.length = float(p[1])
                elif p[0] == "lead":
                    self.lead = float(p[1])
                elif p[0] == "corner":
                    v = [float(x) for x in p[1:8]]
                    self.corners.append((int(v[0]), v[1], v[2], v[3], v[4], v[5], v[6]))
                elif p[0] == "grid":
                    self.step = float(p[1])
                    grid = True
        self.t, self.spd, self.gas, self.brk, self.ph = t, spd, gas, brk, ph
        self.n = len(t)
        if self.n < 10 or self.length <= 0:
            raise ValueError("no grid in " + path)

    def idx(self, d):
        return int(round(d / self.step)) % self.n


class Coach(object):
    """Everything the app shows, from the car state; no AC calls, so it can be tested outside the game."""

    def __init__(self, data):
        self.d = data
        self.state = {}             # corner number -> [armed, done, frozen point or None]
        self.result = None          # (text, colour, seconds left) after the brake press
        self.last_laps = -1
        self.lap_ok = False         # the lap started at the line: the time to the best lap makes sense

    def step(self, dt, pos, speed, gas, brake, lap_ms, laps):
        dd = self.d
        L = dd.length
        if lap_ms < 5000 and pos > 0.5:
            pos = 0.0               # still the previous lap's end
        d = pos * L
        v = max(speed / 3.6, 5.0)
        if laps != self.last_laps:
            self.lap_ok = self.last_laps >= 0 and laps == self.last_laps + 1
            self.last_laps = laps
        out = {"corner": "", "count": "", "count_col": (1, 1, 1), "apex": ""}

        # ---- metres to the braking point of the next corner ----
        best = None
        for c in dd.corners:
            num, b, apex = c[0], c[1], c[2]
            s = self.state.setdefault(num, [False, False, None])
            if wrap(d - apex, L) > 50:          # past this corner: ready for the next lap
                s[0] = s[1] = False
                s[2] = None
            to_apex = wrap(apex - d, L)
            if to_apex > 0 and (best is None or to_apex < best[0]):
                best = (to_apex, c, s)
        if best is not None:
            c, s = best[1], best[2]
            num, b, apex, vb, vm, margin = c[0], c[1], c[2], c[3] / 3.6, c[4] / 3.6, c[5]
            if s[2] is None:
                # faster than the ideal run here -> brake earlier, slower -> later (braking distance ~ v^2),
                # the same as the beeps of AC Coach
                ve = vb + v - dd.spd[dd.idx(d)] / 3.6
                f = 1.0
                if vb > vm + 3 and ve > vm + 3:
                    f = max(0.75, min(1.3, (ve * ve - vm * vm) / (vb * vb - vm * vm)))
                point = apex - (apex - b) * f - margin
            else:
                point = s[2]
            dist = wrap(point - d, L)
            window = max(100.0, v * 3.0)
            if dist <= 0 and s[2] is None:
                s[2] = point                    # the point stays put once you are past it
            if brake < 0.05 and dist < window + 200:
                s[0] = True
            if not s[1] and s[0] and brake > 0.1 and dist < window + 50:
                s[1] = True
                self.result = self.verdict(dist) + (2.0,)
            if not s[1] and dist <= window and dist > -150:
                out["corner"] = T("Z", "T") + str(num)
                out["apex"] = "%d km/h" % round(c[4])
                out["count"] = "%d m" % round(dist)
                out["count_col"] = (1, 1, 1) if dist > 50 else (1, 0.85, 0.2) if dist > 0 else (1, 0.25, 0.2)
        if self.result is not None:
            text, col, left = self.result
            left -= dt
            self.result = (text, col, left) if left > 0 else None
            if self.result is not None:
                out["count"], out["count_col"] = text, col

        # ---- pedals of the ideal run, a reaction time ahead ----
        i = dd.idx(d + v * dd.lead)
        out["brk"], out["gas"] = dd.brk[i], dd.gas[i]
        out["my_brk"], out["my_gas"] = brake, gas
        ph = dd.ph[i]
        if ph < 0:
            out["brk_col"], out["brk_txt"] = None, ""
        else:
            out["brk_col"] = ramp(ph)
            out["brk_txt"] = T("HAMUJ MOCNO", "BRAKE HARD") if ph >= 0.75 else T("ODPUSZCZAJ", "EASE OFF") if ph >= 0.25 else T("PUSC", "LET GO")
        g = dd.gas[i]
        later = dd.gas[(i + int(20 / dd.step)) % dd.n]
        if g >= 0.95:
            out["gas_col"], out["gas_txt"] = (0.2, 0.85, 0.3), T("PELNY GAZ", "FULL THROTTLE")
        elif g > 0.05:
            up = later >= g
            out["gas_col"] = (0.95, 0.85, 0.2) if up else (1, 0.55, 0.15)
            out["gas_txt"] = T("DODAWAJ", "FEED IN") if up else T("ODPUSC GAZ", "LIFT")
        else:
            out["gas_col"], out["gas_txt"] = None, ""

        # ---- time to the best lap ----
        out["delta"] = None
        if self.lap_ok:
            delta = lap_ms / 1000.0 - dd.t[dd.idx(d)]
            if abs(delta) < 30:
                out["delta"] = delta
        return out

    @staticmethod
    def verdict(dist):
        # dist at the brake press: + = braked before the point, - = after it
        m = abs(int(round(dist)))
        if m <= 5:
            return "OK", (0.2, 0.85, 0.3)
        col = (0.95, 0.85, 0.2) if m <= 15 else (1, 0.25, 0.2)
        return (T("WCZESNIE ", "EARLY ") if dist > 0 else T("POZNO ", "LATE ")) + "%d m" % m, col


def ramp(x):
    # 1 = red (hard), 0.5 = yellow, 0 = green (let go)
    if x >= 0.5:
        return (1.0, 0.85 - 0.6 * (x - 0.5) * 2, 0.15)
    return (0.3 + 0.7 * x * 2, 0.85, 0.2 + 0.1 * (1 - x * 2))


# ---------------------------------------------------------------- the app inside AC

app = None
ui = {}                     # label ids
bar_y = {}                  # top of the brake and throttle bars, px
cfg = {"countdown": True, "bars": True, "delta": True}
coach = None
data_file = None
data_mtime = 0
settings_mtime = 0
check_timer = 0.0
view = None


def read_settings():
    global lang_pl, settings_mtime
    f = os.path.join(DATA_DIR, "settings.txt")
    try:
        m = os.path.getmtime(f)
    except OSError:
        return False
    if m == settings_mtime:
        return False
    settings_mtime = m
    with open(f, encoding="utf-8") as fh:
        for line in fh:
            p = line.split()
            if len(p) >= 2 and not p[0].startswith("#"):
                if p[0] == "lang":
                    lang_pl = p[1] == "pl"
                elif p[0] in cfg:
                    cfg[p[0]] = p[1] != "0"
    return True


def combo_file():
    name = safe_name(ac.getCarName(0)) + "__" + safe_name(ac.getTrackName(0))
    conf = ac.getTrackConfiguration(0)
    if conf:
        name += "__" + safe_name(conf)
    return os.path.join(DATA_DIR, name + ".txt")


def load_data():
    global coach, data_file, data_mtime
    if data_file is None:
        data_file = combo_file()
    try:
        m = os.path.getmtime(data_file)
    except OSError:
        if coach is not None:
            coach = None
        return
    if m == data_mtime:
        return
    try:
        dd = Data(data_file)
    except Exception as e:
        ac.log("AC Coach: cannot read " + data_file + ": " + str(e))
        return
    data_mtime = m
    if coach is None:
        coach = Coach(dd)
    else:
        coach.d = dd            # keeps the lap state
    ac.log("AC Coach: loaded " + data_file)


def label(text, size, x, y, align="left"):
    lb = ac.addLabel(app, text)
    ac.setPosition(lb, x, y)
    ac.setFontSize(lb, size)
    ac.setFontAlignment(lb, align)
    return lb


def layout():
    y = 4
    show = lambda lb, on: ac.setVisible(ui[lb], 1 if on else 0)
    for k in ("corner", "count", "apex"):
        show(k, cfg["countdown"])
    ac.setPosition(ui["corner"], 8, y + 6)
    ac.setPosition(ui["count"], W // 2, y)
    ac.setPosition(ui["apex"], W - 8, y + 12)
    if cfg["countdown"]:
        y += ROW_CD
    bar_y["brk"], bar_y["gas"] = y, y + ROW_BAR
    for k in ("b", "g", "brk_txt", "gas_txt"):
        show(k, cfg["bars"])
    if cfg["bars"]:
        ac.setPosition(ui["b"], 10, y + 2)
        ac.setPosition(ui["brk_txt"], BAR_X + BAR_W // 2, y + 3)
        ac.setPosition(ui["g"], 10, y + ROW_BAR + 2)
        ac.setPosition(ui["gas_txt"], BAR_X + BAR_W // 2, y + ROW_BAR + 3)
        y += 2 * ROW_BAR
    show("delta", cfg["delta"])
    ac.setPosition(ui["delta"], W // 2, y)
    if cfg["delta"]:
        y += ROW_DELTA
    ac.setPosition(ui["msg"], 8, 8)
    ac.setSize(app, W, max(y + 4, 60))


def acMain(ac_version):
    global app
    app = ac.newApp("AC Coach")
    ac.setTitle(app, "")
    ac.setIconPosition(app, 0, -10000)
    ac.drawBorder(app, 0)
    ac.setBackgroundOpacity(app, 0.45)
    ui["corner"] = label("", 20, 8, 10)
    ui["count"] = label("", 40, W // 2, 4, "center")
    ui["apex"] = label("", 15, W - 8, 16, "right")
    ui["b"] = label("B", 17, 10, 0)
    ui["g"] = label("G", 17, 10, 0)
    ui["brk_txt"] = label("", 15, 0, 0, "center")
    ui["gas_txt"] = label("", 15, 0, 0, "center")
    ui["delta"] = label("", 26, W // 2, 0, "center")
    ui["msg"] = label("", 15, 8, 8)
    read_settings()
    layout()
    ac.addRenderCallback(app, on_render)
    load_data()
    return "AC Coach"


def acUpdate(delta_t):
    global check_timer, view
    ac.setBackgroundOpacity(app, 0.45)       # AC resets it when the mouse leaves the app
    check_timer += delta_t
    if check_timer > 1.0:
        check_timer = 0.0
        try:
            if read_settings():
                layout()
            load_data()
        except Exception as e:
            ac.log("AC Coach: " + str(e))
    if coach is None:
        view = None
        ac.setText(ui["msg"], T("AC Coach: brak danych dla tego auta i toru.\nPrzejedz czyste okrazenie z wlaczonym AC Coach.",
                                "AC Coach: no data for this car and track yet.\nDrive a clean lap with AC Coach running."))
        for k in ("corner", "count", "apex", "brk_txt", "gas_txt", "delta", "b", "g"):
            ac.setText(ui[k], "")
        return
    ac.setText(ui["msg"], "")
    ac.setText(ui["b"], "B")
    ac.setText(ui["g"], "G")
    try:
        view = coach.step(delta_t,
                          ac.getCarState(0, acsys.CS.NormalizedSplinePosition),
                          ac.getCarState(0, acsys.CS.SpeedKMH),
                          ac.getCarState(0, acsys.CS.Gas),
                          ac.getCarState(0, acsys.CS.Brake),
                          ac.getCarState(0, acsys.CS.LapTime),
                          ac.getCarState(0, acsys.CS.LapCount))
    except Exception as e:
        ac.log("AC Coach: " + str(e))
        view = None
        return
    ac.setText(ui["corner"], view["corner"])
    ac.setText(ui["apex"], view["apex"])
    ac.setText(ui["count"], view["count"])
    r, g, b = view["count_col"]
    ac.setFontColor(ui["count"], r, g, b, 1)
    for k, t in (("brk", "brk_txt"), ("gas", "gas_txt")):
        ac.setText(ui[t], view[t])
        dark = view[k + "_col"] is not None and view[k] > 0.55    # the coloured fill is under the text
        ac.setFontColor(ui[t], *((0.05, 0.05, 0.05, 1) if dark else (1, 1, 1, 1)))
    dl = view["delta"]
    ac.setText(ui["delta"], "" if dl is None else "%+.2f" % dl)
    if dl is not None:
        ac.setFontColor(ui["delta"], *((0.3, 0.9, 0.35, 1) if dl <= 0 else (1, 0.35, 0.3, 1)))


def bar(y, target, mine, col):
    ac.glColor4f(0.12, 0.12, 0.12, 0.85)
    ac.glQuad(BAR_X, y, BAR_W, BAR_H)
    if col is not None and target > 0.01:
        ac.glColor4f(col[0], col[1], col[2], 0.9)
        ac.glQuad(BAR_X, y, BAR_W * min(1.0, target), BAR_H)
    elif col is not None:
        ac.glColor4f(col[0], col[1], col[2], 0.9)       # "let go": a green frame
        ac.glQuad(BAR_X, y, BAR_W, 3)
        ac.glQuad(BAR_X, y + BAR_H - 3, BAR_W, 3)
    ac.glColor4f(1, 1, 1, 1)                             # your pedal
    x = BAR_X + BAR_W * max(0.0, min(1.0, mine))
    ac.glQuad(min(x, BAR_X + BAR_W - 3), y - 3, 3, BAR_H + 6)


def on_render(delta_t):
    if view is None or not cfg["bars"]:
        return
    bar(bar_y["brk"], view["brk"], view["my_brk"], view["brk_col"])
    bar(bar_y["gas"], view["gas"], view["my_gas"], view["gas_col"])


def acShutdown():
    pass
