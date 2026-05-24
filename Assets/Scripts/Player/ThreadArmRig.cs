using UnityEngine;

namespace ShooterPrototype.Player
{
    [DefaultExecutionOrder(210)]
    public sealed class ThreadArmRig : MonoBehaviour
    {
        [Header("Anchors")]
        [SerializeField] private Transform leftShoulder;
        [SerializeField] private Transform rightShoulder;
        [SerializeField] private Transform leftHandTarget;
        [SerializeField] private Transform rightHandTarget;

        [Header("Visual")]
        [SerializeField] private Color threadColor = Color.black;
        [SerializeField] private float threadWidth = 0.02f;
        [SerializeField] private int lineSortingOrder = 30;
        [SerializeField] private int armSmoothSegments = 12;

        [Header("Bend")]
        [SerializeField] private float elbowForwardOffset = 0.12f;
        [SerializeField] private float elbowSideOffset = 0.1f;
        [SerializeField] private float elbowDownOffset = 0.06f;
        [SerializeField] private bool pinArmStartToShoulder = true;

        private LineRenderer leftLine;
        private LineRenderer rightLine;
        private Material sharedThreadMaterial;

        public void Configure(Transform left, Transform right, Transform leftTarget, Transform rightTarget)
        {
            leftShoulder = left;
            rightShoulder = right;
            leftHandTarget = leftTarget;
            rightHandTarget = rightTarget;
            EnsureLines();
            RefreshThreadLines();
        }

        public void SetHandTargets(Transform leftTarget, Transform rightTarget)
        {
            leftHandTarget = leftTarget;
            rightHandTarget = rightTarget;
            RefreshThreadLines();
        }

        private void Awake()
        {
            EnsureLines();
            RefreshThreadLines();
        }

        private void LateUpdate()
        {
            RefreshThreadLines();
        }

        private void EnsureLines()
        {
            if (leftLine == null)
            {
                leftLine = CreateLineRenderer("LeftThread");
            }

            if (rightLine == null)
            {
                rightLine = CreateLineRenderer("RightThread");
            }
        }

        private LineRenderer CreateLineRenderer(string objectName)
        {
            var child = transform.Find(objectName);
            GameObject lineObject;
            if (child == null)
            {
                lineObject = new GameObject(objectName);
                lineObject.transform.SetParent(transform, false);
            }
            else
            {
                lineObject = child.gameObject;
            }

            var line = lineObject.GetComponent<LineRenderer>();
            if (line == null)
            {
                line = lineObject.AddComponent<LineRenderer>();
            }

            line.positionCount = Mathf.Max(3, armSmoothSegments + 1);
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            line.receiveShadows = true;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.generateLightingData = true;
            line.widthMultiplier = Mathf.Max(0.001f, threadWidth);
            line.startWidth = Mathf.Max(0.001f, threadWidth);
            line.endWidth = Mathf.Max(0.001f, threadWidth);
            line.startColor = threadColor;
            line.endColor = threadColor;
            line.sortingOrder = lineSortingOrder;

            if (sharedThreadMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader != null)
                {
                    sharedThreadMaterial = new Material(shader);
                    if (sharedThreadMaterial.HasProperty("_BaseColor"))
                    {
                        sharedThreadMaterial.SetColor("_BaseColor", threadColor);
                    }
                    if (sharedThreadMaterial.HasProperty("_Color"))
                    {
                        sharedThreadMaterial.SetColor("_Color", threadColor);
                    }
                    if (sharedThreadMaterial.HasProperty("_Smoothness"))
                    {
                        sharedThreadMaterial.SetFloat("_Smoothness", 0f);
                    }
                    if (sharedThreadMaterial.HasProperty("_Metallic"))
                    {
                        sharedThreadMaterial.SetFloat("_Metallic", 0f);
                    }
                }
            }

            if (sharedThreadMaterial != null)
            {
                line.sharedMaterial = sharedThreadMaterial;
            }
            else
            {
                // Last-resort fallback so the line never renders magenta.
                line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            return line;
        }

        private void RefreshThreadLines()
        {
            if (leftLine == null || rightLine == null)
            {
                return;
            }

            UpdateArmCurve(leftLine, leftShoulder, leftHandTarget, -1f);
            UpdateArmCurve(rightLine, rightShoulder, rightHandTarget, 1f);
        }

        private void UpdateArmCurve(LineRenderer line, Transform shoulder, Transform handTarget, float sideSign)
        {
            if (line == null || shoulder == null || handTarget == null)
            {
                if (line != null)
                {
                    line.enabled = false;
                }
                return;
            }

            line.enabled = true;
            line.positionCount = Mathf.Max(3, armSmoothSegments + 1);

            var start = shoulder.position;
            var end = handTarget.position;
            var midpoint = (start + end) * 0.5f;
            var side = transform.right * sideSign * elbowSideOffset;
            var forward = transform.forward * elbowForwardOffset;
            var down = Vector3.down * elbowDownOffset;
            var control = midpoint + side + forward + down;

            var segments = line.positionCount;
            for (var i = 0; i < segments; i++)
            {
                var t = segments <= 1 ? 1f : i / (float)(segments - 1);
                var p0 = Vector3.Lerp(start, control, t);
                var p1 = Vector3.Lerp(control, end, t);
                var curvePoint = Vector3.Lerp(p0, p1, t);
                line.SetPosition(i, curvePoint);
            }

            // Keep arm endpoints deterministic:
            // start is always shoulder, end is always current hand target.
            if (pinArmStartToShoulder)
            {
                line.SetPosition(0, start);
            }
            line.SetPosition(segments - 1, end);
        }
    }
}
