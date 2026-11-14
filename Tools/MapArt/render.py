import numpy as np, json, math, random
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops
from scipy import ndimage as nd
random.seed(7); rng=np.random.default_rng(7)
W=4096; OUT=2048; N=4097
L=json.load(open('ASH_RegionLayout.json')); T=json.load(open('terrain_layout.json'))
def px(x,z): return (x/4000*W, (1-z/4000)*W)
h=np.fromfile('/workspace/ash_ref/h.raw','<u2').reshape(N,N).astype(np.float32)/65535*1400
h=h[::-1][:W,:W]; hsm=nd.gaussian_filter(h,2.0)
def noise(scale,seed):
    r=np.random.default_rng(seed); n=r.random((W//scale+2,W//scale+2)).astype(np.float32)
    return nd.zoom(n,scale,order=3)[:W,:W]
# --- paper
paper=np.zeros((W,W,3),np.float32); base=np.array([228,216,190],np.float32)
mott=noise(256,1)*0.6+noise(64,2)*0.3+noise(16,3)*0.1
grain=rng.normal(0,1,(W,W)).astype(np.float32); grain=nd.gaussian_filter(grain,0.7)
paper[:]=base; paper*= (0.93+0.09*mott)[...,None]; paper+= (grain*4)[...,None]
# --- biome washes (warped vertical boundaries)
X=np.arange(W)[None,:]/W*4000; warp=(noise(512,4)-0.5)*600+(noise(128,6)-0.5)*160
xw=X+warp
tints=[np.array([128,150,112]),np.array([176,172,112]),np.array([222,186,132])]
def sm(a,b,x,w=420): return np.clip((x-a)/w+0.5,0,1)
w1=sm(850,0,xw); w2=sm(2050,0,xw)
tint=(tints[0][None,None,:]*(1-w1)[...,None]+tints[1][None,None,:]*(w1-w2)[...,None]+tints[2][None,None,:]*w2[...,None])
wash=0.55+0.10*noise(128,5)
paper=paper*(1-wash[...,None])+ (paper*tint/235.0)*wash[...,None]
# --- hillshade
gy,gx=np.gradient(hsm,0.977); 
az=math.radians(315); alt=math.radians(40)
slp=np.arctan(np.hypot(gx,gy)*1.6); asp=np.arctan2(-gx,gy)
sh=np.sin(alt)*np.cos(slp)+np.cos(alt)*np.sin(slp)*np.cos(az-asp)
sh=np.clip(sh,0,1); paper*= (0.80+0.25*sh)[...,None]
# --- contours
def contour_mask(step):
    idx=np.floor(hsm/step); m=np.zeros_like(idx,bool)
    m[:-1,:]|=idx[:-1,:]!=idx[1:,:]; m[:,:-1]|=idx[:,:-1]!=idx[:,1:]; return m
minor=contour_mask(10); major=contour_mask(50); major=nd.binary_dilation(major,iterations=1)
ink_c=np.array([118,84,52],np.float32)
def inkit(mask,color,alpha):
    a=(mask.astype(np.float32)*alpha)[...,None]
    global paper; paper=paper*(1-a)+color*a
inkit(minor&~major,ink_c,0.38); inkit(major,ink_c,0.70)
img=Image.fromarray(np.clip(paper,0,255).astype(np.uint8)); d=ImageDraw.Draw(img,'RGBA')
def wobble(pts,amp=3.0,step=18):
    out=[]
    for (a,b),(c,e) in zip(pts[:-1],pts[1:]):
        n=max(1,int(math.hypot(c-a,e-b)/step))
        for i in range(n):
            t=i/n; out.append((a+(c-a)*t+random.uniform(-amp,amp)*0.5, b+(e-b)*t+random.uniform(-amp,amp)*0.5))
    out.append(pts[-1]); return out
def catmull(pts,seg=10):
    if len(pts)<3: return pts
    P=[pts[0]]+pts+[pts[-1]]; out=[]
    for i in range(1,len(P)-2):
        p0,p1,p2,p3=P[i-1],P[i],P[i+1],P[i+2]
        for k in range(seg):
            t=k/seg; t2=t*t; t3=t2*t
            out.append(tuple(0.5*((2*p1[j])+(-p0[j]+p2[j])*t+(2*p0[j]-5*p1[j]+4*p2[j]-p3[j])*t2+(-p0[j]+3*p1[j]-3*p2[j]+p3[j])*t3) for j in range(2)))
    out.append(P[-2]); return out
# --- creeks
for c in T['creeks']:
    sub=c[::3]
    if sub[-1] is not c[-1]: sub.append(c[-1])
    pts=[px(x,z) for x,z,a in sub]; ac=[a for _,_,a in sub]
    sm_=catmull(pts,6)
    for i in range(len(pts)-1):
        a=ac[i]; wdt=int(2+min(9,(math.log(max(a,1))-7.5)*2.2))
        seg_=sm_[i*6:(i+1)*6+1]
        if len(seg_)>1: d.line(wobble(seg_,7.0,14),fill=(62,92,128,200),width=max(3,wdt),joint='curve')
# --- grid
for k in range(0,4001,500):
    a=k/4000*W; col=(70,90,110,40 if k%1000 else 70)
    d.line([(a,0),(a,W)],fill=col,width=2 if k%1000 else 3); d.line([(0,a),(W,a)],fill=col,width=2 if k%1000 else 3)
# --- roads & trails
for r in L['roads']:
    pts=catmull([px(p['x'],p['z']) for p in r['pts']],12); wp=wobble(pts,2)
    d.line(wp,fill=(92,48,34,235),width=15,joint='curve'); d.line(wp,fill=(206,128,74,255),width=8,joint='curve')
def dashed(pts,dash=26,gap=18,width=6,col=(70,48,32,230)):
    dist=0; on=True; cur=[pts[0]]
    for (a,b),(c,e) in zip(pts[:-1],pts[1:]):
        L_=math.hypot(c-a,e-b); t=0
        while t<L_:
            rem=(dash if on else gap)-dist; step=min(rem,L_-t); t+=step; dist+=step
            p=(a+(c-a)*t/L_, b+(e-b)*t/L_)
            if on: cur.append(p)
            if dist>=(dash if on else gap):
                if on and len(cur)>1: d.line(cur,fill=col,width=width)
                on=not on; dist=0; cur=[p]
    if on and len(cur)>1: d.line(cur,fill=col,width=width)
for t in L['trails']:
    dashed(wobble(catmull([px(p['x'],p['z']) for p in t['pts']],12),2),width=6 if t['name'] else 5)
# --- fonts
F=lambda s,n='Kalam-Bold.ttf': ImageFont.truetype('/workspace/ash/fonts/'+n,s)
fC=lambda s: ImageFont.truetype('/workspace/ash/fonts/Caveat[wght].ttf',s)
def label(x,y,text,font,col=(48,34,24,255),anchor='mm',halo=True,rot=0):
    if rot:
        bb=d.textbbox((0,0),text,font=font); tw,th=bb[2]-bb[0]+20,bb[3]-bb[1]+30
        t=Image.new('RGBA',(tw,th),(0,0,0,0)); td=ImageDraw.Draw(t)
        if halo: td.text((tw/2,th/2),text,font=font,fill=(232,222,196,200),anchor='mm',stroke_width=6,stroke_fill=(232,222,196,170))
        td.text((tw/2,th/2),text,font=font,fill=col,anchor='mm'); t=t.rotate(rot,expand=True,resample=Image.BICUBIC)
        img.alpha_composite(t,(int(x-t.width/2),int(y-t.height/2))) if img.mode=='RGBA' else img.paste(t,(int(x-t.width/2),int(y-t.height/2)),t)
        return
    if halo: d.text((x,y),text,font=font,fill=(232,222,196,150),anchor=anchor,stroke_width=7,stroke_fill=(232,222,196,150))
    d.text((x,y),text,font=font,fill=col,anchor=anchor)
# road/trail names along a segment
def along(pts,i,text,font,col):
    (a,b),(c,e)=px(pts[i]['x'],pts[i]['z']),px(pts[i+1]['x'],pts[i+1]['z']); ang=-math.degrees(math.atan2(e-b,c-a))
    if ang>90: ang-=180
    if ang<-90: ang+=180
    label((a+c)/2,(b+e)/2-34,text,font,col,rot=ang)
R={r['id']:r for r in L['roads']+L['trails']}
along(R['county_road_south']['pts'],10,'OLD COUNTY ROAD',F(58),(110,50,30,255))
along(R['county_road_north']['pts'],5,'OLD COUNTY ROAD',F(58),(110,50,30,255))
along(R['mule_trail']['pts'],2,'Mule Trail',fC(78),(70,48,32,255))
along(R['descent_route']['pts'],4,'Descent Route',fC(78),(70,48,32,255))
along(R['ridge_trail']['pts'],4,'Ridge Trail',fC(78),(70,48,32,255))
# --- POIs
ink=(44,32,22,255); red=(150,40,28,255)
def tri(x,y,s,fill,out=ink):
    d.polygon([(x,y-s),(x-s*0.9,y+s*0.6),(x+s*0.9,y+s*0.6)],fill=fill,outline=out,width=5); d.ellipse([x-6,y-6,x+6,y+6],fill=out)
def house(x,y,s,fill=(70,52,38,255)):
    d.rectangle([x-s,y-s*0.5,x+s,y+s],fill=fill,outline=ink,width=4); d.polygon([(x-s*1.3,y-s*0.4),(x,y-s*1.4),(x+s*1.3,y-s*0.4)],fill=fill,outline=ink,width=4)
for p in L['pois']:
    x,y=px(p['x'],p['z']); k=p['kind']; nm=p['name']
    if k=='Camp':
        d.ellipse([x-30,y-30,x+30,y+30],outline=red,width=7); house(x,y,14,(150,40,28,255))
        label(x+40,y+70,'REDSTONE CAMP',F(70),red,anchor='lm'); label(x+40,y+124,'start',fC(56),red,anchor='lm')
    elif k=='Survey':
        tri(x,y,30,(214,184,120,255))
        off={'redstone_lookout':(-44,-10,'rm','Lookout tower'),'lookout_ruin':(44,14,'lm','Fire lookout (ruin)')}.get(p['id'],(40,6,'lm',nm))
        label(x+off[0],y+off[1],off[3],fC(70),ink,anchor=off[2])
    elif k=='Crossing':
        d.line([(x-26,y-12),(x+26,y-12)],fill=ink,width=6); d.line([(x-26,y+12),(x+26,y+12)],fill=ink,width=6); label(x,y+56,nm,fC(70))
    elif k=='Ruin':
        d.rectangle([x-18,y-18,x+18,y+18],outline=ink,width=6); d.line([(x-22,y+22),(x+22,y-22)],fill=red,width=5); label(x,y-56,nm,fC(70))
    elif k=='Landmark':
        if p['id']=='the_overlook':
            d.polygon([(x,y-34),(x-30,y+18),(x+30,y+18)],fill=(110,90,70,255),outline=ink,width=4); d.line([(x-12,y-12),(x,y-34),(x+12,y-12)],fill=(235,230,215,255),width=6)
            label(x,y-80,'THE OVERLOOK',F(64),ink)
        else:
            for a in range(0,360,45): d.line([(x,y),(x+28*math.cos(math.radians(a)),y+28*math.sin(math.radians(a)))],fill=ink,width=5)
            label(x+40,y,nm,fC(70),anchor='lm')
    elif k=='Supply':
        d.ellipse([x-15,y-15,x+15,y+15],fill=(222,180,52,255),outline=ink,width=4); label(x+28,y,nm,fC(56),anchor='lm')
# --- biome band labels
for b in L['biomes']:
    cx=(b['x0']+b['x1'])/2/4000*W
    label(cx,W-330,b['name'],F(64),(60,50,38,230)); label(cx,W-270,b['sub'],fC(54),(60,50,38,230))
# --- compass rose (top right) & scale bar & title
cx,cy=W-260,260
d.ellipse([cx-150,cy-150,cx+150,cy+150],outline=ink,width=4); d.ellipse([cx-138,cy-138,cx+138,cy+138],outline=(44,32,22,120),width=2)
for a,ln in [(0,140),(90,100),(180,100),(270,100),(45,60),(135,60),(225,60),(315,60)]:
    r=math.radians(a); tx,ty=cx+ln*math.sin(r),cy-ln*math.cos(r); nx,ny=math.cos(r)*14,math.sin(r)*14
    d.polygon([(tx,ty),(cx+nx,cy+ny),(cx-nx,cy-ny)],fill=red if a==0 else ink)
label(cx,cy-190,'N',F(70),red)
sx,sy=130,420; seg=500/4000*W
for i in range(2): d.rectangle([sx+i*seg,sy,sx+(i+1)*seg,sy+22],fill=ink if i%2==0 else (232,222,196,255),outline=ink,width=3)
label(sx,sy+60,'0',F(42),ink); label(sx+seg,sy+60,'500',F(42),ink); label(sx+2*seg,sy+60,'1 km',F(42),ink)
d.rectangle([90,90,980,330],fill=(232,222,196,200),outline=ink,width=4)
label(130,150,'ASH REGION',F(96),ink,'lm',halo=False); label(130,240,'field survey sheet · contours 10 m',fC(66),ink,'lm',halo=False); label(130,300,'hand-copied, not to be trusted',fC(50),(110,60,40,255),'lm',halo=False)
# --- age: stains, folds, edge burn
img=img.convert('RGB'); a=np.asarray(img).astype(np.float32)
yy,xx=np.mgrid[0:W,0:W]/W
for (sx_,sy_,r_) in []:
    dist=np.hypot(xx-sx_,yy-sy_); ring=np.exp(-((dist-r_)/0.004)**2)*0.10+np.clip(1-dist/r_,0,1)*0.04
    a*= (1-ring)[...,None]
for f in (0.5,):
    for axis in (0,1):
        dist=np.abs((xx if axis==0 else yy)-f); a*= (1-0.06*np.exp(-(dist/0.003)**2))[...,None]
edge=np.minimum(np.minimum(xx,1-xx),np.minimum(yy,1-yy)); burn=np.clip(1-edge/0.05,0,1)**2*(0.5+0.5*noise(64,9))
a*= (1-0.45*burn)[...,None]
img=Image.fromarray(np.clip(a,0,255).astype(np.uint8)).resize((OUT,OUT),Image.LANCZOS)
img.save('ASH_RegionMap.png',optimize=True); img.resize((1024,1024),Image.LANCZOS).save('preview.png'); print('done')
