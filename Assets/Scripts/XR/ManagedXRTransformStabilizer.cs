using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Managed-code replacement for XRI's XRTransformStabilizer.
///
/// XRI 3.0.10 routes its stabilization helpers through Burst function pointers.
/// On affected editor/runtime combinations those pointers can fail to compile and
/// throw every frame. This component keeps the same serialized field names and
/// stabilization behavior while avoiding that Burst-only call path.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("XR/Managed XR Transform Stabilizer")]
[DefaultExecutionOrder(-29985)] // Matches XRInteractionUpdateOrder.k_TransformStabilizer.
public sealed class ManagedXRTransformStabilizer : MonoBehaviour
{
    private const float FrameTimeAt90Hz = 1f / 90f;

    [SerializeField]
    [Tooltip("The Transform whose position and rotation will be matched and stabilized.")]
    private Transform m_Target;

    [SerializeField]
    [Tooltip("Optional ray provider used to stabilize the ray endpoint.")]
    private Object m_AimTargetObject;

    [SerializeField]
    [Tooltip("Read and apply poses in local space instead of world space.")]
    private bool m_UseLocalSpace;

    [SerializeField]
    [Tooltip("Maximum angular distance in degrees over which stabilization is applied.")]
    private float m_AngleStabilization = 20f;

    [SerializeField]
    [Tooltip("Maximum positional distance in meters over which stabilization is applied.")]
    private float m_PositionStabilization = 0.25f;

    private Transform _cachedTransform;
    private IXRRayProvider _aimTarget;

    private void Awake()
    {
        _cachedTransform = transform;
        _aimTarget = m_AimTargetObject as IXRRayProvider;
    }

    private void OnEnable()
    {
        if (_cachedTransform == null)
            _cachedTransform = transform;

        _aimTarget = m_AimTargetObject as IXRRayProvider;
        if (m_Target == null)
            return;

        ReadPose(m_Target, m_UseLocalSpace, out Vector3 position, out Quaternion rotation);
        WritePose(_cachedTransform, m_UseLocalSpace, position, rotation);
    }

    private void Update()
    {
        if (m_Target == null)
            return;

        if (_aimTarget != null && m_AimTargetObject == null)
            _aimTarget = null;

        ReadPose(_cachedTransform, m_UseLocalSpace, out Vector3 currentPosition, out Quaternion currentRotation);
        ReadPose(m_Target, m_UseLocalSpace, out Vector3 targetPosition, out Quaternion targetRotation);

        float localScale = m_UseLocalSpace ? Mathf.Abs(_cachedTransform.lossyScale.x) : 1f;
        localScale = Mathf.Max(0.01f, localScale);

        Vector3 resultPosition = StabilizePosition(
            currentPosition,
            targetPosition,
            Time.deltaTime,
            m_PositionStabilization * localScale);

        Quaternion resultRotation = _aimTarget != null && !m_UseLocalSpace
            ? StabilizeRayRotation(
                currentPosition,
                resultPosition,
                currentRotation,
                targetRotation,
                _aimTarget.rayEndPoint,
                Time.deltaTime,
                m_AngleStabilization,
                localScale)
            : StabilizeRotation(
                currentRotation,
                targetRotation,
                Time.deltaTime,
                m_AngleStabilization);

        WritePose(_cachedTransform, m_UseLocalSpace, resultPosition, resultRotation);
    }

    private static Vector3 StabilizePosition(
        Vector3 start,
        Vector3 target,
        float deltaTime,
        float stabilization)
    {
        if (stabilization <= 0f)
            return target;

        float distance = Vector3.Distance(start, target);
        float lerp = CalculateStabilizedLerp(distance / stabilization, deltaTime);
        return Vector3.LerpUnclamped(start, target, lerp);
    }

    private static Quaternion StabilizeRotation(
        Quaternion start,
        Quaternion target,
        float deltaTime,
        float stabilization)
    {
        if (stabilization <= 0f)
            return target;

        float angle = Quaternion.Angle(start, target);
        float lerp = CalculateStabilizedLerp(angle / stabilization, deltaTime);
        return Quaternion.SlerpUnclamped(start, target, lerp);
    }

    private static Quaternion StabilizeRayRotation(
        Vector3 currentPosition,
        Vector3 resultPosition,
        Quaternion currentRotation,
        Quaternion targetRotation,
        Vector3 rayEnd,
        float deltaTime,
        float angleStabilization,
        float localScale)
    {
        if (angleStabilization <= 0f)
            return targetRotation;

        float rayLength = Vector3.Distance(rayEnd, currentPosition);
        Vector3 currentForward = currentRotation * Vector3.forward;
        Vector3 currentUp = currentRotation * Vector3.up;
        Vector3 direction = currentPosition + currentForward * rayLength - resultPosition;

        Quaternion endpointRotation = direction.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(direction, currentUp)
            : currentRotation;

        float scaleFactor = 1f + Mathf.Log(Mathf.Max(rayLength / localScale, 1f));
        float endpointStabilization = angleStabilization * Mathf.Clamp(scaleFactor, 1f, 3f);
        float regularDistance = Quaternion.Angle(currentRotation, targetRotation) / angleStabilization;
        float endpointDistance = Quaternion.Angle(endpointRotation, targetRotation) / endpointStabilization;

        if (endpointDistance < regularDistance)
        {
            float endpointLerp = CalculateStabilizedLerp(endpointDistance, deltaTime * scaleFactor);
            return Quaternion.SlerpUnclamped(endpointRotation, targetRotation, endpointLerp);
        }

        float regularLerp = CalculateStabilizedLerp(regularDistance, deltaTime * scaleFactor);
        return Quaternion.SlerpUnclamped(currentRotation, targetRotation, regularLerp);
    }

    private static float CalculateStabilizedLerp(float distance, float timeSlice)
    {
        if (distance >= 1f)
            return 1f;
        if (distance <= 0f)
            return 0f;

        float doubleFrameLerp = distance - distance * distance;
        float tripleFrameLerp = doubleFrameLerp * doubleFrameLerp;
        float localTimeSlice = Mathf.Max(0f, timeSlice) / FrameTimeAt90Hz;
        float firstSlice = Mathf.Clamp01(localTimeSlice);
        float secondSlice = Mathf.Clamp01(localTimeSlice - 1f);
        float thirdSlice = Mathf.Clamp01(localTimeSlice - 2f);

        return distance * firstSlice +
               doubleFrameLerp * secondSlice +
               tripleFrameLerp * thirdSlice;
    }

    private static void ReadPose(
        Transform source,
        bool localSpace,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = localSpace ? source.localPosition : source.position;
        rotation = localSpace ? source.localRotation : source.rotation;
    }

    private static void WritePose(
        Transform destination,
        bool localSpace,
        Vector3 position,
        Quaternion rotation)
    {
        if (localSpace)
            destination.SetLocalPositionAndRotation(position, rotation);
        else
            destination.SetPositionAndRotation(position, rotation);
    }
}
