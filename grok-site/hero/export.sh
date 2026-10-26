#!/bin/sh
# Render hero.svg to transparent PNG at 1x/2x/4x with headless Chrome (fresh profile), then WebP.
set -e
cd /workspace/otaE/site/hero
for s in 1 2 4; do rm -rf /tmp/chrh; google-chrome --headless=new --user-data-dir=/tmp/chrh --no-sandbox --disable-gpu --hide-scrollbars --allow-file-access-from-files --default-background-color=00000000 --force-device-scale-factor=$s --window-size=950,1002 --screenshot=hero@${s}x.png file:///tmp/hero.html 2>/dev/null; done
python3 -c "
from PIL import Image
for s in (1,2,4): Image.open('hero@%dx.png'%s).save('hero@%dx.webp'%s,quality=92,method=6)
print([Image.open('hero@%dx.png'%s).size for s in (1,2,4)])"
