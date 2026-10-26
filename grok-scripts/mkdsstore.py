# Writes the Finder layout (.DS_Store) for the install window: 128px icons, 16pt labels, dark grey background.
import sys
from ds_store import DSStore
from mac_alias import Bookmark  # noqa (import check)
p=sys.argv[1]
with DSStore.open(p,'w+') as d:
    d['.']['bwsp']={'WindowBounds':'{{200, 160}, {640, 400}}','ShowSidebar':False,'ShowToolbar':False,
        'ShowStatusBar':False,'ShowPathbar':False,'ShowTabView':False,'ContainerShowSidebar':False,'SidebarWidth':0}
    d['.']['icvp']={'viewOptionsVersion':1,'iconSize':128.0,'textSize':16.0,'gridSpacing':100.0,
        'arrangeBy':'none','labelOnBottom':True,'showIconPreview':True,'showItemInfo':False,
        'gridOffsetX':0.0,'gridOffsetY':0.0,'scrollPositionX':0.0,'scrollPositionY':0.0,
        'backgroundType':1,'backgroundColorRed':0.11,'backgroundColorGreen':0.11,'backgroundColorBlue':0.12}
    d['.']['vSrn']=('long',1)
    d['.']['icvl']=('type','icnv')
    d['Tablet App for Mac.app']['Iloc']=(170,170)
    d['Applications']['Iloc']=(470,170)
