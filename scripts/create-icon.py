"""Add a continuation badge to the credited upstream icon. Requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json

root = Path(__file__).resolve().parent.parent
version = json.loads((root / 'manifest.json').read_text(encoding='utf-8'))['version_number']
image = Image.open(root / 'assets/icon-original.png').convert('RGB').resize((256, 256))
draw = ImageDraw.Draw(image)

def font(size):
    for name in ('arialbd.ttf', 'DejaVuSans-Bold.ttf'):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default(size=size)

draw.rectangle((0, 204, 256, 256), fill='#102d3a')
draw.rectangle((0, 204, 256, 207), fill='#efc36a')
draw.text((128, 222), 'CONTINUED', font=font(25), fill='#fff2d4', anchor='mm')
draw.text((128, 245), f'LEONYEW  /  {version}', font=font(12), fill='#9fd8cd', anchor='mm')
image.save(root / 'icon.png', optimize=True)
print(f'Created 256x256 Continued {version} icon.')
