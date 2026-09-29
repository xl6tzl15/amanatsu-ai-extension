using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Character;
using CharacterCreation;
using UnityEngine;

namespace Amanatsu.AiExtension;

internal static class CreatorApi
{
    static ApiResult Ok(object x) => new(200, x);
    static string Str(JsonElement j, string k) => j.GetProperty(k).GetString();
    static int Int(JsonElement j, string k, int fallback=0) => j.TryGetProperty(k,out var p) ? p.GetInt32() : fallback;
    static float[] C(Color c) => new[]{c.r,c.g,c.b,c.a};
    static float[] V(Vector3 v) => new[]{v.x,v.y,v.z};
    static Vector3 Vec(JsonElement j) { var v=j.EnumerateArray().Select(x=>x.GetSingle()).ToArray(); if(v.Length!=3 || v.Any(x=>!float.IsFinite(x)||Math.Abs(x)>1000)) throw new ArgumentException("invalid vector"); return new Vector3(v[0],v[1],v[2]); }
    static object HairBundles(HumanDataHair.PartsInfo p) {var values=new List<object>();foreach(var kv in p.dictBundle)values.Add(new{index=kv.Key,moveRate=V(kv.Value.moveRate),rotRate=V(kv.Value.rotRate),kv.Value.noShake});return values;}
    static Color Col(JsonElement j) { var v=j.EnumerateArray().Select(x=>x.GetSingle()).ToArray(); if((v.Length!=3&&v.Length!=4)||v.Any(x=>!float.IsFinite(x)||x<0||x>1))throw new ArgumentException("color must be 3 or 4 channels in 0..1"); return new Color(v[0],v[1],v[2],v.Length==4?v[3]:1); }
    static Human H() => HumanCustom.Instance?.Human ?? throw new ArgumentException("open character creation first");
    internal static void SyncCoordinate(Human h) {
        var index=(int)h.FileStatus.coordinateType;
        h.Data.Coordinates[index].Copy(h.Coorde.Now);
    }
    static string CardPath(string name) {
        if(Path.GetFileName(name)!=name || !name.EndsWith(".png",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("card requires a filename.png without directories");
        return Path.GetFullPath(Path.Combine("UserData","chara","female",name));
    }
    internal static ApiResult Execute(string method,string action,JsonElement j)
    {
        var h=H(); var custom=HumanCustom.Instance;
        if(method=="POST" && action=="hair-bundle") {
            var part=Int(j,"part",-1);var index=Int(j,"index",-1);
            if(part<0||part>=h.Coorde.Now.Hair.parts.Length)throw new ArgumentException("hair part out of range");
            var piece=h.Coorde.Now.Hair.parts[part];
            if(!piece.dictBundle.ContainsKey(index))throw new ArgumentException("hair bundle index unavailable for this style");
            var bundle=piece.dictBundle[index];
            Vector3 Rate(string field){var v=Vec(j.GetProperty(field));if(v.x<0||v.y<0||v.z<0||v.x>1||v.y>1||v.z>1)throw new ArgumentException(field+" must be 0..1");return v;}
            var hasMove=j.TryGetProperty("moveRate",out _);var hasRot=j.TryGetProperty("rotRate",out _);
            if(!hasMove&&!hasRot)throw new ArgumentException("provide moveRate or rotRate");
            var move=hasMove?Rate("moveRate"):bundle.moveRate;var rot=hasRot?Rate("rotRate"):bundle.rotRate;
            if(hasMove){bundle.moveRate=move;h.Hair.ChangeSettingHairCorrectPos(part,index);}
            if(hasRot){bundle.rotRate=rot;h.Hair.ChangeSettingHairCorrectRot(part,index);}
            SyncCoordinate(h);return Ok(new{part,index,moveRate=V(bundle.moveRate),rotRate=V(bundle.rotRate)});
        }
        if(method=="POST" && action=="accessory") {
            var slot=Int(j,"slot",-1);var id=Int(j,"id",-1);var hasParent=j.TryGetProperty("parent",out _);var parent=Int(j,"parent",0);
            if(slot<0||slot>=h.Coorde.Now.Accessory.parts.Length)throw new ArgumentException("accessory slot out of range");
            if(!Enum.TryParse<Character.List.Define.CategoryNo>(Str(j,"category"),out var category)||!category.ToString().StartsWith("ao_")||!Human.LstCtrl.ContainsInfo(category,id))throw new ArgumentException("invalid accessory category/id");
            if(hasParent&&!Enum.IsDefined(typeof(HumanAccessory.Define.AccessoryParentKey),parent))throw new ArgumentException("invalid accessory parent");
            h.Acs.ChangeAccessory(slot,category,id,(HumanAccessory.Define.AccessoryParentKey)parent,true,new Il2CppSystem.Nullable<bool>());
            if(!hasParent){var def=h.Acs.GetAccessoryDefaultParentType(slot);h.Acs.ChangeAccessoryParent(slot,def);parent=(int)def;}
            SyncCoordinate(h);return Ok(new{slot,id,category=category.ToString(),parent});
        }
        if(method=="GET" && action=="files") {
            var list=custom._fileUI.CharaFile.ListCtrl._visibleList;
            var entries=new List<object>();for(var i=0;i<list.Count;i++){var x=list[i];entries.Add(new{index=i,x.Name,x.FileName,x.FullPath});}
            return Ok(new{entries});
        }
        if(method=="POST" && action=="native-ui") {
            var command=Str(j,"command");
            var file=custom._fileUI.CharaFile;
            if(command=="new-card") SyncCoordinate(h);
            if(command=="load-card") {
                var list=file.ListCtrl._visibleList;var index=Int(j,"index",-1);
                if(index<0||index>=list.Count)throw new ArgumentException("refresh creator/files and choose its index");
                var info=list[index];file.LoadCharaFile(info);return Ok(new{loaded=info.FileName});
            }
            var button=command switch {"new-card"=>file._fileWindow._btnSave,"capture"=>custom.CaptureFrame._btnCapture,"save-card"=>custom.CaptureFrame._btnSave,"back"=>custom.CaptureFrame._btnBackCustom,_=>throw new ArgumentException("unknown native UI command")};
            if(button==null||!button.gameObject.activeInHierarchy||!button.interactable) return new(409,new{error="native button not ready"});
            button.onClick.Invoke();return Ok(new{command});
        }
        if(method=="GET" && action=="diagnostics") {
            var items=new List<object>();
            var all=Human.List;var count=all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Human>>().Count;
            for(var i=0;i<count;i++) {var x=all[i]; items.Add(new {x.Name, x.Disposed, active=x.GameObject?.activeInHierarchy, position=x.Transform == null ? null : V(x.Transform.position), same=x.Pointer==h.Pointer});}
            return Ok(new {humans=items, current=h.Name, scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name});
        }
        if(method=="GET" && action=="details") {
            var hair=new List<object>(); var clothes=new List<object>(); var eyes=new List<object>(); var acs=new List<object>();
            foreach(var p in h.Coorde.Now.Hair.parts) hair.Add(new {p.id,pos=V(p.pos),rot=V(p.rot),scl=V(p.scl),bundles=HairBundles(p),baseColor=C(p.baseColor),startColor=C(p.startColor),endColor=C(p.endColor),outlineColor=C(p.outlineColor),glossColor=C(p.glossColor),shadowColor=C(p.shadowColor),p.useMesh,meshColor=C(p.meshColor),p.useInner,innerColor=C(p.innerColor)});
            foreach(var p in h.Coorde.Now.Clothes.parts) {var colors=new List<object>(); foreach(var c in p.colorInfo) colors.Add(new{baseColor=C(c.baseColor),c.gloss,c.metallic,pattern=c.patternInfo.pattern,patternColor=C(c.patternInfo.patternColor)}); clothes.Add(new{p.id,colors});}
            foreach(var p in h.FileFace.pupil) eyes.Add(new{p.id,eye01Color=C(p.eye01Color),eye02Color=C(p.eye02Color),eye03Color=C(p.eye03Color)});
            foreach(var p in h.Coorde.Now.Accessory.parts) acs.Add(new{p.type,p.id,category=((Character.List.Define.CategoryNo)p.type).ToString(),parent=p.parentKeyType,move=CreatorApiEx.AccessoryMoveInfo(p),colors=p.color.Select(C).ToArray()});
            return Ok(new{profile=new{h.FileParam.lastname,h.FileParam.firstname,h.FileParam.nickname,h.FileParam.personality,h.FileParam.birthMonth,h.FileParam.birthDay,h.FileParam.voiceRate,h.FileParam.bloodType},coordinate=h.FileStatus.coordinateType,hair,eyes,clothes,accessories=acs,skin=new{mainColor=C(h.FileBody.skinMainColor),h.FileBody.skinId,h.FileBody.detailId,h.FileBody.skinShineId,h.FileBody.skinShinePower,h.FileBody.sunburnUpId,h.FileBody.sunburnDownId,moleId=h.FileFace.moleInfo.ID},bodyLabels=Enum.GetNames(typeof(HumanBody.Define.BodyShapeIdx)),faceLabels=Enum.GetNames(typeof(HumanFace.Define.FaceShapeIdx))});
        }
        if(method=="POST" && action=="skin") {
            // Validate everything first so a bad field leaves the character untouched.
            Color? main=j.TryGetProperty("mainColor",out var mc)?Col(mc):null;
            int? up=j.TryGetProperty("sunburnUpId",out var su)?su.GetInt32():null;
            int? down=j.TryGetProperty("sunburnDownId",out var sd)?sd.GetInt32():null;
            int? mole=j.TryGetProperty("moleId",out var mo)?mo.GetInt32():null;
            int? shine=j.TryGetProperty("shineId",out var si)?si.GetInt32():null;
            float? shinePower=j.TryGetProperty("shinePower",out var sp)?sp.GetSingle():null;
            if(shinePower.HasValue&&(!float.IsFinite(shinePower.Value)||shinePower<0||shinePower>1))throw new ArgumentException("shinePower must be 0..1");
            if(up<0||down<0||mole<0||shine<0)throw new ArgumentException("ids must be >= 0");
            if(main.HasValue)h.FileBody.skinMainColor=main.Value;
            if(up.HasValue)h.FileBody.sunburnUpId=up.Value;
            if(down.HasValue)h.FileBody.sunburnDownId=down.Value;
            if(shine.HasValue)h.FileBody.skinShineId=shine.Value;
            if(shinePower.HasValue)h.FileBody.skinShinePower=shinePower.Value;
            if(mole.HasValue)h.FileFace.moleInfo.ID=mole.Value;
            h.Body.AddUpdateCMBodyFlagsFull();h.Face.AddUpdateCMFaceFlagsFull();
            h.Body.CreateBodyTexture();h.Face.CreateFaceTexture();
            return Ok(new{mainColor=C(h.FileBody.skinMainColor),h.FileBody.sunburnUpId,h.FileBody.sunburnDownId,h.FileBody.skinShineId,h.FileBody.skinShinePower,moleId=h.FileFace.moleInfo.ID});
        }
        if(method=="POST" && action=="hair-flags") {
            var slot=Int(j,"slot",-1);var parts=h.Coorde.Now.Hair.parts;
            if(slot<0||slot>=parts.Length)throw new ArgumentException("hair slot out of range");
            var p=parts[slot];
            if(j.TryGetProperty("useMesh",out var um)){p.useMesh=um.GetBoolean();h.Hair.ChangeSettingHairMeshColor(slot);}
            if(j.TryGetProperty("useInner",out var ui)){p.useInner=ui.GetBoolean();h.Hair.ChangeSettingHairInnerColor(slot);}
            SyncCoordinate(h);return Ok(new{slot,p.useMesh,p.useInner});
        }
        if(method=="POST" && action=="load") {
            return new(501,new {error="card loading is disabled pending safe scene lifecycle handling"});
        }
        if(method=="POST" && action=="profile") {
            // Validate every field before applying any of them.
            var last=Str(j,"lastname");var first=Str(j,"firstname");var nick=Str(j,"nickname");
            if(new[]{last,first,nick}.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>20))throw new ArgumentException("names require 1..20 characters");
            var month=Int(j,"birthMonth",h.FileParam.birthMonth);var day=Int(j,"birthDay",h.FileParam.birthDay);
            if(month<1||month>12||day<1||day>DateTime.DaysInMonth(2000,month))throw new ArgumentException("invalid birthday");
            var personality=Int(j,"personality",h.FileParam.personality);var blood=Int(j,"bloodType",h.FileParam.bloodType);
            var voice=j.TryGetProperty("voiceRate",out var vr)?vr.GetSingle():h.FileParam.voiceRate;
            if(personality!=h.FileParam.personality&&!CreatorApiEx.Personalities(custom).Contains(personality))throw new ArgumentException("unknown personality; see GET creator/personalities");
            if(!float.IsFinite(voice)||voice<0||voice>1)throw new ArgumentException("voiceRate must be 0..1");
            if(blood<0||blood>3)throw new ArgumentException("bloodType must be 0..3");
            h.FileParam.lastname=last;h.FileParam.firstname=first;h.FileParam.nickname=nick;h.FileParam.birthMonth=(byte)month;h.FileParam.birthDay=(byte)day;
            h.FileParam.personality=personality;h.FileParam.voiceRate=voice;h.FileParam.bloodType=(byte)blood;
            custom.UpdateFullNameUI();return Ok(new{last,first,nick,month,day,personality,voiceRate=voice,bloodType=blood});
        }
        if(method=="POST" && action=="color") {
            var target=Str(j,"target");var slot=Int(j,"slot");var channel=Int(j,"channel");var color=Col(j.GetProperty("color"));
            if(target=="hair") {
                var parts=h.Coorde.Now.Hair.parts;if(slot<0||slot>=parts.Length)throw new ArgumentException("hair slot out of range");
                var p=parts[slot];var field=Str(j,"field");
                switch(field){case "base":p.baseColor=color;break;case "start":p.startColor=color;break;case "end":p.endColor=color;break;case "outline":p.outlineColor=color;break;case "gloss":p.glossColor=color;break;case "shadow":p.shadowColor=color;break;case "mesh":p.meshColor=color;break;case "inner":p.innerColor=color;break;default:throw new ArgumentException("unknown hair color field");}
                h.Hair.ChangeSettingHairColor(slot,true,true,true);h.Hair.ChangeSettingHairOutlineColor(slot);h.Hair.ChangeSettingHairGlossColor(slot,0);h.Hair.ChangeSettingHairShadowColor(slot);h.Hair.ChangeSettingHairMeshColor(slot);h.Hair.ChangeSettingHairInnerColor(slot);
            }else if(target=="eye") {
                var parts=h.FileFace.pupil;if(slot<0||slot>=parts.Length||channel<0||channel>2)throw new ArgumentException("eye slot/channel out of range");
                var p=parts[slot];if(channel==0)p.eye01Color=color;else if(channel==1)p.eye02Color=color;else p.eye03Color=color;
                h.Face.ChangeSettingEyeColor();
            }else if(target=="clothes") {
                var parts=h.Coorde.Now.Clothes.parts;if(slot<0||slot>=parts.Length||channel<0||channel>=parts[slot].colorInfo.Length)throw new ArgumentException("clothes slot/channel out of range");
                parts[slot].colorInfo[channel].baseColor=color;h.Cloth.AddUpdateClothesFlagsFull();h.Cloth.CreateClothesTexture(true,slot,true);
            }else if(target=="accessory") {
                var parts=h.Coorde.Now.Accessory.parts;if(slot<0||slot>=parts.Length||channel<0||channel>=parts[slot].color.Length)throw new ArgumentException("accessory slot/channel out of range");
                parts[slot].color[channel]=color;h.Acs.ChangeAccessoryColor(slot);
            }else if(target=="eyebrow") {h.Face.ChangeSettingEyebrowColor(0,new Il2CppSystem.Nullable<Color>(color));h.Face.ChangeSettingEyebrowColor(1,new Il2CppSystem.Nullable<Color>(color));}
            else throw new ArgumentException("unknown color target");
            SyncCoordinate(h);
            return Ok(new{target,slot,channel,color=C(color)});
        }
        if(action=="camera") {
            var c=custom.__camCtrl;
            if(method=="POST"){
                Vector3? pos=j.TryGetProperty("position",out var p)?Vec(p):null;
                Vector3? rot=j.TryGetProperty("rotation",out var r)?Vec(r):null;
                Vector3? dir=j.TryGetProperty("direction",out var d)?Vec(d):null;
                float? fov=j.TryGetProperty("fov",out var f)?f.GetSingle():null;
                if(fov.HasValue&&(!float.IsFinite(fov.Value)||fov<5||fov>100))throw new ArgumentException("fov must be 5..100");
                if(pos.HasValue)c.CameraPos=pos.Value;if(rot.HasValue)c.CameraRot=rot.Value;if(dir.HasValue)c.CameraDir=dir.Value;if(fov.HasValue)c.CameraFov=fov.Value;
            }else if(method!="GET")return new(405,new{error="method not allowed"});
            return Ok(new{position=V(c.CameraPos),rotation=V(c.CameraRot),direction=V(c.CameraDir),fov=c.CameraFov});
        }
        if(method=="POST" && action=="save") {
            var path=CardPath(Str(j,"file"));if(File.Exists(path))throw new ArgumentException("refusing to overwrite an existing card");
            h.Data.SaveCharaFile(path);
            // SaveCharaFile may emit only the serialized card payload. A .png
            // without a PNG header cannot be previewed or loaded by normal
            // card pickers. Never leave such a file in the user's card folder.
            var pngSignature=new byte[]{137,80,78,71,13,10,26,10};
            var valid=false;
            using(var stream=File.OpenRead(path)) {
                var header=new byte[8];
                valid=stream.Read(header,0,header.Length)==header.Length && header.SequenceEqual(pngSignature);
            }
            if(!valid) {
                File.Delete(path);
                return new(501,new{error="direct save did not produce a PNG card; use the native capture/save workflow"});
            }
            return Ok(new{saved=path,bytes=new FileInfo(path).Length});
        }
        if(method=="POST"&&action=="verify-card")return CardInspect.Verify(j,h);
        var kit=Kits.Execute(method,action,j,h);
        if(kit.HasValue)return kit.Value;
        var batch=CreatorApiBatch.Execute(method,action,j,h);
        if(batch.HasValue)return batch.Value;
        var extended=CreatorApiEx.Execute(method,action,j,h,custom);
        if(extended.HasValue)return extended.Value;
        return new(404,new{error="unknown creator endpoint"});
    }
}
