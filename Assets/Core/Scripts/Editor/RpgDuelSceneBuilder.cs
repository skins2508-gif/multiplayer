using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;

namespace Blocks.Gameplay.Core
{
    public static class RpgDuelSceneBuilder
    {
        public const string ScenePath = "Assets/RpgDuel/ArenaBlockout.unity";

        [System.Serializable]
        private class Layout { public Block[] blocks; }
        [System.Serializable]
        private class Block
        {
            public string name;
            public float[] position;
            public float[] size;
            public float[] rotation;
            public string style;
        }

        private static Vector3 Vector(float[] values) => new Vector3(values[0], values[1], values[2]);

        private static void BuildMap()
        {
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/RpgDuel/ArenaLayout.json"));
            foreach (var block in layout.blocks)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = block.name;
                obj.transform.position = Vector(block.position);
                obj.transform.localScale = Vector(block.size);
                obj.transform.rotation = Quaternion.Euler(Vector(block.rotation));
                obj.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    $"Assets/RpgDuel/Materials/Arena_{block.style}.mat");
            }
        }

        [MenuItem("Tools/RPG Arena/Open Blockout Map")]
        public static void OpenMap()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) Create();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/RPG Duel/Create Scene If Missing")]
        public static void Create()
        {
            if (File.Exists(ScenePath)) { Debug.Log("RPG Duel scene already exists: " + ScenePath); return; }
            Directory.CreateDirectory("Assets/RpgDuel");
            AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.name = "ArenaFighter";
                player.AddComponent<NetworkObject>();
                player.AddComponent<NetworkTransform>();
                player.AddComponent<RpgDuelPlayer>().arenaHalfExtent = 19;
                var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                nose.name = "FacingMarker";
                nose.transform.SetParent(player.transform, false);
                nose.transform.localPosition = new Vector3(0, 0.3f, 0.65f);
                nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.6f);
                Object.DestroyImmediate(nose.GetComponent<Collider>());
                var prefab = PrefabUtility.SaveAsPrefabAsset(player, "Assets/RpgDuel/ArenaFighter.prefab");
                Object.DestroyImmediate(player);

                var network = new GameObject("RPG Duel Network");
                var transport = network.AddComponent<UnityTransport>();
                var manager = network.AddComponent<GameNetworkManager>();
                manager.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    PlayerPrefab = prefab,
                    ConnectionApproval = true,
                    EnableSceneManagement = true
                };
                manager.maxPlayers = 8;
                network.AddComponent<RpgDuelUI>();
                BuildMap();
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 39, -26);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = 25;
                camera.gameObject.AddComponent<AudioListener>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                var builds = EditorBuildSettings.scenes.ToList();
                if (!builds.Any(entry => entry.path == ScenePath))
                {
                    builds.Add(new EditorBuildSettingsScene(ScenePath, true));
                    EditorBuildSettings.scenes = builds.ToArray();
                }
                Debug.Log("RPG Duel prototype created. Open " + ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
    }
}




