#if FUSION_WEAVER
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Makeshift.Network
{
    /// <summary>
    /// Starts a Fusion session and spawns the local player's network rig. Shared mode for real
    /// play (needs the Photon App ID in Fusion's settings); Single mode for offline checks.
    /// Invites will pass the session name here (Steam/Meta invite -> Photon session name).
    /// </summary>
    public class MakeshiftNetworkLauncher : MonoBehaviour
    {
        [SerializeField] private NetworkObject networkRigPrefab;
        [SerializeField] private string sessionName = "makeshift-dev";
        [SerializeField] private GameMode gameMode = GameMode.Shared;
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private bool startOnPlay = false;

        public NetworkRunner Runner { get; private set; }
        public NetworkObject LocalRig { get; private set; }

        private async void Start()
        {
            if (startOnPlay) await StartSession(sessionName);
        }

        public async Task<bool> StartSession(string session)
        {
            if (Runner != null) return Runner.IsRunning;

            Runner = gameObject.AddComponent<NetworkRunner>();
            Runner.ProvideInput = false;   // players move physically; there is no input stream

            var scene = SceneManager.GetActiveScene();
            var args = new StartGameArgs
            {
                GameMode = gameMode,
                SessionName = session,
                PlayerCount = maxPlayers,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            };
            if (scene.buildIndex >= 0) args.Scene = SceneRef.FromIndex(scene.buildIndex);

            var result = await Runner.StartGame(args);
            if (!result.Ok)
            {
                Debug.LogError($"[Net] StartGame failed: {result.ShutdownReason} {result.ErrorMessage}");
                return false;
            }

            if (networkRigPrefab != null)
                LocalRig = Runner.Spawn(networkRigPrefab, Vector3.zero, Quaternion.identity, Runner.LocalPlayer);
            return true;
        }

        public async Task Leave()
        {
            if (Runner != null) await Runner.Shutdown();
            Runner = null;
        }
    }
}
#endif
