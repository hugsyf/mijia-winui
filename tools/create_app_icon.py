"""Render the original, code-defined app mark at Windows icon sizes."""
from pathlib import Path
from PIL import Image, ImageDraw

dest = Path(__file__).resolve().parents[1] / "src/MiHome.WinUI/Assets"
dest.mkdir(exist_ok=True)
dest.joinpath("AppIcon.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
<rect x="12" y="12" width="232" height="232" rx="52" fill="#087F8C"/>
<path d="M54 119 128 57 202 119 187 137 184 134V194H142V153H114V194H72V134L69 137Z" fill="white"/>
<circle cx="184" cy="69" r="15" fill="#8FE4CB"/>
</svg>\n''', encoding="utf-8")
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(tuple(n * scale for n in (12, 12, 244, 244)), radius=52 * scale, fill="#087F8C")
points = [(54,119),(128,57),(202,119),(187,137),(184,134),(184,194),(142,194),(142,153),(114,153),(114,194),(72,194),(72,134),(69,137)]
draw.polygon([(x*scale,y*scale) for x,y in points], fill="white")
draw.ellipse(tuple(n * scale for n in (169,54,199,84)), fill="#8FE4CB")
image = image.resize((256,256), Image.Resampling.LANCZOS)
image.save(dest / "AppIcon.png")
image.save(dest / "AppIcon.ico", sizes=[(n,n) for n in (16,20,24,32,40,48,64,96,128,256)])
print("Created original SVG, PNG and ten-size Windows ICO")
