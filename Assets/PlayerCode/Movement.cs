using UnityEngine;
using Unity.Netcode;

/*
    This script provides jumping and movement in Unity 3D - Gatsby
*/

public class Movement : NetworkBehaviour
{
    // Camera Rotation
    public float mouseSensitivity = 2f;
    private float verticalRotation = 0f;
    private float facing;
    private Transform cameraTransform;

    // Ground Movement
    private Animator anim;
    private Rigidbody rb;
    public float MoveSpeed = 5f;
    public float runSpeed = 8f;
    private bool isRunning;
    private float moveHorizontal;
    private float moveForward;

    // Jumping
    public float jumpForce = 10f;
    public float fallMultiplier = 2.5f; // Multiplies gravity when falling down
    public float ascendMultiplier = 2f; // Multiplies gravity for ascending to peak of jump
    private bool isGrounded = true;
    public LayerMask groundLayer;
    private float groundCheckTimer = 0f;
    private float groundCheckDelay = 0.3f;
    private float playerHeight;
    private float raycastDistance;

    public override void OnNetworkSpawn(){
    // Other players' doctors: no camera, no audio, no input
    if (!IsOwner)
    {
        GetComponentInChildren<Camera>().enabled = false;
        GetComponentInChildren<AudioListener>().enabled = false;
        enabled = false;
    }
    }


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponentInChildren<Animator>();
        rb.freezeRotation = true;
        MoveToSpawnPoint();
        facing = transform.eulerAngles.y;
        cameraTransform = GetComponentInChildren<Camera>().transform;

        // Set the raycast to be slightly beneath the player's feet
        playerHeight = GetComponent<CapsuleCollider>().height * transform.localScale.y;
        raycastDistance = (playerHeight / 2) + 0.2f;

        // Hides the mouse
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        moveHorizontal = Input.GetAxisRaw("Horizontal");
        moveForward = Input.GetAxisRaw("Vertical");

        isRunning = Input.GetKey(KeyCode.LeftShift);

        RotateCamera();

        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            Jump();
        }

        // Checking when we're on the ground and keeping track of our ground check delay
        if (groundCheckTimer > 0f)
        {
            groundCheckTimer -= Time.deltaTime;
        }
        else
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
            isGrounded = Physics.Raycast(rayOrigin, Vector3.down, raycastDistance, groundLayer);
        }

        float inputAmount = Mathf.Clamp01(new Vector2(moveHorizontal, moveForward).magnitude);
        float animSpeed = inputAmount * (isRunning ? 1f : 0.5f);
        anim.SetFloat("Speed", animSpeed, 0.1f, Time.deltaTime);
    }

    // Move to this scene's SpawnPoint (spread players out so they don't spawn inside each other).
    // Runs in Start (owner only) so NetworkTransform/Rigidbody are set up and the move isn't overwritten.
    void MoveToSpawnPoint()
    {
        var spawn = GameObject.Find("SpawnPoint");
        if (spawn == null)
            return;

        Vector3 pos = spawn.transform.position + spawn.transform.right * OwnerClientId * 1.5f;
        Quaternion rot = spawn.transform.rotation;

        // Teleport (not a normal move) so other players see us snap there instead of sliding through the level
        var networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (networkTransform != null && IsSpawned)
            networkTransform.Teleport(pos, rot, transform.localScale);
        else
            transform.SetPositionAndRotation(pos, rot);

        rb.position = pos;
        rb.rotation = rot;
        rb.linearVelocity = Vector3.zero;
    }

    void FixedUpdate()
    {
        rb.MoveRotation(Quaternion.Euler(0f, facing, 0f));
        MovePlayer();
        ApplyJumpPhysics();
    }

    void MovePlayer()
    {
        Quaternion facingRotation = Quaternion.Euler(0f, facing, 0f);
        Vector3 movement = (facingRotation * Vector3.right * moveHorizontal + facingRotation * Vector3.forward * moveForward).normalized;

        float speed = isRunning ? runSpeed : MoveSpeed;
        Vector3 targetVelocity = movement * speed;

        // Apply movement to the Rigidbody
        Vector3 velocity = rb.linearVelocity;
        velocity.x = targetVelocity.x;
        velocity.z = targetVelocity.z;
        rb.linearVelocity = velocity;

        // If we aren't moving and are on the ground, stop velocity so we don't slide
        if (isGrounded && moveHorizontal == 0 && moveForward == 0)
        {
            rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        }
    }

    void RotateCamera()
    {
        facing += Input.GetAxis("Mouse X") * mouseSensitivity;

        verticalRotation -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);

        cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
    }

    void Jump()
    {
        isGrounded = false;
        groundCheckTimer = groundCheckDelay;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z); // Initial burst for the jump
    }

    void ApplyJumpPhysics()
    {
        if (rb.linearVelocity.y < 0)
        {
            // Falling: Apply fall multiplier to make descent faster
            rb.linearVelocity += Vector3.up * Physics.gravity.y * fallMultiplier * Time.fixedDeltaTime;
        } // Rising
        else if (rb.linearVelocity.y > 0)
        {
            // Rising: Change multiplier to make player reach peak of jump faster
            rb.linearVelocity += Vector3.up * Physics.gravity.y * ascendMultiplier * Time.fixedDeltaTime;
        }
    }
}
