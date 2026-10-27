# AC Coach - in-game app for Assetto Corsa. Before a turn: one thing to fix there (from your last runs). Before the
# braking point: metres to it. Right after the turn: the time gained or lost and what was different from your best
# run. Inside the turn: nothing - all the attention goes to driving. A turn that goes well a few times goes quiet.
# AC Coach (the Windows program) copies this file to assettocorsa\apps\python\ACCoach\ACCoach.py and writes
# data\<car>__<track>.txt (turns, braking points, best lap), data\<car>__<track>.live.txt (tips, last result;
# rewritten after every turn) and data\settings.txt.
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

W = 340                     # app width, px
ROW_A, ROW_B, ROW_C = 50, 42, 32
LINE = 38                   # characters per line of the tip / result text
RESULT_S = 3.0              # seconds a result stays
TIP_S = 6.0                 # the tip appears this many seconds before the turn (at least TIP_MIN metres)
TIP_MIN = 250.0

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


def two_lines(text):
    # the tip / result text in at most two lines of LINE characters, cut at a space
    if len(text) <= LINE:
        return text
    cut = text.rfind(" ", 0, LINE + 1)
    if cut <= 0:
        cut = LINE
    rest = text[cut:].strip()
    return text[:cut].rstrip() + "\n" + (rest if len(rest) <= LINE else rest[:LINE - 2] + "..")


COLORS = {"record": (0.3, 0.95, 0.4), "good": (0.3, 0.9, 0.35), "even": (1, 1, 1), "bad": (1, 0.4, 0.3), "off": (1, 0.25, 0.2)}


class Data(object):
    """Turns, braking points and the best lap written by AC Coach for one car + track."""

    def __init__(self, path):
        self.turns = []             # (num, onset m, apex m)
        self.corners = []           # (num, brake m, apex m, speed at brake km/h, apex km/h, margin m, lead s)
        self.length = 0.0
        self.step = 2.0
        t, spd = [], []
        grid = False
        with open(path, encoding="utf-8") as f:
            for line in f:
                p = line.split()
                if not p or p[0].startswith("#"):
                    continue
                if grid:
                    t.append(float(p[0])); spd.append(float(p[1]))
                elif p[0] == "length":
                    self.length = float(p[1])
                elif p[0] == "turn":
                    self.turns.append((int(p[1]), float(p[2]), float(p[3])))
                elif p[0] == "corner":
                    v = [float(x) for x in p[1:8]]
                    self.corners.append((int(v[0]), v[1], v[2], v[3], v[4], v[5], v[6]))
                elif p[0] == "grid":
                    self.step = float(p[1])
                    grid = True
        self.t, self.spd = t, spd
        self.n = len(t)
        if self.n < 10 or self.length <= 0:
            raise ValueError("no grid in " + path)

    def idx(self, d):
        return int(round(d / self.step)) % self.n


class Live(object):
    """Tips and the last result written by AC Coach after every turn."""

    def __init__(self, path=None):
        self.seq = -1
        self.result = None          # (seq, num, kind, headline, detail)
        self.tips = {}              # turn number -> text
        if path is None:
            return
        with open(path, encoding="utf-8") as f:
            for line in f:
                p = line.rstrip("\r\n").split(" ", 1)
                if len(p) < 2:
                    continue
                if p[0] == "seq":
                    self.seq = int(p[1])
                elif p[0] == "tip":
                    q = p[1].split(" ", 1)
                    if len(q) == 2:
                        self.tips[int(q[0])] = q[1]
                elif p[0] == "result":
                    q = p[1].split(" ", 3)
                    if len(q) == 4:
                        head, _, detail = q[3].partition("|")
                        self.result = (int(q[0]), int(q[1]), q[2], head, detail)


class Coach(object):
    """Everything the app shows, from the car state; no AC calls, so it can be tested outside the game."""

    def __init__(self, data):
        self.d = data
        self.live = Live()
        self.seen = None            # result seq already shown (None = the first live file: its result is old)
        self.state = {}             # corner number -> [armed, braked, frozen point or None]
        self.result = None          # (headline, detail, colour, seconds left)
        self.last_laps = -1
        self.lap_ok = False         # the lap started at the line: the time to the best lap makes sense
        self.countdown_on = True    # the countdown is shown (it takes the place of a result)

    def set_live(self, live):
        self.live = live
        r = live.result
        if self.seen is not None and r is not None and r[0] != self.seen and r[0] == live.seq:
            self.result = (r[3], r[4], COLORS.get(r[2], (1, 1, 1)), RESULT_S)
        self.seen = live.seq

    def step(self, dt, pos, speed, brake, lap_ms, laps):
        dd = self.d
        L = dd.length
        if lap_ms < 5000 and pos > 0.5:
            pos = 0.0               # still the previous lap's end
        d = pos * L
        v = max(speed / 3.6, 5.0)
        if laps != self.last_laps:
            self.lap_ok = self.last_laps >= 0 and laps == self.last_laps + 1
            self.last_laps = laps
        out = {"corner": "", "count": "", "count_col": (1, 1, 1), "apex": "", "big": "", "big_col": (1, 1, 1),
               "text": "", "text_col": (1, 1, 1), "dist": None}

        # ---- metres to the braking point of the next corner, until the brake goes on ----
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
        counting = False
        if best is not None:
            c, s = best[1], best[2]
            num, b, apex, vb, vm, margin = c[0], c[1], c[2], c[3] / 3.6, c[4] / 3.6, c[5]
            if s[2] is None:
                # faster than the best run here -> brake earlier, slower -> later (braking distance ~ v^2),
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
                s[1] = True                     # braking: the countdown has done its job
            if not s[1] and dist <= window and dist > -150:
                counting = True
                out["corner"] = T("Z", "T") + str(num)
                out["apex"] = "%d km/h" % round(c[4])
                out["count"] = "%d m" % round(dist)
                out["dist"] = dist
                out["count_col"] = (1, 1, 1) if dist > 50 else (1, 0.85, 0.2) if dist > 0 else (1, 0.25, 0.2)

        # ---- the result of the turn just driven (a countdown takes its place) ----
        if self.result is not None:
            head, detail, col, left = self.result
            left -= dt
            self.result = (head, detail, col, left) if left > 0 and not (counting and self.countdown_on) else None
            if self.result is not None:
                out["big"], out["big_col"], out["text"] = head, col, two_lines(detail)

        # ---- one thing to fix in the next turn, on the straight before it ----
        if self.result is None:
            nxt = None
            for num, onset, apex in dd.turns:
                to = wrap(onset - d, L)
                if to > 0 and (nxt is None or to < nxt[0]):
                    nxt = (to, num)
            if nxt is not None and nxt[1] in self.live.tips and nxt[0] <= max(TIP_MIN, v * TIP_S) and brake < 0.1:
                out["text"] = two_lines(T("Z", "T") + str(nxt[1]) + ": " + self.live.tips[nxt[1]])
                out["text_col"] = (1, 0.85, 0.2)

        # ---- time to the best lap ----
        out["delta"] = None
        if self.lap_ok:
            delta = lap_ms / 1000.0 - dd.t[dd.idx(d)]
            if abs(delta) < 30:
                out["delta"] = delta
        return out


# ---------------------------------------------------------------- the app inside AC

app = None
ui = {}                     # label ids
cfg = {"countdown": True, "tips": True, "delta": True}
coach = None
data_file = None
mtimes = {}
check_timer = 0.0
live_timer = 0.0


def changed(f):
    # True when the file is there and new since the last look
    try:
        m = os.path.getmtime(f)
    except OSError:
        return False
    if mtimes.get(f) == m:
        return False
    mtimes[f] = m
    return True


def read_settings():
    global lang_pl
    f = os.path.join(DATA_DIR, "settings.txt")
    if not changed(f):
        return False
    with open(f, encoding="utf-8") as fh:
        for line in fh:
            p = line.split()
            if len(p) >= 2 and not p[0].startswith("#"):
                if p[0] == "lang":
                    lang_pl = p[1] == "pl"
                elif p[0] in cfg:
                    cfg[p[0]] = p[1] != "0"
    return True


def combo_name():
    name = safe_name(ac.getCarName(0)) + "__" + safe_name(ac.getTrackName(0))
    conf = ac.getTrackConfiguration(0)
    if conf:
        name += "__" + safe_name(conf)
    return os.path.join(DATA_DIR, name)


def load_data():
    global coach, data_file
    if data_file is None:
        data_file = combo_name()
    f = data_file + ".txt"
    if not os.path.exists(f):
        coach = None
        return
    if changed(f):
        try:
            dd = Data(f)
        except Exception as e:
            ac.log("AC Coach: cannot read " + f + ": " + str(e))
            return
        if coach is None:
            coach = Coach(dd)
            mtimes.pop(data_file + ".live.txt", None)   # read the tips now
        else:
            coach.d = dd            # keeps the lap state
        ac.log("AC Coach: loaded " + f)
    load_live()


def load_live():
    f = data_file + ".live.txt"
    if coach is None or not changed(f):
        return
    try:
        coach.set_live(Live(f))
    except Exception as e:
        ac.log("AC Coach: cannot read " + f + ": " + str(e))


def label(text, size, x, y, align="left"):
    lb = ac.addLabel(app, text)
    ac.setPosition(lb, x, y)
    ac.setFontSize(lb, size)
    ac.setFontAlignment(lb, align)
    return lb


def layout():
    y = 4
    for k in ("corner", "count", "apex", "big"):
        ac.setVisible(ui[k], 1 if cfg["countdown"] or (k == "big" and cfg["tips"]) else 0)
    ac.setPosition(ui["corner"], 8, y + 10)
    ac.setPosition(ui["count"], W // 2, y)
    ac.setPosition(ui["apex"], W - 8, y + 16)
    ac.setPosition(ui["big"], W // 2, y + 6)
    if cfg["countdown"] or cfg["tips"]:
        y += ROW_A
    ac.setVisible(ui["text"], 1 if cfg["tips"] else 0)
    ac.setPosition(ui["text"], W // 2, y)
    if cfg["tips"]:
        y += ROW_B
    ac.setVisible(ui["delta"], 1 if cfg["delta"] else 0)
    ac.setPosition(ui["delta"], W // 2, y)
    if cfg["delta"]:
        y += ROW_C
    ac.setPosition(ui["msg"], 8, 8)
    ac.setSize(app, W, max(y + 4, 60))


def acMain(ac_version):
    global app
    app = ac.newApp("AC Coach")
    ac.setTitle(app, "")
    ac.setIconPosition(app, 0, -10000)
    ac.drawBorder(app, 0)
    ac.setBackgroundOpacity(app, 0.45)
    ui["corner"] = label("", 20, 8, 14)
    ui["count"] = label("", 36, W // 2, 4, "center")
    ui["apex"] = label("", 15, W - 8, 20, "right")
    ui["big"] = label("", 26, W // 2, 10, "center")
    ui["text"] = label("", 16, W // 2, 54, "center")
    ui["delta"] = label("", 22, W // 2, 96, "center")
    ui["msg"] = label("", 15, 8, 8)
    read_settings()
    layout()
    load_data()
    return "AC Coach"


def show(k, text, col=None):
    ac.setText(ui[k], text)
    if col is not None:
        ac.setFontColor(ui[k], col[0], col[1], col[2], 1)


def acUpdate(delta_t):
    global check_timer, live_timer
    ac.setBackgroundOpacity(app, 0.45)       # AC resets it when the mouse leaves the app
    check_timer += delta_t
    live_timer += delta_t
    try:
        if check_timer > 1.0:
            check_timer = 0.0
            if read_settings():
                layout()
            load_data()
        elif live_timer > 0.25:              # tips and results come after every turn: look often
            live_timer = 0.0
            load_live()
    except Exception as e:
        ac.log("AC Coach: " + str(e))
    if coach is None:
        show("msg", T("AC Coach: brak danych dla tego auta i toru.\nPrzejedz czyste okrazenie z wlaczonym AC Coach.",
                      "AC Coach: no data for this car and track yet.\nDrive a clean lap with AC Coach running."))
        for k in ("corner", "count", "apex", "big", "text", "delta"):
            ac.setText(ui[k], "")
        return
    show("msg", "")
    coach.countdown_on = cfg["countdown"]
    try:
        view = coach.step(delta_t,
                          ac.getCarState(0, acsys.CS.NormalizedSplinePosition),
                          ac.getCarState(0, acsys.CS.SpeedKMH),
                          ac.getCarState(0, acsys.CS.Brake),
                          ac.getCarState(0, acsys.CS.LapTime),
                          ac.getCarState(0, acsys.CS.LapCount))
    except Exception as e:
        ac.log("AC Coach: " + str(e))
        return
    cd = cfg["countdown"]
    show("corner", view["corner"] if cd else "")
    show("apex", view["apex"] if cd else "")
    show("count", view["count"] if cd else "", view["count_col"])
    show("big", view["big"] if cfg["tips"] and not (cd and view["count"]) else "", view["big_col"])
    show("text", view["text"] if cfg["tips"] else "", view["text_col"] if not view["big"] else (0.85, 0.85, 0.85))
    dl = view["delta"]
    show("delta", "" if dl is None else "%+.2f" % dl, None if dl is None else (0.3, 0.9, 0.35) if dl <= 0 else (1, 0.35, 0.3))


def acShutdown():
    pass
