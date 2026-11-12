"""11-12 사용자 "내가 직접 수정 가능하게": 옆 걷기(walk_west) 도트 편집기 서버.
- http://localhost:8765 에서 editor.html
- 편집 대상: 바탕화면 gpt 그림/고퀄/자동/_work/walkside_res/<세트>/walk_west_N.png (저장하면 walk_east_N.png = 좌우 뒤집기도 같이)
- 참고(읽기만): 게임 폴더 Assets/Resources/Sprites/<세트>/idle_west_0.png, 처음 상태: _work/walkside_orig
사용: python server.py  (창 닫으면 꺼짐)"""
import os, json, io, base64
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, unquote
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.abspath(os.path.join(HERE, "..", "..", "..", "Assets", "Resources", "Sprites"))
WORK = os.path.join(os.path.expanduser("~"), "OneDrive", "Desktop", "gpt 그림", "고퀄", "자동", "_work")
EDIT = os.environ.get("PE_EDIT") or os.path.join(WORK, "walkside_res")
ORIG = os.environ.get("PE_ORIG") or os.path.join(WORK, "walkside_orig")
SETS = [("HeroHQ", "일반"), ("HeroHQ1", "고급"), ("HeroHQ2", "희귀"), ("HeroHQ3", "영웅"), ("HeroHQ4", "전설"),
        ("HeroHQ5", "신화"), ("HeroHQ6", "고대"), ("HeroHQ7", "초월"), ("HeroHQ8", "영원"), ("HeroHQ9", "Unknown")]
NAMES = {s for s, _ in SETS}


def safe(set_, name):
    if set_ not in NAMES or not (name.startswith(("walk_west_", "idle_west_")) and name.endswith(".png")) or "/" in name or "\\" in name or ".." in name:
        raise ValueError("bad path")


class H(BaseHTTPRequestHandler):
    def _send(self, code, body, ctype):
        self.send_response(code); self.send_header("Content-Type", ctype); self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

    def log_message(self, *a):
        pass

    def do_GET(self):
        p = unquote(urlparse(self.path).path)
        try:
            if p in ("/", "/editor.html"):
                return self._send(200, open(os.path.join(HERE, "editor.html"), "rb").read(), "text/html; charset=utf-8")
            if p == "/sets":
                return self._send(200, json.dumps(SETS, ensure_ascii=False).encode("utf-8"), "application/json")
            parts = p.strip("/").split("/")
            if len(parts) == 3 and parts[0] in ("edit", "orig", "game"):
                kind, set_, name = parts; safe(set_, name)
                base = {"edit": EDIT, "orig": ORIG, "game": GAME}[kind]
                f = os.path.join(base, set_, name)
                if not os.path.exists(f): return self._send(404, b"", "text/plain")
                return self._send(200, open(f, "rb").read(), "image/png")
        except Exception as e:
            return self._send(400, str(e).encode("utf-8"), "text/plain")
        self._send(404, b"", "text/plain")

    def do_POST(self):
        p = unquote(urlparse(self.path).path)
        try:
            parts = p.strip("/").split("/")
            if len(parts) == 3 and parts[0] == "save":
                _, set_, name = parts; safe(set_, name)
                if not name.startswith("walk_west_"): raise ValueError("walk_west만 저장")
                n = int(self.headers.get("Content-Length", 0))
                data = json.loads(self.rfile.read(n))["png"].split(",", 1)[1]
                im = Image.open(io.BytesIO(base64.b64decode(data))).convert("RGBA")
                if im.size != (64, 64): raise ValueError("64x64 아님")
                d = os.path.join(EDIT, set_)
                im.save(os.path.join(d, name))
                im.transpose(Image.FLIP_LEFT_RIGHT).save(os.path.join(d, name.replace("walk_west_", "walk_east_")))
                return self._send(200, b"ok", "text/plain")
        except Exception as e:
            return self._send(400, str(e).encode("utf-8"), "text/plain")
        self._send(404, b"", "text/plain")


if __name__ == "__main__":
    print("도트 편집기: http://localhost:8765  (편집 폴더:", EDIT, ")")
    HTTPServer(("127.0.0.1", 8765), H).serve_forever()
