using Godot;
using System;

public partial class PlatformerController2D : CharacterBody2D
{
    [Signal]
    public delegate void JumpedEventHandler(bool isGroundJump);

    [Signal]
    public delegate void HitGroundEventHandler();

    [Export]
    private string inputLeft = "ui_left";

    [Export]
    private string inputRight = "ui_right";

    [Export]
    private string inputJump = "ui_up";

    private const float DEFAULT_MAX_JUMP_HEIGHT = 150;
    private const float DEFAULT_MIN_JUMP_HEIGHT = 60;
    private const float DEFAULT_DOUBLE_JUMP_HEIGHT = 100;
    private const float DEFAULT_JUMP_DURATION = 0.3f;

    [Export]
    public float maxJumpHeight = DEFAULT_MAX_JUMP_HEIGHT;

    [Export]
    public float minJumpHeight = DEFAULT_MIN_JUMP_HEIGHT;

    [Export]
    public float doubleJumpHeight = DEFAULT_DOUBLE_JUMP_HEIGHT;

    [Export]
    public float jumpDuration = DEFAULT_JUMP_DURATION;

    [Export]
    public float fallingGravityMultiplier = 1.5f;

    [Export]
    public int numberOfDoubleJumps = 1;

    [Export]
    public float maxAcceleration = 10000;

    [Export]
    public float friction = 20;

    [Export]
    public float coyoteTime = 0.1f;

    [Export]
    public float jumpBuffer = 0.1f;

    // Calculated values
    private float defaultGravity;
    private float jumpVelocity;
    private float doubleJumpVelocity;
    private float releaseGravityMultiplier;

    private int jumpsLeft;
    private bool isHoldingJump = false;

    private enum JumpType
    {
        NONE,
        GROUND,
        AIR
    }

    private JumpType currentJumpType = JumpType.NONE;

    private bool wasOnGround;

    private Vector2 acc = new Vector2();

    private bool isCoyoteTimeEnabled => coyoteTime > 0;
    private bool isJumpBufferEnabled => jumpBuffer > 0;

    private Timer coyoteTimer;
    private Timer jumpBufferTimer;

    public override void _Ready()
    {
        defaultGravity = CalculateGravity(maxJumpHeight, jumpDuration);
        jumpVelocity = CalculateJumpVelocity(maxJumpHeight, jumpDuration);
        doubleJumpVelocity = CalculateJumpVelocity2(doubleJumpHeight, defaultGravity);
        releaseGravityMultiplier = CalculateReleaseGravityMultiplier(
            jumpVelocity,
            minJumpHeight,
            defaultGravity
        );

        if (isCoyoteTimeEnabled)
        {
            coyoteTimer = new Timer();
            AddChild(coyoteTimer);
            coyoteTimer.WaitTime = coyoteTime;
            coyoteTimer.OneShot = true;
        }

        if (isJumpBufferEnabled)
        {
            jumpBufferTimer = new Timer();
            AddChild(jumpBufferTimer);
            jumpBufferTimer.WaitTime = jumpBuffer;
            jumpBufferTimer.OneShot = true;
        }

        base._Ready();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed(inputJump))
        {
            isHoldingJump = true;
            if (CanGroundJump() || CanDoubleJump())
            {
                Jump();
            }
            StartJumpBufferTimer();
        }

        if (@event.IsActionReleased(inputJump))
        {
            isHoldingJump = false;
        }
    }

    void UpdateHorizontalAcceleration()
    {
        acc.X = 0;
        if (Input.IsActionPressed(inputLeft))
        {
            acc.X = -maxAcceleration;
        }

        if (Input.IsActionPressed(inputRight))
        {
            acc.X = maxAcceleration;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        bool isOnGround = IsFeetOnGround();
        UpdateHorizontalAcceleration();

        if (isOnGround && currentJumpType == JumpType.NONE)
        {
            StartCoyoteTimer();
            jumpsLeft = numberOfDoubleJumps;
        }

        // Upon landing on the ground
        if (!wasOnGround && isOnGround)
        {
            currentJumpType = JumpType.NONE;

            EmitSignal("HitGround");

            if (IsJumpBufferTimerRunning())
            {
                GroundJump();
            }
        }

        var gravity = ApplyGravityMultipliersTo(defaultGravity);
        acc.Y = gravity;

        Velocity = new Vector2(Velocity.X * 1 / (1 + ((float)delta * friction)), Velocity.Y);
        Velocity += acc * (float)delta;

        wasOnGround = IsFeetOnGround();
        MoveAndSlide();
    }

    private void StartCoyoteTimer()
    {
        if (isCoyoteTimeEnabled)
        {
            coyoteTimer.Start();
        }
    }

    private void StartJumpBufferTimer()
    {
        if (isJumpBufferEnabled)
        {
            jumpBufferTimer.Start();
        }
    }

    private bool IsCoyoteTimerRunning()
    {
        return isCoyoteTimeEnabled && !coyoteTimer.IsStopped();
    }

    private bool IsJumpBufferTimerRunning()
    {
        return isJumpBufferEnabled && !jumpBufferTimer.IsStopped();
    }

    private bool CanGroundJump()
    {
        return IsCoyoteTimerRunning() || IsFeetOnGround();
    }

    private bool CanDoubleJump()
    {
        return jumpsLeft > 0 && !CanGroundJump();
    }

    private bool IsFeetOnGround()
    {
        return (IsOnFloor() && defaultGravity >= 0) || (IsOnCeiling() && defaultGravity <= 0);
    }

    private void Jump()
    {
        if (CanGroundJump())
        {
            GroundJump();
        }
        else if (CanDoubleJump())
        {
            DoubleJump();
        }
    }

    private void DoubleJump()
    {
        if (jumpsLeft > 0)
        {
            jumpsLeft -= 1;
            Velocity = new Vector2(Velocity.X, -doubleJumpVelocity);
            currentJumpType = JumpType.AIR;
            EmitSignal("Jumped", false);
        }
        else
        {
            GD.Print("warning called double jump with no jumps left");
        }
    }

    private void GroundJump()
    {
        Velocity = new Vector2(Velocity.X, -jumpVelocity);
        currentJumpType = JumpType.GROUND;
        jumpsLeft = numberOfDoubleJumps;
        coyoteTimer.Stop();
        EmitSignal("Jumped", true);
    }

    private float ApplyGravityMultipliersTo(float gravity)
    {
        if (Velocity.Y * Mathf.Sign(defaultGravity) > 0)
        {
            gravity *= fallingGravityMultiplier;
        }
        else if (Velocity.Y * Mathf.Sign(defaultGravity) < 0)
        {
            if (!isHoldingJump && !currentJumpType.Equals(JumpType.AIR))
            {
                gravity *= releaseGravityMultiplier;
            }
        }

        return gravity;
    }

    private float CalculateGravity(float maxJumpHeight, float jumpDuration)
    {
        return 2 * maxJumpHeight / Mathf.Pow(jumpDuration, 2);
    }

    private float CalculateJumpVelocity(float maxJumpHeight, float jumpDuration)
    {
        return 2 * maxJumpHeight / jumpDuration;
    }

    private float CalculateJumpVelocity2(float maxJumpHeight, float gravity)
    {
        return Mathf.Sqrt(Mathf.Abs(2 * gravity * maxJumpHeight)) * Mathf.Sign(maxJumpHeight);
    }

    private float CalculateReleaseGravityMultiplier(
        float jumpVelocity,
        float minJumpHeight,
        float gravity
    )
    {
        var releaseGravity = Mathf.Pow(jumpVelocity, 2) / (2 * minJumpHeight);
        return releaseGravity / gravity;
    }
}
