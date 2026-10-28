import bpy, math, json, os
from mathutils import Vector, Quaternion
root='C:/UnitiyProject/multiplayer'
col=bpy.data.collections['NinjaDash_Work']
rig=next(o for o in col.objects if o.type=='ARMATURE')
# Unity character-space coordinates. One cut, followed by a short recovery.
poses=[
 (0, (.29,1.23,.25),(-.27,1.17,.20),(5,0,0),(0,0,0),(8,-10,-22)),
 (.05,(.40,1.28,.06),(-.30,1.16,.10),(18,18,-5),(.12,.10,-.04),(65,65,-70)),
 (.10,(.32,1.24,.39),(-.35,1.12,-.02),(20,5,-4),(.02,.12,.04),(65,25,-70)),
 (.16,(-.12,1.18,.46),(-.34,1.07,-.10),(19,-18,3),(-.35,.10,.12),(65,-45,-70)),
 (.20,(-.30,1.16,.32),(-.28,1.10,.02),(16,-25,5),(-.52,.12,.12),(65,-70,-70)),
 (.28,(-.08,1.18,.33),(-.27,1.16,.15),(8,-12,2),(-.22,.04,.05),(30,-35,-45)),
 (.38,(.29,1.23,.25),(-.27,1.17,.20),(0,0,0),(0,0,0),(8,-10,-22))]
controls={}
for name in ['right','left','chest','viewOffset','viewEuler']:
    ob=bpy.data.objects.get('Dash_'+name)
    if ob is None:
        ob=bpy.data.objects.new('Dash_'+name,None); col.objects.link(ob)
    ob.empty_display_size=.05
    ob.animation_data_clear(); controls[name]=ob
for pose in poses:
    for i,(name,ob) in enumerate(controls.items(),1):
        ob.location=pose[i]; ob.keyframe_insert(data_path='location',frame=1+pose[0]*100)
        ob['coordinates']='Unity local X right, Y up, Z forward; rotation controls in degrees'
# Sample Blender-authored curves; runtime uses these exact samples.
scene=bpy.context.scene; scene.render.fps=100;scene.frame_start=1;scene.frame_end=39
keys=[]
for frame in range(1,40):
    scene.frame_set(frame)
    key={'time':(frame-1)/100}
    for name,ob in controls.items(): key[name]=dict(zip(['x','y','z'],[round(v,6) for v in ob.location]))
    keys.append(key)
out=root+'/Assets/Shooter/Resources/Ninja/DashSlash.json'
with open(out,'w') as f: json.dump({'duration':.38,'keys':keys},f,indent=2)
def world(v): return Vector((-v[0],-v[2],v[1]))
def rotate_world(bone,q):
    m=rig.matrix_world@bone.matrix;loc,rot,scale=m.decompose()
    from mathutils import Matrix
    bone.matrix=rig.matrix_world.inverted()@Matrix.LocRotScale(loc,q@rot,scale)
    bpy.context.view_layer.update()
def solve(side,goal):
    upper=rig.pose.bones[side+'_UpperArm'];lower=rig.pose.bones[side+'_LowerArm'];hand=rig.pose.bones[side+'_Hand']
    shoulder=rig.matrix_world@upper.head; elbow=rig.matrix_world@lower.head; end=rig.matrix_world@hand.head
    a=(elbow-shoulder).length;b=(end-elbow).length;d=goal-shoulder
    distance=max(abs(a-b)+.001,min(d.length,a+b-.001));d.normalize();goal=shoulder+d*distance
    pole=Vector((-1 if side=='Right' else 1,0,-1));pole=(pole-d*pole.dot(d)).normalized()
    along=(a*a-b*b+distance*distance)/(2*distance)
    desired=shoulder+d*along+pole*math.sqrt(max(0,a*a-along*along))
    rotate_world(upper,(elbow-shoulder).rotation_difference(desired-shoulder))
    elbow=rig.matrix_world@lower.head;end=rig.matrix_world@hand.head
    rotate_world(lower,(end-elbow).rotation_difference(goal-elbow))
rig.animation_data_clear()
for key in keys:
    frame=1+round(key['time']*100);scene.frame_set(frame)
    for b in rig.pose.bones: b.matrix_basis.identity()
    bpy.context.view_layer.update()
    angles=key['chest'];chest=rig.pose.bones['Chest']
    rotate_world(chest,Quaternion((0,0,1),math.radians(-angles['y']))@Quaternion((-1,0,0),math.radians(angles['x'])))
    for side in ['Right','Left']:
        p=key[side.lower()];solve(side,world((p['x'],p['y'],p['z'])))
    for name in ['Chest','Right_UpperArm','Right_LowerArm','Left_UpperArm','Left_LowerArm']:
        b=rig.pose.bones[name];b.rotation_mode='QUATERNION';b.keyframe_insert(data_path='rotation_quaternion',frame=frame)
if rig.animation_data: rig.animation_data.action.name='Ninja_Dash_HorizontalSlash'
scene.frame_set(11)
os.makedirs(root+'/ArtSource/Ninja',exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=root+'/ArtSource/Ninja/NinjaDash.blend',copy=True)
print('Saved Blender rig action and 39 Unity motion samples: '+out)

