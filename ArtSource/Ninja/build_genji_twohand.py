import bpy, math, json, os
from mathutils import Vector, Quaternion
root='C:/UnitiyProject/multiplayer'
with bpy.data.libraries.load(root+'/ArtSource/Ninja/NinjaDash.blend',link=False) as (src,dst):
    dst.collections=['NinjaDash_Work']
col=dst.collections[0]
scene=bpy.data.scenes.new('Genji_TwoHand_Review')
scene.collection.children.link(col)
bpy.context.window.scene=scene
rig=next(o for o in col.objects if o.type=='ARMATURE')
# Normalized phase, right/left wrist in Unity character space, torso degrees,
# first-person offset and weapon degrees. Hit phase .34 matches .17 seconds.
rest=(0,(.29,1.23,.25),(-.27,1.17,.20),(0,0,0),(0,0,0),(8,-10,-22))
clips={
'SlashRight':[
(0,(.20,1.25,.30),(0,0,0),(0,0,0),(0,0,0),(8,-10,-22)),
(.246,(.25,1.55,.18),(0,0,0),(-8,23,-6),(.10,.25,-.12),(-50,25,-45)),
(.369,(.04,1.33,.48),(0,0,0),(8,0,0),(-.20,.03,.15),(35,0,15)),
(.492,(-.22,1.03,.32),(0,0,0),(15,-28,7),(-.45,-.22,.09),(105,-40,105)),
(1,(.20,1.25,.30),(0,0,0),(0,0,0),(0,0,0),(8,-10,-22))],
'SlashLeft':[
(0,(.20,1.25,.30),(0,0,0),(0,0,0),(0,0,0),(8,-10,-22)),
(.246,(-.22,1.48,.25),(0,0,0),(-6,-25,6),(-.43,.20,-.06),(-40,-35,50)),
(.369,(.04,1.31,.48),(0,0,0),(8,0,0),(-.20,.02,.15),(35,0,-15)),
(.492,(.28,1.03,.30),(0,0,0),(15,28,-7),(.12,-.22,.09),(105,40,-105)),
(1,(.20,1.25,.30),(0,0,0),(0,0,0),(0,0,0),(8,-10,-22))]}
# Reuse the verified two-bone rig solver from the dash authoring source.
source=open(root+'/ArtSource/Ninja/build_dash.py',encoding='utf-8-sig').read()
exec(source[source.index('def world('):source.index('rig.animation_data_clear()')])
scene.render.fps=100
for name,poses in clips.items():
    controls={}
    for prop in ['right','left','chest','viewOffset','viewEuler']:
        ob=bpy.data.objects.new(name+'_'+prop,None);col.objects.link(ob);controls[prop]=ob
    for p in poses:
        for i,(prop,ob) in enumerate(controls.items(),1):
            ob.location=p[i];ob.keyframe_insert(data_path='location',frame=1+round(p[0]*100))
    keys=[]
    for f in range(1,102):
        scene.frame_set(f)
        k={'time':(f-1)/100}
        for prop,ob in controls.items(): k[prop]=dict(zip(['x','y','z'],[round(v,6) for v in ob.location]))
        from mathutils import Euler
        e=k['viewEuler']
        q=Euler(tuple(math.radians(e[c]) for c in ['x','y','z']),'YXZ').to_quaternion()
        r=Vector(tuple(k['right'][c] for c in ['x','y','z']))
        k['left']=dict(zip(['x','y','z'],r+q@Vector((0,-.14,0))))
        keys.append(k)
    with open(root+'/Assets/Shooter/Resources/Ninja/'+name+'.json','w') as out:json.dump({'duration':1,'keys':keys},out,indent=2)
    rig.animation_data_clear()
    for k in keys:
        f=1+round(k['time']*100);scene.frame_set(f)
        for b in rig.pose.bones:b.matrix_basis.identity()
        bpy.context.view_layer.update()
        a=k['chest'];rotate_world(rig.pose.bones['Chest'],Quaternion((0,0,1),math.radians(-a['y']))@Quaternion((-1,0,0),math.radians(a['x'])))
        for side in ['Right','Left']:
            p=k[side.lower()];solve(side,world((p['x'],p['y'],p['z'])))
        for bn in ['Chest','Right_UpperArm','Right_LowerArm','Left_UpperArm','Left_LowerArm']:
            b=rig.pose.bones[bn];b.rotation_mode='QUATERNION';b.keyframe_insert(data_path='rotation_quaternion',frame=f)
    rig.animation_data.action.name='Genji_'+name
    rig.animation_data.action.use_fake_user=True
scene.frame_start=1;scene.frame_end=101;scene.frame_set(35)
bpy.ops.wm.save_as_mainfile(filepath=root+'/ArtSource/Ninja/GenjiTwoHand.blend',copy=True)
print('Exported two-handed cuts. Duration .65 sec; hit .24 sec; grip spacing .14m.')
