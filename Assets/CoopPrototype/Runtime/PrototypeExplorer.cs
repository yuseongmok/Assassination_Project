using UnityEngine;
using UnityEngine.InputSystem;
namespace CoopPrototype {
[RequireComponent(typeof(CharacterController))]
public class PrototypeExplorer : MonoBehaviour {
    public Camera view;
    public Transform[] cctvViews;
    public Transform roof;
    CharacterController body; float pitch, verticalSpeed; int cctvIndex = -1;
    Vector3 viewHome; GUIStyle hud;
    void Awake() { body = GetComponent<CharacterController>(); viewHome = view.transform.localPosition; }
    void Start() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    void Teleport(Vector3 pos) {
        body.enabled = false; transform.position = pos; transform.rotation = Quaternion.identity;
        body.enabled = true; pitch = 0; verticalSpeed = 0; cctvIndex = -1;
    }
    void Update() {
        var k = Keyboard.current; var m = Mouse.current;
        if (k == null || m == null) return;
        if(k.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if(m.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) {
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }
        if(k.digit1Key.wasPressedThisFrame) Teleport(new Vector3(-8,.08f,-5));
        if(k.digit2Key.wasPressedThisFrame) Teleport(new Vector3(8,5.08f,-24));
        if(k.cKey.wasPressedThisFrame && cctvViews != null && cctvViews.Length > 0)
            cctvIndex = cctvIndex >= cctvViews.Length - 1 ? -1 : cctvIndex + 1;
        if(k.rKey.wasPressedThisFrame && roof != null) roof.gameObject.SetActive(!roof.gameObject.activeSelf);
        if(Cursor.lockState != CursorLockMode.Locked) return;
        if(cctvIndex >= 0) {
            view.transform.SetPositionAndRotation(cctvViews[cctvIndex].position,cctvViews[cctvIndex].rotation);
            view.fieldOfView = 75; return;
        }
        bool crouch = k.leftCtrlKey.isPressed;
        body.height = crouch ? 1.2f : 1.8f; body.center = Vector3.up * body.height * .5f;
        var look = m.delta.ReadValue() * .1f;
        transform.Rotate(0,look.x,0); pitch = Mathf.Clamp(pitch - look.y,-85,85);
        view.transform.localPosition = crouch ? new Vector3(0,1.02f,0) : viewHome;
        view.transform.localRotation = Quaternion.Euler(pitch,0,0);
        float x=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0), z=(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0);
        Vector3 move=Vector3.ClampMagnitude(transform.right*x + transform.forward*z,1);
        float speed=crouch?2:(k.leftShiftKey.isPressed?6:3.5f);
        if(body.isGrounded && verticalSpeed < 0) verticalSpeed=-2;
        if(body.isGrounded && k.spaceKey.wasPressedThisFrame && !crouch) verticalSpeed=5;
        verticalSpeed += Physics.gravity.y * Time.deltaTime;
        body.Move((move*speed + Vector3.up*verticalSpeed)*Time.deltaTime);
        if(transform.position.y < -10) Teleport(new Vector3(-8,.08f,-5));
        view.fieldOfView=m.rightButton.isPressed?25:75;
        if(k.eKey.wasPressedThisFrame && Physics.Raycast(view.transform.position,view.transform.forward,out var hit,3)) {
            var door=hit.collider.GetComponent<PrototypeDoor>(); if(door != null) door.Toggle();
            else CloseNearbyOpenDoor();
        }
        else if(k.eKey.wasPressedThisFrame) CloseNearbyOpenDoor();
    }
    void CloseNearbyOpenDoor() {
        PrototypeDoor nearest=null;float distance=3f;
        foreach(var door in Object.FindObjectsByType<PrototypeDoor>(FindObjectsSortMode.None)) {
            float d=Vector3.Distance(view.transform.position,door.InteractionPosition);
            if(door.isOpen && d<distance) { nearest=door;distance=d; }
        }
        if(nearest!=null) nearest.Toggle();
    }
    void OnGUI() {
        if(hud==null) hud=new GUIStyle(GUI.skin.label){fontSize=17,normal={textColor=Color.white}};
        GUI.Box(new Rect(14,14,730,112),"");
        GUI.Label(new Rect(28,20,700,30),"CO-OP OFFICE / Layout prototype - no AI or networking",hud);
        GUI.Label(new Rect(28,48,700,30),"WASD Move | Shift Run | Ctrl Crouch | Space Jump | E Door",hud);
        GUI.Label(new Rect(28,76,700,30),"1 Infiltrator | 2 Tower | RMB Zoom | C CCTV | R Roof | Esc Cursor",hud);
        GUI.Label(new Rect(28,Screen.height-45,700,30),cctvIndex>=0?"CCTV "+(cctvIndex+1)+" / Press C to cycle back":"Exploration mode / Guards are stationary placement markers",hud);
        if(cctvIndex<0) GUI.Label(new Rect(Screen.width/2-5,Screen.height/2-12,25,30),"+",hud);
    }
}}
