import numpy as np, json
from scipy import ndimage as nd
N=4097; SP=4000/4096
h=np.fromfile('/workspace/ash_ref/h.raw','<u2').reshape(N,N).astype(np.float32)/65535*1400
hs=nd.uniform_filter(h,9)
gy,gx=np.gradient(hs,SP); slope=np.degrees(np.arctan(np.hypot(gx,gy)))
def H(x,z): return float(h[int(round(z/SP)),int(round(x/SP))])
def S(x,z): return float(slope[int(round(z/SP)),int(round(x/SP))])
def search(cx,cz,rmin,rmax,score,step=6):
    best=None
    for r in np.arange(rmin,rmax+1,step):
        n=max(8,int(2*np.pi*r/step))
        for a in np.linspace(0,2*np.pi,n,endpoint=False):
            x=cx+np.cos(a)*r; z=cz+np.sin(a)*r
            if not(100<x<3900 and 100<z<3900): continue
            s=score(x,z)
            if s is not None and (best is None or s>best[0]): best=(s,x,z)
    return best
def local_prom(x,z,R=60):
    ring=[H(x+np.cos(a)*R,z+np.sin(a)*R) for a in np.linspace(0,2*np.pi,12,endpoint=False)]
    return H(x,z)-np.mean(ring)
def maxslope(x,z,r=5):
    return max(S(x+dx,z+dz) for dx in (-r,0,r) for dz in (-r,0,r))
cab=(522.12,1559.74)
pois={}
def put(pid,name,kind,x,z,note=''):
    pois[pid]=dict(id=pid,name=name,kind=kind,x=round(x,1),z=round(z,1),y=round(H(x,z),1),slope=round(maxslope(x,z),1),note=note)
put('redstone_camp','Redstone Camp','Camp',*cab)
# tutorial lookout: high, buildable, 50-170 m from cabin
b=search(cab[0],cab[1],55,170,lambda x,z: (H(x,z)+2*local_prom(x,z,40)) if maxslope(x,z,4)<16 else None)
put('redstone_lookout','Redstone Camp Lookout','Survey',b[1],b[2])
put('the_overlook','The Overlook','Landmark',539.53,2265.36)
def flat_near(cx,cz,r,prom_w=0.0,maxs=10):
    b=search(cx,cz,0,r,lambda x,z: (-maxslope(x,z,6)+prom_w*local_prom(x,z,50)) if maxslope(x,z,6)<maxs else None,step=10)
    if b is None: b=search(cx,cz,0,r,lambda x,z: -maxslope(x,z,6),step=10)
    return b[1],b[2]
put('ranger_station','Ranger Station','Survey',*flat_near(1170,1170,180,prom_w=0.6))
acc=np.load('acc_filled.npy'); A=lambda x,z: float(acc[min(512,int(z/7.8125)),min(512,int(x/7.8125))])
b=search(1600,800,0,260,lambda x,z: np.log(A(x,z))*10-3*maxslope(x,z,4)-0.02*np.hypot(x-1600,z-800) if A(x,z)>2500 else None,step=8)
put('creek_crossing','Creek Crossing','Crossing',b[1],b[2])
put('burned_homestead','Burned Homestead','Ruin',*flat_near(1760,700,160))
put('ash_hollow','Ash Hollow','Survey',*flat_near(2210,505,180))
put('coyote_flats','Coyote Flats','Survey',*flat_near(2690,1500,200))
b=search(200,935,0,200,lambda x,z: (S(x,z)*0.3 - maxslope(x,z,4)*0.0 + (10 if maxslope(x,z,4)<14 else -50)) ,step=10)
put('old_quarry','Old Quarry','Landmark',b[1],b[2])
b=search(160,330,0,220,lambda x,z: (H(x,z)+1.5*local_prom(x,z,50)) if maxslope(x,z,4)<16 else None,step=8)
put('lookout_ruin','Western Fire Lookout (ruin)','Survey',b[1],b[2])
for pid,name,(x,z) in [('cache_north','Hunting cabin (cache)',(2420,3020)),('cache_far_north','Line shack (cache)',(3320,3320)),('cache_gully','Washout cache',(1250,1850)),('cache_prairie','Windmill pump',(3150,900))]:
    put(pid,name,'Supply',*flat_near(x,z,150))
for p in pois.values(): print(p)
# creeks: trace receivers from acc
rec=np.load('rec_filled.npy'); n=513; T=2600
acc_f=acc.flatten(); strong=acc_f>=T
up_has=np.zeros(n*n,bool)
for i in np.nonzero(strong)[0]:
    r=rec[i]
    if r>=0: up_has[r]=True
heads=[i for i in np.nonzero(strong)[0] if not up_has[i]]
seen=set(); creeks=[]
for hd in sorted(heads,key=lambda i:-acc_f[i]):
    line=[]; i=hd
    while i>=0:
        r_,c_=divmod(i,n); line.append((c_*7.8125,r_*7.8125,acc_f[i]))
        if i in seen: break
        seen.add(i); i=rec[i]
    if len(line)>=6: creeks.append(line)
print('creeks',len(creeks),'pts',sum(len(c) for c in creeks))
json.dump({'pois':pois,'creeks':[[ [round(a,1),round(b,1),round(float(w),0)] for a,b,w in c] for c in creeks]},open('terrain_layout.json','w'))
