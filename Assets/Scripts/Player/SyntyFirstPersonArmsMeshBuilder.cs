using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public enum FirstPersonArmsCoverage
    {
        HandsAndForearms,
        HandsOnly,
        FullArms,
        ArmsWithoutShoulders
    }

    internal static class SyntyFirstPersonArmsMeshBuilder
    {
        private static readonly string[] FullArmBoneNameTokens =
        {
            "Clavicle",
            "Shoulder",
            "Elbow",
            "UpperArm",
            "LowerArm",
            "ForeArm",
            "LeftArm",
            "RightArm",
            "LeftForeArm",
            "RightForeArm",
            "LeftShoulder",
            "RightShoulder",
            "Hand",
            "Finger",
            "Thumb",
            "IndexFinger",
            "MiddleFinger",
            "RingFinger",
            "Pinky",
            "Little"
        };

        private static readonly string[] ForearmAndHandBoneNameTokens =
        {
            "Elbow",
            "LowerArm",
            "ForeArm",
            "Hand",
            "Finger",
            "Thumb",
            "IndexFinger",
            "MiddleFinger",
            "RingFinger",
            "Pinky",
            "Little"
        };

        private static readonly string[] HandOnlyBoneNameTokens =
        {
            "Hand",
            "Finger",
            "Thumb",
            "IndexFinger",
            "MiddleFinger",
            "RingFinger",
            "Pinky",
            "Little"
        };

        public static Mesh ExtractArmsMesh(
            SkinnedMeshRenderer source,
            float minArmBoneWeight,
            FirstPersonArmsCoverage coverage = FirstPersonArmsCoverage.HandsAndForearms)
        {
            if (source == null || source.sharedMesh == null)
            {
                return null;
            }

            var armBoneIndices = CollectArmBoneIndices(source.bones, coverage);
            if (armBoneIndices.Count == 0)
            {
                return null;
            }

            var mesh = source.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"[SyntyFirstPersonArmsMeshBuilder] Mesh '{mesh.name}' is not readable. " +
                    "Enable Read/Write on the model import settings or bake first-person arms in the prefab.",
                    source);
                return null;
            }

            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv = mesh.uv;
            var boneWeights = mesh.boneWeights;
            if (boneWeights == null || boneWeights.Length != vertices.Length)
            {
                return null;
            }

            var vertexIsArm = new bool[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertexIsArm[i] = IsArmWeighted(boneWeights[i], armBoneIndices, minArmBoneWeight);
            }

            var subMeshCount = mesh.subMeshCount;
            var includedTriangles = new List<int>(mesh.triangles.Length);
            for (var subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var triangles = mesh.GetTriangles(subMesh);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = triangles[i];
                    var b = triangles[i + 1];
                    var c = triangles[i + 2];
                    if (vertexIsArm[a] || vertexIsArm[b] || vertexIsArm[c])
                    {
                        includedTriangles.Add(a);
                        includedTriangles.Add(b);
                        includedTriangles.Add(c);
                    }
                }
            }

            if (includedTriangles.Count == 0)
            {
                return null;
            }

            var remap = new int[vertices.Length];
            for (var i = 0; i < remap.Length; i++)
            {
                remap[i] = -1;
            }

            var newVertices = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newTangents = new List<Vector4>();
            var newUv = new List<Vector2>();
            var newBoneWeights = new List<BoneWeight>();
            var newTriangles = new List<int>(includedTriangles.Count);

            for (var i = 0; i < includedTriangles.Count; i++)
            {
                var sourceIndex = includedTriangles[i];
                if (remap[sourceIndex] < 0)
                {
                    remap[sourceIndex] = newVertices.Count;
                    newVertices.Add(vertices[sourceIndex]);
                    if (normals != null && normals.Length == vertices.Length)
                    {
                        newNormals.Add(normals[sourceIndex]);
                    }

                    if (tangents != null && tangents.Length == vertices.Length)
                    {
                        newTangents.Add(tangents[sourceIndex]);
                    }

                    if (uv != null && uv.Length == vertices.Length)
                    {
                        newUv.Add(uv[sourceIndex]);
                    }

                    newBoneWeights.Add(boneWeights[sourceIndex]);
                }

                newTriangles.Add(remap[sourceIndex]);
            }

            var armsMesh = new Mesh
            {
                name = mesh.name + "_FirstPersonArms"
            };
            armsMesh.SetVertices(newVertices);
            if (newNormals.Count == newVertices.Count)
            {
                armsMesh.SetNormals(newNormals);
            }
            else
            {
                armsMesh.RecalculateNormals();
            }

            if (newTangents.Count == newVertices.Count)
            {
                armsMesh.SetTangents(newTangents);
            }

            if (newUv.Count == newVertices.Count)
            {
                armsMesh.SetUVs(0, newUv);
            }

            armsMesh.boneWeights = newBoneWeights.ToArray();
            armsMesh.bindposes = mesh.bindposes;
            armsMesh.SetTriangles(newTriangles, 0);
            armsMesh.RecalculateBounds();
            return armsMesh;
        }

        private static HashSet<int> CollectArmBoneIndices(IReadOnlyList<Transform> bones, FirstPersonArmsCoverage coverage)
        {
            var indices = new HashSet<int>();
            if (bones == null)
            {
                return indices;
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null)
                {
                    continue;
                }

                if (IsArmBoneName(bone.name, coverage))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private static bool IsArmBoneName(string boneName, FirstPersonArmsCoverage coverage)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return false;
            }

            if (coverage == FirstPersonArmsCoverage.ArmsWithoutShoulders)
            {
                if (IsShoulderOnlyBoneName(boneName))
                {
                    return false;
                }

                return IsArmBoneName(boneName, FirstPersonArmsCoverage.FullArms);
            }

            var tokens = ResolveBoneNameTokens(coverage);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (boneName.IndexOf(tokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsShoulderOnlyBoneName(string boneName)
        {
            return boneName.IndexOf("Clavicle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Shoulder", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string[] ResolveBoneNameTokens(FirstPersonArmsCoverage coverage)
        {
            switch (coverage)
            {
                case FirstPersonArmsCoverage.HandsOnly:
                    return HandOnlyBoneNameTokens;
                case FirstPersonArmsCoverage.FullArms:
                    return FullArmBoneNameTokens;
                default:
                    return ForearmAndHandBoneNameTokens;
            }
        }

        private static bool IsArmWeighted(BoneWeight boneWeight, HashSet<int> armBoneIndices, float minArmBoneWeight)
        {
            var armWeight = 0f;
            if (armBoneIndices.Contains(boneWeight.boneIndex0))
            {
                armWeight += boneWeight.weight0;
            }

            if (armBoneIndices.Contains(boneWeight.boneIndex1))
            {
                armWeight += boneWeight.weight1;
            }

            if (armBoneIndices.Contains(boneWeight.boneIndex2))
            {
                armWeight += boneWeight.weight2;
            }

            if (armBoneIndices.Contains(boneWeight.boneIndex3))
            {
                armWeight += boneWeight.weight3;
            }

            return armWeight >= minArmBoneWeight;
        }
    }
}
