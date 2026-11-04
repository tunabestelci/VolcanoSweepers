using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public Camera cam;
    public float walkSpeed = 3.6f;
    public float sprintSpeed = 5.8f;
    public float mouseSensitivity = 0.1f;
    public float jumpSpeed = 7f;

    CharacterController cc;
    float pitch;
    float verticalVel;
    float shakeTime, shakeAmount;
    Vector3 camBase;
    Vector3 spawn;
    Terrain terrain;

    public float Speed01 { get; private set; }
    public float Pitch => pitch;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        camBase = cam.transform.localPosition;
        spawn = transform.position;
        terrain = FindFirstObjectByType<Terrain>();
        LockCursor(true);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    float GroundHeight(Vector3 p)
    {
        var field = LavaField.I;
        if (field != null) return field.HeightAt(p);
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        return 0f;
    }

    public void LookAt(Vector3 point)
    {
        Vector3 d = point - cam.transform.position;
        transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
        pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    public void Teleport(Vector3 pos)
    {
        cc.enabled = false;
        transform.position = pos;
        cc.enabled = true;
    }

    public void Shake(float amount, float time)
    {
        shakeAmount = amount;
        shakeTime = time;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (mouse.leftButton.wasPressedThisFrame) LockCursor(true);
            return;
        }

        Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
        transform.Rotate(0f, look.x, 0f);
        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed) input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        float speed = kb.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

        // Dikey hareket: lav yuzeyini (kazildikca alcalir) veya araziyi takip et
        float ground = GroundHeight(transform.position);
        float y = transform.position.y;
        bool grounded = y <= ground + 0.05f && verticalVel <= 0.01f;
        if (grounded && kb.spaceKey.wasPressedThisFrame)
        {
            verticalVel = jumpSpeed;
            grounded = false;
        }
        if (!grounded)
        {
            verticalVel -= 20f * Time.deltaTime;
            move.y = verticalVel;
        }
        else
        {
            verticalVel = 0f;
            move.y = (ground - y) * 12f;   // basamak cikma / yumusak inis
        }
        cc.Move(move * Time.deltaTime);

        // Alandan cok uzaklasma
        Vector3 flat = transform.position - spawn;
        flat.y = 0f;
        if (flat.magnitude > 140f)
        {
            Vector3 back = -flat.normalized * (flat.magnitude - 140f);
            cc.Move(back);
        }

        Speed01 = Mathf.Lerp(Speed01, input.magnitude * (speed / sprintSpeed), 10f * Time.deltaTime);

        // Kamera sarsintisi
        Vector3 shake = Vector3.zero;
        if (shakeTime > 0f)
        {
            shakeTime -= Time.deltaTime;
            shake = Random.insideUnitSphere * shakeAmount;
        }
        cam.transform.localPosition = camBase + shake;
    }
}
