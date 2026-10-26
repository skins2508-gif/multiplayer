using System.Collections.Generic;
using UnityEngine;

namespace Blocks.Gameplay.Shooter
{
    public sealed partial class ShooterTrainingBot
    {
        GameObject characterModel;
        Animator characterAnimator;
        readonly Dictionary<Transform, Quaternion> boneRest = new Dictionary<Transform, Quaternion>();
        readonly List<Material> modelMaterials = new List<Material>();
        Transform heldWeapon;
        readonly Transform[] cannonMuzzles = new Transform[2];
        readonly List<Transform> cannonRotors = new List<Transform>();
        Vector3 modelRestPosition;
        float walkSwing;
        GenjiLocomotionPose genjiLocomotion;
        Vector3 genjiPreviousPosition, genjiVelocity;

        public GameObject CopyDestroyedMech()
        {
            if(hero!=2)
            {
                // Replicated pilot state can arrive before the explosion RPC on a peer.
                var staging=Create(null,0);
                staging.transform.SetPositionAndRotation(transform.position,transform.rotation);
                staging.SetHero(2);
                var snapshot=staging.CopyDestroyedMech();
                Destroy(staging.gameObject);
                return snapshot;
            }
            if(characterModel==null)return null;
            var copy=Instantiate(characterModel,characterModel.transform.position,characterModel.transform.rotation);
            copy.transform.localScale=characterModel.transform.lossyScale;
            copy.SetActive(true);
            // The living bot may respawn/change hero before the old mech detonates.
            foreach(var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)if(materials[i]!=null)materials[i]=new Material(materials[i]);
                renderer.sharedMaterials=materials;
            }
            return copy;
        }

        void BuildCharacterModel()
        {
            genjiLocomotion=null;genjiPreviousPosition=transform.position;genjiVelocity=Vector3.zero;
            if(characterModel!=null) { characterModel.SetActive(false); Destroy(characterModel); }
            foreach(var material in modelMaterials)Destroy(material);
            modelMaterials.Clear();boneRest.Clear();characterAnimator=null;heldWeapon=null;
            cannonMuzzles[0]=cannonMuzzles[1]=null;cannonRotors.Clear();
            string path=hero==2?"Dva/DvaPinkDisplay":hero==4?"DvaPilot/PilotBody":hero==3?"Cowboy/CowboyBody":hero==1?"GenjiHero/GenjiHeroBody":"Bots/GenjiBody";
            var source=Resources.Load<GameObject>(path);
            if(source==null) { Debug.LogWarning("AI character model missing: "+path,this);return; }
            characterModel=Instantiate(source,transform,false);
            characterModel.name=ShooterEscortMatch.BotHeroName(hero)+"_AI_Model";
            foreach(var component in characterModel.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
            foreach(var collider in characterModel.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var body in characterModel.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
            foreach(var animator in characterModel.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled=false;
                if(animator.isHuman)characterAnimator=animator;
            }
            if(hero==0)
            {
                var soldier=Resources.Load<GameObject>("Soldier/SoldierBody");
                var skin=soldier!=null?soldier.GetComponent<SkinnedMeshRenderer>():null;
                foreach(var renderer in characterModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(renderer.name=="Armature_Mesh" && skin!=null)
                    {
                        // A fresh renderer avoids reusing the imported mesh's GPU vertex
                        // layout when the soldier has a different vertex count/stride.
                        var replacement=new GameObject("SoldierSkin").AddComponent<SkinnedMeshRenderer>();
                        replacement.transform.SetParent(renderer.transform,false);
                        replacement.sharedMesh=skin.sharedMesh;replacement.sharedMaterials=skin.sharedMaterials;
                        replacement.bones=renderer.bones;replacement.rootBone=renderer.rootBone;
                        // The resource is a mesh/material container without live bones;
                        // read mesh bounds rather than asking that renderer to skin it.
                        replacement.localBounds=skin.sharedMesh.bounds;replacement.updateWhenOffscreen=true;
                        FitSkeletonToSkin(replacement);
                        renderer.sharedMesh=null;
                    }
            }
            // DvaPinkDisplay's barrels point along -X (rotor -> muzzle), so +90
            // maps its authored front onto the bot's +Z aiming direction.
            if(hero==2)characterModel.transform.localRotation=Quaternion.Euler(0,90,0);
            foreach(var part in characterModel.GetComponentsInChildren<Transform>(true))part.gameObject.layer=gameObject.layer;
            var renderers=characterModel.GetComponentsInChildren<Renderer>(true);
            foreach(var renderer in renderers)
            {
                renderer.enabled=true;renderer.forceRenderingOff=false;
                if(hero!=2)continue;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null)continue;
                    string name=materials[i]!=null?materials[i].name.ToLowerInvariant():"pink";
                    var material=new Material(shader);
                    material.color=name.Contains("pink")?new Color(1,.23f,.52f):name.Contains("mint")?new Color(.04f,.9f,.65f):name.Contains("white")?new Color(.85f,.9f,1):new Color(.12f,.2f,.28f);
                    materials[i]=material;modelMaterials.Add(material);
                }
                renderer.sharedMaterials=materials;
            }
            if(renderers.Length>0 && (hero==1 || hero==2 || hero==3))
            {
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                characterModel.transform.localScale*= (hero==2?1.95f:1.8f)/Mathf.Max(.001f,bounds.size.y);
                bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                characterModel.transform.position+=transform.up*(transform.position.y-bounds.min.y);
            }
            modelRestPosition=characterModel.transform.localPosition;
            foreach(var part in characterModel.GetComponentsInChildren<Transform>(true))
            {
                if(part.name=="DvaMuzzle_L")cannonMuzzles[0]=part;
                if(part.name=="DvaMuzzle_R")cannonMuzzles[1]=part;
                if(part.name.StartsWith("DvaRotor_"))cannonRotors.Add(part);
            }
            // Keep the existing muzzle and hit capsule independent of the visual skeleton.
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if(renderer.transform.IsChildOf(characterModel.transform) || renderer.gameObject==label.gameObject)continue;
                renderer.enabled=false;
            }
            if(characterAnimator!=null)
            {
                if(hero==3 || hero==4)
                {
                    using(var handler=new HumanPoseHandler(characterAnimator.avatar,characterAnimator.transform))
                    {
                        var pose=new HumanPose();handler.GetHumanPose(ref pose);
                        WeaponController.SetCowboyGrip(ref pose);handler.SetHumanPose(ref pose);
                    }
                }
                // Reset the entire skeleton each frame before IK. Imported humanoid
                // bone axes differ, and rotations from a previous shot must not accumulate.
                foreach(var bone in characterAnimator.GetComponentsInChildren<Transform>(true))
                {
                    boneRest[bone]=bone.localRotation;
                }
                if(hero==4)AttachCharacterWeapon("DvaPilot/PilotPistol",.32f);
                if(hero==1 || hero==3)AttachCharacterWeapon(hero==1?"Ninja/Shuriken":"Revolver/Gun",hero==1?.18f:.48f);
                if(hero==0)
                {
                    var hand=characterAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                    var rifle=Resources.Load<GameObject>("Bots/AssaultRifle");
                    if(hand!=null && rifle!=null)
                    {
                        heldWeapon=new GameObject("AI_RifleSocket").transform;heldWeapon.SetParent(hand,false);
                        Instantiate(rifle,heldWeapon,false);
                    }
                }
            }
            if(hero==1 && characterAnimator!=null)genjiLocomotion=new GenjiLocomotionPose(characterAnimator);
            AnimateCharacterModel(0,0);
        }

        static void FitSkeletonToSkin(SkinnedMeshRenderer skin)
        {
            // SoldierBody was bound to its own A-pose arm lengths. Reconstruct that
            // rest skeleton before solving hands, otherwise the glove mesh is offset
            // from the hand bone even when the IK target is mathematically correct.
            var bones=skin.bones;var binds=skin.sharedMesh.bindposes;
            if(bones.Length!=binds.Length)return;
            var targets=new Dictionary<Transform,Matrix4x4>();
            for(int i=0;i<bones.Length;i++)
                if(bones[i]!=null)targets[bones[i]]=skin.transform.localToWorldMatrix*binds[i].inverse;
            foreach(var bone in skin.transform.root.GetComponentsInChildren<Transform>(true))
                if(targets.TryGetValue(bone,out var matrix))bone.SetPositionAndRotation(matrix.GetColumn(3),matrix.rotation);
        }

        void AttachCharacterWeapon(string path,float size)
        {
            var hand=characterAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            var source=Resources.Load<GameObject>(path);if(hand==null || source==null)return;
            // Keep weapon scale/aim independent of the imported wrist's rotation and scale.
            var item=Instantiate(source,characterModel.transform,false);heldWeapon=item.transform;
            foreach(var collider in item.GetComponentsInChildren<Collider>())collider.enabled=false;
            var renderers=item.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return;
            var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            if(hero==4)DvaPistolScale.Normalize(item.transform);
            else item.transform.localScale*=size/Mathf.Max(.001f,bounds.size.magnitude);
            if(hero==3)
            {
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.mainTexture=Resources.Load<Texture2D>("Revolver/GunColor");
                material.SetFloat("_Metallic",.65f);material.SetFloat("_Smoothness",.45f);modelMaterials.Add(material);
                foreach(var renderer in renderers)
                {
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)materials[i]=material;
                    renderer.sharedMaterials=materials;
                }
            }
        }

        void AnimateCharacterModel(float swing,float kick)
        {
            if(characterModel==null)return;
            if(hero==2)
            {
                characterModel.transform.localPosition=modelRestPosition-Vector3.forward*(.065f*kick);
                if(Time.time-attackedAt<.2f) foreach(var rotor in cannonRotors)
                    if(rotor!=null)rotor.Rotate(720*Time.deltaTime,0,0,Space.Self);
                var muzzle=cannonMuzzles[attackSequence%2];
                if(muzzle!=null)Muzzle.position=muzzle.position;
                return;
            }
            if(characterAnimator==null)return;
            genjiLocomotion?.ResetRoot();
            foreach(var entry in boneRest)entry.Key.localRotation=entry.Value;
            // Bend around the character's right axis, not each asset's local bone X.
            if(genjiLocomotion!=null)
                genjiLocomotion.Apply(transform,genjiVelocity,true,-1,Time.deltaTime,true);
            else
            {
                PoseBone(HumanBodyBones.LeftUpperLeg,swing);
                PoseBone(HumanBodyBones.RightUpperLeg,-swing);
                PoseBone(HumanBodyBones.LeftLowerLeg,Mathf.Max(0,-swing)*1.3f);
                PoseBone(HumanBodyBones.RightLowerLeg,Mathf.Max(0,swing)*1.3f);
            }

            Vector3 right,left;
            if(hero==3 || hero==4)
            {
                right=new Vector3(.24f,1.35f+.025f*kick,.48f-.055f*kick);
                left=new Vector3(-.22f,1.08f,.18f);
            }
            else if(hero==1)
            {
                right=Vector3.Lerp(new Vector3(.29f,1.23f,.28f),new Vector3(.23f,1.35f,.56f),kick);
                left=new Vector3(-.27f,1.16f,.22f);
            }
            else
            {
                right=new Vector3(.16f,1.17f,.32f-.025f*kick);
                left=new Vector3(.10f,1.19f,.62f-.025f*kick);
            }
            float reload=Time.time<reloadUntil?Mathf.Sin(Mathf.Clamp01(1-(reloadUntil-Time.time)/(hero==3?1.8f:1.5f))*Mathf.PI):0;
            right=Vector3.Lerp(right,new Vector3(.18f,1.13f,.30f),reload);
            left=Vector3.Lerp(left,new Vector3(-.02f,1.13f,.33f),reload);
            WeaponController.SolveArm(characterAnimator,transform,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,right,1);
            WeaponController.SolveArm(characterAnimator,transform,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,left,-1);
            if(hero==0 && heldWeapon!=null)
            {
                Quaternion aim=transform.rotation*Quaternion.Euler(-6*kick+15*reload,0,0);
                var hand=characterAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                var middle=characterAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                var upper=characterAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lower=characterAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var supportHand=characterAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                float reach=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,supportHand.position)-.005f;
                Vector3 grip=new Vector3(.14f,1.30f,.25f-.045f*kick),support=Vector3.zero;
                for(int i=0;i<4;i++)
                {
                    WeaponController.SolveArm(characterAnimator,transform,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,grip,1);
                    WeaponController.AlignCowboyWrist(characterAnimator,aim*Vector3.forward);
                    Vector3 palm=middle!=null?Vector3.Lerp(hand.position,middle.position,.65f):hand.position;
                    heldWeapon.rotation=aim*Quaternion.Euler(0,-90,0);
                    // Pfb_assaultRifle: +X barrel, grip centered at (.04,.05,0).
                    heldWeapon.position=palm-heldWeapon.TransformVector(new Vector3(.04f,.05f,0));
                    support=heldWeapon.TransformPoint(new Vector3(.24f,.025f,.03f));
                    Vector3 delta=support-upper.position;
                    if(delta.magnitude<=reach)break;
                    grip-=transform.InverseTransformVector(delta.normalized*(delta.magnitude-reach));
                }
                Vector3 target=Vector3.Lerp(transform.InverseTransformPoint(support),new Vector3(.05f,1.2f,.25f),reload);
                WeaponController.SolveArm(characterAnimator,transform,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,target,-1);
                Muzzle.position=heldWeapon.TransformPoint(new Vector3(.7462f,.1505f,.0004f));
            }
            else if((hero==3 || hero==4) && heldWeapon!=null)
            {
                Quaternion aim=transform.rotation*Quaternion.Euler(-12*kick,0,-45*reload);
                WeaponController.AlignCowboyWrist(characterAnimator,aim*Vector3.forward);
                var hand=characterAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                var middle=characterAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                Vector3 palm=middle!=null?Vector3.Lerp(hand.position,middle.position,.65f):hand.position;
                heldWeapon.rotation=aim;
                if(hero==4)
                {
                    heldWeapon.position=palm;
                    var muzzle=System.Array.Find(heldWeapon.GetComponentsInChildren<Transform>(),t=>t.name=="PilotMuzzle");
                    Muzzle.position=muzzle!=null?muzzle.position:heldWeapon.position+aim*Vector3.forward*.18f;
                }
                else
                {
                    heldWeapon.position=palm-heldWeapon.TransformVector(new Vector3(0,-.03f,-.115f));
                    Muzzle.position=heldWeapon.TransformPoint(new Vector3(0,.025f,.165f));
                }
            }
            else if(hero==1 && heldWeapon!=null)
            {
                var hand=characterAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                WeaponController.AlignCowboyWrist(characterAnimator,transform.forward);
                // The shuriken is authored flat in XZ; turn its face toward the throw.
                heldWeapon.SetPositionAndRotation(hand.position+transform.forward*.10f-transform.up*.025f,transform.rotation*Quaternion.Euler(90,0,35));
                Muzzle.position=heldWeapon.position;
            }
        }
        void PoseBone(HumanBodyBones id,float angle)
        {
            var bone=characterAnimator.GetBoneTransform(id);
            if(bone!=null)bone.rotation=Quaternion.AngleAxis(angle,transform.right)*bone.rotation;
        }

        void LateUpdate()
        {
            genjiVelocity=(transform.position-genjiPreviousPosition)/Mathf.Max(.001f,Time.deltaTime);
            genjiPreviousPosition=transform.position;
            float kick=Mathf.Sin(Mathf.Clamp01((Time.time-attackedAt)/.24f)*Mathf.PI);
            AnimateCharacterModel(walkSwing,kick);
            if(hero==2 && Time.time-attackedAt<.35f)
                foreach(var rotor in cannonRotors)rotor.Rotate(transform.forward,-1500*Time.deltaTime,Space.World);
        }
    }
}
