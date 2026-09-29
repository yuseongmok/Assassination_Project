using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoopPrototype.Editor {
public static class PrototypeMapBuilder {
    const string Root="Assets/CoopPrototype";
    [Serializable] public class Layout { public Item[] items; public Door[] doors; public Marker[] markers; public CCTV[] cameras; }
    [Serializable] public class Item { public string name,material,group; public float x,y,z,w,h,d,rotation; public bool collider; }
    [Serializable] public class Door { public string name,axis; public float x,z,width; }
    [Serializable] public class Marker { public string name,kind; public float x,y,z; }
    [Serializable] public class CCTV { public string name; public float x,y,z,yaw; }
    static Dictionary<string,Material> mats;
    static Dictionary<string,Transform> groups;
    static Transform root;
    static Material Mat(string key,string hex) {
        string path=Root+"/Materials/"+key+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null) { m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path); }
        ColorUtility.TryParseHtmlString(hex,out var color);m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.15f);
        if(key=="lamp" || key=="screen") { m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*1.3f); }
        EditorUtility.SetDirty(m);return m;
    }
    static Transform Group(string name) {
        if(!groups.ContainsKey(name)) {var g=new GameObject(name);g.transform.SetParent(root);groups[name]=g.transform;}
        return groups[name];
    }
    static GameObject Cube(string name,Vector3 pos,Vector3 size,string material,string group,bool collision=true) {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(Group(group));
        g.transform.position=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mats[material];
        if(!collision) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }
    [MenuItem("Tools/Coop Prototype/Create New Test Map")]
    public static void CreateInteractive() {
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build(false);
    }
    // Batch entrypoint. Existing scenes are left untouched; always creates a unique new scene.
    public static void BuildBatch() {
        try { Build(true);EditorApplication.Exit(0); }
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Build(bool batch) {
        Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Scenes");AssetDatabase.Refresh();
        var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(Root+"/Data/PrototypeLayout.json"));
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        root=new GameObject("Coop Office Prototype").transform;groups=new Dictionary<string,Transform>();mats=new Dictionary<string,Material>();
        string[,] palette={{"wall","#CBD2CF"},{"trim","#354850"},{"ground","#6E817D"},{"concrete","#8E9C98"},{"lobby","#B7B9A5"},{"security","#819B9B"},{"office","#BDAF98"},{"corridor","#B1BDB9"},{"storage","#859D8E"},{"archive","#879AA8"},{"target","#B5A58D"},{"wood","#967453"},{"dark","#293C45"},{"screen","#46C4BB"},{"lamp","#FFE3AB"},{"amber","#E9AF58"},{"green","#496A52"},{"guard","#DC786C"}};
        for(int i=0;i<palette.GetLength(0);i++) mats[palette[i,0]]=Mat(palette[i,0],palette[i,1]);
        foreach(var i in layout.items) {
            var obj=Cube(i.name,new Vector3(i.x,i.y,i.z),new Vector3(i.w,i.h,i.d),i.material,i.group,i.collider);
            obj.transform.rotation=Quaternion.Euler(0,i.rotation,0);
        }
        foreach(var d in layout.doors) {
            var size=d.axis=="x"?new Vector3(d.width-.12f,2.82f,.13f):new Vector3(.13f,2.82f,d.width-.12f);
            var obj=Cube(d.name+" [E]",new Vector3(d.x,1.41f,d.z),size,"security","Doors");
            obj.AddComponent<PrototypeDoor>();
        }
        foreach(var marker in layout.markers) {
            var anchor=new GameObject(marker.name);anchor.transform.SetParent(Group("Gameplay anchors"));anchor.transform.position=new Vector3(marker.x,marker.y,marker.z);
            if(marker.kind=="guard" || marker.kind=="target") {
                var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);body.name=marker.name+" (placement only)";body.transform.SetParent(anchor.transform);
                body.transform.localPosition=Vector3.up*.9f;body.transform.localScale=new Vector3(.55f,.9f,.55f);
                body.GetComponent<Renderer>().sharedMaterial=mats[marker.kind=="guard"?"guard":"amber"];
                UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            }
        }
        var cctv=new List<Transform>();
        foreach(var c in layout.cameras) {
            var anchor=new GameObject(c.name);anchor.transform.SetParent(Group("CCTV viewpoints"));anchor.transform.position=new Vector3(c.x,c.y,c.z);anchor.transform.rotation=Quaternion.Euler(25,c.yaw,0);cctv.Add(anchor.transform);
            var model=Cube(c.name+" housing",anchor.transform.position+Vector3.up*.18f,new Vector3(.35f,.18f,.5f),"dark","CCTV models",false);model.transform.rotation=anchor.transform.rotation;
        }
        var sun=new GameObject("Daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.color=new Color(1,.94f,.83f);sun.transform.rotation=Quaternion.Euler(48,-30,0);sun.shadows=LightShadows.Soft;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.69f,.75f);RenderSettings.fog=false;
        foreach(var pos in new[]{new Vector3(-8,2.9f,3),new Vector3(0,2.9f,3),new Vector3(8,2.9f,3),new Vector3(-8,2.9f,12),new Vector3(0,2.9f,12),new Vector3(8,2.9f,12),new Vector3(0,2.9f,7.5f)}) {
            var l=new GameObject("Interior fill").AddComponent<Light>();l.transform.SetParent(Group("Lighting"));l.transform.position=pos;l.type=LightType.Point;l.range=9;l.intensity=2;l.color=new Color(1,.92f,.78f);l.shadows=LightShadows.None;
        }
        var player=new GameObject("Prototype Explorer (local test only)");player.layer=2;player.transform.position=new Vector3(-8,.08f,-5);
        var controller=player.AddComponent<CharacterController>();controller.height=1.8f;controller.radius=.28f;controller.center=Vector3.up*.9f;controller.stepOffset=.35f;controller.skinWidth=.025f;
        var cameraObject=new GameObject("Explorer Camera");cameraObject.tag="MainCamera";cameraObject.transform.SetParent(player.transform);cameraObject.transform.localPosition=new Vector3(0,1.65f,0);
        var cam=cameraObject.AddComponent<Camera>();cam.nearClipPlane=.05f;cam.farClipPlane=160;cam.fieldOfView=75;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.55f,.68f,.73f);cameraObject.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var explorer=player.AddComponent<PrototypeExplorer>();explorer.view=cam;explorer.cctvViews=cctv.ToArray();explorer.roof=Group("Roof");
        Physics.SyncTransforms();
        ValidateLayout();
        var scenePath=AssetDatabase.GenerateUniqueAssetPath(Root+"/Scenes/CoopOffice_Prototype.unity");
        EditorSceneManager.SaveScene(scene,scenePath);AssetDatabase.SaveAssets();
        string export=Environment.GetEnvironmentVariable("COOP_MAP_OUTPUT");
        if(!string.IsNullOrEmpty(export)) {
            Directory.CreateDirectory(export);Group("Roof").gameObject.SetActive(false);
            RenderPreview(cam,new Vector3(34,39,-48),new Vector3(0,0,-3),34,Path.Combine(export,"첫맵_3D미리보기.png"));
            RenderPreview(cam,new Vector3(0,60,-5.5f),new Vector3(0,0,-5.5f),27,Path.Combine(export,"첫맵_실제탑뷰.png"));
            Group("Roof").gameObject.SetActive(true);cam.transform.localPosition=new Vector3(0,1.65f,0);cam.transform.localRotation=Quaternion.identity;cam.orthographic=false;cam.enabled=true;
            EditorSceneManager.SaveScene(scene,scenePath);
            File.WriteAllText(Path.Combine(export,"unity-scene-path.txt"),scenePath);
        }
        Debug.Log("COOP_MAP_SUCCESS "+scenePath+"; "+layout.items.Length+" objects, "+layout.doors.Length+" doors. Layout ray tests passed.");
        if(!batch) {Selection.activeGameObject=root.gameObject;SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(0,1,-4),new Vector3(40,12,50)),false);}
    }
    static void ValidateLayout() {
        var origin=new Vector3(8,6.65f,-24);var guard=new Vector3(8,1.3f,3);var dir=guard-origin;
        if(Physics.Raycast(origin,dir.normalized,out var hit,dir.magnitude))throw new Exception("Primary sniper lane blocked by "+hit.collider.name);
        var target=new Vector3(9.8f,1.3f,12.6f);dir=target-origin;
        if(!Physics.Raycast(origin,dir.normalized,dir.magnitude))throw new Exception("Rear target should be protected from direct tower sight.");
        foreach(var pos in new[]{new Vector3(-8,1,-5),new Vector3(8,6,-24),new Vector3(-8,1,7.5f),new Vector3(6,1,12)})
            if(Physics.CheckCapsule(pos+Vector3.down*.6f,pos+Vector3.up*.5f,.27f))throw new Exception("Blocked test position "+pos);
    }
    static void RenderPreview(Camera cam,Vector3 pos,Vector3 target,float size,string path) {
        cam.transform.position=pos;cam.transform.LookAt(target);cam.orthographic=true;cam.orthographicSize=size;cam.backgroundColor=new Color(.08f,.13f,.17f);
        var rt=new RenderTexture(1800,1400,24,RenderTextureFormat.ARGB32);rt.Create();
        var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
        RenderPipeline.SubmitRenderRequest(cam,request);
        var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=old;
        UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }
}}
