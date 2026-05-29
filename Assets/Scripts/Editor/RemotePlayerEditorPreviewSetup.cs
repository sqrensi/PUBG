#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class RemotePlayerEditorPreviewSetup
    {
        [MenuItem("Shooter Prototype/Debug/Spawn Remote Preview Player")]
        public static void SpawnRemotePreviewPlayer()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemotePlayerEditorPreview] Enter Play Mode first.");
                return;
            }

            var remotePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRemotePrefabCreator.TargetPrefabPath);
            if (remotePrefab == null)
            {
                Debug.LogError(
                    "[RemotePlayerEditorPreview] PlayerCleanRemote prefab not found. " +
                    "Run Create PlayerClean Remote Prefab first.");
                return;
            }

            var spawnPosition = Vector3.zero;
            var spawnYaw = 0f;
            var localMarker = Object.FindObjectOfType<LocalPlayerMarker>();
            if (localMarker != null)
            {
                spawnPosition = localMarker.transform.position + localMarker.transform.right * 3f;
                spawnYaw = localMarker.transform.eulerAngles.y;
            }

            RemotePlayerEditorPreview.Spawn(remotePrefab, spawnPosition, spawnYaw);
        }

        [MenuItem("Shooter Prototype/Debug/Remove Remote Preview Player")]
        public static void RemoveRemotePreviewPlayer()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemotePlayerEditorPreview] Enter Play Mode first.");
                return;
            }

            RemotePlayerEditorPreview.DestroyPreview();
            Debug.Log("[RemotePlayerEditorPreview] Removed preview player.");
        }

        [MenuItem("Shooter Prototype/Debug/Spawn Remote Preview Player", true)]
        [MenuItem("Shooter Prototype/Debug/Remove Remote Preview Player", true)]
        private static bool ValidatePlayModeMenu()
        {
            return Application.isPlaying;
        }
    }
}
#endif
