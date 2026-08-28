using Normal.Realtime;
using UnityEngine;

namespace Jingchen.Mic
{
    [DisallowMultipleComponent]
    internal sealed class LapelMicVisual : MonoBehaviour
    {
        private const string UpperChestBoneName = "Bip01 Spine2";
        private const float RemoteVoiceThreshold = 0.03f;

        private static readonly Color BodyColor = new Color(0.045f, 0.055f, 0.065f, 1f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.20f, 0.22f, 1f);
        private static readonly Color MutedColor = new Color(0.45f, 0.035f, 0.025f, 1f);
        private static readonly Color ReadyColor = new Color(1.0f, 0.48f, 0.02f, 1f);
        private static readonly Color TransmittingColor = new Color(0.02f, 0.85f, 0.18f, 1f);

        private RealtimeAvatar avatar;
        private RealtimeAvatarVoice voice;
        private Transform upperChestBone;
        private GameObject visualRoot;
        private Material bodyMaterial;
        private Material buttonMaterial;
        private Material statusMaterial;
        private bool isLocalAvatar;
        private bool localHandNear;
        private bool localTransmitting;
        private Color appliedStatusColor = Color.clear;

        internal Vector3 InteractionPosition
        {
            get
            {
                CalculatePose(out Vector3 position, out Quaternion rotation);
                return position;
            }
        }

        internal void Initialize(
            RealtimeAvatar realtimeAvatar,
            RealtimeAvatarVoice avatarVoice,
            bool localAvatar)
        {
            avatar = realtimeAvatar;
            voice = avatarVoice;
            isLocalAvatar = localAvatar;

            if (upperChestBone == null)
                upperChestBone = FindDescendantByName(avatar != null ? avatar.transform : null, UpperChestBoneName);

            if (visualRoot == null)
                CreateVisual();

            UpdatePose();
            UpdateStatusColor();
        }

        internal void SetLocalState(bool handNear, bool transmitting)
        {
            if (!isLocalAvatar)
                return;

            localHandNear = handNear;
            localTransmitting = transmitting;
            UpdateStatusColor();
        }

        private void LateUpdate()
        {
            if (avatar == null)
                return;

            if (upperChestBone == null)
                upperChestBone = FindDescendantByName(avatar.transform, UpperChestBoneName);

            UpdatePose();
            UpdateStatusColor();
        }

        private void OnDestroy()
        {
            DestroyMaterial(bodyMaterial);
            DestroyMaterial(buttonMaterial);
            DestroyMaterial(statusMaterial);
        }

        private void CreateVisual()
        {
            if (avatar == null)
                return;

            visualRoot = new GameObject("Jingchen Lapel Microphone");
            visualRoot.transform.SetParent(avatar.transform, false);

            bodyMaterial = CreateMaterial(BodyColor);
            buttonMaterial = CreateMaterial(ButtonColor);
            statusMaterial = CreateMaterial(MutedColor);

            CreatePart(
                "Microphone Body",
                PrimitiveType.Cube,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.055f, 0.075f, 0.022f),
                bodyMaterial);

            CreatePart(
                "Microphone Clip",
                PrimitiveType.Cube,
                new Vector3(0f, 0f, -0.015f),
                Quaternion.identity,
                new Vector3(0.018f, 0.055f, 0.008f),
                buttonMaterial);

            CreatePart(
                "Push To Talk Button",
                PrimitiveType.Cylinder,
                new Vector3(0f, -0.010f, 0.014f),
                Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.015f, 0.003f, 0.015f),
                buttonMaterial);

            CreatePart(
                "Microphone Status Light",
                PrimitiveType.Sphere,
                new Vector3(0f, 0.024f, 0.015f),
                Quaternion.identity,
                new Vector3(0.012f, 0.012f, 0.006f),
                statusMaterial);

            SetLayerRecursively(visualRoot, avatar.gameObject.layer);
        }

        private void CreatePart(
            string partName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = partName;
            part.transform.SetParent(visualRoot.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;

            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null)
                Destroy(partCollider);

            Renderer partRenderer = part.GetComponent<Renderer>();
            if (partRenderer != null && material != null)
                partRenderer.sharedMaterial = material;
        }

        private void UpdatePose()
        {
            if (visualRoot == null)
                return;

            CalculatePose(out Vector3 position, out Quaternion rotation);
            visualRoot.transform.SetPositionAndRotation(position, rotation);
        }

        private void CalculatePose(out Vector3 position, out Quaternion rotation)
        {
            Transform avatarTransform = avatar != null ? avatar.transform : transform;
            Vector3 up = avatarTransform.up;
            Vector3 forward = Vector3.ProjectOnPlane(avatarTransform.forward, up).normalized;

            if (forward.sqrMagnitude < 0.001f && avatar != null && avatar.head != null)
                forward = Vector3.ProjectOnPlane(avatar.head.forward, up).normalized;

            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 chestCenter;

            if (upperChestBone != null)
            {
                chestCenter = upperChestBone.position;
            }
            else if (avatar != null && avatar.head != null)
            {
                chestCenter = avatar.head.position - up * 0.30f;
            }
            else
            {
                chestCenter = avatarTransform.position + up * 1.35f;
            }

            position = chestCenter - right * 0.10f + up * 0.03f + forward * 0.08f;
            rotation = Quaternion.LookRotation(forward, up);
        }

        private void UpdateStatusColor()
        {
            Color targetColor;

            if (isLocalAvatar)
            {
                targetColor = localTransmitting
                    ? TransmittingColor
                    : localHandNear
                        ? ReadyColor
                        : MutedColor;
            }
            else
            {
                targetColor = voice != null && voice.voiceVolume > RemoteVoiceThreshold
                    ? TransmittingColor
                    : MutedColor;
            }

            if (targetColor == appliedStatusColor)
                return;

            appliedStatusColor = targetColor;
            SetMaterialColor(statusMaterial, targetColor);
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                hideFlags = HideFlags.DontSave
            };

            SetMaterialColor(material, color);
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
                Destroy(material);
        }

        private static Transform FindDescendantByName(Transform root, string targetName)
        {
            if (root == null)
                return null;

            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform descendant in descendants)
            {
                if (descendant.name == targetName)
                    return descendant;
            }

            return null;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;

            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
