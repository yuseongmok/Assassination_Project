using UnityEngine;
namespace CoopPrototype {
public class PrototypeDoor : MonoBehaviour {
    public bool isOpen;
    Vector3 closedPosition;
    public Vector3 InteractionPosition => transform.parent.TransformPoint(closedPosition);
    void Awake() { closedPosition = transform.localPosition; }
    public void Toggle() { isOpen = !isOpen; }
    void Update() {
        // Lift clear of the entire 2.9m opening instead of clipping through adjacent walls.
        transform.localPosition = Vector3.MoveTowards(transform.localPosition,
            closedPosition + (isOpen ? Vector3.up * 3.4f : Vector3.zero), 4f * Time.deltaTime);
    }
}}
