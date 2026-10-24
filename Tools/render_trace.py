"""Render a LevelLab trace JSON to a PNG for quick visual review of level layouts and solution paths."""
import json, sys
from PIL import Image, ImageDraw

def main(src, dst, scale=48):
    d = json.load(open(src))
    minx, miny, maxx, maxy = d["bounds"]
    W, H = int((maxx - minx) * scale), int((maxy - miny) * scale)
    img = Image.new("RGB", (W, H), (14, 16, 40))
    g = ImageDraw.Draw(img, "RGBA")
    X = lambda x: (x - minx) * scale
    Y = lambda y: (maxy - y) * scale
    def circle(x, y, r, **kw):
        g.ellipse([X(x) - r * scale, Y(y) - r * scale, X(x) + r * scale, Y(y) + r * scale], **kw)
    for s in d["sources"]:
        for r in s["regions"]:
            if "rect" in r:
                a, b, c, e = r["rect"]; g.rectangle([X(a), Y(e), X(c), Y(b)], fill=(90, 140, 255, 30), outline=(90, 140, 255, 90))
            else:
                x, y, rr = r["circle"]; circle(x, y, rr, fill=(90, 140, 255, 30), outline=(90, 140, 255, 90))
    for t in d["targets"]:
        x, y = t["pos"]
        if t["kind"] == "OrbitRing":
            circle(x, y, t["radius"] + t["tolerance"], outline=(80, 255, 170, 200), width=2)
            circle(x, y, t["radius"] - t["tolerance"], outline=(80, 255, 170, 200), width=2)
        else:
            circle(x, y, t["radius"], outline=(80, 255, 170, 230), width=3)
        g.text((X(x) - 3, Y(y) - 6), str(t["order"]), fill=(80, 255, 170))
    for h in d["hazards"]:
        circle(*h["pos"], h["radius"], fill=(130, 110, 100))
    for s in d["sources"]:
        col = (255, 210, 90) if s["kind"] == "Star" else (40, 0, 60) if s["kind"] == "BlackHole" else (120, 200, 255)
        circle(*s["pos"], s["radius"], fill=col, outline=(255, 255, 255))
    for p in d["planets"]:
        circle(*p["pos"], p["radius"], fill=(90, 200, 120))
    for path in d["idlePath"]:
        g.line([(X(x), Y(y)) for x, y in path], fill=(255, 90, 90, 140), width=1)
    for step in d["solution"]:
        if isinstance(step, dict):
            circle(*step["pos"], 0.25, outline=(255, 255, 255), width=3)
        else:
            for path in step:
                if len(path) > 1:
                    g.line([(X(x), Y(y)) for x, y in path], fill=(255, 255, 255, 220), width=2)
    g.text((6, 6), d["id"] + "  " + d["result"], fill=(255, 255, 255))
    img.save(dst)

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
