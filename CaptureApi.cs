using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Character;
using CharacterCreation;
using UnityEngine;
namespace Amanatsu.AiExtension;

internal static class CaptureApi
{
    private static bool _blink;
    internal static bool PoseRequested;
    private static (int brow,int eyes,int mouth)? _face;
    internal static void BeginCapture(string body=null){
        var h=HumanNow();
        JsonElement j=default;var has=!string.IsNullOrWhiteSpace(body);
        if(has)j=JsonDocument.Parse(body).RootElement;
        int? brow=null,eyes=null,mouth=null;Action pose=null;
        if(has&&j.TryGetProperty("expression",out var e)){
            if(e.TryGetProperty("eyebrow",out var b))brow=b.GetInt32();
            if(e.TryGetProperty("eyes",out var ey))eyes=ey.GetInt32();
            if(e.TryGetProperty("mouth",out var m))mouth=m.GetInt32();
        }
        if(has&&j.TryGetProperty("pose",out var p))pose=CreatorApiEx.ResolvePose(h,p);
        var f=h.Face;
        if(brow<0||brow>=(f.eyebrowCtrl?.GetMaxPtn()??0)||eyes<0||eyes>=(f.eyesCtrl?.GetMaxPtn()??0)||mouth<0||mouth>=(f.mouthCtrl?.GetMaxPtn()??0))
            throw new ArgumentException("expression pattern out of range");
        _blink=f.GetEyesBlinkFlag();f.ChangeEyesBlinkFlag(false);
        _face=null;
        if(brow.HasValue||eyes.HasValue||mouth.HasValue){
            _face=(f.GetEyebrowPtn(),f.GetEyesPtn(),f.GetMouthPtn());
            if(brow.HasValue)f.ChangeEyebrowPtn(brow.Value,false);
            if(eyes.HasValue)f.ChangeEyesPtn(eyes.Value,false);
            if(mouth.HasValue)f.ChangeMouthPtn(mouth.Value,false);
        }
        PoseRequested=pose!=null;
        pose?.Invoke();
    }
    internal static void EndCapture(){
        var h=HumanCustom.Instance?.Human;if(h==null)return;
        h.Face.ChangeEyesBlinkFlag(_blink);
        if(_face.HasValue){h.Face.ChangeEyebrowPtn(_face.Value.brow,false);h.Face.ChangeEyesPtn(_face.Value.eyes,false);h.Face.ChangeMouthPtn(_face.Value.mouth,false);_face=null;}
    }
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    static Human HumanNow()=>HumanCustom.Instance?.Human ?? throw new ArgumentException("open character creation first");
    internal static object Landmarks() {
        var h=HumanNow();var nodes=h.GameObject.GetComponentsInChildren<Transform>(true);
        return new{bones=nodes.Where(t=>t.name.StartsWith("cf_j_",StringComparison.OrdinalIgnoreCase)).Select(t=>new{name=t.name,position=V(t.position)}).ToArray(),faceRenderers=h.Face.objHead.GetComponentsInChildren<Renderer>(true).Select(r=>new{name=r.name,center=V(r.bounds.center),size=V(r.bounds.size)}).ToArray()};
    }
    internal static ApiResult Frame(JsonElement j) {
        var h=HumanNow();var region=j.GetProperty("region").GetString();var view=j.TryGetProperty("view",out var v)?v.GetString():"front";
        var nodes=h.GameObject.GetComponentsInChildren<Transform>(true).ToArray();
        Transform Bone(params string[] names)=>nodes.FirstOrDefault(t=>names.Any(n=>string.Equals(t.name,n,StringComparison.OrdinalIgnoreCase)));
        var renderer=h.Face.objHead.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r=>r.name=="cf_O_face");
        if(renderer==null)throw new ArgumentException("face mesh is unavailable");
        // Renderer.bounds is an oversized animation/culling box in this game.
        // Bake the current deformed mesh to measure the actual head geometry.
        var mesh=new Mesh();Bounds face;
        try {
            renderer.BakeMesh(mesh);mesh.RecalculateBounds();var local=mesh.bounds;
            face=new Bounds(renderer.transform.TransformPoint(local.center),Vector3.zero);
            for(var x=-1;x<=1;x+=2)for(var y=-1;y<=1;y+=2)for(var z=-1;z<=1;z+=2)
                face.Encapsulate(renderer.transform.TransformPoint(local.center+Vector3.Scale(local.extents,new Vector3(x,y,z))));
        }finally{UnityEngine.Object.Destroy(mesh);}
        var root=h.Transform;
        var neck=Bone("cf_j_neck","cf_J_Neck");var hip=Bone("cf_j_hips","cf_J_Hips");var chest=Bone("cf_j_spine03","cf_J_Spine03");
        var feet=nodes.Where(t=>t.name.Equals("cf_j_foot_L",StringComparison.OrdinalIgnoreCase)||t.name.Equals("cf_j_foot_R",StringComparison.OrdinalIgnoreCase)).ToArray();
        var floor=feet.Length>0?feet.Min(t=>t.position.y):root.position.y;
        var top=face.max.y;var height=top-floor;
        if(height<=0||!float.IsFinite(height))throw new ArgumentException("invalid character bounds");
        var chestY=chest?.position.y??(floor+height*.72f);var hipY=hip?.position.y??(floor+height*.49f);var neckY=neck?.position.y??face.min.y;
        Bounds bounds;
        switch(region){
            case "face":bounds=face;bounds.Expand(face.size.y*.12f);break;
            case "bust":bounds=new Bounds(new Vector3(face.center.x,(top+chestY-height*.07f)/2,face.center.z),new Vector3(height*.34f,top-chestY+height*.07f,height*.24f));break;
            case "upper_body":bounds=new Bounds(new Vector3(face.center.x,(top+hipY)/2,root.position.z),new Vector3(height*.46f,top-hipY,height*.3f));break;
            case "waist":bounds=new Bounds(new Vector3(root.position.x,hipY,root.position.z),new Vector3(height*.38f,height*.30f,height*.30f));break;
            case "legs":bounds=new Bounds(new Vector3(root.position.x,(hipY+floor)/2,root.position.z),new Vector3(height*.32f,hipY-floor+height*.06f,height*.3f));break;
            case "full":bounds=new Bounds(new Vector3(root.position.x,(top+floor)/2,root.position.z),new Vector3(height*.56f,height*1.08f,height*.38f));break;
            default:throw new ArgumentException("region must be face, bust, upper_body, waist, legs or full");
        }
        var yaw=view switch{"front"=>180f,"back"=>0f,"left"=>90f,"right"=>270f,"top"=>180f,"bottom"=>180f,_=>throw new ArgumentException("invalid view")};
        var pitch=view=="top"?90f:view=="bottom"?-90f:0f;
        var rot=new Vector3(pitch,yaw+root.eulerAngles.y,0);var q=Quaternion.Euler(rot);var up=q*Vector3.up;var right=q*Vector3.right;var forward=q*Vector3.forward;
        var c=HumanCustom.Instance.__camCtrl;var aspect=c.NowCamera.aspect;const float fov=23;
        float extentY=0,extentX=0,depth=0;var e=bounds.extents;
        for(var x=-1;x<=1;x+=2)for(var y=-1;y<=1;y+=2)for(var z=-1;z<=1;z+=2){var p=new Vector3(e.x*x,e.y*y,e.z*z);extentY=Math.Max(extentY,Math.Abs(Vector3.Dot(p,up)));extentX=Math.Max(extentX,Math.Abs(Vector3.Dot(p,right)));depth=Math.Max(depth,Math.Abs(Vector3.Dot(p,forward)));}
        var tan=Mathf.Tan(fov*Mathf.Deg2Rad/2);var distance=Math.Max(extentY/tan,extentX/(tan*aspect))*1.12f+depth;
        c.CameraPos=bounds.center;c.CameraRot=rot;c.CameraDir=new Vector3(0,0,-distance);c.CameraFov=fov;
        return new(200,new{region,view,center=V(bounds.center),size=V(bounds.size),distance,fov,measuredHeight=height,anchors=new{faceCenter=V(face.center),faceSize=V(face.size),neckY,chestY,hipY,floor},source="current world-space head mesh bounds and skeleton bones"});
    }
}
