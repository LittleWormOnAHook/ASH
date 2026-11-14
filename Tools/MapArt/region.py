import json
T=json.load(open('terrain_layout.json')); P={k:(v['x'],v['z']) for k,v in T['pois'].items()}
rc,rl,ov,rs,cc,bh,ah,cf,oq,lr=[P[k] for k in ['redstone_camp','redstone_lookout','the_overlook','ranger_station','creek_crossing','burned_homestead','ash_hollow','coyote_flats','old_quarry','lookout_ruin']]
roads=[
 dict(id='county_road_south',name='Old County Road',kind='road',pts=[rc,(700,1480),(950,1330),rs,(1470,1060),cc,(1730,790),bh,(2000,560),ah,(2900,480),(3500,330),(3990,250)]),
 dict(id='county_road_north',name='Old County Road',kind='road',pts=[ah,(2450,900),(2560,1200),cf,(2800,1900),(3100,2500),(3400,3100),(3700,3600),(3850,3990)]),
]
trails=[
 dict(id='mule_trail',name='Mule Trail',kind='trail',pts=[rc,(575,1720),(640,1950),(610,2150),ov]),
 dict(id='descent_route',name='Descent Route',kind='trail',pts=[rl,(650,1400),(900,1150),(1150,950),(1300,700),(1450,420),(1700,200),(1950,120)]),
 dict(id='ridge_trail',name='Ridge Trail',kind='trail',pts=[rc,(430,1380),(340,1180),oq,(240,700),(260,420),lr]),
 dict(id='washout_path',name='',kind='trail',pts=[(575,1720),(850,1800),P['cache_gully']]),
 dict(id='north_track',name='',kind='trail',pts=[(2800,1900),(2600,2500),P['cache_north']]),
 dict(id='shack_spur',name='',kind='trail',pts=[(3400,3100),P['cache_far_north']]),
 dict(id='windmill_spur',name='',kind='trail',pts=[(2900,480),(3050,640),P['cache_prairie']]),
]
R={r['id']:r for r in roads+trails}
def seg(rid,a=0,b=None,w=150): pts=R[rid]['pts'][a:b]; return dict(name=rid,points=[list(p) for p in pts],width=w)
def circ(p,r): return dict(center=list(p),radius=r)
surveys=[
 dict(id='s1_redstone',order=1,name='Redstone Camp Lookout',structure='Lookout tower',kind='TopoPanel',poi='redstone_lookout',
      circles=[circ(rc,420),circ(rs,220),circ(ov,200)],paths=[seg('county_road_south',0,4,170),seg('mule_trail',w=150),seg('descent_route',0,4,150)],
      unlocks=['ranger_station','the_overlook'],
      journal='From the tower the camp makes sense: the mule trail climbs north to The Overlook, and the county road drops east through the gullies to an old ranger station. A descent trail cuts down the draw below the tower.',
      next='Ranger Station, east down the county road (about 850 m).'),
 dict(id='s2_ranger',order=2,name='Ranger Station Roof',structure='Ranger station roof',kind='SurveyMarker',poi='ranger_station',
      circles=[circ(rs,650),circ(cc,220),circ(bh,220),circ(ah,260)],paths=[seg('county_road_south',3,10,220),seg('descent_route',w=170)],
      unlocks=['creek_crossing','burned_homestead','ash_hollow'],
      journal='The survey disc on the roof still has its bearings. The creek crossing and a burned homestead lie south-east, and past them the road runs on to Ash Hollow, where a water tower stands above the flats.',
      next='Ash Hollow water tower, south-east along the county road.'),
 dict(id='s3_ash_hollow',order=3,name='Ash Hollow Water Tower',structure='Water tower',kind='TrailRegister',poi='ash_hollow',
      circles=[circ(ah,750),circ(cf,320),circ(P['cache_prairie'],220)],paths=[seg('county_road_south',9,None,260),seg('county_road_north',0,4,260),seg('windmill_spur',w=160)],
      unlocks=['coyote_flats','cache_prairie'],
      journal='From the tank catwalk you can see the county road run east out of the region, and a radio mast standing over Coyote Flats to the north. A windmill pump turns out on the prairie.',
      next='Coyote Flats radio mast, north along the county road.'),
 dict(id='s4_coyote',order=4,name='Coyote Flats Radio Mast',structure='Radio relay hut',kind='RadioRelay',poi='coyote_flats',
      circles=[circ(cf,950),circ(P['cache_north'],260),circ(P['cache_far_north'],260),circ(lr,180),circ(oq,200)],paths=[seg('county_road_north',2,None,300),seg('north_track',w=180),seg('shack_spur',w=180)],
      unlocks=['cache_north','cache_far_north','lookout_ruin','old_quarry'],
      journal='The relay log names a hunting cabin and a line shack to the north, both stocked before the ash came. Far to the west, on the highest ridge, the old fire lookout still catches the light.',
      next='The western fire lookout ruin, high on the far ridges (late).'),
 dict(id='s5_lookout_ruin',order=5,name='Western Fire Lookout',structure='Fire lookout ruin',kind='SurveyMarker',poi='lookout_ruin',
      circles=[],paths=[],unlocks=[k for k in P],revealAll=True,
      journal='The whole country lies open from up here: every ridge trail, every road out. The region map is complete.',
      next='Choose your way out.'),
]
def XZ(p): return dict(x=round(p[0],1),z=round(p[1],1))
pois=[dict(id=k,name=v['name'],kind=v['kind'],x=round(v['x'],1),z=round(v['z'],1)) for k,v in T['pois'].items()]
def route(r): return dict(id=r['id'],name=r['name'],kind=r['kind'],pts=[XZ(p) for p in r['pts']])
def surv(s):
    return dict(id=s['id'],order=s['order'],name=s['name'],structure=s['structure'],kind=s['kind'],poi=s['poi'],
        circles=[dict(x=round(c['center'][0],1),z=round(c['center'][1],1),r=c['radius']) for c in s['circles']],
        paths=[dict(name=p['name'],width=p['width'],pts=[XZ(q) for q in p['points']]) for p in s['paths']],
        unlocks=s['unlocks'],journal=s['journal'],next=s['next'],revealAll=bool(s.get('revealAll',False)))
region=dict(regionName='Ash Region',worldMinX=0,worldMinZ=0,worldSize=4000,startRevealRadius=140,walkRevealRadius=30,gpsUncertainty=70,gpsDrift=25,
            creeks=[dict(acc=round(float(c[-1][2])),pts=[XZ((p[0],p[1])) for p in (c[::3]+[c[-1]])]) for c in T['creeks']],
            pois=pois,roads=[route(r) for r in roads],trails=[route(r) for r in trails],surveys=[surv(s) for s in surveys],
            biomes=[dict(name='WESTERN RIDGES',sub='stone / timber / exposure',x0=0,x1=850),dict(name='GULLIES',sub='water / cover / foraging',x0=850,x1=2050),dict(name='PRAIRIE',sub='travel / visibility / wind',x0=2050,x1=4000)],
            provenance="Procedural fallback terrain (not real DEM data). POIs fitted to the terrain from Anthony's map draft layout.")
json.dump(region,open('ASH_RegionLayout.json','w'),indent=1)
print('ok', len(pois),'pois', len(surveys),'surveys')
