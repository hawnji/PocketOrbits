"""Normalise level JSON formatting (2-space indent, short arrays and small objects kept on one line)."""
import json, re, sys, glob, os

def compact(text):
    # Collapse arrays that contain only numbers.
    text = re.sub(r'\[\s*(-?[\d.eE+-]+(?:\s*,\s*-?[\d.eE+-]+)*)\s*\]', lambda m: '[' + ', '.join(x.strip() for x in m.group(1).split(',')) + ']', text)
    # Collapse small flat objects onto one line.
    def obj(m):
        inner = ' '.join(l.strip() for l in m.group(1).splitlines())
        return '{ ' + inner.strip() + ' }' if len(inner) < 150 else m.group(0)
    for _ in range(3):
        text = re.sub(r'\{\n((?:[ ]+"[^"\n]+": (?:"[^"\n]*"|[^\[\{\n]+|\[[^\[\]\n]*\]|\{[^{}\n]*\}),?\n)+)[ ]*\}', obj, text)
    return text

def main(paths):
    for p in paths:
        d = json.load(open(p))
        out = compact(json.dumps(d, indent=2)) + "\n"
        open(p, 'w').write(out)

if __name__ == "__main__":
    args = sys.argv[1:] or sorted(glob.glob(os.path.join(os.path.dirname(__file__), '../Assets/Game/Data/Resources/Levels/*.json')))
    main(args)
