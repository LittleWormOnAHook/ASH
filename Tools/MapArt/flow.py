import numpy as np, heapq, json
from scipy import ndimage as nd
N=4097; n=513; S=8
h=np.fromfile('/workspace/ash_ref/h.raw','<u2').reshape(N,N).astype(np.float64)/65535*1400
g=nd.uniform_filter(h,9)[::S,::S][:n,:n]
filled=g.copy(); done=np.zeros((n,n),bool); pq=[]
for r in range(n):
    for c in (0,n-1): heapq.heappush(pq,(g[r,c],r,c)); done[r,c]=True
for c in range(1,n-1):
    for r in (0,n-1): heapq.heappush(pq,(g[r,c],r,c)); done[r,c]=True
eps=1e-4; nb=[(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]
order=[]
rec=-np.ones(n*n,np.int64)
while pq:
    z,r,c=heapq.heappop(pq); order.append(r*n+c)
    for dr,dc in nb:
        rr,cc=r+dr,c+dc
        if 0<=rr<n and 0<=cc<n and not done[rr,cc]:
            done[rr,cc]=True; nz=max(g[rr,cc],z+eps); filled[rr,cc]=nz
            rec[rr*n+cc]=r*n+c   # flows toward the cell it was reached from (guaranteed drainage)
            heapq.heappush(pq,(nz,rr,cc))
# prefer steepest descent where the natural receiver is valid (lower filled)
F=filled.flatten()
for i in range(n*n):
    r,c=divmod(i,n); best=None; bd=0
    for dr,dc in nb:
        rr,cc=r+dr,c+dc
        if 0<=rr<n and 0<=cc<n:
            j=rr*n+cc; drop=(F[i]-F[j])/(1.4142 if dr and dc else 1)
            if drop>bd: bd=drop; best=j
    if best is not None: rec[i]=best
acc=np.ones(n*n)
for i in sorted(range(n*n),key=lambda i:-F[i]):
    if rec[i]>=0: acc[rec[i]]+=acc[i]
print('max acc',acc.max(),'sinks',((rec<0)).sum())
np.save('acc_filled.npy',acc.reshape(n,n)); np.save('rec_filled.npy',rec)
