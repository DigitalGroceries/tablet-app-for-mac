from PIL import Image, ImageDraw
o=Image.open('App-original.png').convert('RGBA'); n=Image.open('hero@1x.png').convert('RGBA'); n4=Image.open('hero@4x.png').convert('RGBA')
def checker(w,h,s=16):
    c=Image.new('RGBA',(w,h),(60,60,60,255)); d=ImageDraw.Draw(c)
    for y in range(0,h,s):
        for x in range(0,w,s):
            if (x//s+y//s)%2: d.rectangle([x,y,x+s-1,y+s-1],fill=(90,90,90,255))
    return c
S=4; box=90
def panel(img,crop,nearest,label):
    c=img.crop(crop).resize((box*S,box*S),Image.NEAREST if nearest else Image.LANCZOS)
    bg=checker(box*S,box*S); bg.alpha_composite(c)
    out=Image.new('RGBA',(box*S,box*S+30),(22,22,22,255)); out.paste(bg,(0,30)); ImageDraw.Draw(out).text((8,8),label,fill=(228,228,228,255)); return out
rows=[]
for name in ('top-left','bottom-right'):
    if name=='bottom-right':
        co=(o.width-box,o.height-box,o.width,o.height); cn=(n.width-box,n.height-box,n.width,n.height); c4=(n4.width-box*4,n4.height-box*4,n4.width,n4.height)
    else:
        co=(1,1,1+box,1+box); cn=(0,0,box,box); c4=(0,0,box*4,box*4)
    ps=[panel(o,co,True,f'Original {name}, 400%'),panel(n,cn,True,f'New @1x {name}, 400%'),panel(n4,c4,False,f'New @4x {name}, native')]
    row=Image.new('RGBA',(ps[0].width*3+40,ps[0].height),(22,22,22,255))
    for i,p in enumerate(ps): row.paste(p,(i*(p.width+20),0))
    rows.append(row)
# icon corner too (icon squircle top-left)
ps=[panel(o,(59,67,59+box,67+box),True,'Original icon corner, 400%'),panel(n,(58,66,58+box,66+box),True,'New icon corner @1x, 400%'),panel(n4,(232,264,232+box*4,264+box*4),False,'New icon corner @4x, native')]
row=Image.new('RGBA',(ps[0].width*3+40,ps[0].height),(22,22,22,255))
for i,p in enumerate(ps): row.paste(p,(i*(p.width+20),0))
rows.append(row)
tl=lambda im,off:im.crop((off,off,off+230,off+80)).resize((920,320),Image.NEAREST)
t=Image.new('RGBA',(920*2+20,350),(22,22,22,255)); t.paste(tl(o,1),(0,30)); t.paste(tl(n,0),(940,30)); d=ImageDraw.Draw(t); d.text((8,8),'Original traffic lights, 400%',fill=(228,228,228,255)); d.text((948,8),'New traffic lights, 400%',fill=(228,228,228,255))
H=sum(r.height+20 for r in rows)+t.height; W=max(rows[0].width,t.width)
out=Image.new('RGBA',(W,H),(22,22,22,255)); y=0
for r in rows: out.paste(r,(0,y)); y+=r.height+20
out.paste(t,(0,y)); out.convert('RGB').save('corner-comparison-400.png'); print(out.size)
