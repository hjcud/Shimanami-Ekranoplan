
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class AirplaneState : UdonSharpBehaviour
{
    // Other Scripts
    public Throttle_Controll Throttle_Controll;
    public Controller_Controll Controller_Controll;

    // Debug Text Box
    /*
    public Text Plane_Speed;
    public Text Plane_Yaw;
    public Text Plane_Pitch;
    public Text Plane_Roll;
    public Text Plane_drag;
    public Text Plane_acceleration;
    public Text Plane_Lift;
    public Text Wolrd_x;
    public Text Wolrd_y;
    public Text Wolrd_z;
    */

    public Transform mapTransform = null;

    // VectorDebug
    public LineRenderer DebugLine_1 = null;
    public LineRenderer DebugLine_2 = null;

    // Airplane Transform
    [UdonSynced] public Vector3 airplaneVelocity = Vector3.zero;    // Current velocity vector
    [UdonSynced] public Vector3 airplaneRotation = Vector3.zero;    // Rotation in Euler angles
    [UdonSynced] public Vector3 worldRotation = Vector3.zero;       // Rotation in World rotation

    [UdonSynced] public Vector3 lift = Vector3.zero;
    [UdonSynced] public Vector3 drag = Vector3.zero;
    [UdonSynced] public Vector3 forwardForce = Vector3.zero;
    [UdonSynced] public Vector3 acceleration = Vector3.zero;

    // Airplane Properties
    private float mass = 286000f;
    private float dragCoefficient = 0.8f;
    private float liftCoefficient = 0.01f;
    private float dragFactor = 49; // 1/2 * Air Density(1.225) * Surface Area(80 m^2)
    
    public void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        CalculateVelocity(dt);
    }

    public void CalculateVelocity(float dt)
    {
        // Get values form other scripts
        float throttle = Throttle_Controll.throttlePower;   // -0.3 ~ 1.25
        float yaw = -Controller_Controll.yaw;                // -0.5 ~ 0.5 (-L / +R)
        float pitch = Controller_Controll.pitch;            // -0.5 ~ 0.5 (-D / +U)
        float roll = Controller_Controll.roll;              // -0.5 ~ 0.5 (-L / +R)

        Vector3 direction = new Vector3(yaw, pitch, 1f).normalized;

        Vector3 rotationAxis = new Vector3(0f, 0f, 1f);
        Quaternion rotation = Quaternion.AngleAxis(roll, rotationAxis);

        // Final airplane velocity
        airplaneVelocity = rotation * direction * throttle;

        if (airplaneVelocity == Vector3.zero) return;
        DebugLine_1.SetPosition(1, (airplaneVelocity * 0.25f) + DebugLine_1.GetPosition(0));

        UpdateState(dt, yaw, pitch, roll, throttle);
        RequestSerialization();
        //********** Have to Fix **********
        /*
        float speed = airplaneVelocity.magnitude;
        float force = dragFactor * Mathf.Pow(speed, 2);

        float thrustForce = throttle * 5000000;

        // Calculate lift force
        float liftForce = force * liftCoefficient / mass;
        // Calculate drag force
        float dragForce = force * dragCoefficient / mass;

        // Update airplane velocity based on forces
        UpdateVelocity(dt, thrustForce, liftForce, dragForce, yaw, pitch, roll);

        RequestSerialization();
        UpdatePlaneData();
        */
    }

    private void UpdateState(float dt, float yaw, float pitch, float roll, float throttle)
    {
        if (mapTransform == null || airplaneVelocity == Vector3.zero) return;


        airplaneRotation.x += pitch * dt * 1f; // Pitch adjustment
        airplaneRotation.y += -yaw * dt * 15f;   // Yaw adjustment
        airplaneRotation.z += roll * dt * 30f;  // Roll adjustment
        mapTransform.rotation = Quaternion.Euler(airplaneRotation);
        // mapTransform.position -= new Vector3(0f, 0f, throttle * 10f * dt); 월드 움직이면 안됨. 타일 움직이는 시스템으로 바꿀 것

        if (mapTransform.eulerAngles == Vector3.zero) return;
        DebugLine_2.SetPosition(1, (mapTransform.forward * 0.25f) + DebugLine_2.GetPosition(0));
    }

    /*
    private void UpdateVelocity(float dt, float thrustForce, float liftForce, float dragForce, float yaw, float pitch, float roll)
    {
        // Calculate directional changes based on yaw, pitch, and roll inputs
        airplaneRotation.x += pitch * dt * 30f; // Pitch adjustment
        airplaneRotation.y += yaw * dt * 30f;   // Yaw adjustment
        airplaneRotation.z += roll * dt * 30f;  // Roll adjustment

        // Forward force calculation
        forwardForce = Vector3.forward * thrustForce;
        
        // Calculate resulting force vector
        lift = Vector3.up * liftForce;
        drag = airplaneVelocity.normalized * dragForce;

        // Sum forces and update velocity
        Vector3 netForce = forwardForce + lift + drag;
        acceleration = netForce / mass;
        airplaneVelocity += acceleration * dt;

        // Apply rotation to the velocity vector
        Quaternion rotation = Quaternion.Euler(airplaneRotation);
        airplaneVelocity = rotation * airplaneVelocity;

    }
    */
    
    /*
    public void UpdatePlaneData()
    {
        Plane_Speed.text = airplaneVelocity.magnitude.ToString();

        Plane_Yaw.text = airplaneRotation.y.ToString();
        Plane_Pitch.text = airplaneRotation.x.ToString();
        Plane_Roll.text = airplaneRotation.z.ToString();

        Plane_drag.text = drag.magnitude.ToString();
        Plane_acceleration.text = acceleration.magnitude.ToString();
        // Plane_G.text = localGForce.ToString();
        Plane_Lift.text = lift.ToString();

        //World_Pitch.text = worldPitch.ToString();

        mapTransform.rotation = Quaternion.Euler(airplaneRotation);
        mapTransform.position += (airplaneRotation.x < 0 ? -airplaneVelocity : airplaneVelocity) * Time.fixedDeltaTime * 0.05f;
        //mapTransform.position = new Vector3(0f, airplaneRotation.x > 0 ? mapTransform.position.y - Vector3.Magnitude(lift) : mapTransform.position.y + Vector3.Magnitude(lift), 0f);
    }
    */
}
