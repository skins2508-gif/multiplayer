import bpy, re, json, os, math, struct
import numpy as np
from mathutils import Matrix, Vector, Quaternion
BASE=r'C:\UnitiyProject\multiplayer'
OUT=os.path.join(BASE,'ArtSource/Soldier')
nodes={o.name[2:]:o for o in bpy.data.objects if o.name.startswith('U_')}
def update():bpy.context.view_layer.update()
def rotate_world(o,q):
 m=o.matrix_world.copy(); loc=m.translation.copy();o.matrix_world=q.to_matrix().to_4x4()@m;o.matrix_world.translation=loc;update()
def solve(side,goal):
 u,l,h=[nodes[side+s] for s in ['_UpperArm','_LowerArm','_Hand']]
 shoulder=u.matrix_world.translation.copy();el=l.matrix_world.translation.copy();hand=h.matrix_world.translation.copy()
 a=(el-shoulder).length;b=(hand-el).length;d=Vector(goal)-shoulder;dist=min(max(d.length,abs(a-b)+.001),a+b-.001);d.normalize();goal=shoulder+d*dist
 pole=Vector((1 if side=='Right' else -1,-1,0));pole-=d*pole.dot(d);pole.normalize()
 along=(a*a-b*b+dist*dist)/(2*dist);elbow=shoulder+d*along+pole*math.sqrt(max(0,a*a-along*along))
 rotate_world(u,(el-shoulder).rotation_difference(elbow-shoulder))
 rotate_world(l,(h.matrix_world.translation-l.matrix_world.translation).rotation_difference(goal-l.matrix_world.translation))
# Restore source rotations so the procedure is repeatable.
pose=json.load(open(os.path.join(OUT,'HoldPose.BeforeBlender.json'),encoding='utf-8-sig'))
for b in pose['bones']:
 q=b['rotation'];nodes[b['name']].rotation_quaternion=Quaternion((q['w'],q['x'],q['y'],q['z']))
update()
r=nodes['Right_Hand'];l=nodes['Left_Hand'];att=nodes['Right_Hand_Attach']
frame=Quaternion(Vector((0,1,0)),-math.pi/2)
rr=frame@att.matrix_world.to_quaternion().inverted()@r.matrix_world.to_quaternion()
lr=frame@att.matrix_world.to_quaternion().inverted()@l.matrix_world.to_quaternion()
# Stock rear is x=-.246, comb is y=.15 in the actual imported rifle.
pocket=nodes['Right_UpperArm'].matrix_world.translation+Vector((-.075,-.015,.015))
origin=pocket-frame@Vector((-.23,.15,0))
offset=r.matrix_world.to_quaternion().inverted()@(att.matrix_world.translation-r.matrix_world.translation)
grip=origin-rr@offset
solve('Right',grip);r.matrix_world=Matrix.LocRotScale(r.matrix_world.translation,rr,Vector((1,1,1)));update()
support=att.matrix_world@Vector((.11,-.015,.08))
solve('Left',support);l.matrix_world=Matrix.LocRotScale(l.matrix_world.translation,lr,Vector((1,1,1)));update()
# Curl each finger around the grip, rather than leaving the sampled open hands.
for side,axis in [('Right',Vector((0,1,0))),('Left',Vector((0,0,-1)))]:
 for finger in ['Pinky','Ring','Middle','Index']:
  for joint,angle in [('Proximal',35),('Intermediate',65),('Distal',40)]:
   if side=='Right' and finger=='Index':angle*=.45
   rotate_world(nodes[side+'_'+finger+joint],Quaternion(axis,math.radians(angle)))
# Recreate the exact skinned Unity mesh for a faithful Blender preview.
s=open(os.path.join(OUT,'SoldierBody.BeforeBlender.asset')).read()
rows=re.findall(r'e\d\d: ([-\d.eE]+)',s[s.index('  m_BindPose:'):s.index('  m_BoneNameHashes:')])
bind=np.array(list(map(float,rows))).reshape(-1,4,4)
order=json.loads(bpy.context.scene['unity_bone_order']);order=[n for n in order if n not in ['Right_Hand_Attach','Right_Offhand_IK'] and not n.endswith(('_Outer','_Inner','_Heel'))]
assert len(order)==len(bind)
count=int(re.search(r'm_VertexCount: (\d+)',s).group(1));data=bytearray.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',s).group(1))
v=np.ndarray((count,10),dtype='<f4',buffer=data,offset=0,strides=(40,4))[:,:3].copy()
weight=np.ndarray((count,4),dtype='<f4',buffer=data,offset=count*48,strides=(32,4))
idx=np.ndarray((count,4),dtype='<i4',buffer=data,offset=count*48+16,strides=(32,4))
# Close the actual rigid glove geometry; keep its existing wrist skinning.
# The source glove has no finger weights, so new finger weights would stretch it.
rest=np.linalg.inv(bind)
for side in ['Left','Right']:
 hi=order.index(side+'_Hand');sign=1 if side=='Right' else -1
 wrist=rest[hi,:3,3];forward=np.array([sign*.50,-.85,-.17]);forward/=np.linalg.norm(forward)
 skin=np.array(nodes[side+'_Hand'].matrix_world)@bind[hi]
 inward=np.linalg.inv(skin[:3,:3])@np.array([1.,0,0]);inward-=forward*np.dot(inward,forward);inward/=np.linalg.norm(inward)
 mask=np.where((idx[:,0]==hi)&(weight[:,0]>.65))[0]
 for vi in mask:
  delta=v[vi]-wrist;t=np.dot(delta,forward);start=.07
  if t<=start:continue
  length=t-start;radius=.033;angle=min(length/radius,2.7)
  center=forward*(start+radius*math.sin(angle))+inward*(radius*(1-math.cos(angle)))
  sideways=delta-forward*t
  v[vi]=wrist+center+sideways
# Update positions and vertex normals in the Unity asset as well as the preview.
vertex_stream=np.ndarray((count,10),dtype='<f4',buffer=data,offset=0,strides=(40,4));vertex_stream[:,:3]=v
faces=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',s).group(1)),dtype='<u2').reshape(-1,3)
normal=np.zeros_like(v);fn=np.cross(v[faces[:,1]]-v[faces[:,0]],v[faces[:,2]]-v[faces[:,0]])
for j in range(3):np.add.at(normal,faces[:,j],fn)
normal/=np.maximum(np.linalg.norm(normal,axis=1,keepdims=True),1e-8);vertex_stream[:,3:6]=normal
mats=np.array([np.array(nodes[n].matrix_world) for n in order])@bind
v4=np.column_stack((v,np.ones(count)));baked=np.zeros((count,3))
for j in range(4):baked+=np.einsum('nij,nj->ni',mats[idx[:,j]],v4)[:,:3]*weight[:,j,None]
C=Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1)))
indices=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',s).group(1)),dtype='<u2').reshape(-1,3)
name='Soldier_Adjusted'
if name in bpy.data.objects:bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
mesh=bpy.data.meshes.new(name);mesh.from_pydata(baked[:,[0,2,1]].tolist(),[],indices[:,::-1].tolist());mesh.update();body=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(body)
mat=bpy.data.materials.get('Soldier_Preview') or bpy.data.materials.new('Soldier_Preview');mat.diffuse_color=(.20,.26,.22,1);body.data.materials.append(mat)
for o in list(bpy.data.objects):
 if o.name.startswith('Rifle_Adjusted'):bpy.data.objects.remove(o,do_unlink=True)
for o in list(bpy.data.objects):
 if o.type=='MESH' and 'assaultRifle' in o.name:
  vs=[o.matrix_world@v.co for v in o.data.vertices];uv=[Vector((-p.x,p.z,p.y)) for p in vs]
  points=[C@(att.matrix_world@p) for p in uv]
  me=bpy.data.meshes.new('Rifle_Adjusted');me.from_pydata(points,[],[list(reversed(p.vertices)) for p in o.data.polygons]);me.update();ob=bpy.data.objects.new('Rifle_Adjusted',me);bpy.context.scene.collection.objects.link(ob)
  ma=bpy.data.materials.get('Rifle_Preview') or bpy.data.materials.new('Rifle_Preview');ma.diffuse_color=(.10,.13,.16,1);me.materials.append(ma)
for b in pose['bones']:
 q=nodes[b['name']].rotation_quaternion;b['rotation']={'x':q.x,'y':q.y,'z':q.z,'w':q.w}
json.dump(pose,open(os.path.join(OUT,'HoldPose.Blender.json'),'w'),indent=2)
cal={'rightHand':list(grip),'supportLocal':[.11,-.015,.08],'stockPocket':list(pocket)}
json.dump(cal,open(os.path.join(OUT,'RifleCalibration.json'),'w'),indent=2)
print('Calibration:',cal,'skin vertices',count,'weights sum',float(weight.sum(axis=1).min()),float(weight.sum(axis=1).max()))

patched=re.sub(r'(_typelessdata: )[0-9a-f]+',lambda m:m.group(1)+data.hex(),s)
open(os.path.join(OUT,'SoldierBody.GripAdjusted.asset'),'w').write(patched)
